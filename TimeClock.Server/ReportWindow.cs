using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TimeClock.Core.Models;

namespace TimeClock.Server
{
    public partial class ReportWindow : Window
    {
        private readonly string _csvFolder;
        private readonly List<UserProfile> _users;
        private readonly UserExtrasRepository? _extrasRepo;

        private readonly HashSet<DateTime> _festivitaCsv = new();

        private List<ReportRow> _righe = new();

        private int _loadedYear;
        private int _loadedMonth;
        private string _loadedUserId = "";

        public ReportWindow(string csvFolder, List<UserProfile> users)
        {
            InitializeComponent();

            _csvFolder = csvFolder;
            _users = users ?? new List<UserProfile>();
            _extrasRepo = Directory.Exists(_csvFolder) ? new UserExtrasRepository(_csvFolder) : null;

            CaricaFestivitaCsv();
            PopolaCombo();
        }

        private void PopolaCombo()
        {
            // MonthCombo in XAML contiene già i 12 mesi: selezioniamo il mese corrente
            MonthCombo.SelectedIndex = Math.Max(0, DateTime.Today.Month - 1);

            var users = _users
                .OrderBy(u => u.SequenceNumber)
                .Select(u => new UserItem(u))
                .ToList();

            UserCombo.ItemsSource = users;
            UserCombo.DisplayMemberPath = nameof(UserItem.FullName);
            if (users.Count > 0) UserCombo.SelectedIndex = 0;
        }

        private void CaricaFestivitaCsv()
        {
            _festivitaCsv.Clear();

            try
            {
                var path = Path.Combine(_csvFolder, "festivita.csv");
                if (!File.Exists(path)) return;

                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("#")) continue;

                    var parts = line.Split(';');
                    if (parts.Length < 1) continue;

                    if (DateTime.TryParse(parts[0].Trim(), out var dt))
                        _festivitaCsv.Add(dt.Date);
                }
            }
            catch
            {
                // non bloccare il report se le festività non si caricano
            }
        }

        private void Chiudi_Click(object sender, RoutedEventArgs e) => Close();

        private void CaricaReport_Click(object sender, RoutedEventArgs e)
        {
            if (UserCombo.SelectedItem is not UserItem userItem)
            {
                MessageBox.Show("Seleziona un utente.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MonthCombo.SelectedIndex < 0)
            {
                MessageBox.Show("Seleziona un mese.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var user = userItem.User;
            int month = MonthCombo.SelectedIndex + 1;

            // anno: scegliamo l'occorrenza più recente del mese selezionato (non nel futuro)
            int year = DateTime.Today.Year;
            if (month > DateTime.Today.Month) year--;

            _loadedYear = year;
            _loadedMonth = month;
            _loadedUserId = user.Id;

            var start = new DateTime(year, month, 1);
            var endPlusBuffer = start.AddMonths(1).AddDays(2); // buffer: +2 giorni per turni notturni

            // Legge timbrature utente
           //var userFile = Path.Combine(_csvFolder, $"timbrature_{user.Id}.csv");
            var userFile = Path.Combine(_csvFolder, $"{user.Id}.csv");

            if (!File.Exists(userFile))
            {
                MessageBox.Show($"File timbrature non trovato:\n{userFile}", "Mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
                ReportGrid.ItemsSource = null;
                _righe = new List<ReportRow>();
                AggiornaTotali();
                return;
            }

            var allEntries = new List<TimeCardEntry>();

            try
            {
                foreach (var line in File.ReadLines(userFile))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(';');
                    if (parts.Length < 2) parts = line.Split(',');

                    if (parts.Length < 2) continue;

                    if (!DateTime.TryParse(parts[0].Trim(), out var dt)) continue;
                    if (dt < start || dt >= endPlusBuffer) continue;

                    if (!Enum.TryParse(parts[1].Trim(), true, out PunchType tipo)) continue;

                    allEntries.Add(new TimeCardEntry
                    {
                        UserId = user.Id,
                        DataOra = dt,
                        Tipo = tipo
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel parsing timbrature:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            allEntries = allEntries.OrderBy(e2 => e2.DataOra).ToList();
            var pairs = CostruisciCoppieGlobali(allEntries);

            // consideriamo solo i turni che iniziano nel mese selezionato
            pairs = pairs
                .Where(p => p.Ingresso.Date >= start.Date && p.Ingresso.Date < start.AddMonths(1).Date)
                .ToList();

            // costruzione righe report: una riga per ogni giorno (anche se vuoto)
            var rows = new List<ReportRow>();
            int daysInMonth = DateTime.DaysInMonth(year, month);

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                var dayPairs = pairs.Where(p => p.Ingresso.Date == date.Date).ToList();

                if (dayPairs.Count == 0)
                {
                    var r0 = new ReportRow(date) { Giorno = day };
                    r0.IsFestivo = IsGiornoFestivo(date);
                    RicalcolaRiga(r0, user);
                    rows.Add(r0);
                    continue;
                }

                // max 2 coppie per riga; se più, crea più righe
                for (int i = 0; i < dayPairs.Count; i += 2)
                {
                    var r = new ReportRow(date) { Giorno = day };

                    r.Entrata1 = dayPairs[i].Ingresso;
                    r.Uscita1 = dayPairs[i].Uscita;

                    if (i + 1 < dayPairs.Count)
                    {
                        r.Entrata2 = dayPairs[i + 1].Ingresso;
                        r.Uscita2 = dayPairs[i + 1].Uscita;
                    }

                    r.IsFestivo = IsGiornoFestivo(date);
                    RicalcolaRiga(r, user);
                    rows.Add(r);
                }
            }

            _righe = rows;
            ReportGrid.ItemsSource = _righe;
            AggiornaTotali();
        }

        private void ReportGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // la riga è ancora in edit; ricalcoliamo dopo che l'edit è stato committato
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (UserCombo.SelectedItem is not UserItem userItem) return;
                if (e.Row?.Item is not ReportRow row) return;

                RicalcolaRiga(row, userItem.User);
                ReportGrid.Items.Refresh();
                AggiornaTotali();
            }), DispatcherPriority.Background);
        }

        // ===========================
        //   SALVA MODIFICHE
        // ===========================
        private void SalvaModifiche_Click(object sender, RoutedEventArgs e)
        {
            if (_righe.Count == 0 || string.IsNullOrWhiteSpace(_loadedUserId) || _loadedMonth == 0 || _loadedYear == 0)
            {
                MessageBox.Show("Carica prima un report.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var outFile = Path.Combine(_csvFolder, $"report_{_loadedUserId}_{_loadedYear:D4}-{_loadedMonth:D2}.csv");

                var lines = new List<string>
                {
                    "Data;Entrata1;Uscita1;Entrata2;Uscita2;Festivo;OreOrdinarie;OreStraordinarie;Note"
                };

                foreach (var r in _righe)
                {
                    lines.Add(string.Join(";", new[]
                    {
                        r.Data.ToString("yyyy-MM-dd"),
                        r.Entrata1?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        r.Uscita1?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        r.Entrata2?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        r.Uscita2?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        r.IsFestivo ? "1" : "0",
                        r.OreOrdinarieVisual,
                        r.OreStraordinarieVisual,
                        (r.Note ?? "").Replace(";", ",")
                    }));
                }

                File.WriteAllLines(outFile, lines, Encoding.UTF8);
                MessageBox.Show($"Modifiche salvate in:\n{outFile}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel salvataggio:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===========================
        //   AGGIUNGI TIMBRATURA
        // ===========================
        private void AggiungiTimbratura_Click(object sender, RoutedEventArgs e)
        {
            if (UserCombo.SelectedItem is not UserItem userItem)
            {
                MessageBox.Show("Seleziona un utente.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var user = userItem.User;

            var (ok, dt, tipo) = ShowAddPunchDialog();
            if (!ok) return;

            try
            {
               // var userFile = Path.Combine(_csvFolder, $"timbrature_{user.Id}.csv");
               // var userFile = Path.Combine(_csvFolder, "{user.Id}" + ".csv");
                var userFile = Path.Combine(_csvFolder, $"{user.Id}.csv");


                var line = $"{dt:yyyy-MM-dd HH:mm};{tipo}";
                File.AppendAllLines(userFile, new[] { line }, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nell'aggiunta timbratura:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // ricarica il report corrente
            CaricaReport_Click(sender, e);
        }

        private (bool ok, DateTime dt, PunchType tipo) ShowAddPunchDialog()
        {
            var w = new Window
            {
                Title = "Aggiungi timbratura",
                Width = 420,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new TextBlock { Text = "Data:", FontWeight = FontWeights.Bold });
            var dp = new DatePicker { SelectedDate = DateTime.Today, Margin = new Thickness(0, 6, 0, 8) };
            Grid.SetRow(dp, 1);
            root.Children.Add(dp);

            var timeLbl = new TextBlock { Text = "Ora (HH:mm):", FontWeight = FontWeights.Bold };
            Grid.SetRow(timeLbl, 2);
            root.Children.Add(timeLbl);

            var tbTime = new TextBox { Text = "08:00", Margin = new Thickness(0, 6, 0, 8) };
            Grid.SetRow(tbTime, 3);
            root.Children.Add(tbTime);

            var tipoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            tipoPanel.Children.Add(new TextBlock { Text = "Tipo:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            var cbTipo = new ComboBox { Width = 140, Margin = new Thickness(10, 0, 0, 0) };
            cbTipo.Items.Add(PunchType.Entrata);
            cbTipo.Items.Add(PunchType.Uscita);
            cbTipo.SelectedIndex = 0;
            tipoPanel.Children.Add(cbTipo);

            Grid.SetRow(tipoPanel, 4);
            root.Children.Add(tipoPanel);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 10, 8, 0) };
            var cancelBtn = new Button { Content = "Annulla", Width = 90, Margin = new Thickness(0, 10, 0, 0) };

            bool accepted = false;
            okBtn.Click += (_, __) => { accepted = true; w.Close(); };
            cancelBtn.Click += (_, __) => { accepted = false; w.Close(); };

            buttons.Children.Add(okBtn);
            buttons.Children.Add(cancelBtn);

            Grid.SetRow(buttons, 5);
            root.Children.Add(buttons);

            w.Content = root;
            w.ShowDialog();

            if (!accepted || dp.SelectedDate == null) return (false, DateTime.MinValue, PunchType.Entrata);

            if (!TimeSpan.TryParse(tbTime.Text?.Trim(), CultureInfo.InvariantCulture, out var ts) &&
                !TimeSpan.TryParse(tbTime.Text?.Trim(), CultureInfo.GetCultureInfo("it-IT"), out ts))
            {
                MessageBox.Show("Ora non valida. Usa HH:mm.", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
                return (false, DateTime.MinValue, PunchType.Entrata);
            }

            var dt = dp.SelectedDate.Value.Date.Add(ts);
            var tipo = (cbTipo.SelectedItem is PunchType t) ? t : PunchType.Entrata;

            return (true, dt, tipo);
        }

        // ===========================
        //   ESPORTA TXT
        // ===========================
        private void EsportaTXT_Click(object sender, RoutedEventArgs e)
        {
            if (ReportGrid.ItemsSource is not IEnumerable<ReportRow> righe || !righe.Any())
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
                "Data\tGiorno\tEntrata1\tUscita1\tEntrata2\tUscita2\tOrdinarie\tStraordinarie\tFestivo\tNote"
            };

            foreach (var r in righe)
            {
                lines.Add(string.Join("\t", new[]
                {
                    r.Data.ToString("yyyy-MM-dd"),
                    r.Giorno.ToString(CultureInfo.InvariantCulture),
                    r.Entrata1Visual,
                    r.Uscita1Visual,
                    r.Entrata2Visual,
                    r.Uscita2Visual,
                    r.OreOrdinarieVisual,
                    r.OreStraordinarieVisual,
                    r.IsFestivo ? "SI" : "NO",
                    r.Note ?? ""
                }));
            }

            File.WriteAllLines(dlg.FileName, lines, Encoding.UTF8);
            MessageBox.Show("Esportazione TXT completata.");
        }

        // ===========================
        //   ESPORTA PDF (stub)
        // ===========================
        private void EsportaPDF_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Esportazione PDF non inclusa in questa build (serve una libreria esterna). Usa TXT per ora.");
        }

        // ===========================
        //   CALCOLO ORE
        // ===========================

        private void RicalcolaRiga(ReportRow row, UserProfile user)
        {
            (DateTime In, DateTime Out)? c1 = null;
            (DateTime In, DateTime Out)? c2 = null;

            if (row.Entrata1.HasValue && row.Uscita1.HasValue)
            {
                var i = row.Entrata1.Value;
                var o = row.Uscita1.Value;
                if (o < i) o = o.AddDays(1);
                c1 = (i, o);
            }

            if (row.Entrata2.HasValue && row.Uscita2.HasValue)
            {
                var i = row.Entrata2.Value;
                var o = row.Uscita2.Value;
                if (o < i) o = o.AddDays(1);
                c2 = (i, o);
            }

            CalcolaTotaliRiga(row, user, c1, c2);
        }

        private void CalcolaTotaliRiga(
            ReportRow row,
            UserProfile user,
            (DateTime In, DateTime Out)? c1,
            (DateTime In, DateTime Out)? c2)
        {
            var data = row.Data.Date;

            // aggiorna flag festivo (override manuale solo ON)
            bool festivoGlobale = IsGiornoFestivo(data);
            if (festivoGlobale) row.IsFestivo = true;

            // Durate visuali (senza blocchi)
            row.Durata1Visual = c1.HasValue ? FormatDuration(c1.Value.Out - c1.Value.In) : "";
            row.Durata2Visual = c2.HasValue ? FormatDuration(c2.Value.Out - c2.Value.In) : "";

            // segmenti reali del giorno
            var segments = new List<(DateTime In, DateTime Out)>();
            if (c1.HasValue && c1.Value.Out > c1.Value.In) segments.Add(c1.Value);
            if (c2.HasValue && c2.Value.Out > c2.Value.In) segments.Add(c2.Value);

            int blockMinutes = SnapBlock(App.ParametriGlobali?.SogliaMinutiStraordinario ?? 0);

            // minuti riconosciuti (bloccati)
            int recognizedMinutes = 0;
            foreach (var seg in segments)
                recognizedMinutes += RecognizedMinutes(seg.In, seg.Out, blockMinutes);

            if (recognizedMinutes < 0) recognizedMinutes = 0;

            // se festivo: tutto straordinario
            if (row.IsFestivo)
            {
                row.OreOrdinarie = 0m;
                row.OreStraordinarie = recognizedMinutes / 60m;
                return;
            }

            int dailyQuotaMinutes = GetDailyQuotaMinutes(user);

            // orari previsti (intervalli) – per la penalità a blocchi
            var expected = GetExpectedIntervals(data, user);

            // minuti ordinari \"in orario\" (con recupero fino all'inizio dell'intervallo successivo)
            int ordinaryScheduleMinutes = 0;
            for (int i = 0; i < expected.Count; i++)
            {
                var (start, end) = expected[i];
                var recoveryEnd = (i + 1 < expected.Count) ? expected[i + 1].Start : data.AddDays(1); // stop a mezzanotte
                ordinaryScheduleMinutes += OrdinaryMinutesForInterval(segments, start, end, recoveryEnd, blockMinutes);
            }

            if (ordinaryScheduleMinutes > dailyQuotaMinutes)
                ordinaryScheduleMinutes = dailyQuotaMinutes;

            // \"prima ordinarie poi straordinarie\": il tempo fuori orario può completare il monte ore giornaliero
            int remainingNeed = Math.Max(0, dailyQuotaMinutes - ordinaryScheduleMinutes);
            int remainingRecognized = Math.Max(0, recognizedMinutes - ordinaryScheduleMinutes);

            int ordinaryTotalMinutes = ordinaryScheduleMinutes + Math.Min(remainingNeed, remainingRecognized);
            int extraMinutes = Math.Max(0, recognizedMinutes - ordinaryTotalMinutes);

            row.OreOrdinarie = ordinaryTotalMinutes / 60m;
            row.OreStraordinarie = extraMinutes / 60m;
        }

        private int GetDailyQuotaMinutes(UserProfile user)
        {
            // 1) extras (utenti_extras.json)
            try
            {
                if (_extrasRepo != null)
                {
                    double fallbackOre = (user.OreContrattoSettimanali > 0) ? (user.OreContrattoSettimanali / 5.0) : 8.0;
                    double ore = _extrasRepo.GetOreGiornaliereOrFallback(user, fallbackOre, 5);
                    if (ore > 0)
                        return (int)Math.Round(ore * 60.0);
                }
            }
            catch { /* ignore */ }

            // 2) fallback: ore settimanali / 5
            if (user.OreContrattoSettimanali > 0)
                return (int)Math.Round((user.OreContrattoSettimanali / 5.0) * 60.0);

            // 3) fallback hard
            return 8 * 60;
        }

        private static int SnapBlock(int value)
        {
            int[] allowed = { 0, 15, 30 };
            int best = allowed[0];
            int bestDist = Math.Abs(value - best);

            foreach (var v in allowed)
            {
                int dist = Math.Abs(value - v);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = v;
                }
            }
            return best;
        }

        private static int RecognizedMinutes(DateTime start, DateTime end, int blockMinutes)
        {
            if (end <= start) return 0;
            if (blockMinutes <= 0)
                return (int)Math.Floor((end - start).TotalMinutes);

            var s = RoundUpToBlock(start, blockMinutes);
            var e = RoundDownToBlock(end, blockMinutes);

            if (e <= s) return 0;
            return (int)Math.Floor((e - s).TotalMinutes);
        }

        private static DateTime RoundUpToBlock(DateTime dt, int blockMinutes)
        {
            long blockTicks = TimeSpan.FromMinutes(blockMinutes).Ticks;
            long remainder = dt.Ticks % blockTicks;
            if (remainder == 0) return dt;
            return new DateTime(dt.Ticks + (blockTicks - remainder), dt.Kind);
        }

        private static DateTime RoundDownToBlock(DateTime dt, int blockMinutes)
        {
            long blockTicks = TimeSpan.FromMinutes(blockMinutes).Ticks;
            long remainder = dt.Ticks % blockTicks;
            return new DateTime(dt.Ticks - remainder, dt.Kind);
        }

        private static int OrdinaryMinutesForInterval(
            List<(DateTime In, DateTime Out)> segments,
            DateTime expectedStart,
            DateTime expectedEnd,
            DateTime recoveryEnd,
            int blockMinutes)
        {
            if (expectedEnd <= expectedStart) expectedEnd = expectedEnd.AddDays(1);
            if (recoveryEnd <= expectedStart) recoveryEnd = recoveryEnd.AddDays(1);

            int expectedMinutes = (int)Math.Floor((expectedEnd - expectedStart).TotalMinutes);
            if (expectedMinutes <= 0) return 0;

            // clip segmenti a [expectedStart, recoveryEnd]
            var clipped = new List<(DateTime In, DateTime Out)>();
            foreach (var seg in segments)
            {
                var a = seg.In < expectedStart ? expectedStart : seg.In;
                var b = seg.Out > recoveryEnd ? recoveryEnd : seg.Out;
                if (b > a) clipped.Add((a, b));
            }

            if (blockMinutes <= 0)
            {
                // senza blocchi: somma overlap con la finestra prevista (senza recupero fuori finestra)
                int tot = 0;
                foreach (var seg in clipped)
                {
                    var a = seg.In < expectedStart ? expectedStart : seg.In;
                    var b = seg.Out > expectedEnd ? expectedEnd : seg.Out;
                    if (b > a)
                        tot += (int)Math.Floor((b - a).TotalMinutes);
                }
                return Math.Min(tot, expectedMinutes);
            }

            int expectedBlocks = expectedMinutes / blockMinutes;
            if (expectedBlocks <= 0) return 0;

            var workedBlocks = new HashSet<int>();

            foreach (var seg in clipped)
            {
                double startOffset = (seg.In - expectedStart).TotalMinutes;
                double endOffset = (seg.Out - expectedStart).TotalMinutes;

                int startIndex = (int)Math.Ceiling(startOffset / blockMinutes);
                int endIndexExclusive = (int)Math.Floor(endOffset / blockMinutes);

                for (int i = startIndex; i < endIndexExclusive; i++)
                {
                    if (i >= 0) workedBlocks.Add(i);
                }
            }

            return Math.Min(expectedBlocks, workedBlocks.Count) * blockMinutes;
        }

        private List<(DateTime Start, DateTime End)> GetExpectedIntervals(DateTime day, UserProfile user)
        {
            var list = new List<(DateTime Start, DateTime End)>();

            if (TryParseTimeOnDate(user.OrarioIngresso1, day, out var s1) &&
                TryParseTimeOnDate(user.OrarioUscita1, day, out var e1))
            {
                if (e1 <= s1) e1 = e1.AddDays(1);
                list.Add((s1, e1));
            }

            if (TryParseTimeOnDate(user.OrarioIngresso2, day, out var s2) &&
                TryParseTimeOnDate(user.OrarioUscita2, day, out var e2))
            {
                if (e2 <= s2) e2 = e2.AddDays(1);
                list.Add((s2, e2));
            }

            return list.OrderBy(x => x.Start).ToList();
        }

        private static bool TryParseTimeOnDate(string hhmm, DateTime day, out DateTime dt)
        {
            dt = default;

            if (string.IsNullOrWhiteSpace(hhmm))
                return false;

            if (!TimeSpan.TryParse(hhmm.Trim(), CultureInfo.InvariantCulture, out var ts) &&
                !TimeSpan.TryParse(hhmm.Trim(), CultureInfo.GetCultureInfo("it-IT"), out ts))
            {
                return false;
            }

            dt = day.Date.Add(ts);
            return true;
        }

        private bool IsGiornoFestivo(DateTime date)
        {
            date = date.Date;

            var p = App.ParametriGlobali;

            // 1) giorni sempre festivi (sab/dom se spuntati)
            if (p?.GiorniSempreFestivi != null && p.GiorniSempreFestivi.Contains(date.DayOfWeek))
                return true;

            // 2) festività ricorrenti (mese/giorno)
            if (p?.FestivitaRicorrenti != null && p.FestivitaRicorrenti.Any(f => f.Mese == date.Month && f.Giorno == date.Day))
                return true;

            // 3) festività aggiuntive da parametri_straordinari.json
            if (p?.FestivitaAggiuntive != null && p.FestivitaAggiuntive.Any(d => d.Date == date))
                return true;

            // 4) festività da CSV in cartella
            if (_festivitaCsv.Contains(date))
                return true;

            return false;
        }

        // ===========================
        //   SUPPORTO UI
        // ===========================

        private void AggiornaTotali()
        {
            decimal totOrd = 0m;
            decimal totExtra = 0m;

            foreach (var r in _righe)
            {
                totOrd += r.OreOrdinarie;
                totExtra += r.OreStraordinarie;
            }

            TotOrdinarieText.Text = FormatHoursAsHHmm(totOrd);
            TotStraordinarieText.Text = FormatHoursAsHHmm(totExtra);
        }

        private static string FormatDuration(TimeSpan ts)
        {
            int totalMinutes = (int)Math.Round(ts.TotalMinutes);
            int h = totalMinutes / 60;
            int m = Math.Abs(totalMinutes % 60);
            return $"{h:00}:{m:00}";
        }

        private static string FormatHoursAsHHmm(decimal hours)
        {
            int totalMinutes = (int)Math.Round(hours * 60m);
            int h = totalMinutes / 60;
            int m = Math.Abs(totalMinutes % 60);
            return $"{h:00}:{m:00}";
        }

        /// <summary>
        /// Costruisce coppie Entrata/Uscita in ordine cronologico, ignorando uscite orfane.
        /// </summary>
        private static List<(DateTime Ingresso, DateTime Uscita)> CostruisciCoppieGlobali(List<TimeCardEntry> entries)
        {
            var result = new List<(DateTime Ingresso, DateTime Uscita)>();
            DateTime? lastIngresso = null;

            foreach (var e in entries.OrderBy(x => x.DataOra))
            {
                if (e.Tipo == PunchType.Entrata)
                {
                    lastIngresso = e.DataOra;
                }
                else if (e.Tipo == PunchType.Uscita)
                {
                    if (lastIngresso.HasValue && e.DataOra > lastIngresso.Value)
                    {
                        result.Add((lastIngresso.Value, e.DataOra));
                        lastIngresso = null;
                    }
                }
            }

            return result;
        }

        // ---------------------------
        // Helper per ComboBox utenti
        // ---------------------------
        private sealed class UserItem
        {
            public UserProfile User { get; }
            public string FullName => $"{User.Nome} {User.Cognome} ({User.Id})";
            public UserItem(UserProfile u) => User = u;
            public override string ToString() => FullName;
        }

        // ---------------------------
        // Riga Report
        // ---------------------------
        public class ReportRow
        {
            public int Giorno { get; set; }
            public DateTime Data { get; }

            public DateTime? Entrata1 { get; set; }
            public DateTime? Uscita1 { get; set; }
            public DateTime? Entrata2 { get; set; }
            public DateTime? Uscita2 { get; set; }

            public string Entrata1Visual => Entrata1?.ToString("HH:mm:ss") ?? "";
            public string Uscita1Visual => Uscita1?.ToString("HH:mm:ss") ?? "";
            public string Entrata2Visual => Entrata2?.ToString("HH:mm:ss") ?? "";
            public string Uscita2Visual => Uscita2?.ToString("HH:mm:ss") ?? "";

            public string Durata1Visual { get; set; } = "";
            public string Durata2Visual { get; set; } = "";

            public bool IsFestivo { get; set; }
            public string Note { get; set; } = "";

            public decimal OreOrdinarie { get; set; }
            public decimal OreStraordinarie { get; set; }

            public string OreOrdinarieVisual => FormatMinutes((int)Math.Round(OreOrdinarie * 60m));
            public string OreStraordinarieVisual => FormatMinutes((int)Math.Round(OreStraordinarie * 60m));

            public ReportRow(DateTime date) => Data = date.Date;

            private static string FormatMinutes(int minutes)
            {
                if (minutes < 0) minutes = 0;
                int h = minutes / 60;
                int m = minutes % 60;
                return $"{h:00}:{m:00}";
            }
        }
    }
}
