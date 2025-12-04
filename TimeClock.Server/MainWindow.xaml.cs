using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using TimeClock.Core.Models;
using TimeClock.Core.Services;



namespace TimeClock.Server
{
    public partial class MainWindow : Window
    {
        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b &&
                int.TryParse(b.Tag?.ToString(), out int index))
            {
                MainTabs.SelectedIndex = index;
            }
        }
        private void Min_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Funzione: aggiungi utente (da implementare)");
        }
        private void AddHoliday_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Funzione: aggiungi festività (da implementare)");
        }
        private void GenerateReport_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Funzione: genera report (da implementare)");
        }
        private string _csvFolder = string.Empty;

        private void SelectCsvFolder_Click(object sender, RoutedEventArgs e)
        {
            // Usa la classica dialog per scegliere una cartella
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    _csvFolder = dialog.SelectedPath;
                    CsvPathBox.Text = _csvFolder;

                    // Se hai un metodo che ricarica gli utenti dal CSV, lo richiami qui
                    ;
                }
            }
        }

        private System.Windows.Controls.TextBox SharedFolderTextBox;

        public MainWindow()
        {
            InitializeComponent();
            SharedFolderTextBox = new System.Windows.Controls.TextBox();
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SharedFolderTextBox.Text = dlg.SelectedPath;
            }
        }

        private void ProcessMonth_Click(object sender, RoutedEventArgs e)
        {
            string folder = SharedFolderTextBox.Text;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("Seleziona una cartella valida.");
                return;
            }

            try
            {
                var repo = new CsvRepository();
                var users = repo.Load(Path.Combine(folder, "utenti.csv"))
                    .Select(f => new UserProfile
                    {
                        Id = f[0],
                        Nome = f[1],
                        Cognome = f[2],
                        Ruolo = f[3],
                        DataAssunzione = DateTime.Parse(f[4]),
                        OreContrattoSettimanali = double.Parse(f[5]),
                        CompensoOrarioBase = decimal.Parse(f[6]),
                        CompensoOrarioExtra = decimal.Parse(f[7])
                    })
                    .ToList();

                var holidays = repo.Load(Path.Combine(folder, "festivita.csv"))
                    .Select(f => new Holiday { Data = DateTime.Parse(f[0]), Descrizione = f[1] })
                    .ToList();

                var settingsFile = Path.Combine(folder, "parametri_straordinari.csv");
                var settingsFields = repo.Load(settingsFile).FirstOrDefault();
                var settings = new OvertimeSettings
                {
                    SogliaMinuti = int.Parse(settingsFields?[0] ?? "0"),
                    UnitaArrotondamentoMinuti = int.Parse(settingsFields?[1] ?? "15")
                };

                int year = DateTime.Now.Year;
                int month = DateTime.Now.Month;

                var calculator = new PayPeriodCalculator(settings, holidays);

                var summaries = new List<PaySummary>();
                foreach (var user in users)
                {
                    string userFile = Path.Combine(folder, $"{user.Id}.csv");
                    var entries = repo.Load(userFile)
                        .Select(f => new TimeCardEntry
                        {
                            UserId = user.Id,
                            DataOra = DateTime.Parse(f[0]),
                            Tipo = Enum.TryParse<PunchType>(f[1], true, out var tipo) ? tipo : PunchType.Entrata
                        })
                        .ToList();

                    var summary = calculator.Calculate(user, entries, year, month);
                    summaries.Add(summary);
                }

                var reportService = new ReportService();
                reportService.GenerateReports(users, summaries, year, month, folder);
                MessageBox.Show("Report generati con successo!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'elaborazione: {ex.Message}");
            }
        }
    }
}