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
using TimeClock.Server.Services;


namespace TimeClock.Server
{
    public partial class MainWindow : Window
    {
        private static readonly (int Mese, int Giorno, string Nome)[] FestivitaNazionaliFisse =
        {
            (1, 1, "01/01 - Capodanno"),
            (1, 6, "06/01 - Epifania"),
            (4, 25, "25/04 - Festa della Liberazione"),
            (5, 1, "01/05 - Festa del Lavoro"),
            (6, 2, "02/06 - Festa della Repubblica"),
            (8, 15, "15/08 - Ferragosto"),
            (11, 1, "01/11 - Ognissanti"),
            (12, 8, "08/12 - Immacolata Concezione"),
            (12, 25, "25/12 - Natale"),
            (12, 26, "26/12 - Santo Stefano")
        };

        // percorso della cartella condivisa con i CSV
        private string _csvFolder;

        // elenco utenti caricati
        private List<UserProfile> _users = new List<UserProfile>();
        private readonly UserDataMigrationService _migrationService = new();

        // i parametri globali sono gestiti in App.ParametriGlobali

        public MainWindow()
        {
            InitializeComponent();

            // Legge l'ultima cartella salvata nelle impostazioni
            _csvFolder = Properties.Settings.Default.CsvFolderPath;

            if (!string.IsNullOrWhiteSpace(_csvFolder) && Directory.Exists(_csvFolder))
            {
                CsvPathBox.Text = _csvFolder;
                App.ConfigureDataFolder(_csvFolder);

                // Carica subito i dati
                LoadUsers();
                LoadSettings();
            }
            else
            {
                CsvPathBox.Text = "Nessuna cartella selezionata";
                _csvFolder = string.Empty;
                App.ConfigureDataFolder(null);
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
                    if (_migrationService.TryMigrateCsvToJsonWithBackup(csvPath, jsonPath, out var migratedUsers, out var backupPath))
                    {
                        _users = migratedUsers;
                        SaveUsers();
                        AuditLogger.Log(_csvFolder, "users_migration", $"source=csv; backup={backupPath}; users={_users.Count}");
                    }
                    else
                    {
                        // fallback sicuro: carica comunque gli utenti legacy senza toccare i file
                        _users = _migrationService.LoadUsersLegacyCsv(csvPath);
                    }
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
                SafeFileWriter.WriteAllTextAtomic(jsonPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante il salvataggio di utenti.json: {ex.Message}");
            }

            // Esportazione CSV (solo per compatibilità / eventuali strumenti esterni)
            try
            {
                var lines = _users.Select(u => CsvCodec.BuildLine(new[]
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

                SafeFileWriter.WriteAllLinesAtomic(csvPath, lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'aggiornamento di utenti.csv: {ex.Message}");
            }
        }


        /// <summary>
        /// Carica i parametri globali nella UI.
        /// </summary>
        private void LoadSettings()
        {
            var p = App.ParametriGlobali ?? new ParametriStraordinari();
            App.ParametriGlobali = p;
            EnsureFixedNationalHolidays(p);

            p.SogliaMinutiStraordinario = NormalizzaSoglia(p.SogliaMinutiStraordinario);
            OvertimeSlider.Value = p.SogliaMinutiStraordinario;
            OvertimeValue.Text = $"{p.SogliaMinutiStraordinario} minuti";

            SaturdayCheck.IsChecked = p.GiorniSempreFestivi.Contains(DayOfWeek.Saturday);
            SundayCheck.IsChecked = p.GiorniSempreFestivi.Contains(DayOfWeek.Sunday);

            RefreshNationalHolidayCheckboxes(p);
            RefreshCustomHolidayList();
        }

        private void EnsureFixedNationalHolidays(ParametriStraordinari p)
        {
            var allowed = new HashSet<(int Mese, int Giorno)>(FestivitaNazionaliFisse.Select(f => (f.Mese, f.Giorno)));
            p.FestivitaRicorrenti ??= new List<(int Mese, int Giorno)>();
            p.FestivitaRicorrenti = p.FestivitaRicorrenti
                .Where(f => allowed.Contains((f.Mese, f.Giorno)))
                .Distinct()
                .OrderBy(f => f.Mese)
                .ThenBy(f => f.Giorno)
                .ToList();
        }

        private void RefreshNationalHolidayCheckboxes(ParametriStraordinari p)
        {
            var selected = new HashSet<(int Mese, int Giorno)>(p.FestivitaRicorrenti.Select(f => (f.Mese, f.Giorno)));

            NationalHolidayPanel.Children.Clear();
            foreach (var f in FestivitaNazionaliFisse)
            {
                NationalHolidayPanel.Children.Add(new CheckBox
                {
                    Content = f.Nome,
                    Foreground = SaturdayCheck.Foreground,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = selected.Contains((f.Mese, f.Giorno)),
                    Tag = (f.Mese, f.Giorno)
                });
            }
        }

        private void RefreshCustomHolidayList()
        {
            var p = App.ParametriGlobali ?? new ParametriStraordinari();
            p.NomiFestivitaAggiuntive ??= new Dictionary<string, string>();

            var items = p.FestivitaAggiuntive
                .Select(d =>
                {
                    string key = d.ToString("yyyy-MM-dd");
                    string nome = p.NomiFestivitaAggiuntive.TryGetValue(key, out var n) && !string.IsNullOrWhiteSpace(n)
                        ? n
                        : "Festivita personalizzata";
                    return $"{d:dd/MM/yyyy} - {nome}";
                })
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            CustomHolidayList.ItemsSource = null;
            CustomHolidayList.ItemsSource = items;
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
                    // Accesso rapido: dal menu "Report" apre direttamente la finestra report.
                    if (index == 2)
                    {
                        GenerateReport_Click(sender, e);
                        return;
                    }

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

                    AuditLogger.Log(_csvFolder, "add_user", $"id={dlg.User.Id}; nome={dlg.User.Nome} {dlg.User.Cognome}; seq={dlg.User.SequenceNumber}");
                    MessageBox.Show("Utente aggiunto con successo");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'aggiunta dell'utente: {ex.Message}");
            }
        }

        private void SaveUsersGrid_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Seleziona prima la cartella dati (CSV/JSON).");
                return;
            }

            var grid = this.FindName("UsersGrid") as DataGrid;
            if (grid != null)
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }

            SaveUsers();
            AuditLogger.Log(_csvFolder, "save_users_grid", $"utenti={_users.Count}");
            MessageBox.Show("Modifiche anagrafica salvate.");
        }


        /// <summary>
        /// Aggiorna solo l'etichetta della soglia nella UI.
        /// Il salvataggio avviene con il pulsante "Salva parametri".
        /// </summary>
        private void OvertimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int value = NormalizzaSoglia((int)e.NewValue);

            // In fase di inizializzazione il ValueChanged può scattare prima che i controlli siano pronti
            if (OvertimeValue != null)
                OvertimeValue.Text = $"{value} minuti";
        }

        private void AddCustomHolidayButton_Click(object sender, RoutedEventArgs e)
        {
            if (CustomHolidayPicker.SelectedDate == null)
            {
                MessageBox.Show("Seleziona una data festiva personalizzata.");
                return;
            }

            var p = App.ParametriGlobali ?? new ParametriStraordinari();
            App.ParametriGlobali = p;
            p.NomiFestivitaAggiuntive ??= new Dictionary<string, string>();

            var data = CustomHolidayPicker.SelectedDate.Value.Date;
            string nomeFestivita = string.IsNullOrWhiteSpace(CustomHolidayNameBox.Text)
                ? "Festivita personalizzata"
                : CustomHolidayNameBox.Text.Trim();
            string key = data.ToString("yyyy-MM-dd");

            if (!p.FestivitaAggiuntive.Contains(data))
            {
                p.FestivitaAggiuntive.Add(data);
            }

            // Se la data esiste gia, aggiorniamo comunque il nome.
            p.NomiFestivitaAggiuntive[key] = nomeFestivita;
            RefreshCustomHolidayList();
            CustomHolidayNameBox.Text = string.Empty;
        }

        private void SaveOvertimeParamsButton_Click(object sender, RoutedEventArgs e)
        {
            var p = App.ParametriGlobali ?? new ParametriStraordinari();
            App.ParametriGlobali = p;

            p.SogliaMinutiStraordinario = NormalizzaSoglia((int)OvertimeSlider.Value);
            p.FestivitaRicorrenti = NationalHolidayPanel.Children
                .OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true && cb.Tag is ValueTuple<int, int>)
                .Select(cb =>
                {
                    var t = (ValueTuple<int, int>)cb.Tag;
                    return (Mese: t.Item1, Giorno: t.Item2);
                })
                .Distinct()
                .OrderBy(f => f.Mese)
                .ThenBy(f => f.Giorno)
                .ToList();
            EnsureFixedNationalHolidays(p);

            p.GiorniSempreFestivi = new List<DayOfWeek>();
            if (SaturdayCheck.IsChecked == true)
                p.GiorniSempreFestivi.Add(DayOfWeek.Saturday);
            if (SundayCheck.IsChecked == true)
                p.GiorniSempreFestivi.Add(DayOfWeek.Sunday);

            App.SalvaParametriGlobali();
            AuditLogger.Log(_csvFolder, "save_overtime_params", $"soglia={p.SogliaMinutiStraordinario}; nazionaliFisse={p.FestivitaRicorrenti.Count}; sabato={SaturdayCheck.IsChecked == true}; domenica={SundayCheck.IsChecked == true}; festiviCustom={p.FestivitaAggiuntive.Count}");

            MessageBox.Show("Parametri salvati.");
        }

        private void CancelOvertimeParamsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings();
        }

        private int NormalizzaSoglia(int value)
        {
            if (value <= 0) return 0;
            if (value <= 15) return 15;
            return 30;
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
                    App.ConfigureDataFolder(_csvFolder);

                    // salva nelle impostazioni utente
                    Properties.Settings.Default.CsvFolderPath = _csvFolder;
                    Properties.Settings.Default.Save();

                    // ricarica i dati
                    LoadUsers();
                    LoadSettings();
                }
            }
        }

        /// <summary>
        /// Genera i report mensili per gli utenti selezionati (o tutti).
        /// </summary>
        private void GenerateReport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Seleziona prima la cartella dei dati.");
                return;
            }

            if (_users == null || _users.Count == 0)
            {
                MessageBox.Show("Non ci sono utenti caricati.");
                return;
            }

            var wnd = new ReportWindow(_csvFolder, _users)
            {
                Owner = this
            };

            wnd.ShowDialog();
        
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
