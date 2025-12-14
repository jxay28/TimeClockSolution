using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Server
{
    public class ReportRow
    {
        public int Giorno { get; set; }
        public string? Entrata1 { get; set; }
        public string? Uscita1 { get; set; }
        public string? Entrata2 { get; set; }
        public string? Uscita2 { get; set; }

        // Queste rimangono double per i calcoli matematici
        public double OreOrdinarie { get; set; }
        public double OreStraordinarie { get; set; }

        public bool IsFestivo { get; set; }
        public string? Note { get; set; }

        // --- NUOVE PROPRIETÀ PER LA GRAFICA (HH:mm) ---

        public string OreOrdinarieVisual => ConvertiDecimaliInOre(OreOrdinarie);

        public string OreStraordinarieVisual => ConvertiDecimaliInOre(OreStraordinarie);

        // Funzione interna per formattare (es. 8.5 -> "08:30")
        private string ConvertiDecimaliInOre(double oreDecimali)
        {
            var ts = TimeSpan.FromHours(oreDecimali);
            // Usiamo (int)ts.TotalHours per gestire anche totali > 24 ore correttamente
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
        }
    }
    public partial class ReportWindow : Window
    {
        private readonly string _csvFolder;
        private readonly List<UserProfile> _users;

        public ReportWindow(string csvFolder, List<UserProfile> users)
        {
            InitializeComponent();

            _csvFolder = csvFolder;
            _users = users ?? new List<UserProfile>();

            // Popola la combo utenti
            UserCombo.ItemsSource = _users;
            // Se UserProfile non ha DisplayMemberPath configurato, verrà usato ToString()

            // Seleziona mese corrente
            int currentMonth = DateTime.Now.Month;
            if (currentMonth >= 1 && currentMonth <= 12)
                MonthCombo.SelectedIndex = currentMonth - 1;
        }

        // ===========================
        //   CARICA REPORT
        // ===========================
        private void CaricaReport_Click(object sender, RoutedEventArgs e)
        {
            var user = UserCombo.SelectedItem as UserProfile;
            // Controllo input
            if (user == null)
            {
                MessageBox.Show("Seleziona un utente.");
                return;
            }

            if (MonthCombo.SelectedIndex < 0)
            {
                MessageBox.Show("Seleziona un mese.");
                return;
            }

            int month = MonthCombo.SelectedIndex + 1;
            int year = DateTime.Now.Year;

            // Controllo cartella
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Cartella CSV non impostata.");
                return;
            }

            string userFile = System.IO.Path.Combine(_csvFolder, $"{user.Id}.csv");
            if (!System.IO.File.Exists(userFile))
            {
                MessageBox.Show($"Nessun file di timbrature trovato per l'utente {user.Nome} {user.Cognome}.");
                ReportGrid.ItemsSource = new List<ReportRow>();
                return;
            }

            // 1. CARICAMENTO DATI (Mese corrente + buffer inizio mese prossimo per turni notturni)
            var repo = new CsvRepository();
            var allEntries = new List<TimeCardEntry>();

            foreach (var row in repo.Load(userFile))
            {
                if (row.Length < 2 || !DateTime.TryParse(row[0], out var dt)) continue;

                // Carichiamo il mese target E i primi 2 giorni del mese successivo 
                // per catturare eventuali uscite di turni notturni (es. 31/01 22:00 -> 01/02 06:00)
                bool isTargetMonth = (dt.Year == year && dt.Month == month);
                bool isBufferNextMonth = (dt.Date <= new DateTime(year, month, 1).AddMonths(1).AddDays(2));

                if (isTargetMonth || isBufferNextMonth)
                {
                    PunchType tipo;
                    if (!Enum.TryParse(row[1], true, out tipo)) tipo = PunchType.Entrata;

                    allEntries.Add(new TimeCardEntry { UserId = user.Id, DataOra = dt, Tipo = tipo });
                }
            }

            // Ordiniamo cronologicamente
            allEntries = allEntries.OrderBy(x => x.DataOra).ToList();

            // 2. ACCOPPIAMENTO (PAIRING) INTELLIGENTE
            var tutteLeCoppie = CostruisciCoppieGlobali(allEntries);

            // Filtriamo solo le coppie INIZIATE nel mese selezionato
            var coppieDelMese = tutteLeCoppie
                .Where(c => c.Ingresso.Month == month && c.Ingresso.Year == year)
                .ToList();

            // 3. GENERAZIONE RIGHE 
            var righeReport = new List<ReportRow>();
            int daysInMonth = DateTime.DaysInMonth(year, month);

            // Raggruppiamo per giorno
            var gruppiPerGiorno = coppieDelMese
                .GroupBy(c => c.Ingresso.Day)
                .ToDictionary(g => g.Key, g => g.ToList());

            // CICLO SUI GIORNI DEL MESE
            for (int day = 1; day <= daysInMonth; day++)
            {
                DateTime dataCorrente = new DateTime(year, month, day);

                if (!gruppiPerGiorno.ContainsKey(day))
                {
                    // Nessuna timbrata: riga vuota
                    var emptyRow = new ReportRow { Giorno = day };
                    // Passiamo null come coppie
                    CalcolaTotaliRiga(emptyRow, dataCorrente, user, null, null);
                    righeReport.Add(emptyRow);
                }
                else
                {
                    var coppieGiorno = gruppiPerGiorno[day];
                    coppieGiorno = coppieGiorno.OrderBy(c => c.Ingresso).ToList();

                    int index = 0;
                    // CICLO WHILE per gestire più righe nello stesso giorno (> 4 timbrate)
                    while (index < coppieGiorno.Count)
                    {
                        var row = new ReportRow { Giorno = day };

                        // Coppia 1 (obbligatoria se siamo qui)
                        var c1 = coppieGiorno[index];
                        row.Entrata1 = c1.Ingresso.ToString("HH:mm");
                        row.Uscita1 = c1.Uscita.ToString("HH:mm");
                        index++;

                        // Coppia 2 (opzionale)
                        // Usiamo la sintassi corretta per il Nullable Tuple
                        (DateTime, DateTime)? c2 = null;

                        if (index < coppieGiorno.Count)
                        {
                            var pair2 = coppieGiorno[index];
                            c2 = pair2;

                            row.Entrata2 = pair2.Ingresso.ToString("HH:mm");
                            row.Uscita2 = pair2.Uscita.ToString("HH:mm");
                            index++;
                        }

                        // Calcolo ore
                        CalcolaTotaliRiga(row, dataCorrente, user, c1, c2);

                        // Aggiungiamo alla lista TEMPORANEA (non alla Grid!)
                        righeReport.Add(row);
                    }
                }
            } // FINE CICLO FOR

            // 4. ASSEGNAZIONE ALLA GRIGLIA (SOLO ALLA FINE)
            // Resettiamo prima per sicurezza
            ReportGrid.ItemsSource = null;
            ReportGrid.ItemsSource = righeReport;

            AggiornaTotali();
        }

        // ===========================
        //   RICALCOLA REPORT
        // ===========================
        // 1. EVENTO: Scatta quando l'utente finisce di modificare una cella e preme invio o cambia selezione
        private void ReportGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Usiamo il Dispatcher per attendere che il valore digitato venga 
            // effettivamente salvato nella proprietà dell'oggetto ReportRow
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var row = e.Row.Item as ReportRow;
                if (row != null)
                {
                    // Ricalcoliamo solo questa riga
                    RicalcolaLogicaRiga(row);

                    // Aggiorniamo la grafica della riga (necessario per forzare il refresh delle colonne Ordinarie/Extra)
                    ReportGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                    ReportGrid.CommitEdit(DataGridEditingUnit.Row, true);

                    // Recuperiamo la lista aggiornata delle righe dal DataGrid
                    var righe = ReportGrid.ItemsSource as IEnumerable<ReportRow>;
                    ReportGrid.ItemsSource = null;
                    ReportGrid.ItemsSource = righe?.ToList();

                    // Aggiorniamo i totali generali
                    AggiornaTotali();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // 2. LOGICA: Questa funzione prende una riga, legge le stringhe (es "23:00") 
        // e rifà i calcoli matematici applicando le regole (notturno, 8 ore, ecc)
        private void RicalcolaLogicaRiga(ReportRow r)
        {
            var user = UserCombo.SelectedItem as UserProfile;
            if (user == null || MonthCombo.SelectedIndex < 0) return;

            int month = MonthCombo.SelectedIndex + 1;
            int year = DateTime.Now.Year;
            DateTime dataBase = new DateTime(year, month, r.Giorno);

            // --- RICOSTRUZIONE COPPIA 1 ---
            (DateTime, DateTime)? c1 = null;
            if (!string.IsNullOrWhiteSpace(r.Entrata1) && !string.IsNullOrWhiteSpace(r.Uscita1))
            {
                if (TimeSpan.TryParse(r.Entrata1, out var tIn1) && TimeSpan.TryParse(r.Uscita1, out var tOut1))
                {
                    DateTime dtIn = dataBase.Add(tIn1);
                    DateTime dtOut = dataBase.Add(tOut1);
                    // Fix Notturno
                    if (tOut1 < tIn1) dtOut = dtOut.AddDays(1);
                    c1 = (dtIn, dtOut);
                }
            }

            // --- RICOSTRUZIONE COPPIA 2 ---
            (DateTime, DateTime)? c2 = null;
            if (!string.IsNullOrWhiteSpace(r.Entrata2) && !string.IsNullOrWhiteSpace(r.Uscita2))
            {
                if (TimeSpan.TryParse(r.Entrata2, out var tIn2) && TimeSpan.TryParse(r.Uscita2, out var tOut2))
                {
                    DateTime dtIn = dataBase.Add(tIn2);
                    DateTime dtOut = dataBase.Add(tOut2);
                    // Fix Notturno
                    if (tOut2 < tIn2) dtOut = dtOut.AddDays(1);
                    c2 = (dtIn, dtOut);
                }
            }

            // Chiamata al calcolatore centrale (quello con la regola delle 8 ore)
            CalcolaTotaliRiga(r, dataBase, user, c1, c2);
        }

        // ===========================
        //   ESPORTA TXT
        // ===========================
        private void EsportaTXT_Click(object sender, RoutedEventArgs e)
        {
            var righe = ReportGrid.ItemsSource as IEnumerable<ReportRow>;
            if (righe == null || !righe.Any())
            {
                MessageBox.Show("Non ci sono dati da esportare.");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "File di testo (*.txt)|*.txt",
                FileName = "report_mensile.txt"
            };

            if (dlg.ShowDialog() != true)
                return;

            var lines = new List<string>
            {
                "Giorno\tEntrata1\tUscita1\tEntrata2\tUscita2\tOreOrdinarie\tOreStraordinarie\tFestivo\tNote"
            };

            foreach (var r in righe)
            {
                lines.Add(string.Join("\t", new[]
                {
                    r.Giorno.ToString(),
                    r.Entrata1 ?? "",
                    r.Uscita1 ?? "",
                    r.Entrata2 ?? "",
                    r.Uscita2 ?? "",
                    r.OreOrdinarie.ToString("F2", CultureInfo.InvariantCulture),
                    r.OreStraordinarie.ToString("F2", CultureInfo.InvariantCulture),
                    r.IsFestivo ? "SI" : "NO",
                    r.Note ?? ""
                }));
            }

            File.WriteAllLines(dlg.FileName, lines);
            MessageBox.Show("Esportazione TXT completata.");
        }

        // ===========================
        //   ESPORTA PDF (stub)
        // ===========================
        private void EsportaPDF_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Per il PDF servirà una libreria esterna (es. iTextSharp, PdfSharp). Al momento è disponibile l'esportazione TXT.");
        }

        // ===========================
        //   FUNZIONI DI SUPPORTO
        // ===========================

        /// <summary>
        /// Dato l'elenco di timbrature (Entrata/Uscita) per un giorno,
        /// costruisce coppie Ingresso–Uscita in ordine cronologico.
        /// </summary>
        private List<(DateTime Ingresso, DateTime Uscita)> CostruisciCoppieGlobali(List<TimeCardEntry> entries)
        {
            var result = new List<(DateTime Ingresso, DateTime Uscita)>();

            // Stack o variabile temporanea per l'ultima entrata aperta
            DateTime? lastIngresso = null;

            foreach (var e in entries)
            {
                if (e.Tipo == PunchType.Entrata)
                {
                    // Se avevamo già un ingresso aperto senza uscita (es. dimenticanza timbratura), 
                    // cosa facciamo? Per ora sovrascriviamo (nuovo inizio turno) o ignoriamo il precedente.
                    // Politica standard: l'ultimo IN vince.
                    lastIngresso = e.DataOra;
                }
                else if (e.Tipo == PunchType.Uscita)
                {
                    if (lastIngresso.HasValue)
                    {
                        // Abbiamo una coppia valida!
                        // Verifica di sanità: l'uscita deve essere dopo l'entrata
                        if (e.DataOra > lastIngresso.Value)
                        {
                            result.Add((lastIngresso.Value, e.DataOra));
                        }

                        // Chiudiamo il turno
                        lastIngresso = null;
                    }
                    // Se lastIngresso è null, è un'uscita orfana (timbrata per sbaglio o inizio perso). La ignoriamo.
                }
            }

            return result;
        }
        private void CalcolaTotaliRiga(ReportRow row, DateTime dataRiferimento, UserProfile user,
                               (DateTime In, DateTime Out)? c1,
                               (DateTime In, DateTime Out)? c2)
        {
            // Recupera parametri globali (default 15 minuti se null)
            int sogliaMinuti = App.ParametriGlobali != null ? App.ParametriGlobali.SogliaMinutiStraordinario : 15;

            // Determina se festivo
            bool isFestivo = IsGiornoFestivo(dataRiferimento);
            row.IsFestivo = isFestivo;

            // Se non ci sono timbrature (riga vuota), esci
            if (c1 == null)
            {
                row.OreOrdinarie = 0;
                row.OreStraordinarie = 0;
                return;
            }

            double oreLavorateTotali = 0;

            // Durata C1
            oreLavorateTotali += (c1.Value.Out - c1.Value.In).TotalHours;

            // Durata C2
            if (c2.HasValue)
            {
                oreLavorateTotali += (c2.Value.Out - c2.Value.In).TotalHours;
            }

            // SE FESTIVO -> TUTTO STRAORDINARIO
            if (isFestivo)
            {
                row.OreOrdinarie = 0;
                row.OreStraordinarie = Math.Round(oreLavorateTotali, 2);
                return;
            }

            // SE FERIALE -> Divisione Ordinario/Extra
            // Qui applichiamo la logica semplice: ore totali vs ore previste o turni.
            // Per ora usiamo la logica base: le ore lavorate sono ordinarie, 
            // a meno che non superino un monte ore giornaliero (es. 8h) o siano fuori orario.
            // Usiamo una logica semplificata "a soglia" per evitare complessità eccessiva immediata:

            // Esempio: 8 ore ordinarie max, il resto straordinario
            double limiteGiornaliero = 8.0;

            if (user.OreContrattoSettimanali > 0)
            {
                // Se vuoi essere preciso, dividi ore settimanali / 5 o 6 giorni
                // limiteGiornaliero = user.OreContrattoSettimanali / 5.0; 
            }

            double ord = 0;
            double extra = 0;

            if (oreLavorateTotali > limiteGiornaliero)
            {
                ord = limiteGiornaliero;
                extra = oreLavorateTotali - limiteGiornaliero;
            }
            else
            {
                ord = oreLavorateTotali;
                extra = 0;
            }

            // Controllo Soglia Minima Straordinari
            if (extra * 60 < sogliaMinuti)
            {
                ord += extra; // Assorbi in ordinario
                extra = 0;
            }

            row.OreOrdinarie = Math.Round(ord, 2);
            row.OreStraordinarie = Math.Round(extra, 2);
        }

        // Funzione helper per confrontare orari reali con previsti
        private void CalcolaSpacchettamentoOre(DateTime inReale, DateTime outReale, UserProfile user, ref double accOrd, ref double accExtra)
        {
            // Nota: Gestire orari previsti che scavalcano la mezzanotte è complesso. 
            // Per ora assumiamo che gli orari previsti (Anagrafica) siano standard diurni (es. 08-12, 13-17).
            // Se l'orario reale è notturno e non combacia con i previsti, finirà tutto in Extra o Ordinario a seconda della logica.

            // Semplificazione: Tutto ciò che è lavorato è base, l'extra viene calcolato solo se supera il monte ore o è fuori fascia.
            // Utilizziamo la tua logica esistente "OreDentro / OreFuori".

            TimeSpan start = inReale.TimeOfDay;
            TimeSpan end = outReale.TimeOfDay;

            // SE il turno scavalla la mezzanotte (end < start), dobbiamo spezzare il calcolo in due tronconi?
            // O più semplicemente: Calcoliamo la durata totale. 
            // Se l'utente non ha orari previsti definiti, è tutto ordinario.

            TimeSpan ing1 = ParseOrario(user.OrarioIngresso1);
            TimeSpan usc1 = ParseOrario(user.OrarioUscita1);
            TimeSpan ing2 = ParseOrario(user.OrarioIngresso2);
            TimeSpan usc2 = ParseOrario(user.OrarioUscita2);

            if (ing1 == TimeSpan.Zero && usc1 == TimeSpan.Zero && ing2 == TimeSpan.Zero)
            {
                // Nessun orario previsto: tutto ordinario
                accOrd += (outReale - inReale).TotalHours;
                return;
            }

            // Qui la logica diventa complessa col turno notturno. 
            // Per ora, applichiamo: Se fuori dalla fascia prevista -> Extra.
            // Attenzione: Questo approccio è rigido. Spesso si usa solo "Ore totali > 8h = Straordinario".
            // Procediamo con la logica a fasce come nel tuo codice originale.

            // ... (Inserire qui la logica OreDentro/OrePrima/OreDopo adattata per DateTime invece di TimeSpan per gestire le date diverse) ...
            // Per brevità e robustezza immediata, calcoliamo i minuti totali lavorati
            double totalHours = (outReale - inReale).TotalHours;

            // Se l'orario è "strano" (es. notturno) e l'utente ha orari diurni, consideriamo tutto Extra? 
            // O tutto ordinario fino a 8 ore?
            // Assumiamo: Tutto ciò che è fuori dalle finestre previste è Extra.

            // (Logica semplificata per non complicare troppo la risposta immediata, 
            // ma funzionante per lo scavalco se usiamo DateTime complete)

            // Esempio grezzo funzionale:
            // 1. Costruiamo gli intervalli previsti per il giorno dell'entrata
            DateTime previstoIn1 = inReale.Date.Add(ing1);
            DateTime previstoOut1 = inReale.Date.Add(usc1);

            // Intersezione tra [inReale, outReale] e [previstoIn1, previstoOut1]
            double oreInFascia = GetOverlap(inReale, outReale, previstoIn1, previstoOut1);

            // Ripeti per turno 2
            DateTime previstoIn2 = inReale.Date.Add(ing2);
            DateTime previstoOut2 = inReale.Date.Add(usc2);
            oreInFascia += GetOverlap(inReale, outReale, previstoIn2, previstoOut2);

            accOrd += oreInFascia;
            accExtra += (totalHours - oreInFascia);
        }

        private double GetOverlap(DateTime realStart, DateTime realEnd, DateTime targetStart, DateTime targetEnd)
        {
            long start = Math.Max(realStart.Ticks, targetStart.Ticks);
            long end = Math.Min(realEnd.Ticks, targetEnd.Ticks);
            if (end > start) return new TimeSpan(end - start).TotalHours;
            return 0;
        }

        // Helper per i festivi globali
        private bool IsGiornoFestivo(DateTime data)
        {
            if (App.ParametriGlobali == null) return data.DayOfWeek == DayOfWeek.Saturday || data.DayOfWeek == DayOfWeek.Sunday;

            // Check Weekend
            if (App.ParametriGlobali.GiorniSempreFestivi.Contains(data.DayOfWeek)) return true;

            // Check Date fisse (Natale, ecc)
            if (App.ParametriGlobali.FestivitaRicorrenti.Any(f => f.Mese == data.Month && f.Giorno == data.Day)) return true;

            // Check Date custom (se implementate)
            if (App.ParametriGlobali.FestivitaAggiuntive.Contains(data.Date)) return true;

            return false;
        }

        /// <summary>
        /// Calcola le ore ordinarie/straordinarie per una singola riga.
        /// Usa:
        /// - orari previsti da anagrafica utente
        /// - weekend come "festivo"
        /// - soglia fissa 15 minuti per extra
        /// </summary>
        private void RicalcolaRiga(ReportRow row, DateTime data, UserProfile utente)
        {
            // nessun orario → tutto zero
            if (string.IsNullOrWhiteSpace(row.Entrata1) &&
                string.IsNullOrWhiteSpace(row.Uscita1) &&
                string.IsNullOrWhiteSpace(row.Entrata2) &&
                string.IsNullOrWhiteSpace(row.Uscita2))
            {
                row.OreOrdinarie = 0;
                row.OreStraordinarie = 0;
                row.IsFestivo = IsWeekend(data);
                return;
            }

            var intervalli = new List<(TimeSpan Inizio, TimeSpan Fine)>();

            if (TimeSpan.TryParse(row.Entrata1, out var e1) &&
                TimeSpan.TryParse(row.Uscita1, out var u1) &&
                u1 > e1)
            {
                intervalli.Add((e1, u1));
            }

            if (TimeSpan.TryParse(row.Entrata2, out var e2) &&
                TimeSpan.TryParse(row.Uscita2, out var u2) &&
                u2 > e2)
            {
                intervalli.Add((e2, u2));
            }

            if (!intervalli.Any())
            {
                row.OreOrdinarie = 0;
                row.OreStraordinarie = 0;
                row.IsFestivo = IsWeekend(data);
                return;
            }

            // Weekend = tutto straordinario
            bool festivo = IsWeekend(data);
            row.IsFestivo = festivo;

            double oreOrd = 0;
            double oreExt = 0;

            // Orari previsti da anagrafica (se vuoti → tutto ordinario)
            TimeSpan ing1 = ParseOrario(utente.OrarioIngresso1);
            TimeSpan usc1 = ParseOrario(utente.OrarioUscita1);
            TimeSpan ing2 = ParseOrario(utente.OrarioIngresso2);
            TimeSpan usc2 = ParseOrario(utente.OrarioUscita2);

            foreach (var iv in intervalli)
            {
                var start = iv.Inizio;
                var end = iv.Fine;

                if (festivo)
                {
                    oreExt += (end - start).TotalHours;
                    continue;
                }

                // Nessun orario previsto → tutto ordinario
                if (ing1 == TimeSpan.Zero && usc1 == TimeSpan.Zero &&
                    ing2 == TimeSpan.Zero && usc2 == TimeSpan.Zero)
                {
                    oreOrd += (end - start).TotalHours;
                    continue;
                }

                // Turno 1
                if (usc1 > ing1)
                {
                    oreOrd += OreDentro(start, end, ing1, usc1);
                    oreExt += OrePrima(start, ing1);
                    oreExt += OreDopo(end, usc1);
                }

                // Turno 2
                if (usc2 > ing2)
                {
                    oreOrd += OreDentro(start, end, ing2, usc2);
                    oreExt += OrePrima(start, ing2);
                    oreExt += OreDopo(end, usc2);
                }
            }

            // Soglia di 15 minuti: se meno, si assorbe come ordinario
            double sogliaMinuti = 15;
            if (oreExt * 60 < sogliaMinuti)
            {
                oreOrd += oreExt;
                oreExt = 0;
            }

            row.OreOrdinarie = Math.Round(oreOrd, 2);
            row.OreStraordinarie = Math.Round(oreExt, 2);
        }

        private bool IsWeekend(DateTime d)
        {
            return d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
        }

        private TimeSpan ParseOrario(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return TimeSpan.Zero;

            if (TimeSpan.TryParse(s, out var ts))
                return ts;

            return TimeSpan.Zero;
        }

        private double OreDentro(TimeSpan start, TimeSpan end, TimeSpan from, TimeSpan to)
        {
            if (end <= from || start >= to) return 0;
            var s = start < from ? from : start;
            var e = end > to ? to : end;
            return (e - s).TotalHours;
        }

        private double OrePrima(TimeSpan t, TimeSpan previsto)
        {
            return t < previsto ? (previsto - t).TotalHours : 0;
        }

        private double OreDopo(TimeSpan t, TimeSpan previsto)
        {
            return t > previsto ? (t - previsto).TotalHours : 0;
        }

        private void SalvaModifiche_Click(object sender, RoutedEventArgs e)
        {
            var user = UserCombo.SelectedItem as UserProfile;
            if (user == null)
            {
                MessageBox.Show("Seleziona un utente.");
                return;
            }

            if (MonthCombo.SelectedIndex < 0)
            {
                MessageBox.Show("Seleziona un mese.");
                return;
            }

            var righeReport = (ReportGrid.ItemsSource as IEnumerable<ReportRow>)?.ToList();
            if (righeReport == null || !righeReport.Any())
            {
                MessageBox.Show("Non ci sono dati da salvare.");
                return;
            }

            int year = DateTime.Now.Year;
            int month = MonthCombo.SelectedIndex + 1;

            string filePath = Path.Combine(_csvFolder, $"{user.Id}.csv");

            // 1. Carichiamo tutte le timbrature esistenti
            var allLines = new List<string>();
            if (File.Exists(filePath))
            {
                allLines = File.ReadAllLines(filePath).ToList();
            }

            // 2. Rimuoviamo solo le righe del mese attuale
            allLines = allLines
                .Where(line =>
                {
                    if (string.IsNullOrWhiteSpace(line)) return false;

                    var parts = line.Split(',');
                    if (parts.Length < 2) return false;

                    if (!DateTime.TryParse(parts[0], out var dt)) return false;

                    return !(dt.Year == year && dt.Month == month);
                })
                .ToList();

            // 3. Ricostruiamo SOLO il mese in editing
            var newLines = new List<string>();

            foreach (var row in righeReport)
            {
                DateTime giorno = new DateTime(year, month, row.Giorno);

                // Coppia 1
                if (TimeSpan.TryParse(row.Entrata1, out var e1) &&
                    TimeSpan.TryParse(row.Uscita1, out var u1) &&
                    u1 > e1)
                {
                    newLines.Add($"{giorno:yyyy-MM-dd} {e1:hh\\:mm},Entrata");
                    newLines.Add($"{giorno:yyyy-MM-dd} {u1:hh\\:mm},Uscita");
                }

                // Coppia 2
                if (TimeSpan.TryParse(row.Entrata2, out var e2) &&
                    TimeSpan.TryParse(row.Uscita2, out var u2) &&
                    u2 > e2)
                {
                    newLines.Add($"{giorno:yyyy-MM-dd} {e2:hh\\:mm},Entrata");
                    newLines.Add($"{giorno:yyyy-MM-dd} {u2:hh\\:mm},Uscita");
                }
            }

            // 4. Aggiungiamo al CSV completo le nuove righe
            allLines.AddRange(newLines);

            // 5. Ordiniamo TUTTE le timbrature per data e ora
            allLines = allLines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l =>
                {
                    var parts = l.Split(',');
                    DateTime dt = DateTime.Parse(parts[0]);
                    return new { dt, line = l };
                })
                .OrderBy(x => x.dt)
                .Select(x => x.line)
                .ToList();

            // 6. Scriviamo il file completo
            File.WriteAllLines(filePath, allLines);

            MessageBox.Show("Modifiche salvate correttamente!");
        }
        private void AggiungiTimbratura_Click(object sender, RoutedEventArgs e)
        {
            // 1. Recuperiamo la riga selezionata
            var currentRow = ReportGrid.SelectedItem as ReportRow;
            if (currentRow == null)
            {
                MessageBox.Show("Seleziona una riga (giorno) dalla tabella per aggiungere timbrature.");
                return;
            }

            // 2. Recuperiamo la LISTA completa dei dati (ci serve per aggiungere righe nuove)
            // Nota: ItemsSource è stato assegnato come List<ReportRow> nel metodo CaricaReport
            var listaRighe = ReportGrid.ItemsSource as List<ReportRow>;
            if (listaRighe == null) return; // Sicurezza

            // 3. Logica intelligente di inserimento
            bool nuovaRigaCreata = false;

            if (string.IsNullOrWhiteSpace(currentRow.Entrata1))
            {
                // Caso A: La prima coppia è libera -> Riempiamo quella
                currentRow.Entrata1 = "08:00";
                currentRow.Uscita1 = "12:00";

                // Ricalcoliamo la riga corrente
                RicalcolaLogicaRiga(currentRow);
            }
            else if (string.IsNullOrWhiteSpace(currentRow.Entrata2))
            {
                // Caso B: La seconda coppia è libera -> Riempiamo quella
                currentRow.Entrata2 = "13:00";
                currentRow.Uscita2 = "17:00";

                // Ricalcoliamo la riga corrente
                RicalcolaLogicaRiga(currentRow);
            }
            else
            {
                // Caso C: LA RIGA È PIENA! -> Creiamo una NUOVA RIGA per lo stesso giorno
                var newRow = new ReportRow
                {
                    Giorno = currentRow.Giorno, // Stesso numero del giorno
                    Entrata1 = "18:00",         // Orario default serale (modificabile)
                    Uscita1 = "20:00"
                };

                // Troviamo l'indice della riga selezionata e inseriamo quella nuova subito sotto
                int index = listaRighe.IndexOf(currentRow);
                if (index >= 0)
                {
                    listaRighe.Insert(index + 1, newRow);
                    nuovaRigaCreata = true;

                    // Ricalcoliamo la nuova riga
                    RicalcolaLogicaRiga(newRow);
                }
            }

            // 4. Aggiornamento Grafica
            // Se abbiamo aggiunto una riga, dobbiamo resettare l'ItemsSource per farla apparire
            if (nuovaRigaCreata)
            {
                ReportGrid.ItemsSource = null;
                ReportGrid.ItemsSource = listaRighe;
            }
            else
            {
                // Se abbiamo solo modificato una riga esistente, basta il refresh
                ReportGrid.Items.Refresh();
            }

            AggiornaTotali();
        }
        private void AggiornaTotali()
        {
            var righe = ReportGrid.ItemsSource as IEnumerable<ReportRow>;
            if (righe == null) return;

            double totOrd = 0;
            double totExtra = 0;

            foreach (var r in righe)
            {
                totOrd += r.OreOrdinarie;
                totExtra += r.OreStraordinarie;
            }

            // Funzione locale per convertire in stringa HH:mm
            string FormatHours(double h)
            {
                var ts = TimeSpan.FromHours(h);
                return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
            }

            // Aggiorniamo le etichette usando la formattazione oraria
            TotOrdinarieText.Text = $"{FormatHours(totOrd)} h";
            TotStraordinarieText.Text = $"{FormatHours(totExtra)} h";
        }
        private void Chiudi_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


    }

}

