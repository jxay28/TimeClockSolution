// ===============================
// MainWindow.xaml.cs (VERSIONE CORRETTA)
// Upgrade Dashboard: riquadri (Presenti/Assenti/Ore settimanali) + grafico colonne ingressi Lun-Dom
// ===============================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TimeClock.Core.Models;
using Forms = System.Windows.Forms;

namespace TimeClock.Server
{
    // DTO per il grafico colonne (Lun-Dom)
    public sealed class IngressiGiorno
    {
        public string Giorno { get; set; } = "";
        public int Conteggio { get; set; }
    }

    public partial class MainWindow : Window
    {
        private string _csvFolder = "";
        private List<UserProfile> _users = new();
        private List<HolidayRow> _holidays = new();

        private DispatcherTimer _refreshTimer; // timer per countdown dashboard
        private ObservableCollection<DashboardUserStatus> _dashboardUsers = new(); // lista collegata alla tabella DashboardGrid

        private UserExtrasRepository? _extrasRepo;

        private bool _loadingOvertimeUI;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ===============================
        //   NUOVE PROPRIETÀ DASHBOARD
        // ===============================
        public int PresentiOggi { get; set; }
        public int AssentiOggi { get; set; }
        public string OreSettimanaliTotali { get; set; } = "00:00";

        public ObservableCollection<IngressiGiorno> IngressiPerGiorno { get; set; } = new();

        // I CheckBox delle festività nazionali in XAML non hanno x:Name: li troviamo via VisualTree.
        private readonly List<CheckBox> _nationalHolidayCheckBoxes = new();

        // Mappa "testo checkbox" -> (mese,giorno) per le festività a data fissa.
        // (Pasqua e Lunedì dell'Angelo non sono qui perché sono mobili.)
        private static readonly Dictionary<string, (int Mese, int Giorno)> NationalHolidayMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["1 gennaio - Capodanno"] = (1, 1),
                ["6 gennaio - Epifania"] = (1, 6),
                ["25 aprile - Liberazione"] = (4, 25),
                ["1 maggio - Festa dei lavoratori"] = (5, 1),
                ["2 giugno - Festa della Repubblica"] = (6, 2),
                ["15 agosto - Ferragosto"] = (8, 15),
                ["1 novembre - Ognissanti"] = (11, 1),
                ["8 dicembre - Immacolata Concezione"] = (12, 8),
                ["25 dicembre - Natale"] = (12, 25),
                ["26 dicembre - Santo Stefano"] = (12, 26),
            };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadCsvFolderFromSettings();
            WireOvertimeTabEvents();
            RefreshAll();
            UsersGrid.CellEditEnding += UsersGrid_CellEditEnding;

            // Dashboard: collega lista al DataGrid
            DashboardGrid.ItemsSource = _dashboardUsers;

            // Timer per aggiornare il tempo trascorso (solo UI)
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(1);
            _refreshTimer.Tick += (s, ev) =>
            {
                UpdateDashboardTimers();
                // Se vuoi che i numeri in alto cambino "live" durante la giornata, puoi ricalcolare qui.
                // Lo facciamo in modo leggero: solo presenti/assenti.
                AggiornaRiepilogoDashboardSoloPresenze();
            };
            _refreshTimer.Start();

            // Carica stato iniziale (ultima timbratura di ciascun utente)
            LoadDashboardData();

            // Calcola riepilogo + grafico
            AggiornaRiepilogoDashboard();
            AggiornaGraficoIngressi();

            // Binding: il XAML può fare Binding a queste proprietà
            DataContext = this;
        }

        // ===============================
        //   DASHBOARD: CARICAMENTO STATO
        // ===============================
        private void LoadDashboardData()
        {
            _dashboardUsers.Clear();
            if (string.IsNullOrWhiteSpace(_csvFolder) || !Directory.Exists(_csvFolder)) return;

            foreach (var user in _users)
            {
                var userFile = Path.Combine(_csvFolder, $"{user.Id}.csv");
                if (!File.Exists(userFile)) continue;

                try
                {
                    // Legge l'ultima riga del file CSV per sapere se è ENTRATA o USCITA
                    var lastLine = File.ReadLines(userFile).LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
                    if (lastLine == null) continue;

                    var parts = lastLine.Split(';');
                    if (parts.Length < 2) parts = lastLine.Split(',');
                    if (parts.Length < 2) continue;

                    if (DateTime.TryParse(parts[0], out DateTime dt) && Enum.TryParse(parts[1], true, out PunchType tipo))
                    {
                        _dashboardUsers.Add(new DashboardUserStatus
                        {
                            UserId = user.Id,
                            NomeCompleto = $"{user.Nome} {user.Cognome}",
                            Stato = tipo.ToString().ToUpperInvariant(), // "ENTRATA" o "USCITA"
                            UltimaTimbratura = dt,
                            TempoTrascorso = ""
                        });
                    }
                }
                catch
                {
                    // errore lettura file singolo utente: ignora
                }
            }
        }

        private void UpdateDashboardTimers()
        {
            foreach (var item in _dashboardUsers)
            {
                if (item.Stato == "ENTRATA" && item.UltimaTimbratura.HasValue)
                {
                    TimeSpan diff = DateTime.Now - item.UltimaTimbratura.Value;
                    item.TempoTrascorso = $"{(int)diff.TotalHours:00}:{diff.Minutes:00}:{diff.Seconds:00}";
                }
                else
                {
                    item.TempoTrascorso = "--:--:--";
                }
            }
        }

        // ===============================
        //   RIEPILOGO (Presenti/Assenti/Ore settimana)
        // ===============================
        private void AggiornaRiepilogoDashboardSoloPresenze()
        {
            var oggi = DateTime.Today;

            PresentiOggi = _dashboardUsers.Count(u =>
                u.Stato == "ENTRATA" && u.UltimaTimbratura.HasValue && u.UltimaTimbratura.Value.Date == oggi);

            AssentiOggi = Math.Max(0, _users.Count - PresentiOggi);
            // Niente calcolo ore qui per non fare I/O ogni secondo.
        }

        private void AggiornaRiepilogoDashboard()
        {
            AggiornaRiepilogoDashboardSoloPresenze();

            // Ore totali settimana Lun-Ven di tutti i dipendenti
            // Calcolo robusto: per ogni utente, crea coppie Entrata/Uscita e somma solo i segmenti che cadono tra Lun e Ven.
            var startWeek = InizioSettimanaLunedi(DateTime.Today);
            var endWeek = startWeek.AddDays(5); // esclusivo: Sab 00:00

            double totalMinutes = 0;

            foreach (var user in _users)
            {
                var file = Path.Combine(_csvFolder, $"{user.Id}.csv");
                if (!File.Exists(file)) continue;

                List<(DateTime dt, PunchType tipo)> entries = new();

                try
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = line.Split(';');
                        if (parts.Length < 2) parts = line.Split(',');
                        if (parts.Length < 2) continue;

                        if (!DateTime.TryParse(parts[0].Trim(), out var dt)) continue;
                        if (dt < startWeek || dt >= endWeek) continue;

                        if (!Enum.TryParse(parts[1].Trim(), true, out PunchType tipo)) continue;

                        entries.Add((dt, tipo));
                    }
                }
                catch
                {
                    continue;
                }

                if (entries.Count == 0) continue;

                entries = entries.OrderBy(x => x.dt).ToList();

                // Coppie Entrata/Uscita
                DateTime? lastIn = null;
                foreach (var e in entries)
                {
                    if (e.tipo == PunchType.Entrata)
                    {
                        lastIn = e.dt;
                    }
                    else if (e.tipo == PunchType.Uscita)
                    {
                        if (lastIn.HasValue && e.dt > lastIn.Value)
                        {
                            var segStart = lastIn.Value;
                            var segEnd = e.dt;

                            // Clippa al range Lun-Ven
                            if (segEnd > startWeek && segStart < endWeek)
                            {
                                var a = segStart < startWeek ? startWeek : segStart;
                                var b = segEnd > endWeek ? endWeek : segEnd;
                                if (b > a)
                                    totalMinutes += (b - a).TotalMinutes;
                            }

                            lastIn = null;
                        }
                    }
                }
            }

            OreSettimanaliTotali = FormatMinutesAsHHmm((int)Math.Round(totalMinutes));
        }

        // ===============================
        //   GRAFICO INGRESSI Lun-Dom (conteggio ENTRATA)
        // ===============================
        private void AggiornaGraficoIngressi()
        {
            IngressiPerGiorno.Clear();

            // Ultimi 7 giorni (Lun-Dom della settimana corrente)
            var start = InizioSettimanaLunedi(DateTime.Today);
            var end = start.AddDays(7); // esclusivo

            var labels = new[] { "Lun", "Mar", "Mer", "Gio", "Ven", "Sab", "Dom" };
            var counts = new int[7];

            foreach (var user in _users)
            {
                var file = Path.Combine(_csvFolder, $"{user.Id}.csv");
                if (!File.Exists(file)) continue;

                try
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = line.Split(';');
                        if (parts.Length < 2) parts = line.Split(',');
                        if (parts.Length < 2) continue;

                        if (!DateTime.TryParse(parts[0].Trim(), out var dt)) continue;
                        if (dt < start || dt >= end) continue;

                        if (!Enum.TryParse(parts[1].Trim(), true, out PunchType tipo)) continue;

                        if (tipo == PunchType.Entrata)
                        {
                            // mappa DayOfWeek -> indice lun=0 ... dom=6
                            int idx = ((int)dt.DayOfWeek + 6) % 7;
                            counts[idx]++;
                        }
                    }
                }
                catch
                {
                    // ignora file problematico
                }
            }

            // Scala visiva per altezza barra (WPF puro): 1 unità = 4 px
            // Nota: in XAML l'altezza del Border è binding su Conteggio, quindi Conteggio deve essere "altezza".
            // Qui trasformiamo conteggi reali in altezza.
            int pxPerIngresso = 6;
            int minHeight = 6;

            for (int i = 0; i < 7; i++)
            {
                int h = Math.Max(minHeight, counts[i] * pxPerIngresso);

                IngressiPerGiorno.Add(new IngressiGiorno
                {
                    Giorno = labels[i],
                    Conteggio = h
                });
            }
        }

        private static DateTime InizioSettimanaLunedi(DateTime d)
        {
            d = d.Date;
            int diff = (7 + (d.DayOfWeek - DayOfWeek.Monday)) % 7;
            return d.AddDays(-diff);
        }

        private static string FormatMinutesAsHHmm(int minutes)
        {
            if (minutes < 0) minutes = 0;
            int h = minutes / 60;
            int m = minutes % 60;
            return $"{h:00}:{m:00}";
        }

        // ---------------------------
        // Top bar + menu laterale
        // ---------------------------
        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int idx))
                MainTabs.SelectedIndex = idx;
        }

        // ---------------------------
        // Dashboard: aggiornamento manuale
        // ---------------------------
        /// <summary>
        /// Gestisce il click sul pulsante di refresh della dashboard.
        /// Ricarica i dati dello stato utenti e aggiorna il grafico.
        /// </summary>
        private void RefreshDashboard_Click(object sender, RoutedEventArgs e)
        {
            // Carica nuovamente i dati sulla timbratura degli utenti
            LoadDashboardData();
            // Aggiorna il grafico degli ingressi
            AggiornaGraficoIngressi();
            // Forza l'aggiornamento della griglia
            DashboardGrid.Items.Refresh();
        }

        // ---------------------------
        // Cartella CSV
        // ---------------------------
        private void LoadCsvFolderFromSettings()
        {
            try { _csvFolder = Properties.Settings.Default.CsvFolderPath ?? ""; }
            catch { _csvFolder = ""; }

            CsvPathBox.Text = _csvFolder;
            _extrasRepo = Directory.Exists(_csvFolder) ? new UserExtrasRepository(_csvFolder) : null;
        }

        private bool EnsureCsvFolderSelected()
        {
            if (!string.IsNullOrWhiteSpace(_csvFolder) && Directory.Exists(_csvFolder))
                return true;

            MessageBox.Show(
                "Seleziona prima la cartella CSV (timbrature / utenti / festività).",
                "Cartella mancante",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        private void SelectCsvFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Seleziona la cartella che contiene i file CSV/JSON (timbrature, utenti, festività).",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(_csvFolder) ? _csvFolder : ""
            };

            if (dlg.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath))
                return;

            _csvFolder = dlg.SelectedPath;
            CsvPathBox.Text = _csvFolder;

            try
            {
                Properties.Settings.Default.CsvFolderPath = _csvFolder;
                Properties.Settings.Default.Save();
            }
            catch { /* ignore */ }

            _extrasRepo = Directory.Exists(_csvFolder) ? new UserExtrasRepository(_csvFolder) : null;

            RefreshAll();

            // Ricarica dashboard con nuova cartella
            LoadDashboardData();
            AggiornaRiepilogoDashboard();
            AggiornaGraficoIngressi();
        }

        // ---------------------------
        // Refresh
        // ---------------------------
        private void RefreshAll()
        {
            LoadUsers();
            LoadHolidays();
            ApplyGlobalOvertimeParamsToUI();
        }

        // ---------------------------
        // Utenti
        // ---------------------------
        private string UsersJsonPath => Path.Combine(_csvFolder, "utenti.json");
        private string UsersCsvLegacyPath => Path.Combine(_csvFolder, "utenti.csv");

        private void LoadUsers()
        {
            _users = new List<UserProfile>();

            if (!EnsureCsvFolderSelected())
            {
                UsersGrid.ItemsSource = null;
                return;
            }

            try
            {
                if (File.Exists(UsersJsonPath))
                {
                    var txt = File.ReadAllText(UsersJsonPath);
                    var loaded = JsonSerializer.Deserialize<List<UserProfile>>(txt, _jsonOptions);
                    if (loaded != null) _users = loaded;
                }
                else if (File.Exists(UsersCsvLegacyPath))
                {
                    _users = LoadUsersFromLegacyCsv(UsersCsvLegacyPath);
                    SaveUsers(); // migrazione automatica
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel caricamento utenti: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            UsersGrid.ItemsSource = null;
            UsersGrid.ItemsSource = _users;
        }

        private static List<UserProfile> LoadUsersFromLegacyCsv(string csvPath)
        {
            var list = new List<UserProfile>();

            foreach (var line in File.ReadLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;

                var parts = line.Split(';');
                if (parts.Length < 4) parts = line.Split(',');
                if (parts.Length < 4) continue;

                var u = new UserProfile();
                int idx = 0;

                if (int.TryParse(parts[0].Trim(), out var seq))
                {
                    u.SequenceNumber = seq;
                    idx = 1;
                }

                if (parts.Length > idx) u.Id = parts[idx++].Trim();
                if (parts.Length > idx) u.Nome = parts[idx++].Trim();
                if (parts.Length > idx) u.Cognome = parts[idx++].Trim();
                if (parts.Length > idx) u.Ruolo = parts[idx++].Trim();

                if (parts.Length > idx && DateTime.TryParse(parts[idx].Trim(), out var da)) u.DataAssunzione = da; idx++;
                if (parts.Length > idx && double.TryParse(parts[idx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var oreSett)) u.OreContrattoSettimanali = oreSett; idx++;
                if (parts.Length > idx && decimal.TryParse(parts[idx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var basePay)) u.CompensoOrarioBase = basePay; idx++;
                if (parts.Length > idx && decimal.TryParse(parts[idx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var extraPay)) u.CompensoOrarioExtra = extraPay; idx++;

                if (parts.Length > idx) u.OrarioIngresso1 = parts[idx++].Trim();
                if (parts.Length > idx) u.OrarioUscita1 = parts[idx++].Trim();
                if (parts.Length > idx) u.OrarioIngresso2 = parts[idx++].Trim();
                if (parts.Length > idx) u.OrarioUscita2 = parts[idx++].Trim();

                if (!string.IsNullOrWhiteSpace(u.Id))
                    list.Add(u);
            }

            return list;
        }

        private void SaveUsers()
        {
            if (!EnsureCsvFolderSelected()) return;

            try
            {
                File.WriteAllText(UsersJsonPath, JsonSerializer.Serialize(_users, _jsonOptions));

                var lines = _users
                    .OrderBy(u => u.SequenceNumber)
                    .Select(u => string.Join(";", new[]
                    {
                        u.SequenceNumber.ToString(CultureInfo.InvariantCulture),
                        u.Id ?? "",
                        u.Nome ?? "",
                        u.Cognome ?? "",
                        u.Ruolo ?? "",
                        u.DataAssunzione.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        u.OreContrattoSettimanali.ToString(CultureInfo.InvariantCulture),
                        u.CompensoOrarioBase.ToString(CultureInfo.InvariantCulture),
                        u.CompensoOrarioExtra.ToString(CultureInfo.InvariantCulture),
                        u.OrarioIngresso1 ?? "",
                        u.OrarioUscita1 ?? "",
                        u.OrarioIngresso2 ?? "",
                        u.OrarioUscita2 ?? ""
                    }))
                    .ToList();

                File.WriteAllLines(UsersCsvLegacyPath, lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel salvataggio utenti: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UsersGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            if (e.Row.Item is not UserProfile user)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    SaveUsers();

                    if (_extrasRepo != null)
                    {
                        double oreGiornaliere = user.OreContrattoSettimanali > 0
                            ? user.OreContrattoSettimanali / 5.0
                            : 8.0;

                        int giorniSettimanali = 5;
                        _extrasRepo.Set(user.Id, oreGiornaliere, giorniSettimanali);
                        _extrasRepo.Save();
                    }

                    // Aggiorna riepilogo dopo modifiche utenti
                    AggiornaRiepilogoDashboard();
                    AggiornaGraficoIngressi();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Errore salvataggio automatico:\n{ex.Message}",
                        "Errore",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }, DispatcherPriority.Background);
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCsvFolderSelected()) return;

            var wnd = new AddUserWindow(_csvFolder) { Owner = this };

            if (wnd.ShowDialog() == true && wnd.User != null)
            {
                if (_users.Any(u => string.Equals(u.Id, wnd.User.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("Esiste già un utente con lo stesso ID.", "Duplicato", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _users.Add(wnd.User);
                SaveUsers();

                try
                {
                    if (_extrasRepo != null)
                    {
                        var fallbackOreGiornaliere = (wnd.User.OreContrattoSettimanali > 0)
                            ? wnd.User.OreContrattoSettimanali / 5.0
                            : 8.0;

                        _extrasRepo.Set(wnd.User.Id, fallbackOreGiornaliere, 5);
                        _extrasRepo.Save();
                    }
                }
                catch { /* non bloccare */ }

                UsersGrid.ItemsSource = null;
                UsersGrid.ItemsSource = _users;

                // refresh dashboard
                LoadDashboardData();
                AggiornaRiepilogoDashboard();
                AggiornaGraficoIngressi();
            }
        }

        // ---------------------------
        // Festività (festivita.csv)
        // ---------------------------
        private string HolidaysCsvPath => Path.Combine(_csvFolder, "festivita.csv");

        private void LoadHolidays()
        {
            _holidays = new List<HolidayRow>();

            if (!EnsureCsvFolderSelected())
            {
                HolidayGrid.ItemsSource = null;
                return;
            }

            try
            {
                if (!File.Exists(HolidaysCsvPath))
                {
                    HolidayGrid.ItemsSource = null;
                    return;
                }

                foreach (var line in File.ReadLines(HolidaysCsvPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("#")) continue;

                    var parts = line.Split(';');
                    if (parts.Length < 2) parts = line.Split(',');
                    if (parts.Length < 2) continue;

                    if (!DateTime.TryParse(parts[0].Trim(), out var dt)) continue;
                    var desc = parts[1].Trim();

                    _holidays.Add(new HolidayRow { Date = dt.Date, Description = desc });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel caricamento festività: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            HolidayGrid.ItemsSource = null;
            HolidayGrid.ItemsSource = _holidays.OrderBy(h => h.Date).ToList();
        }

        private void SaveHolidays()
        {
            if (!EnsureCsvFolderSelected()) return;

            try
            {
                var lines = _holidays
                    .OrderBy(h => h.Date)
                    .Select(h => $"{h.Date:yyyy-MM-dd};{h.Description}")
                    .ToList();

                File.WriteAllLines(HolidaysCsvPath, lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel salvataggio festività: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddHoliday_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCsvFolderSelected()) return;

            var (ok, dt, desc) = ShowAddHolidayDialog();
            if (!ok) return;

            if (_holidays.Any(h => h.Date == dt.Date))
            {
                MessageBox.Show("Esiste già una festività in questa data.", "Duplicato", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _holidays.Add(new HolidayRow { Date = dt.Date, Description = desc });
            SaveHolidays();
            LoadHolidays();
        }

        private (bool ok, DateTime date, string desc) ShowAddHolidayDialog()
        {
            var w = new Window
            {
                Title = "Aggiungi festività",
                Width = 420,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new TextBlock { Text = "Data:", FontWeight = FontWeights.Bold });
            var dp = new DatePicker { SelectedDate = DateTime.Today, Margin = new Thickness(0, 6, 0, 8) };
            Grid.SetRow(dp, 1);
            root.Children.Add(dp);

            var descLbl = new TextBlock { Text = "Descrizione:", FontWeight = FontWeights.Bold };
            Grid.SetRow(descLbl, 2);
            root.Children.Add(descLbl);

            var tb = new TextBox { Margin = new Thickness(0, 6, 0, 8) };
            Grid.SetRow(tb, 3);
            root.Children.Add(tb);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 10, 8, 0) };
            var cancelBtn = new Button { Content = "Annulla", Width = 90, Margin = new Thickness(0, 10, 0, 0) };

            bool accepted = false;
            okBtn.Click += (_, __) => { accepted = true; w.Close(); };
            cancelBtn.Click += (_, __) => { accepted = false; w.Close(); };

            buttons.Children.Add(okBtn);
            buttons.Children.Add(cancelBtn);

            Grid.SetRow(buttons, 4);
            root.Children.Add(buttons);

            w.Content = root;
            w.ShowDialog();

            if (!accepted || dp.SelectedDate == null) return (false, DateTime.MinValue, "");

            var d = dp.SelectedDate.Value.Date;
            var t = (tb.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t)) t = "Festività";

            return (true, d, t);
        }

        // ---------------------------
        // Report
        // ---------------------------
        private void GenerateReport_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCsvFolderSelected()) return;

            if (_users.Count == 0)
            {
                MessageBox.Show("Nessun utente caricato. Aggiungi almeno un utente.", "Utenti mancanti",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var w = new ReportWindow(_csvFolder, _users) { Owner = this };
            w.ShowDialog();
        }

        // --------------------------------------------------------------------
        // PARAMETRI STRAORDINARI (parametri_straordinari.json)
        // --------------------------------------------------------------------
        private void OvertimeTab_Loaded(object sender, RoutedEventArgs e)
        {
            HookNationalHolidayCheckboxes();
            ApplyGlobalOvertimeParamsToUI();
        }

        private void WireOvertimeTabEvents()
        {
            OvertimeSlider.Minimum = 0;
            OvertimeSlider.Maximum = 30;
            OvertimeSlider.TickFrequency = 15;
            OvertimeSlider.IsSnapToTickEnabled = true;

            OvertimeSlider.ValueChanged += OvertimeSlider_ValueChanged;

            SaturdayCheck.Checked += WeekendCheck_Changed;
            SaturdayCheck.Unchecked += WeekendCheck_Changed;

            SundayCheck.Checked += WeekendCheck_Changed;
            SundayCheck.Unchecked += WeekendCheck_Changed;

            AddCustomHolidayButton.Click += AddCustomHolidayButton_Click;
            SaveOvertimeParamsButton.Click += SaveOvertimeParamsButton_Click;
            CancelOvertimeParamsButton.Click += CancelOvertimeParamsButton_Click;

            HookNationalHolidayCheckboxes();
        }

        private void ApplyGlobalOvertimeParamsToUI()
        {
            if (App.ParametriGlobali == null)
            {
                App.ParametriGlobali = new ParametriStraordinari();
                App.SalvaParametriGlobali();
            }

            _loadingOvertimeUI = true;
            try
            {
                int block = SnapBlock(App.ParametriGlobali.SogliaMinutiStraordinario);
                OvertimeSlider.Value = block;
                OvertimeValue.Text = block == 0 ? "0 minuti" : $"{block} minuti";

                App.ParametriGlobali.GiorniSempreFestivi ??= new List<DayOfWeek>();
                SaturdayCheck.IsChecked = App.ParametriGlobali.GiorniSempreFestivi.Contains(DayOfWeek.Saturday);
                SundayCheck.IsChecked = App.ParametriGlobali.GiorniSempreFestivi.Contains(DayOfWeek.Sunday);

                App.ParametriGlobali.FestivitaRicorrenti ??= new List<GiornoMese>();

                foreach (var cb in _nationalHolidayCheckBoxes)
                {
                    var key = (cb.Content?.ToString() ?? "").Trim();
                    if (!NationalHolidayMap.TryGetValue(key, out var md)) continue;

                    cb.IsChecked = App.ParametriGlobali.FestivitaRicorrenti
                        .Any(x => x.Mese == md.Mese && x.Giorno == md.Giorno);
                }

                App.ParametriGlobali.FestivitaAggiuntive ??= new List<DateTime>();
                RefreshCustomHolidayList();
            }
            finally
            {
                _loadingOvertimeUI = false;
            }
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

        private void OvertimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingOvertimeUI) return;

            int snapped = SnapBlock((int)Math.Round(e.NewValue));

            if (Math.Abs(e.NewValue - snapped) > 0.001)
            {
                _loadingOvertimeUI = true;
                OvertimeSlider.Value = snapped;
                _loadingOvertimeUI = false;
                return;
            }

            OvertimeValue.Text = snapped == 0 ? "0 minuti" : $"{snapped} minuti";

            App.ParametriGlobali.SogliaMinutiStraordinario = snapped;
            App.SalvaParametriGlobali();
        }

        private void WeekendCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingOvertimeUI) return;

            App.ParametriGlobali.GiorniSempreFestivi ??= new List<DayOfWeek>();

            UpdateDayOfWeek(App.ParametriGlobali.GiorniSempreFestivi, DayOfWeek.Saturday, SaturdayCheck.IsChecked == true);
            UpdateDayOfWeek(App.ParametriGlobali.GiorniSempreFestivi, DayOfWeek.Sunday, SundayCheck.IsChecked == true);

            App.SalvaParametriGlobali();
        }

        private static void UpdateDayOfWeek(List<DayOfWeek> list, DayOfWeek day, bool enabled)
        {
            if (enabled)
            {
                if (!list.Contains(day)) list.Add(day);
            }
            else
            {
                list.RemoveAll(d => d == day);
            }
        }

        private void HookNationalHolidayCheckboxes()
        {
            _nationalHolidayCheckBoxes.Clear();

            foreach (var cb in FindVisualChildren<CheckBox>(this))
            {
                var content = (cb.Content?.ToString() ?? "").Trim();
                if (!NationalHolidayMap.ContainsKey(content)) continue;

                _nationalHolidayCheckBoxes.Add(cb);

                cb.Checked -= NationalHolidayCheck_Changed;
                cb.Unchecked -= NationalHolidayCheck_Changed;
                cb.Checked += NationalHolidayCheck_Changed;
                cb.Unchecked += NationalHolidayCheck_Changed;
            }
        }

        private void NationalHolidayCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingOvertimeUI) return;

            if (sender is not CheckBox cb) return;
            var keyText = (cb.Content?.ToString() ?? "").Trim();

            if (!NationalHolidayMap.TryGetValue(keyText, out var md)) return;

            App.ParametriGlobali.FestivitaRicorrenti ??= new List<GiornoMese>();

            bool isChecked = cb.IsChecked == true;

            var esistente = App.ParametriGlobali.FestivitaRicorrenti
                .FirstOrDefault(x => x.Mese == md.Mese && x.Giorno == md.Giorno);

            if (isChecked)
            {
                if (esistente == null)
                {
                    App.ParametriGlobali.FestivitaRicorrenti.Add(new GiornoMese
                    {
                        Mese = md.Mese,
                        Giorno = md.Giorno
                    });
                }
            }
            else
            {
                if (esistente != null)
                    App.ParametriGlobali.FestivitaRicorrenti.Remove(esistente);
            }

            App.SalvaParametriGlobali();
        }

        private void AddCustomHolidayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loadingOvertimeUI) return;

            if (CustomHolidayPicker.SelectedDate == null)
            {
                MessageBox.Show("Seleziona una data.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dt = CustomHolidayPicker.SelectedDate.Value.Date;

            App.ParametriGlobali.FestivitaAggiuntive ??= new List<DateTime>();
            if (!App.ParametriGlobali.FestivitaAggiuntive.Any(x => x.Date == dt))
            {
                App.ParametriGlobali.FestivitaAggiuntive.Add(dt);
                App.SalvaParametriGlobali();
                RefreshCustomHolidayList();
            }
        }

        private void RemoveCustomHoliday_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not DateTime dt) return;

            App.ParametriGlobali.FestivitaAggiuntive ??= new List<DateTime>();
            App.ParametriGlobali.FestivitaAggiuntive.RemoveAll(x => x.Date == dt.Date);

            App.SalvaParametriGlobali();
            RefreshCustomHolidayList();
        }

        private void RefreshCustomHolidayList()
        {
            CustomHolidayList.Items.Clear();

            var list = (App.ParametriGlobali.FestivitaAggiuntive ?? new List<DateTime>())
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            foreach (var dt in list)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                sp.Children.Add(new TextBlock
                {
                    Text = dt.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("it-IT")),
                    Width = 110,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var btn = new Button
                {
                    Content = "Rimuovi",
                    Tag = dt,
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(8, 2, 8, 2)
                };
                btn.Click += RemoveCustomHoliday_Click;

                sp.Children.Add(btn);

                CustomHolidayList.Items.Add(sp);
            }
        }

        private void SaveOvertimeParamsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.SalvaParametriGlobali();
                MessageBox.Show("Parametri straordinari salvati.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel salvataggio parametri: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelOvertimeParamsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parametri_straordinari.json");
                if (File.Exists(file))
                {
                    var json = File.ReadAllText(file);
                    var p = JsonSerializer.Deserialize<ParametriStraordinari>(json);
                    if (p != null) App.ParametriGlobali = p;
                }

                ApplyGlobalOvertimeParamsToUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel ripristino parametri: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------------------
        // Utils
        // ---------------------------
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);

                if (child is T t)
                    yield return t;

                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        private sealed class HolidayRow
        {
            public DateTime Date { get; set; }
            public string Description { get; set; } = "";
        }

        public sealed class DashboardUserStatus : System.ComponentModel.INotifyPropertyChanged
        {
            public string UserId { get; set; } = "";
            public string NomeCompleto { get; set; } = "";
            public string Stato { get; set; } = ""; // "ENTRATA" o "USCITA"
            public DateTime? UltimaTimbratura { get; set; }

            private string _tempoTrascorso = "";
            public string TempoTrascorso
            {
                get => _tempoTrascorso;
                set { _tempoTrascorso = value; OnPropertyChanged(nameof(TempoTrascorso)); }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }

    // Converter per colorare lo stato in dashboard (già presente nel tuo progetto)
    public class StatusToColorConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? stato = value as string;
            if (!string.IsNullOrEmpty(stato) &&
               (stato.Equals("ENTRATA", StringComparison.OrdinalIgnoreCase) ||
                stato.Equals("INGRESSO", StringComparison.OrdinalIgnoreCase)))
            {
                return new SolidColorBrush(Colors.LightGreen);
            }
            return new SolidColorBrush(Colors.Red);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
