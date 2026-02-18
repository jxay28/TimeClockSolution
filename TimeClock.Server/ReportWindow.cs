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

        // Timbrature
        public string? Entrata1 { get; set; }
        public string? Uscita1 { get; set; }
        public string? Entrata2 { get; set; }
        public string? Uscita2 { get; set; }

        // --- MODIFICA: Queste verranno popolate dal metodo di calcolo ---
        public string? Durata1Visual { get; set; }
        public string? Durata2Visual { get; set; }

        // Totali matematici
        public double OreOrdinarie { get; set; }
        public double OreStraordinarie { get; set; }

        public bool IsFestivo { get; set; }
        public string? Note { get; set; }

        // Proprietà visuali per la griglia (Ore totali)
        public string OreOrdinarieVisual => ConvertiDecimaliInOre(OreOrdinarie);
        public string OreStraordinarieVisual => ConvertiDecimaliInOre(OreStraordinarie);

        private string ConvertiDecimaliInOre(double oreDecimali)
        {
            var ts = TimeSpan.FromHours(oreDecimali);
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
        }
    }



    public partial class ReportWindow : Window
    {
        private readonly string _csvFolder;
        private readonly List<UserProfile> _users;
        private readonly WorkTimeCalculator _workTimeCalculator = new();

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

            // Popola e seleziona anno corrente
            int currentYear = DateTime.Now.Year;
            YearCombo.ItemsSource = Enumerable.Range(currentYear - 5, 8).ToList();
            YearCombo.SelectedItem = currentYear;
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
            if (YearCombo.SelectedItem is not int year)
            {
                MessageBox.Show("Seleziona un anno.");
                return;
            }

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
                DateTime firstNextMonth = new DateTime(year, month, 1).AddMonths(1);
                bool isBufferNextMonth = (dt.Date >= firstNextMonth && dt.Date <= firstNextMonth.AddDays(2));

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

            // Assenze (ferie/permesso/malattia)
            var absenceRepo = new AbsenceRepository();
            string assenzePath = Path.Combine(_csvFolder, "assenze.csv");
            var assenzePerGiorno = absenceRepo.Load(assenzePath)
                .Where(a => a.UserId == user.Id && a.Data.Year == year && a.Data.Month == month)
                .GroupBy(a => a.Data.Day)
                .ToDictionary(g => g.Key, g => g.ToList());

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

                    if (assenzePerGiorno.TryGetValue(day, out var assenzeGiorno) && assenzeGiorno.Any())
                    {
                        ApplicaAssenzeAllaRiga(emptyRow, user, assenzeGiorno);
                    }

                    righeReport.Add(emptyRow);
                }
                else
                {
                    var coppieGiorno = gruppiPerGiorno[day];
                    coppieGiorno = coppieGiorno.OrderBy(c => c.Ingresso).ToList();
                    var righeGiorno = new List<ReportRow>();

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

                        if (index < coppieGiorno.Count)
                        {
                            var pair2 = coppieGiorno[index];
                            row.Entrata2 = pair2.Ingresso.ToString("HH:mm");
                            row.Uscita2 = pair2.Uscita.ToString("HH:mm");
                            index++;
                        }

                        righeGiorno.Add(row);
                    }

                    ApplicaCalcoloGiornalieroSuRighe(righeGiorno, dataCorrente, user, coppieGiorno);

                    if (assenzePerGiorno.TryGetValue(day, out var assenzeGiorno) && assenzeGiorno.Any())
                    {
                        const string warning = "ATTENZIONE: presenti assenze registrate e timbrature nello stesso giorno";
                        foreach (var row in righeGiorno)
                        {
                            row.Note = string.IsNullOrWhiteSpace(row.Note)
                                ? warning
                                : row.Note + " | " + warning;
                        }
                    }

                    righeReport.AddRange(righeGiorno);
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
                    // Ricalcoliamo l'intera giornata (se ci sono più righe con lo stesso giorno).
                    RicalcolaLogicaGiorno(row.Giorno);

                    // Aggiorniamo la grafica della riga (necessario per forzare il refresh delle colonne Ordinarie/Extra)

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
        private void RicalcolaLogicaGiorno(int giorno)
        {
            var user = UserCombo.SelectedItem as UserProfile;
            if (user == null || MonthCombo.SelectedIndex < 0 || YearCombo.SelectedItem is not int year) return;
            var righe = (ReportGrid.ItemsSource as IEnumerable<ReportRow>)?.ToList();
            if (righe == null) return;

            int month = MonthCombo.SelectedIndex + 1;
            DateTime dataBase = new DateTime(year, month, giorno);
            var righeGiorno = righe.Where(x => x.Giorno == giorno).ToList();
            if (!righeGiorno.Any()) return;

            var coppieGiorno = EstraiCoppieDaRighe(righeGiorno, dataBase);
            ApplicaCalcoloGiornalieroSuRighe(righeGiorno, dataBase, user, coppieGiorno);
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

            SafeFileWriter.WriteAllLinesAtomic(dlg.FileName, lines);
            MessageBox.Show("Esportazione TXT completata.");
        }

        // ===========================
        //   ESPORTA PDF (stub)
        // ===========================
        private void EsportaPDF_Click(object sender, RoutedEventArgs e)
        {
            var righe = ReportGrid.ItemsSource as IEnumerable<ReportRow>;
            if (righe == null || !righe.Any())
            {
                MessageBox.Show("Non ci sono dati da esportare.");
                return;
            }

            try
            {
                var pd = new PrintDialog();
                if (pd.ShowDialog() == true)
                {
                    pd.PrintVisual(ReportGrid, "Report Mensile TimeClock");
                    MessageBox.Show("Stampa avviata. Per salvare in PDF scegli la stampante 'Microsoft Print to PDF'.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'esportazione PDF: {ex.Message}");
            }
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
            // Pairing formalizzato nel Core (gestione cross-day e regole edge-case in un unico punto).
            return _workTimeCalculator
                .BuildPairsCrossDay(entries)
                .Select(p => (Ingresso: p.In, Uscita: p.Out))
                .ToList();
        }
        private void CalcolaTotaliRiga(ReportRow row, DateTime dataRiferimento, UserProfile user,
                       (DateTime In, DateTime Out)? c1,
                       (DateTime In, DateTime Out)? c2)
        {
            var policy = BuildWorkTimePolicy();

            var pairs = new List<(DateTime In, DateTime Out)>();
            if (c1.HasValue) pairs.Add(c1.Value);
            if (c2.HasValue) pairs.Add(c2.Value);

            var result = _workTimeCalculator.CalculateDay(user, dataRiferimento, pairs, policy);

            row.IsFestivo = result.IsHoliday;
            row.OreOrdinarie = Math.Round(result.OrdinaryMinutes / 60.0, 2);
            row.OreStraordinarie = Math.Round(result.OvertimeMinutes / 60.0, 2);

            row.Durata1Visual = result.Pairs.ElementAtOrDefault(0)?.DurationVisual ?? string.Empty;
            row.Durata2Visual = result.Pairs.ElementAtOrDefault(1)?.DurationVisual ?? string.Empty;

            row.Note = result.Notes.Any()
                ? string.Join(" | ", result.Notes)
                : null;
        }

        private List<(DateTime Ingresso, DateTime Uscita)> EstraiCoppieDaRighe(List<ReportRow> righeGiorno, DateTime dataBase)
        {
            var pairs = new List<(DateTime Ingresso, DateTime Uscita)>();

            foreach (var row in righeGiorno)
            {
                if (TryBuildPair(dataBase, row.Entrata1, row.Uscita1, out var p1))
                    pairs.Add(p1);
                if (TryBuildPair(dataBase, row.Entrata2, row.Uscita2, out var p2))
                    pairs.Add(p2);
            }

            return pairs
                .OrderBy(p => p.Ingresso)
                .ToList();
        }

        private static bool TryBuildPair(DateTime dataBase, string? entrata, string? uscita, out (DateTime Ingresso, DateTime Uscita) pair)
        {
            pair = default;

            if (string.IsNullOrWhiteSpace(entrata) || string.IsNullOrWhiteSpace(uscita))
                return false;

            if (!TimeSpan.TryParse(entrata, out var tIn) || !TimeSpan.TryParse(uscita, out var tOut))
                return false;

            DateTime inDt = dataBase.Add(tIn);
            DateTime outDt = dataBase.Add(tOut);
            if (tOut < tIn)
                outDt = outDt.AddDays(1);

            if (outDt < inDt)
                return false;

            pair = (inDt, outDt);
            return true;
        }

        private void ApplicaCalcoloGiornalieroSuRighe(
            List<ReportRow> righeGiorno,
            DateTime dataRiferimento,
            UserProfile user,
            List<(DateTime Ingresso, DateTime Uscita)> coppieGiorno)
        {
            if (righeGiorno == null || righeGiorno.Count == 0)
                return;

            var policy = BuildWorkTimePolicy();
            var result = _workTimeCalculator.CalculateDay(
                user,
                dataRiferimento,
                coppieGiorno.Select(c => (c.Ingresso, c.Uscita)),
                policy);

            var ordByRow = new int[righeGiorno.Count];
            var extByRow = new int[righeGiorno.Count];

            int ordinaryRemaining = Math.Max(0, result.OrdinaryMinutes);
            int overtimeRemaining = Math.Max(0, result.OvertimeMinutes);

            for (int i = 0; i < result.Pairs.Count; i++)
            {
                int rowIndex = i / 2;
                if (rowIndex >= righeGiorno.Count)
                    break;

                int pairMinutes = Math.Max(0, result.Pairs[i].DurationMinutes);
                int ord = Math.Min(pairMinutes, ordinaryRemaining);
                ordinaryRemaining -= ord;

                int extraPotential = Math.Max(0, pairMinutes - ord);
                int ext = Math.Min(extraPotential, overtimeRemaining);
                overtimeRemaining -= ext;

                ordByRow[rowIndex] += ord;
                extByRow[rowIndex] += ext;
            }

            for (int i = 0; i < righeGiorno.Count; i++)
            {
                var row = righeGiorno[i];
                row.IsFestivo = result.IsHoliday;
                row.OreOrdinarie = Math.Round(ordByRow[i] / 60.0, 2);
                row.OreStraordinarie = Math.Round(extByRow[i] / 60.0, 2);
                row.Durata1Visual = string.Empty;
                row.Durata2Visual = string.Empty;
                row.Note = null;
            }

            for (int i = 0; i < result.Pairs.Count; i++)
            {
                int rowIndex = i / 2;
                if (rowIndex >= righeGiorno.Count)
                    break;

                if (i % 2 == 0)
                    righeGiorno[rowIndex].Durata1Visual = result.Pairs[i].DurationVisual;
                else
                    righeGiorno[rowIndex].Durata2Visual = result.Pairs[i].DurationVisual;
            }

            if (result.Notes.Any())
                righeGiorno[0].Note = string.Join(" | ", result.Notes);
        }

        private WorkTimePolicy BuildWorkTimePolicy()
        {
            var p = App.ParametriGlobali;

            int soglia = p?.SogliaMinutiStraordinario > 0
                ? p.SogliaMinutiStraordinario
                : 15;

            return new WorkTimePolicy
            {
                RoundEntryUp = true,
                RoundExitDown = true,
                RoundingBlockMinutes = soglia,
                OvertimeThresholdMinutes = soglia,
                OvertimeBlockMinutes = soglia,
                DeficitRecoveryBlockMinutes = soglia,
                AlwaysHolidayDays = p?.GiorniSempreFestivi?.ToList() ?? new List<DayOfWeek>
                {
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday
                },
                RecurringHolidays = p?.FestivitaRicorrenti?
                    .Select(f => (Month: f.Mese, Day: f.Giorno))
                    .ToList()
                    ?? new List<(int Month, int Day)>(),
                AdditionalHolidayDates = p?.FestivitaAggiuntive?
                    .Select(d => d.Date)
                    .ToHashSet()
                    ?? new HashSet<DateTime>()
            };
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
            if (App.ParametriGlobali == null)
                return data.DayOfWeek == DayOfWeek.Saturday || data.DayOfWeek == DayOfWeek.Sunday;

            // Weekend configurati da Parametri
            if (App.ParametriGlobali.GiorniSempreFestivi.Contains(data.DayOfWeek))
                return true;

            // Festività ricorrenti custom
            if (App.ParametriGlobali.FestivitaRicorrenti.Any(f => f.Mese == data.Month && f.Giorno == data.Day))
                return true;

            // Festività personalizzate
            if (App.ParametriGlobali.FestivitaAggiuntive.Contains(data.Date))
                return true;

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

        private int CalcolaMinutiPrevisti(UserProfile user)
        {
            // Regola business: ore giornaliere = ore settimanali / 5 (lun-ven).
            int minuti = 0;
            if (user != null && user.OreContrattoSettimanali > 0)
                minuti = (int)Math.Round((user.OreContrattoSettimanali / 5.0) * 60.0, MidpointRounding.AwayFromZero);

            // fallback: 8 ore/giorno
            if (minuti <= 0)
                minuti = 8 * 60;

            return minuti;
        }

        private int CalcolaMinutiPrevistiFascia(UserProfile user, int fascia)
        {
            var inizio = fascia == 1 ? ParseOrario(user.OrarioIngresso1) : ParseOrario(user.OrarioIngresso2);
            var fine = fascia == 1 ? ParseOrario(user.OrarioUscita1) : ParseOrario(user.OrarioUscita2);

            if (fine > inizio)
                return (int)(fine - inizio).TotalMinutes;

            return 0;
        }

        private int ApplicaRecuperoBlocchi(int minuti, int soglia)
        {
            if (minuti <= 0)
                return 0;

            if (soglia <= 0)
                return minuti;

            // Recupero: arrotonda sempre al blocco superiore
            return (int)Math.Ceiling(minuti / (double)soglia) * soglia;
        }

        private int CalcolaStraordinarioBlocchi(int minutiExtra, int soglia)
        {
            if (minutiExtra <= 0)
                return 0;

            if (soglia <= 0)
                return minutiExtra;

            // Straordinario: parte al raggiungimento della soglia
            // e poi conteggia blocchi completi.
            if (minutiExtra < soglia)
                return 0;

            return (minutiExtra / soglia) * soglia;
        }

        private int CalcolaStraordinarioFascia((DateTime In, DateTime Out)? coppia, TimeSpan previstoIn, TimeSpan previstoOut, int soglia)
        {
            if (!coppia.HasValue || previstoOut <= previstoIn)
                return 0;

            var inReale = coppia.Value.In.TimeOfDay;
            var outReale = coppia.Value.Out.TimeOfDay;

            int minutiAnticipo = inReale < previstoIn
                ? (int)Math.Round((previstoIn - inReale).TotalMinutes)
                : 0;

            int minutiPosticipo = outReale > previstoOut
                ? (int)Math.Round((outReale - previstoOut).TotalMinutes)
                : 0;

            return CalcolaStraordinarioBlocchi(minutiAnticipo, soglia)
                 + CalcolaStraordinarioBlocchi(minutiPosticipo, soglia);
        }

        private (DateTime In, DateTime Out)? ArrotondaCoppia((DateTime In, DateTime Out)? coppia, int bloccoMinuti)
        {
            if (!coppia.HasValue)
                return null;

            if (bloccoMinuti <= 0)
                return coppia;

            var entrata = ArrotondaEntrataSu(coppia.Value.In, bloccoMinuti);
            var uscita = ArrotondaUscitaGiu(coppia.Value.Out, bloccoMinuti);

            // Evita durate negative dopo arrotondamento.
            if (uscita < entrata)
                uscita = entrata;

            return (entrata, uscita);
        }

        private DateTime ArrotondaEntrataSu(DateTime value, int bloccoMinuti)
        {
            var minutiDaGiorno = value.Hour * 60 + value.Minute;
            var minutiArrotondati = (int)Math.Ceiling(minutiDaGiorno / (double)bloccoMinuti) * bloccoMinuti;
            return value.Date.AddMinutes(minutiArrotondati);
        }

        private DateTime ArrotondaUscitaGiu(DateTime value, int bloccoMinuti)
        {
            var minutiDaGiorno = value.Hour * 60 + value.Minute;
            var minutiArrotondati = (int)Math.Floor(minutiDaGiorno / (double)bloccoMinuti) * bloccoMinuti;
            return value.Date.AddMinutes(minutiArrotondati);
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

            if (YearCombo.SelectedItem is not int year)
            {
                MessageBox.Show("Seleziona un anno.");
                return;
            }

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

                    var parts = CsvCodec.ParseLine(line);
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

                // Coppia 1 (supporto turni notturni: uscita il giorno successivo)
                if (TimeSpan.TryParse(row.Entrata1, out var e1) &&
                    TimeSpan.TryParse(row.Uscita1, out var u1))
                {
                    DateTime in1 = giorno.Add(e1);
                    DateTime out1 = giorno.Add(u1);
                    if (out1 <= in1)
                        out1 = out1.AddDays(1);

                    newLines.Add(CsvCodec.BuildLine(new[] { in1.ToString("yyyy-MM-dd HH:mm"), "Entrata" }));
                    newLines.Add(CsvCodec.BuildLine(new[] { out1.ToString("yyyy-MM-dd HH:mm"), "Uscita" }));
                }

                // Coppia 2 (supporto turni notturni)
                if (TimeSpan.TryParse(row.Entrata2, out var e2) &&
                    TimeSpan.TryParse(row.Uscita2, out var u2))
                {
                    DateTime in2 = giorno.Add(e2);
                    DateTime out2 = giorno.Add(u2);
                    if (out2 <= in2)
                        out2 = out2.AddDays(1);

                    newLines.Add(CsvCodec.BuildLine(new[] { in2.ToString("yyyy-MM-dd HH:mm"), "Entrata" }));
                    newLines.Add(CsvCodec.BuildLine(new[] { out2.ToString("yyyy-MM-dd HH:mm"), "Uscita" }));
                }
            }

            // 4. Aggiungiamo al CSV completo le nuove righe
            allLines.AddRange(newLines);

            // 5. Ordiniamo TUTTE le timbrature per data e ora
            allLines = allLines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l =>
                {
                    var parts = CsvCodec.ParseLine(l);
                    DateTime dt = DateTime.Parse(parts[0]);
                    return new { dt, line = l };
                })
                .OrderBy(x => x.dt)
                .Select(x => x.line)
                .ToList();

            // 6. Scriviamo il file completo
            SafeFileWriter.WriteAllLinesAtomic(filePath, allLines);
            AuditLogger.Log(_csvFolder, "save_report_edits", $"user={user.Id}; year={year}; month={month}; righe={righeReport.Count}");

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

            var selectedUser = UserCombo.SelectedItem as UserProfile;
            if (selectedUser != null && MonthCombo.SelectedIndex >= 0 && YearCombo.SelectedItem is int selectedYear)
            {
                AuditLogger.Log(_csvFolder, "add_manual_punch", $"user={selectedUser.Id}; day={currentRow.Giorno}; month={MonthCombo.SelectedIndex + 1}; year={selectedYear}");
            }

            AggiornaTotali();
        }
        private void ApplicaAssenzeAllaRiga(ReportRow row, UserProfile user, List<AbsenceRecord> assenze)
        {
            if (assenze == null || assenze.Count == 0)
                return;

            double oreDefault = user.OreContrattoSettimanali > 0
                ? Math.Round(user.OreContrattoSettimanali / 5.0, 2)
                : 8.0;

            double oreTotali = assenze.Sum(a => a.Ore > 0 ? a.Ore : oreDefault);
            row.OreOrdinarie = Math.Round(oreTotali, 2);
            row.OreStraordinarie = 0;

            var tags = assenze
                .Select(a => $"{a.Tipo} ({(a.Ore > 0 ? a.Ore.ToString("0.##", CultureInfo.InvariantCulture) : oreDefault.ToString("0.##", CultureInfo.InvariantCulture))}h)")
                .ToList();

            row.Note = "Assenza: " + string.Join("; ", tags);
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

