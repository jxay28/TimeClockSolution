using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.VisualBasic; // per InputBox festività
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Server
{
    public partial class MainWindow : Window
    {
        // percorso della cartella condivisa con i CSV
        private string _csvFolder;

        // elenco utenti caricati
        private List<UserProfile> _users = new List<UserProfile>();

        // elenco festività caricate
        private List<Holiday> _holidays = new List<Holiday>();

        // impostazioni straordinari correnti
        private OvertimeSettings _overtimeSettings = new OvertimeSettings
        {
            SogliaMinuti = 0,
            UnitaArrotondamentoMinuti = 15
        };

        public MainWindow()
        {
            InitializeComponent();

            // Legge l'ultima cartella salvata nelle impostazioni
            _csvFolder = Properties.Settings.Default.CsvFolderPath;

            if (!string.IsNullOrWhiteSpace(_csvFolder) && Directory.Exists(_csvFolder))
            {
                CsvPathBox.Text = _csvFolder;

                // Carica subito i dati
                LoadUsers();
                LoadHolidays();
                LoadSettings();
            }
            else
            {
                CsvPathBox.Text = "Nessuna cartella selezionata";
                _csvFolder = string.Empty;
            }
        }

        /// <summary>
        /// Carica l'elenco degli utenti dal file utenti.csv e aggiorna griglia e combo.
        /// CSV: Id,SequenceNumber,Nome,Cognome,Ruolo,DataAssunzione,Ore,Base,Extra
        /// </summary>
        private void LoadUsers()
        {
            _users = new List<UserProfile>();

            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            string path = Path.Combine(_csvFolder, "utenti.csv");
            if (!File.Exists(path))
                return;

            var repo = new CsvRepository();

            foreach (var fields in repo.Load(path))
            {
                try
                {
                    var user = new UserProfile
                    {
                        Id = fields.ElementAtOrDefault(0),
                        SequenceNumber = int.TryParse(fields.ElementAtOrDefault(1), out var seq) ? seq : 0,
                        Nome = fields.ElementAtOrDefault(2),
                        Cognome = fields.ElementAtOrDefault(3),
                        Ruolo = fields.ElementAtOrDefault(4),
                        DataAssunzione = DateTime.TryParse(fields.ElementAtOrDefault(5), out var d) ? d : DateTime.MinValue,
                        OreContrattoSettimanali = double.TryParse(fields.ElementAtOrDefault(6), NumberStyles.Any, CultureInfo.InvariantCulture, out var ore) ? ore : 0,
                        CompensoOrarioBase = decimal.TryParse(fields.ElementAtOrDefault(7), NumberStyles.Any, CultureInfo.InvariantCulture, out var cb) ? cb : 0,
                        CompensoOrarioExtra = decimal.TryParse(fields.ElementAtOrDefault(8), NumberStyles.Any, CultureInfo.InvariantCulture, out var ce) ? ce : 0
                    };

                    // ignora eventuali righe senza Id o Nome
                    if (!string.IsNullOrWhiteSpace(user.Id))
                        _users.Add(user);
                }
                catch
                {
                    // ignora righe malformate
                }
            }

            // aggiorna la griglia degli utenti
            var grid = this.FindName("UsersGrid") as System.Windows.Controls.DataGrid;
            if (grid != null)
            {
                grid.ItemsSource = null;
                grid.ItemsSource = _users;
            }

            // aggiorna la combo per il report
            var combo = this.FindName("UserSelectorReport") as System.Windows.Controls.ComboBox;
            if (combo != null)
            {
                combo.ItemsSource = null;
                combo.ItemsSource = _users;
            }
        }

        /// <summary>
        /// Carica le festività da festivita.csv e aggiorna la griglia dedicata.
        /// </summary>
        private void LoadHolidays()
        {
            _holidays = new List<Holiday>();

            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            string path = Path.Combine(_csvFolder, "festivita.csv");
            if (!File.Exists(path))
                return;

            var repo = new CsvRepository();

            foreach (var fields in repo.Load(path))
            {
                try
                {
                    var h = new Holiday
                    {
                        Data = DateTime.TryParse(fields.ElementAtOrDefault(0), out var d) ? d : DateTime.MinValue,
                        Descrizione = fields.ElementAtOrDefault(1)
                    };
                    _holidays.Add(h);
                }
                catch
                {
                    // ignora riga malformata
                }
            }

            var grid = this.FindName("HolidayGrid") as System.Windows.Controls.DataGrid;
            if (grid != null)
            {
                grid.ItemsSource = null;
                grid.ItemsSource = _holidays;
            }
        }

        /// <summary>
        /// Carica le impostazioni straordinari dal file parametri_straordinari.csv e aggiorna lo slider.
        /// </summary>
        private void LoadSettings()
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            string path = Path.Combine(_csvFolder, "parametri_straordinari.csv");
            if (!File.Exists(path))
                return;

            var repo = new CsvRepository();
            var fields = repo.Load(path).FirstOrDefault();

            if (fields != null)
            {
                _overtimeSettings.SogliaMinuti = int.TryParse(fields.ElementAtOrDefault(0), out var s) ? s : 0;
                _overtimeSettings.UnitaArrotondamentoMinuti = int.TryParse(fields.ElementAtOrDefault(1), out var u) ? u : 15;
            }

            var slider = this.FindName("OvertimeSlider") as System.Windows.Controls.Slider;
            var valueLabel = this.FindName("OvertimeValue") as System.Windows.Controls.TextBlock;

            if (slider != null)
                slider.Value = _overtimeSettings.SogliaMinuti;

            if (valueLabel != null)
                valueLabel.Text = $"{_overtimeSettings.SogliaMinuti} minuti";
        }

        /// <summary>
        /// Gestisce il click sui pulsanti del menu laterale e cambia tab a seconda del Tag.
        /// </summary>
        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag != null)
            {
                if (int.TryParse(btn.Tag.ToString(), out int index))
                {
                    var tab = this.FindName("MainTabs") as System.Windows.Controls.TabControl;
                    if (tab != null)
                    {
                        tab.SelectedIndex = index;
                    }
                }
            }
        }

        /// <summary>
        /// Minimizza la finestra.
        /// </summary>
        private void Min_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// Chiude la finestra.
        /// </summary>
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// Aggiunge un nuovo utente tramite AddUserWindow e salva su utenti.csv.
        /// </summary>
        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Seleziona prima la cartella CSV");
                return;
            }

            try
            {
                var dlg = new AddUserWindow(_csvFolder)
                {
                    Owner = this
                };

                bool? result = dlg.ShowDialog();

                if (result == true && dlg.User != null)
                {
                    var u = dlg.User;

                    string line = string.Join(",", new[]
                    {
                u.Id,
                u.SequenceNumber.ToString(),
                u.Nome,
                u.Cognome,
                u.Ruolo,
                u.DataAssunzione.ToString("yyyy-MM-dd"),
                u.OreContrattoSettimanali.ToString(CultureInfo.InvariantCulture),
                u.CompensoOrarioBase.ToString(CultureInfo.InvariantCulture),
                u.CompensoOrarioExtra.ToString(CultureInfo.InvariantCulture)
            });

                    string path = Path.Combine(_csvFolder, "utenti.csv");
                    File.AppendAllText(path, line + Environment.NewLine);

                    LoadUsers();
                    MessageBox.Show("Utente aggiunto con successo");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'aggiunta dell'utente: {ex.Message}");
            }
        }


        /// <summary>
        /// Aggiunge una festività chiedendo data e descrizione e salva su festivita.csv.
        /// </summary>
        private void AddHoliday_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Seleziona prima la cartella CSV");
                return;
            }

            try
            {
                string dataStr = Interaction.InputBox("Data (YYYY-MM-DD):", "Nuova festività", DateTime.Now.ToString("yyyy-MM-dd"));
                if (!DateTime.TryParse(dataStr, out var dt))
                {
                    MessageBox.Show("Data non valida");
                    return;
                }

                string descrizione = Interaction.InputBox("Descrizione:", "Nuova festività", "");

                string line = string.Join(",", new[] { dt.ToString("yyyy-MM-dd"), descrizione });

                string path = Path.Combine(_csvFolder, "festivita.csv");
                File.AppendAllText(path, line + Environment.NewLine);

                LoadHolidays();
                MessageBox.Show("Festività aggiunta con successo");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'aggiunta della festività: {ex.Message}");
            }
        }

        /// <summary>
        /// Aggiorna la soglia dei minuti straordinari quando lo slider cambia e salva il file.
        /// </summary>
        private void OvertimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int value = (int)e.NewValue;

            var label = this.FindName("OvertimeValue") as System.Windows.Controls.TextBlock;
            if (label != null)
            {
                label.Text = $"{value} minuti";
            }

            _overtimeSettings.SogliaMinuti = value;

            if (!string.IsNullOrWhiteSpace(_csvFolder))
            {
                string path = Path.Combine(_csvFolder, "parametri_straordinari.csv");
                File.WriteAllText(path, string.Join(",", new[]
                {
                    _overtimeSettings.SogliaMinuti.ToString(),
                    _overtimeSettings.UnitaArrotondamentoMinuti.ToString()
                }));
            }
        }

        /// <summary>
        /// Gestione click del bottone di scelta cartella CSV.
        /// </summary>
        private void SelectCsvFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    _csvFolder = dialog.SelectedPath;
                    CsvPathBox.Text = _csvFolder;

                    // salva nelle impostazioni utente
                    Properties.Settings.Default.CsvFolderPath = _csvFolder;
                    Properties.Settings.Default.Save();

                    // ricarica i dati
                    LoadUsers();
                    LoadHolidays();
                    LoadSettings();
                }
            }
        }

        /// <summary>
        /// Genera i report mensili per gli utenti selezionati (o tutti).
        /// </summary>
        private void GenerateReport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder) || !Directory.Exists(_csvFolder))
            {
                MessageBox.Show("Seleziona una cartella valida.");
                return;
            }

            try
            {
                var repo = new CsvRepository();

                if (_users == null || !_users.Any())
                {
                    LoadUsers();
                }

                var userSelector = this.FindName("UserSelectorReport") as System.Windows.Controls.ComboBox;
                List<UserProfile> targetUsers;

                if (userSelector != null && userSelector.SelectedItem is UserProfile selectedUser)
                {
                    targetUsers = new List<UserProfile> { selectedUser };
                }
                else
                {
                    targetUsers = _users ?? new List<UserProfile>();
                }

                if (_holidays == null || !_holidays.Any())
                {
                    LoadHolidays();
                }

                int year = DateTime.Now.Year;
                int month = DateTime.Now.Month;

                var calculator = new PayPeriodCalculator(_overtimeSettings ?? new OvertimeSettings(), _holidays);
                var summaries = new List<PaySummary>();

                foreach (var user in targetUsers)
                {
                    string userFile = Path.Combine(_csvFolder, $"{user.Id}.csv");
                    if (!File.Exists(userFile))
                        continue;

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
                reportService.GenerateReports(targetUsers, summaries, year, month, _csvFolder);

                MessageBox.Show("Report generati con successo!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'elaborazione: {ex.Message}");
            }
        }

        /// <summary>
        /// Wrapper per compatibilità con vecchia interfaccia. Chiama GenerateReport_Click.
        /// </summary>
        private void ProcessMonth_Click(object sender, RoutedEventArgs e)
        {
            GenerateReport_Click(sender, e);
        }
    }
}
