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
        public string? Entrata { get; set; }
        public string? Uscita { get; set; }
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
            _users = users;

            UserCombo.ItemsSource = _users;

            int currentMonth = DateTime.Now.Month;
            MonthCombo.SelectedIndex = currentMonth - 1;
        }

        private void CaricaReport_Click(object sender, RoutedEventArgs e)
        {
            var user = UserCombo.SelectedItem as UserProfile;
            if (user == null) return;
            if (MonthCombo.SelectedIndex < 0) return;

            int month = MonthCombo.SelectedIndex + 1;
            int year = DateTime.Now.Year;

            string file = Path.Combine(_csvFolder, $"{user.Id}.csv");
            if (!File.Exists(file))
            {
                ReportGrid.ItemsSource = new List<ReportRow>();
                return;
            }

            var repo = new CsvRepository();
            var entries = new List<TimeCardEntry>();

            foreach (var row in repo.Load(file))
            {
                if (row.Length < 2) continue;
                if (!DateTime.TryParse(row[0], out var dt)) continue;

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

            var monthEntries = entries
                .Where(e2 => e2.DataOra.Year == year && e2.DataOra.Month == month)
                .OrderBy(e2 => e2.DataOra)
                .ToList();

            var coppie = CostruisciCoppieGlobali(monthEntries);

            var righe = new List<ReportRow>();

            foreach (var c in coppie)
            {
                var r = new ReportRow
                {
                    Giorno = c.Ingresso.Day,
                    Entrata = c.Ingresso.ToString("HH:mm"),
                    Uscita = c.Uscita.ToString("HH:mm")
                };

                RicalcolaRiga(r, c.Ingresso.Date, user);
                righe.Add(r);
            }

            righe = righe
                .OrderBy(r => r.Giorno)
                .ThenBy(r => TimeSpan.Parse(r.Entrata))
                .ToList();

            ReportGrid.ItemsSource = righe;
            AggiornaTotali();
        }

        private List<(DateTime Ingresso, DateTime Uscita)> CostruisciCoppieGlobali(List<TimeCardEntry> entries)
        {
            var coppie = new List<(DateTime Ingresso, DateTime Uscita)>();
            DateTime? lastIn = null;
            TimeSpan maxShift = TimeSpan.FromHours(16);

            var list = entries.OrderBy(e => e.DataOra).ToList();

            foreach (var e in list)
            {
                if (e.Tipo == PunchType.Entrata)
                {
                    lastIn = e.DataOra;
                }
                else if (e.Tipo == PunchType.Uscita && lastIn.HasValue)
                {
                    if (e.DataOra > lastIn.Value &&
                        (e.DataOra - lastIn.Value) <= maxShift)
                    {
                        coppie.Add((lastIn.Value, e.DataOra));
                        lastIn = null;
                    }
                    else
                    {
                        lastIn = null;
                    }
                }
            }

            return coppie;
        }

        private void RicalcolaReport_Click(object sender, RoutedEventArgs e)
        {
            var user = UserCombo.SelectedItem as UserProfile;
            if (user == null) return;
            if (MonthCombo.SelectedIndex < 0) return;

            int month = MonthCombo.SelectedIndex + 1;
            int year = DateTime.Now.Year;

            var righe = (ReportGrid.ItemsSource as IEnumerable<ReportRow>)?.ToList();
            if (righe == null) return;

            foreach (var r in righe)
            {
                var data = new DateTime(year, month, r.Giorno);
                RicalcolaRiga(r, data, user);
            }

            ReportGrid.ItemsSource = null;
            ReportGrid.ItemsSource = righe;

            AggiornaTotali();
        }

        private void EsportaTXT_Click(object sender, RoutedEventArgs e)
        {
            var righe = (ReportGrid.ItemsSource as IEnumerable<ReportRow>)?.ToList();
            if (righe == null || !righe.Any()) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "File di testo (*.txt)|*.txt",
                FileName = "report_mensile.txt"
            };

            if (dlg.ShowDialog() != true) return;

            var lines = new List<string>
            {
                "Giorno\tEntrata\tUscita\tOrdinarie\tStraordinarie\tFestivo\tNote"
            };

            foreach (var r in righe)
            {
                lines.Add($"{r.Giorno}\t{r.Entrata}\t{r.Uscita}\t{r.OreOrdinarie:F2}\t{r.OreStraordinarie:F2}\t{(r.IsFestivo ? "SI" : "NO")}\t{r.Note}");
            }

            File.WriteAllLines(dlg.FileName, lines);
        }

        private void EsportaPDF_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Funzione PDF non ancora implementata.");
        }

        private void AggiornaTotali()
        {
            var righe = (ReportGrid.ItemsSource as IEnumerable<ReportRow>)?.ToList();
            if (righe == null) return;

            double totOrd = righe.Sum(r => r.OreOrdinarie);
            double totExt = righe.Sum(r => r.OreStraordinarie);

            TotOrdinarieText.Text = totOrd.ToString("F2");
            TotStraordinarieText.Text = totExt.ToString("F2");
        }

        private bool IsWeekend(DateTime d)
        {
            return d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
        }

        private TimeSpan ParseOrario(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return TimeSpan.Zero;
            if (TimeSpan.TryParse(s, out var ts)) return ts;
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

        private void RicalcolaRiga(ReportRow row, DateTime data, UserProfile utente)
        {
            if (!TimeSpan.TryParse(row.Entrata, out var ein) ||
                !TimeSpan.TryParse(row.Uscita, out var uout) ||
                uout <= ein)
            {
                row.OreOrdinarie = 0;
                row.OreStraordinarie = 0;
                row.IsFestivo = IsWeekend(data);
                return;
            }

            bool festivo = IsWeekend(data);
            row.IsFestivo = festivo;

            TimeSpan ing1 = ParseOrario(utente.OrarioIngresso1);
            TimeSpan usc1 = ParseOrario(utente.OrarioUscita1);
            TimeSpan ing2 = ParseOrario(utente.OrarioIngresso2);
            TimeSpan usc2 = ParseOrario(utente.OrarioUscita2);

            double oreOrd = 0, oreExt = 0;

            if (festivo)
            {
                row.OreOrdinarie = 0;
                row.OreStraordinarie = (uout - ein).TotalHours;
                return;
            }

            if (ing1 == TimeSpan.Zero && usc1 == TimeSpan.Zero &&
                ing2 == TimeSpan.Zero && usc2 == TimeSpan.Zero)
            {
                row.OreOrdinarie = (uout - ein).TotalHours;
                return;
            }

            oreOrd += OreDentro(ein, uout, ing1, usc1);
            oreOrd += OreDentro(ein, uout, ing2, usc2);

            oreExt += OrePrima(ein, ing1);
            oreExt += OreDopo(uout, usc1);
            oreExt += OrePrima(ein, ing2);
            oreExt += OreDopo(uout, usc2);

            double soglia = 15;
            if (oreExt * 60 < soglia)
            {
                oreOrd += oreExt;
                oreExt = 0;
            }

            row.OreOrdinarie = Math.Round(oreOrd, 2);
            row.OreStraordinarie = Math.Round(oreExt, 2);
        }
        private void AggiungiTimbratura_Click(object sender, RoutedEventArgs e)
        {
            var righe = (ReportGrid.ItemsSource as List<ReportRow>);
            if (righe == null) return;

            if (MonthCombo.SelectedIndex < 0)
            {
                MessageBox.Show("Seleziona un mese.");
                return;
            }

            if (righe.Count == 0)
            {
                MessageBox.Show("Carica prima un report.");
                return;
            }

            int month = MonthCombo.SelectedIndex + 1;
            int year = DateTime.Now.Year;

            // Predefiniamo una timbratura fittizia
            var nuovaRiga = new ReportRow
            {
                Giorno = 1,
                Entrata = "00:00",
                Uscita = "00:00",
                OreOrdinarie = 0,
                OreStraordinarie = 0,
                IsFestivo = false,
                Note = ""
            };

            righe.Add(nuovaRiga);

            ReportGrid.ItemsSource = null;
            ReportGrid.ItemsSource = righe;
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

            int month = MonthCombo.SelectedIndex + 1;
            int year = DateTime.Now.Year;

            var righe = (ReportGrid.ItemsSource as IEnumerable<ReportRow>)?.ToList();
            if (righe == null)
            {
                MessageBox.Show("Nessun dato da salvare.");
                return;
            }

            string userFile = Path.Combine(_csvFolder, $"{user.Id}.csv");

            var repo = new CsvRepository();
            var original = repo.Load(userFile).ToList();

            // 1️⃣ Conversione originale → TimeCardEntry
            var entries = new List<TimeCardEntry>();
            foreach (var row in original)
            {
                if (row.Length < 2) continue;
                if (!DateTime.TryParse(row[0], out var dt)) continue;

                PunchType tipo = PunchType.Entrata;
                Enum.TryParse(row[1], true, out tipo);

                entries.Add(new TimeCardEntry
                {
                    UserId = user.Id,
                    DataOra = dt,
                    Tipo = tipo
                });
            }

            // 2️⃣ Rimuoviamo solo il mese selezionato
            entries = entries
                .Where(e2 => !(e2.DataOra.Month == month && e2.DataOra.Year == year))
                .ToList();

            // 3️⃣ Aggiungiamo tutte le coppie modificate del mese selezionato
            foreach (var r in righe)
            {
                if (!TimeSpan.TryParse(r.Entrata, out var ein)) continue;
                if (!TimeSpan.TryParse(r.Uscita, out var uout)) continue;

                var ingresso = new DateTime(year, month, r.Giorno, ein.Hours, ein.Minutes, 0);
                int uscitaGiorno = r.Giorno;

                if (uout < ein)
                    uscitaGiorno += 1;

                var uscita = new DateTime(year, month, uscitaGiorno, uout.Hours, uout.Minutes, 0);

                entries.Add(new TimeCardEntry
                {
                    UserId = user.Id,
                    DataOra = ingresso,
                    Tipo = PunchType.Entrata
                });

                entries.Add(new TimeCardEntry
                {
                    UserId = user.Id,
                    DataOra = uscita,
                    Tipo = PunchType.Uscita
                });
            }

            // 4️⃣ Ordiniamo tutto
            entries = entries
                .OrderBy(e => e.DataOra)
                .ToList();

            // 5️⃣ Riscriviamo nel CSV (stile OPZIONE C)
            var lines = entries.Select(e =>
                $"{e.DataOra:yyyy-MM-ddTHH:mm:ss},{e.Tipo}");

            File.WriteAllLines(userFile, lines);

            MessageBox.Show("Modifiche salvate correttamente.");
        }


    }
}
