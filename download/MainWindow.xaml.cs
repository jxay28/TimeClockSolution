using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Microsoft.VisualBasic; // for InputBox dialogs
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
        private OvertimeSettings _overtimeSettings;

        public MainWindow()
        {
            InitializeComponent();
            // inizializza il percorso cartella CSV a stringa vuota
            _csvFolder = string.Empty;
        }

        /// <summary>
        /// Carica l'elenco degli utenti dal file utenti.csv e aggiorna la griglia e la combo.
        /// </summary>
        private void LoadUsers()
        {
            _users = new List<UserProfile>();
            if (string.IsNullOrWhiteSpace(_csvFolder)) return;
            string path = Path.Combine(_csvFolder, "utenti.csv");
            if (!File.Exists(path)) return;
            var repo = new CsvRepository();
            foreach (var fields in repo.Load(path))
            {
                try
                {
                    var user = new UserProfile
                    {
                        Id = fields.ElementAtOrDefault(0),
                        Nome = fields.ElementAtOrDefault(1),
                        Cognome = fields.ElementAtOrDefault(2),
                        Ruolo = fields.ElementAtOrDefault(3),
                        DataAssunzione = DateTime.TryParse(fields.ElementAtOrDefault(4), out var d) ? d : DateTime.MinValue,
                        OreContrattoSettimanali = double.TryParse(fields.ElementAtOrDefault(5), out var ore) ? ore : 0,
                        CompensoOrarioBase = decimal.TryParse(fields.ElementAtOrDefault(6), out var cb) ? cb : 0,
                        CompensoOrarioExtra = decimal.TryParse(fields.ElementAtOrDefault(7), out var ce) ? ce : 0
                    };
                    _users.Add(user);
                }
                catch { /* ignora righe malformate */ }
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
            if (string.IsNullOrWhiteSpace(_csvFolder)) return;
            string path = Path.Combine(_csvFolder, "festivita.csv");
            if (!File.Exists(path)) return;
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
                catch { }
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
            _overtimeSettings = new OvertimeSettings { SogliaMinuti = 0, UnitaArrotondamentoMinuti = 15 };
            if (string.IsNullOrWhiteSpace(_csvFolder)) return;
            string path = Path.Combine(_csvFolder, "parametri_straordinari.csv");
            if (!File.Exists(path)) return;
            var repo = new CsvRepository();
            var fields = repo.Load(path).FirstOrDefault();
            if (fields != null)
            {
                _overtimeSettings.SogliaMinuti = int.TryParse(fields.ElementAtOrDefault(0), out var s) ? s : 0;
                _overtimeSettings.UnitaArrotondamentoMinuti = int.TryParse(fields.ElementAtOrDefault(1), out var u) ? u : 15;
            }
            // aggiorna slider e testo
            var slider = this.FindName("OvertimeSlider") as System.Windows.Controls.Slider;
            var valueLabel = this.FindName("OvertimeValue") as System.Windows.Controls.TextBlock;
            if (slider != null)
            {
                slider.Value = _overtimeSettings.SogliaMinuti;
            }
            if (valueLabel != null)
            {
                valueLabel.Text = $"{_overtimeSettings.SogliaMinuti} minuti";
            }
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
        /// Aggiunge un nuovo utente chiedendo i dati tramite InputBox e salva su utenti.csv.
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
                string nome = Interaction.InputBox("Nome:", "Aggiungi utente", "");
                if (string.IsNullOrWhiteSpace(nome)) return;
                string cognome = Interaction.InputBox("Cognome:", "Aggiungi utente", "");
                if (string.IsNullOrWhiteSpace(cognome)) return;
                string ruolo = Interaction.InputBox("Ruolo:", "Aggiungi utente", "");
                string dataAss = Interaction.InputBox("Data assunzione (YYYY-MM-DD):", "Aggiungi utente", DateTime.Now.ToString("yyyy-MM-dd"));
                if (!DateTime.TryParse(dataAss, out var dtAss))
                {
                    MessageBox.Show("Data assunzione non valida");
                    return;
                }
                string oreStr = Interaction.InputBox("Ore settimanali (numero):", "Aggiungi utente", "40");
                if (!double.TryParse(oreStr, out var oreSett))
                {
                    MessageBox.Show("Ore settimanali non valide");
                    return;
                }
                string compBaseStr = Interaction.InputBox("Compenso orario base:", "Aggiungi utente", "10");
                if (!decimal.TryParse(compBaseStr, out var compBase))
                {
                    MessageBox.Show("Compenso orario base non valido");
                    return;
                }
                string compExtraStr = Interaction.InputBox("Compenso orario extra:", "Aggiungi utente", "15");
                if (!decimal.TryParse(compExtraStr, out var compExtra))
                {
                    MessageBox.Show("Compenso orario extra non valido");
                    return;
                }
                // genera ID utente unico
                string id = Guid.NewGuid().ToString();
                string line = string.Join(",", new [] {
                    id,
                    nome,
                    cognome,
                    ruolo,
                    dtAss.ToString("yyyy-MM-dd"),
                    oreSett.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    compBase.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    compExtra.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
                string path = Path.Combine(_csvFolder, "utenti.csv");
                File.AppendAllText(path, line + Environment.NewLine);
                LoadUsers();
                MessageBox.Show("Utente aggiunto con successo");
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
                string line = string.Join(",", new [] { dt.ToString("yyyy-MM-dd"), descrizione });
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
        /// Aggiorna la soglia dei minuti straordinari quando lo slider cambia.
        /// </summary>
        private void OvertimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int value = (int)e.NewValue;
            var label = this.FindName("OvertimeValue") as System.Windows.Controls.TextBlock;
            if (label != null)
            {
                label.Text = $"{value} minuti";
            }
            // aggiorna le impostazioni e salva sul file
            if (_overtimeSettings != null)
            {
                _overtimeSettings.SogliaMinuti = value;
                // salva
                if (!string.IsNullOrWhiteSpace(_csvFolder))
                {
                    string path = Path.Combine(_csvFolder, "parametri_straordinari.csv");
                    File.WriteAllText(path, string.Join(",", new [] {
                        _overtimeSettings.SogliaMinuti.ToString(),
                        _overtimeSettings.UnitaArrotondamentoMinuti.ToString()
                    }));
                }
            }
        }

        /// <summary>
        /// Gestione click del bottone di scelta cartella. Consente di selezionare
        /// la cartella condivisa che contiene i file CSV e ricarica i dati.
        /// </summary>
        private void SelectCsvFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _csvFolder = dlg.SelectedPath;
                // aggiorna la casella di testo nella UI se esiste
                var pathBox = this.FindName("CsvPathBox") as System.Windows.Controls.TextBox;
                if (pathBox != null)
                {
                    pathBox.Text = _csvFolder;
                }
                // carica tutti i dati dalla cartella
                LoadUsers();
                LoadHolidays();
                LoadSettings();
            }
        }

        /// <summary>
        /// Genera i report mensili per gli utenti selezionati (o tutti).
        /// Legge ore e calcola straordinari utilizzando il repository e le impostazioni correnti.
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
                // usa la lista utenti caricata. Se vuota, ricarica
                if (_users == null || !_users.Any())
                {
                    LoadUsers();
                }
                // determina se generare per un singolo utente
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
                // assicura che le festività e impostazioni siano caricate
                if (_holidays == null || !_holidays.Any())
                {
                    LoadHolidays();
                }
                if (_overtimeSettings == null)
                {
                    LoadSettings();
                }
                int year = DateTime.Now.Year;
                int month = DateTime.Now.Month;
                var calculator = new PayPeriodCalculator(_overtimeSettings ?? new OvertimeSettings(), _holidays);
                var summaries = new List<PaySummary>();
                foreach (var user in targetUsers)
                {
                    string userFile = Path.Combine(_csvFolder, $"{user.Id}.csv");
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