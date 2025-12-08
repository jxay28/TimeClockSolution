using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.VisualBasic; // per InputBox festività
using TimeClock.Core.Models;
using TimeClock.Core.Services;
using System.Text.Json;
using System.Windows.Controls;


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
        /// <summary>
        /// Carica l'elenco degli utenti dal file utenti.json (master).
        /// Se utenti.json non esiste, prova a migrare da utenti.csv.
        /// Aggiorna griglia e combo.
        /// </summary>
        private void LoadUsers()
        {
            _users = new List<UserProfile>();

            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            string jsonPath = Path.Combine(_csvFolder, "utenti.json");
            string csvPath = Path.Combine(_csvFolder, "utenti.csv");

            // 1) JSON master
            if (File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    var list = JsonSerializer.Deserialize<List<UserProfile>>(json);
                    if (list != null)
                        _users = list;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore durante il caricamento di utenti.json: {ex.Message}");
                }
            }
            // 2) Prima esecuzione: migra da CSV se presente
            else if (File.Exists(csvPath))
            {
                try
                {
                    var repo = new CsvRepository();

                    foreach (var fields in repo.Load(csvPath))
                    {
                        try
                        {
                            var user = new UserProfile
                            {
                                Id = fields.ElementAtOrDefault(0) ?? string.Empty,
                                SequenceNumber = int.TryParse(fields.ElementAtOrDefault(1), out var seq) ? seq : 0,
                                Nome = fields.ElementAtOrDefault(2) ?? string.Empty,
                                Cognome = fields.ElementAtOrDefault(3) ?? string.Empty,
                                Ruolo = fields.ElementAtOrDefault(4) ?? string.Empty,
                                DataAssunzione = DateTime.TryParse(fields.ElementAtOrDefault(5), out var d) ? d : DateTime.MinValue,
                                OreContrattoSettimanali = double.TryParse(fields.ElementAtOrDefault(6),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var ore)
                                    ? ore
                                    : 0,
                                CompensoOrarioBase = decimal.TryParse(fields.ElementAtOrDefault(7),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var cb)
                                    ? cb
                                    : 0,
                                CompensoOrarioExtra = decimal.TryParse(fields.ElementAtOrDefault(8),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var ce)
                                    ? ce
                                    : 0,
                                // Campi extra orari (se non ci sono nel CSV restano vuoti)
                                OrarioIngresso1 = fields.ElementAtOrDefault(9),
                                OrarioUscita1 = fields.ElementAtOrDefault(10),
                                OrarioIngresso2 = fields.ElementAtOrDefault(11),
                                OrarioUscita2 = fields.ElementAtOrDefault(12)
                            };

                            _users.Add(user);
                        }
                        catch
                        {
                            // ignora la riga malformata
                        }
                    }

                    // una volta caricati dal CSV, salviamo subito in JSON+CSV pulito
                    SaveUsers();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore durante il caricamento di utenti.csv: {ex.Message}");
                }
            }

            // aggiorna la griglia degli utenti
            var grid = this.FindName("UsersGrid") as DataGrid;
            if (grid != null)
            {
                grid.ItemsSource = null;
                grid.ItemsSource = _users;
            }

            // aggiorna la combo per il report
            var combo = this.FindName("UserSelectorReport") as ComboBox;
            if (combo != null)
            {
                combo.ItemsSource = null;
                combo.ItemsSource = _users;
            }
        }
        /// <summary>
        /// Salva gli utenti su utenti.json (master) e genera anche utenti.csv per compatibilità.
        /// </summary>
        private void SaveUsers()
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            string jsonPath = Path.Combine(_csvFolder, "utenti.json");
            string csvPath = Path.Combine(_csvFolder, "utenti.csv");

            // Salvataggio JSON
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(_users, options);
                File.WriteAllText(jsonPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante il salvataggio di utenti.json: {ex.Message}");
            }

            // Esportazione CSV (solo per compatibilità / eventuali strumenti esterni)
            try
            {
                var lines = _users.Select(u => string.Join(",", new[]
                {
            u.Id,
            u.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            u.Nome,
            u.Cognome,
            u.Ruolo,
            u.DataAssunzione.ToString("yyyy-MM-dd"),
            u.OreContrattoSettimanali.ToString(System.Globalization.CultureInfo.InvariantCulture),
            u.CompensoOrarioBase.ToString(System.Globalization.CultureInfo.InvariantCulture),
            u.CompensoOrarioExtra.ToString(System.Globalization.CultureInfo.InvariantCulture),
            u.OrarioIngresso1 ?? string.Empty,
            u.OrarioUscita1 ?? string.Empty,
            u.OrarioIngresso2 ?? string.Empty,
            u.OrarioUscita2 ?? string.Empty
        }));

                File.WriteAllLines(csvPath, lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'aggiornamento di utenti.csv: {ex.Message}");
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
        /// Aggiunge un nuovo utente tramite AddUserWindow, aggiorna la lista _users
        /// e salva subito su utenti.json + utenti.csv.
        /// </summary>
        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Seleziona prima la cartella dati (CSV/JSON).");
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
                    _users.Add(dlg.User);

                    // Salvataggio su JSON + CSV
                    SaveUsers();

                    // Aggiorna griglia
                    var grid = this.FindName("UsersGrid") as DataGrid;
                    if (grid != null)
                    {
                        grid.ItemsSource = null;
                        grid.ItemsSource = _users;
                    }

                    // Aggiorna combo report
                    var combo = this.FindName("UserSelectorReport") as ComboBox;
                    if (combo != null)
                    {
                        combo.ItemsSource = null;
                        combo.ItemsSource = _users;
                    }

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
