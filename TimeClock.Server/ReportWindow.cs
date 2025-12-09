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

        public double OreOrdinarie { get; set; }
        public double OreStraordinarie { get; set; }

        public bool IsFestivo { get; set; }
        public string? Note { get; set; }
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

            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Cartella CSV non impostata.");
                return;
            }

            string userFile = Path.Combine(_csvFolder, $"{user.Id}.csv");
            if (!File.Exists(userFile))
            {
                MessageBox.Show($"Nessun file di timbrature trovato per l'utente {user.Nome} {user.Cognome}.");
                ReportGrid.ItemsSource = new List<ReportRow>();
                return;
            }

            // Carica timbrature dal CSV
            var repo = new CsvRepository();
            var entries = new List<TimeCardEntry>();

            foreach (var row in repo.Load(userFile))
            {
                if (row.Length < 2)
                    continue;

                if (!DateTime.TryParse(row[0], out var dt))
                    continue;

                PunchType tipo;
                if (!Enum.TryParse(row[1], true, out tipo))
                    tipo = PunchType.Entrata;

                entries.Add(new TimeCardEntry
                {
                    UserId = user.Id,
                    DataOra = dt,
                    Tipo = tipo
                });
            }

            // Filtra il mese richiesto
            var monthEntries = entries
                .Where(e2 => e2.DataOra.Year == year && e2.DataOra.Month == month)
                .OrderBy(e2 => e2.DataOra)
                .ToList();

            int daysInMonth = DateTime.DaysInMonth(year, month);
            var righe = new List<ReportRow>();

            for (int day = 1; day <= daysInMonth; day++)
            {
                DateTime data = new DateTime(year, month, day);
                var dayEntries = monthEntries
                    .Where(e2 => e2.DataOra.Date == data.Date)
                    .OrderBy(e2 => e2.DataOra)
                    .ToList();

                var reportRow = new ReportRow
                {
                    Giorno = day
                };

                // Costruisci massimo 2 intervalli (Entrata1/Uscita1, Entrata2/Uscita2)
                var coppie = CostruisciCoppie(dayEntries);

                if (coppie.Count > 0)
                {
                    var c1 = coppie[0];
                    reportRow.Entrata1 = c1.Ingresso.ToString("HH:mm");
                    reportRow.Uscita1 = c1.Uscita.ToString("HH:mm");
                }

                if (coppie.Count > 1)
                {
                    var c2 = coppie[1];
                    reportRow.Entrata2 = c2.Ingresso.ToString("HH:mm");
                    reportRow.Uscita2 = c2.Uscita.ToString("HH:mm");
                }

                // Calcola ore ordinarie/extra in base agli orari previsti utente
                RicalcolaRiga(reportRow, data, user);

                righe.Add(reportRow);
            }

            ReportGrid.ItemsSource = righe;
        }

        // ===========================
        //   RICALCOLA REPORT
        // ===========================
        private void RicalcolaReport_Click(object sender, RoutedEventArgs e)
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

            int month = MonthCombo.SelectedIndex + 1;
            int year = DateTime.Now.Year;

            var righeEnumerable = ReportGrid.ItemsSource as IEnumerable<ReportRow>;
            if (righeEnumerable == null)
                return;

            var righe = righeEnumerable.ToList();

            foreach (var r in righe)
            {
                var data = new DateTime(year, month, r.Giorno);
                RicalcolaRiga(r, data, user);
            }

            ReportGrid.ItemsSource = null;
            ReportGrid.ItemsSource = righe;
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
        private List<(DateTime Ingresso, DateTime Uscita)> CostruisciCoppie(List<TimeCardEntry> dayEntries)
        {
            var result = new List<(DateTime Ingresso, DateTime Uscita)>();
            DateTime? lastIn = null;

            foreach (var e in dayEntries)
            {
                if (e.Tipo == PunchType.Entrata)
                {
                    lastIn = e.DataOra;
                }
                else if (e.Tipo == PunchType.Uscita && lastIn != null)
                {
                    if (e.DataOra > lastIn.Value)
                    {
                        result.Add((lastIn.Value, e.DataOra));
                    }
                    lastIn = null;
                }
            }

            return result;
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

            var righe = (ReportGrid.ItemsSource as List<ReportRow>);
            if (righe == null)
                return;

            // Prendi la riga selezionata
            var row = ReportGrid.SelectedItem as ReportRow;
            if (row == null)
            {
                MessageBox.Show("Seleziona una riga (giorno) dalla tabella.");
                return;
            }

            // Se la riga non ha coppie → crea la prima
            if (string.IsNullOrWhiteSpace(row.Entrata1))
            {
                row.Entrata1 = "08:00";
                row.Uscita1 = "12:00";
            }
            else if (string.IsNullOrWhiteSpace(row.Entrata2))
            {
                row.Entrata2 = "13:00";
                row.Uscita2 = "17:00";
            }
            else
            {
                MessageBox.Show("Questo giorno ha già due coppie di timbrature.");
                return;
            }

            // Ricalcola la riga
            int month = MonthCombo.SelectedIndex + 1;
            int year = DateTime.Now.Year;
            DateTime data = new DateTime(year, month, row.Giorno);
            RicalcolaRiga(row, data, user);

            // Refresh DataGrid
            ReportGrid.ItemsSource = null;
            ReportGrid.ItemsSource = righe;
        }


    }

}

