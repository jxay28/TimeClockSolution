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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TimeClock.Server.Services;
using TimeClock.Server.Models;


namespace TimeClock.Server
{
    public class DashboardRow
    {
        public string Matricola { get; set; } = "000";
        public string Nome { get; set; } = string.Empty;
        public string Stato { get; set; } = "Fuori";
        public Brush StatoBrush { get; set; } = Brushes.IndianRed;
        public string UltimaTimbrata { get; set; } = "-";
        public DateTime? InizioDentro { get; set; }
        public string TempoDentro { get; set; } = "-";
    }

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
        private readonly DispatcherTimer _dashboardTimer = new DispatcherTimer();

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
                LoadCompanyInfoUI();
            }
            else
            {
                CsvPathBox.Text = "Nessuna cartella selezionata";
                _csvFolder = string.Empty;
                App.ConfigureDataFolder(null);
                LoadCompanyInfoUI();
            }

            CheckPasswordOnStartup();

            _dashboardTimer.Interval = TimeSpan.FromSeconds(1);
            _dashboardTimer.Tick += DashboardTimer_Tick;
            _dashboardTimer.Start();
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

            // aggiorna la combo per il report e per le assenze
            var combo = this.FindName("UserSelectorReport") as ComboBox;
            if (combo != null)
            {
                combo.ItemsSource = null;
                combo.ItemsSource = _users;
            }

            var absenceCombo = this.FindName("AbsenceUserCombo") as ComboBox;
            if (absenceCombo != null)
            {
                absenceCombo.ItemsSource = null;
                absenceCombo.ItemsSource = _users;
            }

            RefreshDashboard();
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
                    if (index == 3)
                    {
                        GenerateReport_Click(sender, e);
                        return;
                    }

                    var tab = this.FindName("MainTabs") as System.Windows.Controls.TabControl;
                    if (tab != null)
                    {
                        tab.SelectedIndex = index;

                        // Se selezioniamo la tab Assenze (indice 4), carichiamo i dati
                        if (index == 4)
                        {
                            LoadAbsences();
                        }
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

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Drag solo da header: evita effetti collaterali sui controlli del contenuto.
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void RefreshDashboard_Click(object sender, RoutedEventArgs e)
        {
            RefreshDashboard();
        }

        private void RefreshDashboard()
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                DashboardGrid.ItemsSource = new List<DashboardRow>();
                return;
            }

            var repo = new CsvRepository();
            var rows = new List<DashboardRow>();

            foreach (var user in _users.OrderBy(u => u.SequenceNumber))
            {
                string userFile = Path.Combine(_csvFolder, $"{user.Id}.csv");
                string stato = "Fuori";
                Brush color = Brushes.IndianRed;
                string ultimaTimbrata = "-";

                DateTime? lastPunchTime = null;

                if (File.Exists(userFile))
                {
                    var punches = repo.Load(userFile).ToList();
                    if (punches.Any())
                    {
                        var last = punches.Last();
                        string tipo = last.ElementAtOrDefault(1) ?? string.Empty;
                        string tsRaw = last.ElementAtOrDefault(0) ?? string.Empty;

                        if (DateTime.TryParse(tsRaw, out var ts))
                        {
                            lastPunchTime = ts;
                            ultimaTimbrata = ts.ToString("dd/MM/yyyy HH:mm");
                        }
                        else if (!string.IsNullOrWhiteSpace(tsRaw))
                            ultimaTimbrata = tsRaw;

                        if (string.Equals(tipo, "Entrata", StringComparison.OrdinalIgnoreCase))
                        {
                            stato = "Dentro";
                            color = Brushes.MediumSeaGreen;
                        }
                    }
                }

                rows.Add(new DashboardRow
                {
                    Matricola = $"{Math.Max(0, user.SequenceNumber):D3}",
                    Nome = $"{user.Nome} {user.Cognome}".Trim(),
                    Stato = stato,
                    StatoBrush = color,
                    UltimaTimbrata = ultimaTimbrata,
                    InizioDentro = stato == "Dentro" ? lastPunchTime : null,
                    TempoDentro = stato == "Dentro" ? FormattaTempoDentro(lastPunchTime) : "-"
                });
            }

            DashboardGrid.ItemsSource = rows;
            DashboardGrid.Items.Refresh();
        }

        private void DashboardTimer_Tick(object? sender, EventArgs e)
        {
            if (DashboardGrid.ItemsSource is not IEnumerable<DashboardRow> rows)
                return;

            foreach (var row in rows)
            {
                row.TempoDentro = row.Stato == "Dentro"
                    ? FormattaTempoDentro(row.InizioDentro)
                    : "-";
            }

            DashboardGrid.Items.Refresh();
        }

        private string FormattaTempoDentro(DateTime? inizioDentro)
        {
            if (!inizioDentro.HasValue)
                return "-";

            var span = DateTime.Now - inizioDentro.Value;
            if (span.TotalSeconds < 0)
                span = TimeSpan.Zero;

            return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
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

                    RefreshDashboard();

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
            RefreshDashboard();
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
                    LoadCompanyInfoUI();
                }
            }
        }

        private void LoadCompanyInfoUI()
        {
            var c = App.DatiAzienda ?? new CompanyInfo();
            AziendaRagioneSocialeBox.Text = c.RagioneSociale ?? string.Empty;
            AziendaIndirizzoBox.Text = c.Indirizzo ?? string.Empty;
            AziendaCittaBox.Text = c.Citta ?? string.Empty;
            AziendaPivaBox.Text = c.PartitaIva ?? string.Empty;
            AziendaTelefonoBox.Text = c.Telefono ?? string.Empty;
            AziendaEmailBox.Text = c.Email ?? string.Empty;
        }

        private void SaveCompanyInfo_Click(object sender, RoutedEventArgs e)
        {
            App.DatiAzienda = new CompanyInfo
            {
                RagioneSociale = AziendaRagioneSocialeBox.Text?.Trim() ?? string.Empty,
                Indirizzo = AziendaIndirizzoBox.Text?.Trim() ?? string.Empty,
                Citta = AziendaCittaBox.Text?.Trim() ?? string.Empty,
                PartitaIva = AziendaPivaBox.Text?.Trim() ?? string.Empty,
                Telefono = AziendaTelefonoBox.Text?.Trim() ?? string.Empty,
                Email = AziendaEmailBox.Text?.Trim() ?? string.Empty
            };

            App.SalvaDatiAzienda();
            AuditLogger.Log(_csvFolder, "save_company_info", $"ragione_sociale={App.DatiAzienda.RagioneSociale}");
            MessageBox.Show("Dati aziendali salvati.");
        }

        // ===========================
        //   SICUREZZA E PASSWORD
        // ===========================
        private void EnablePasswordCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (PasswordPanel != null)
            {
                PasswordPanel.IsEnabled = EnablePasswordCheck.IsChecked == true;
            }
        }

        private void SalvaPassword_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Seleziona prima la cartella dati.");
                return;
            }

            string pwdFilePath = Path.Combine(_csvFolder, "password.csv");

            if (EnablePasswordCheck.IsChecked == true)
            {
                var password = ServerPasswordBox.Password;
                if (string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show("Inserisci una password valida.");
                    return;
                }

                try
                {
                    File.WriteAllText(pwdFilePath, $"Password,{password}");
                    ServerPasswordBox.Password = string.Empty;
                    MessageBox.Show("Password salvata e attivata con successo.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore nel salvare la password: {ex.Message}");
                }
            }
            else
            {
                if (File.Exists(pwdFilePath))
                {
                    try
                    {
                        File.Delete(pwdFilePath);
                        MessageBox.Show("Protezione con password disattivata.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Errore nel rimuovere la password: {ex.Message}");
                    }
                }
                else
                {
                    MessageBox.Show("Protezione con password disattivata.");
                }
            }
        }

        private void CheckPasswordOnStartup()
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            string pwdFilePath = Path.Combine(_csvFolder, "password.csv");
            if (File.Exists(pwdFilePath))
            {
                try
                {
                    var lines = File.ReadAllLines(pwdFilePath);
                    if (lines.Length > 0 && lines[0].StartsWith("Password,"))
                    {
                        var parts = lines[0].Split(',');
                        if (parts.Length == 2)
                        {
                            string expectedPwd = parts[1];
                            var pwdWindow = new PasswordWindow(expectedPwd);
                            bool? result = pwdWindow.ShowDialog();

                            if (result != true || !pwdWindow.IsAuthenticated)
                            {
                                Application.Current.Shutdown();
                            }
                        }
                    }
                }
                catch
                {
                    // Errore nella lettura della password (forse file corrotto). Evitiamo di bloccare o blocchiamo a prescindere? 
                    // Per sicurezza, se c'è un file password e non possiamo leggerlo blocchiamo tutto.
                    MessageBox.Show("Impossibile leggere il file delle password. Accesso negato.", "Errore di sicurezza", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                }
            }
        }

        // ===========================
        //   GESTIONE ASSENZE
        // ===========================
        private void LoadAbsences()
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            string path = Path.Combine(_csvFolder, "assenze.csv");
            var repo = new AbsenceRepository();
            var limitDate = DateTime.Now.AddMonths(-3); // Mostriamo solo gli ultimi 3 mesi per non appesantire

            try
            {
                var assenze = repo.Load(path)
                                  .Where(a => a.Data >= limitDate)
                                  .OrderByDescending(a => a.Data)
                                  .ToList();

                // Creiamo un wrapper per la visualizzazione nella griglia
                var viewList = assenze.Select(a =>
                {
                    var u = _users.FirstOrDefault(x => x.Id == a.UserId);
                    string nome = u != null ? $"{u.Nome} {u.Cognome}" : a.UserId;
                    return new { OriginalRecord = a, Data = a.Data, Tipo = a.Tipo.ToString(), Ore = a.Ore, Note = $"{nome} - {a.Note}" };
                }).ToList();

                var grid = this.FindName("AbsencesGrid") as DataGrid;
                if (grid != null)
                {
                    grid.ItemsSource = viewList;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore caricamento assenze: {ex.Message}");
            }
        }

        private void AddAbsence_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                MessageBox.Show("Seleziona prima la cartella dati.");
                return;
            }

            var combo = this.FindName("AbsenceUserCombo") as ComboBox;
            var picker = this.FindName("AbsenceDatePicker") as DatePicker;
            var typeCombo = this.FindName("AbsenceTypeCombo") as ComboBox;
            var hoursBox = this.FindName("AbsenceHoursBox") as TextBox;
            var notesBox = this.FindName("AbsenceNotesBox") as TextBox;

            if (combo?.SelectedItem is not UserProfile user)
            {
                MessageBox.Show("Seleziona un dipendente.");
                return;
            }

            if (picker?.SelectedDate == null)
            {
                MessageBox.Show("Seleziona una data.");
                return;
            }

            AbsenceType tipo = AbsenceType.Ferie;
            if (typeCombo?.SelectedItem is ComboBoxItem cbi && cbi.Content != null)
            {
                Enum.TryParse(cbi.Content.ToString(), out tipo);
            }

            double ore = 0;
            if (hoursBox != null && !string.IsNullOrWhiteSpace(hoursBox.Text))
            {
                if (!double.TryParse(hoursBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out ore))
                {
                    MessageBox.Show("Formato ore non valido.");
                    return;
                }
            }

            var record = new AbsenceRecord
            {
                UserId = user.Id,
                Data = picker.SelectedDate.Value,
                Tipo = tipo,
                Ore = ore,
                Note = notesBox?.Text?.Trim()
            };

            try
            {
                var repo = new AbsenceRepository();
                string path = Path.Combine(_csvFolder, "assenze.csv");
                repo.Append(path, record);

                AuditLogger.Log(_csvFolder, "add_absence", $"userId={user.Id}; data={record.Data:yyyy-MM-dd}; tipo={record.Tipo}; ore={record.Ore}");

                // Pulisci form
                if (hoursBox != null) hoursBox.Text = "";
                if (notesBox != null) notesBox.Text = "";

                LoadAbsences();
                MessageBox.Show("Assenza registrata correttamente.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore salvataggio assenza: {ex.Message}");
            }
        }

        private void DeleteAbsence_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_csvFolder)) return;

            if (sender is Button btn && btn.DataContext != null)
            {
                dynamic ctx = btn.DataContext;
                AbsenceRecord recordToDelete = ctx.OriginalRecord;

                if (MessageBox.Show($"Sei sicuro di voler eliminare l'assenza del {recordToDelete.Data:dd/MM/yyyy} ({recordToDelete.Tipo})?",
                                    "Conferma eliminazione",
                                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        string path = Path.Combine(_csvFolder, "assenze.csv");
                        var repo = new AbsenceRepository();
                        var allAbsences = repo.Load(path);

                        // Filtra escludendo l'elemento da rimuovere (match esatto su ID, Data, Tipo, Ore)
                        var filteredAbsences = allAbsences.Where(a =>
                            !(a.UserId == recordToDelete.UserId &&
                              a.Data == recordToDelete.Data &&
                              a.Tipo == recordToDelete.Tipo &&
                              Math.Abs(a.Ore - recordToDelete.Ore) < 0.01)
                        ).ToList();

                        // Riscriviamo l'intero file CSV utilizzando la funzione CsvCodec usata in AbsenceRepository
                        var lines = filteredAbsences.Select(a => CsvCodec.BuildLine(new[]
                        {
                            a.UserId,
                            a.Data.ToString("yyyy-MM-dd"),
                            a.Tipo.ToString(),
                            a.Ore.ToString(CultureInfo.InvariantCulture),
                            a.Note ?? string.Empty
                        }));

                        SafeFileWriter.WriteAllLinesAtomic(path, lines);

                        AuditLogger.Log(_csvFolder, "delete_absence", $"userId={recordToDelete.UserId}; data={recordToDelete.Data:yyyy-MM-dd}; tipo={recordToDelete.Tipo}");
                        LoadAbsences();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Errore durante l'eliminazione: {ex.Message}");
                    }
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
