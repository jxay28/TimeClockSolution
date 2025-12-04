# Sistema di gestione timbrature con C# e file CSV

Questo documento descrive l’architettura e le specifiche funzionali per un sistema di gestione delle presenze e degli straordinari basato su due applicazioni C# (Server e Client) che comunicano attraverso file CSV in una cartella condivisa.  Il documento è strutturato per mostrare i modelli di dati, le interazioni fra i componenti e le classi chiave da implementare.

## 1 Modelli dei dati CSV

Il sistema memorizza i dati in file CSV.  Un file CSV è un formato testuale per dati tabellari in cui ogni riga rappresenta un record e i campi sono separati da virgole【219304724000031†L158-L164】.  Di seguito sono descritti i file utilizzati e le loro colonne.

| File/oggetto | Scopo | Struttura (campi)|
|---|---|---|
| **`utenti.csv`** | Anagrafica dipendenti | *IdUtente*, *Nome*, *Cognome*, *Ruolo*, *DataAssunzione* (ISO 8601), *OreContrattoSettimanali*, *CompensoOrarioBase*, *CompensoOrarioExtra* |
| **`festivita.csv`** | Calendario delle festività | *Data* (ISO 8601), *Descrizione*.  Durante queste date tutte le ore sono considerate straordinari |
| **`parametri_straordinari.csv`** | Parametri globali | *SogliaMinuti* (es. 15, 30), *UnitaArrotondamentoMinuti* (es. 15).  Definisce la soglia minima per riconoscere gli straordinari e l’unità di arrotondamento |
| **`<IdUtente>.csv`** | File di timbrature individuale per ciascun utente | *DataOra* (timestamp ISO 8601), *Tipo* (ENTRATA/USCITA) |
| **`report_commercialista_yyyyMM.csv`** | Riepilogo mensile per lo studio contabile | *IdUtente*, *CodiceFiscale*, *Mese*, *Anno*, *OreOrdinarieTotali*, *OreStraordinarieTotali* |
| **`report_paghe_yyyyMM.csv`** | Output per software paghe | Esempio: *IdUtente*, *AnnoMese*, *OreOrdinarie*, *OreStraordinarie*, *CompensoBase*, *CompensoStraordinario* |

### Formato CSV

* **Codifica**: usare UTF‑8 per garantire compatibilità.
* **Separatore**: virgola (,).  Per valori che contengono virgole o spazi si usano virgolette (CSV conforme a RFC 4180).
* **File per utente**: la scelta di un file per utente riduce la concorrenza fra le istanze del client; ogni file viene aperto con `FileShare.None` o `FileShare.Read` per impedire scritture concorrenti【186257342178846†L154-L161】【515616528172414†L158-L165】.

## 2 Software Server

L’applicazione server gestisce la configurazione, l’anagrafica, le festività, l’elaborazione delle timbrature e la generazione dei report.  È una applicazione desktop o servizio Windows sviluppato in C# (compatibile con .NET 6/7).  L’interfaccia amministrativa può utilizzare librerie moderne come **MahApps.Metro** o **Material Design XAML**, che offrono stili e controlli moderni; ad esempio MahApps.Metro fornisce un framework per creare interfacce in stile metro senza sforzo【41669206706288†L15-L30】 e MaterialDesignInXAML include stili per componenti esistenti e nuovi controlli che seguono la logica di design Material【181236034552035†L205-L208】.

### 2.1 Gestione anagrafica

* **Creazione/aggiornamento utenti** – l’amministratore inserisce i dati anagrafici e contrattuali dell’utente.  Ogni record viene scritto nel file `utenti.csv`.
* **Validazioni**: assicurarsi che l’IdUtente sia univoco e che ore settimanali, compensi e date siano valorizzati.  Eventuali modifiche a un utente devono aggiornare il file CSV in modo atomico.

### 2.2 Festività e logica straordinari

* **Calendario festività** – l’amministratore può definire o importare le festività annuali nel file `festivita.csv`.  Le date vengono caricate in una struttura in memoria (`HolidayCalendar`).  Tutte le ore lavorate in questi giorni sono conteggiate come straordinari.
* **Parametri straordinari** – il file `parametri_straordinari.csv` contiene la **soglia minima in minuti** per attivare l’arrotondamento e l’unità di arrotondamento (es. 15 minuti).  Se il tempo extra accumulato supera la soglia, l’algoritmo aggiunge un’unità di tempo corrispondente; se è minore o uguale alla soglia, l’unità viene ignorata.
* **Rounding** – l’algoritmo calcola la differenza fra ore effettive e ore contrattuali; per gli straordinari oltre la soglia, i minuti vengono arrotondati secondo l’unità.  Il calcolo usa `DateTime` e `TimeSpan`: la differenza fra due date restituisce un oggetto `TimeSpan` con proprietà come `Hours` e `Minutes`【945773032665247†L74-L99】.

### 2.3 Elaborazione mensile e reportistica

Per ogni utente e per il mese/anno selezionato:

1. **Recupero timbrature** – leggere il file `<IdUtente>.csv` e ordinare le righe per data/ora.
2. **Calcolo ore giornaliere** – per ogni coppia ENTRATA/USCITA calcolare la durata (TimeSpan).  Se manca una timbratura di uscita, generare un avviso e ignorare l’ultima timbratura incompleta.
3. **Determinazione ordinarie/straordinarie** – confrontare le ore totali giornaliere con il monte ore contrattuale (ore settimanali divise per giorni lavorativi).  Le ore eccedenti e le ore nei giorni festivi sono straordinarie.  Applicare la logica di arrotondamento.
4. **Compenso** – calcolare il compenso: ore ordinarie × compenso base + ore straordinarie × compenso extra.
5. **Generazione report** – produrre due CSV: uno per il commercialista con il riepilogo delle ore e uno per il gestionale paghe con il dettaglio di periodo (mese/anno, ore, compensi).  I file vengono creati nella cartella condivisa e nominati `report_commercialista_yyyyMM.csv` e `report_paghe_yyyyMM.csv`.

### 2.4 Archiviazione e concorrenza

* Tutti i file CSV si trovano in una **cartella condivisa**.  Il server accede ai file tramite percorsi configurabili.
* Per evitare conflitti quando più processi accedono ai file, l’applicazione usa la sintassi `FileStream` con `FileAccess.Write` e condivisione `FileShare.Read`.  Se un processo tenta di aprire il file mentre è in uso, l’operazione genera un’eccezione e viene gestita tramite retry【515616528172414†L158-L184】.
* All’interno dell’applicazione, il codice che scrive su un file usa il costrutto `lock` o `Mutex` per impedire accessi contemporanei da thread differenti【785865764637246†L143-L156】.  Questa sincronizzazione impedisce che due thread scrivano simultaneamente nello stesso file.

## 3 Software Client (timbratura)

Il client è un’applicazione WPF leggera installata su ogni computer dei dipendenti.  La finestra principale si apre al centro dello schermo e presenta un design moderno tramite librerie come MahApps.Metro o Material Design.

### 3.1 Setup e interfaccia utente

* **Selezione percorso** – al primo avvio l’utente configura il percorso della cartella condivisa attraverso un dialogo.  Il percorso viene salvato localmente (es. in un file di configurazione).  In assenza del percorso, i pulsanti sono disabilitati.
* **Caricamento utenti** – il client legge `utenti.csv` dalla cartella condivisa e popola una combinazione a tendina.  L’utente deve scegliere il proprio nome prima di poter timbrare.
* **Design** – usare controlli come `ComboBox`, `Button`, `TextBlock` con stili moderni.  MahApps.Metro sostituisce gli stili dei controlli WPF con un look moderno e fornisce controlli aggiuntivi【41669206706288†L15-L30】, mentre MaterialDesignInXAML offre stili basati sulla logica Material Design【181236034552035†L205-L208】.

### 3.2 Funzionalità di timbratura

* **Pulsanti Entrata/Uscita** – all’avvio, entrambi i pulsanti sono disabilitati.  Dopo la selezione dell’utente vengono abilitati in base allo stato:
  * Se l’ultima riga del file `<IdUtente>.csv` è “ENTRATA”, il dipendente è dentro: si abilita solo il pulsante “USCITA”.
  * Se l’ultima riga è “USCITA” o il file è vuoto, si abilita solo “ENTRATA”.
* **Scrittura timbratura** – quando l’utente preme il pulsante, l’applicazione aggiunge una nuova riga con `DateTime.Now` (formato ISO 8601) e il tipo della timbratura.  La scrittura avviene aprendo il file con `FileShare.None` (blocco esclusivo) o `FileShare.Read` (letture permesse ma scritture esclusive)【186257342178846†L154-L161】【515616528172414†L158-L184】 per impedire che due istanze scrivano contemporaneamente.
* **Convalide** – se si tenta di timbrare due volte ENTRATA o due volte USCITA, il client avvisa l’utente e non registra la timbratura.

### 3.3 Resilienza e concorrenza

Poiché più istanze del client possono essere attive su PC diversi, il controllo dello stato deve funzionare anche in presenza di accessi concorrenti.  Le misure principali sono:

* **Blocco file** – il client usa l’apertura del file con `FileShare.None` o `FileShare.Read` in fase di scrittura per garantire che una sola istanza scriva alla volta【515616528172414†L158-L184】.  Se il file è in uso, l’applicazione effettua un retry dopo un breve ritardo.
* **Lettura sicura** – la lettura del file può essere fatta con `FileAccess.Read` e `FileShare.ReadWrite` poiché leggere non richiede il blocco completo.  Tuttavia, per determinare lo stato (ultima timbratura) si legge la riga finale in modo atomico.
* **Eccezioni** – eventuali eccezioni di I/O (es. file bloccato) vengono gestite con messaggi utente e tentativi successivi.

## 4 Interazione Server–Client

L’architettura segue un modello **client–server**.  La cartella condivisa funge da repository dei dati e da punto di integrazione.  La figura seguente riassume le interazioni:

![Diagramma sistema]({{file:diagram}})

Nel diagramma:

1. **Cartella Condivisa**: contiene i file CSV di configurazione, le timbrature per utente e i report.
2. **Applicazione Client**: legge `utenti.csv` e scrive/legge `<IdUtente>.csv`.  L’interazione con la cartella avviene tramite apertura in modalità esclusiva per la scrittura.
3. **Applicazione Server**: legge/scrive `utenti.csv`, `festivita.csv` e `parametri_straordinari.csv`; legge tutti i `<IdUtente>.csv` per elaborare le ore; genera i report mensili.

## 5 Specifiche C# e classi chiave

Questa sezione propone le classi principali da implementare.  Si consiglia di utilizzare il pattern **MVVM** per separare logica e interfaccia e di organizzare il codice in progetti *Core* (logica e modelli), *DataAccess* (accesso ai CSV) e *WpfClient* (interfaccia utente).

### 5.1 Modelli

```csharp
// Rappresenta un dipendente nella anagrafica
public class UserProfile
{
    public string Id { get; set; }             // identificatore univoco
    public string Nome { get; set; }
    public string Cognome { get; set; }
    public string Ruolo { get; set; }
    public DateTime DataAssunzione { get; set; }
    public double OreContrattoSettimanali { get; set; }
    public decimal CompensoOrarioBase { get; set; }
    public decimal CompensoOrarioExtra { get; set; }
}

// Rappresenta una singola timbratura
public class TimeCardEntry
{
    public string UserId { get; set; }
    public DateTime DataOra { get; set; }
    public PunchType Tipo { get; set; } // ENTRATA o USCITA
}

// Enum per il tipo di timbratura
public enum PunchType { Entrata, Uscita }

// Contiene i parametri globali per gli straordinari
public class OvertimeSettings
{
    public int SogliaMinuti { get; set; }            // es. 15
    public int UnitaArrotondamentoMinuti { get; set; } // es. 15, 30
}

// Rappresenta una festività
public class Holiday
{
    public DateTime Data { get; set; }
    public string Descrizione { get; set; }
}
```

### 5.2 Accesso ai dati (CSV)

L’accesso ai file CSV deve essere incapsulato in classi di repository che gestiscono la serializzazione/deserializzazione e la concorrenza.

```csharp
public class CsvRepository<T>
{
    private readonly object _sync = new();

    // Legge tutti i record da un CSV
    public IEnumerable<T> Load(string path, Func<string[], T> map)
    {
        using var stream = new FileStream(path, FileMode.OpenOrCreate,
            FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var fields = line.Split(',');
            yield return map(fields);
        }
    }

    // Aggiunge un record al CSV in modalità thread‑safe
    public void Append(string path, string csvLine)
    {
        // blocco per il thread corrente
        lock (_sync)
        {
            // apertura con FileShare.Read per bloccare altre scritture【515616528172414†L158-L184】
            using var stream = new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.Write, FileShare.Read);
            stream.Seek(0, SeekOrigin.End);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.WriteLine(csvLine);
        }
    }
}
```

**Nota sulla concorrenza** – l’uso di `lock` per sincronizzare l’accesso da più thread e di `FileShare.Read`/`FileShare.None` per bloccare la scrittura da altri processi evita corruzioni del file【785865764637246†L143-L156】【515616528172414†L158-L184】.  In caso di eccezioni (file in uso), è consigliabile implementare un meccanismo di retry con attesa.

### 5.3 Calcolo delle ore e straordinari

La classe `PayPeriodCalculator` elabora le timbrature di un utente per un mese e restituisce le ore ordinarie e straordinarie.

```csharp
public class PayPeriodCalculator
{
    private readonly OvertimeSettings _settings;
    private readonly IEnumerable<Holiday> _holidays;

    public PayPeriodCalculator(OvertimeSettings settings, IEnumerable<Holiday> holidays)
    {
        _settings = settings;
        _holidays = holidays;
    }

    // Restituisce un riepilogo mensile (ore ordinarie, straordinarie e importi)
    public PaySummary Calculate(UserProfile user, IEnumerable<TimeCardEntry> entries, int year, int month)
    {
        var groupedByDate = entries
            .Where(e => e.DataOra.Year == year && e.DataOra.Month == month)
            .OrderBy(e => e.DataOra)
            .GroupBy(e => e.DataOra.Date);

        double totaleOrdinarie = 0;
        double totaleStraordinarie = 0;

        foreach (var dayGroup in groupedByDate)
        {
            double hours = 0;
            var punches = dayGroup.ToList();
            for (int i = 0; i < punches.Count - 1; i += 2)
            {
                if (punches[i].Tipo == PunchType.Entrata && punches[i + 1].Tipo == PunchType.Uscita)
                {
                    TimeSpan durata = punches[i + 1].DataOra - punches[i].DataOra;
                    hours += durata.TotalHours;
                }
            }

            bool isHoliday = _holidays.Any(h => h.Data.Date == dayGroup.Key);
            double contractHours = user.OreContrattoSettimanali / 5.0; // esempio: 5 giorni lavorativi

            if (isHoliday)
            {
                totaleStraordinarie += hours;
            }
            else
            {
                if (hours > contractHours)
                {
                    double extra = hours - contractHours;
                    // Applicare soglia e arrotondamento
                    double extraMinutes = extra * 60;
                    if (extraMinutes > _settings.SogliaMinuti)
                    {
                        int units = (int)Math.Ceiling(extraMinutes / _settings.UnitaArrotondamentoMinuti);
                        totaleStraordinarie += units * (_settings.UnitaArrotondamentoMinuti / 60.0);
                    }
                    totaleOrdinarie += contractHours;
                }
                else
                {
                    totaleOrdinarie += hours;
                }
            }
        }

        decimal compensoBase = (decimal)totaleOrdinarie * user.CompensoOrarioBase;
        decimal compensoExtra = (decimal)totaleStraordinarie * user.CompensoOrarioExtra;

        return new PaySummary
        {
            OreOrdinarie = totaleOrdinarie,
            OreStraordinarie = totaleStraordinarie,
            CompensoOrdinarie = compensoBase,
            CompensoStraordinarie = compensoExtra
        };
    }
}

public class PaySummary
{
    public double OreOrdinarie { get; set; }
    public double OreStraordinarie { get; set; }
    public decimal CompensoOrdinarie { get; set; }
    public decimal CompensoStraordinarie { get; set; }
}
```

### 5.4 Servizio di report

Il servizio di report legge i `PaySummary` di tutti gli utenti e scrive i file CSV di output.  L’interfaccia è simile:

```csharp
public class ReportService
{
    public void GenerateReports(IEnumerable<UserProfile> users, IEnumerable<PaySummary> summaries, int year, int month, string outputFolder)
    {
        var reportCommLines = new List<string> { "IdUtente,CodiceFiscale,Mese,Anno,OreOrdinarie,OreStraordinarie" };
        var reportPagheLines = new List<string> { "IdUtente,AnnoMese,OreOrdinarie,OreStraordinarie,CompensoBase,CompensoStraordinario" };

        foreach (var u in users)
        {
            var summary = summaries.FirstOrDefault(s => s.UserId == u.Id);
            if (summary == null) continue;
            string monthString = month.ToString("D2");
            reportCommLines.Add($"{u.Id},{/*CodiceFiscale*/},{monthString},{year},{summary.OreOrdinarie:F2},{summary.OreStraordinarie:F2}");
            reportPagheLines.Add($"{u.Id},{year}{monthString},{summary.OreOrdinarie:F2},{summary.OreStraordinarie:F2},{summary.CompensoOrdinarie:F2},{summary.CompensoStraordinarie:F2}");
        }

        File.WriteAllLines(Path.Combine(outputFolder, $"report_commercialista_{year}{month:D2}.csv"), reportCommLines);
        File.WriteAllLines(Path.Combine(outputFolder, $"report_paghe_{year}{month:D2}.csv"), reportPagheLines);
    }
}
```

## 6 Considerazioni finali

Il sistema proposto consente di gestire le presenze e calcolare gli straordinari in modo semplice usando file CSV.  L’utilizzo di librerie UI moderne come **MahApps.Metro** e **Material Design XAML** garantisce un’interfaccia utente pulita e coerente【41669206706288†L15-L30】【181236034552035†L205-L208】.  Per evitare problemi di concorrenza, è fondamentale aprire i file con opzioni di condivisione adeguate e sincronizzare le operazioni mediante `lock` o `FileStream.Lock`【785865764637246†L143-L156】【515616528172414†L158-L184】.  L’architettura client–server con cartella condivisa consente un’implementazione semplice senza database, pur mantenendo la scalabilità attraverso file separati per utente e report mensili.
