using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeClock.Core.Models;
using Forms = System.Windows.Forms;

namespace TimeClock.Server
{
    public partial class MainWindow : Window
    {
        private string _csvFolder = "";
        private List<UserProfile> _users = new();
        private List<HolidayRow> _holidays = new();

        private UserExtrasRepository? _extrasRepo;

        private bool _loadingOvertimeUI;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

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

                // se il primo campo è numerico, lo consideriamo SequenceNumber
                if (int.TryParse(parts[0].Trim(), out var seq))
                {
                    u.SequenceNumber = seq;
                    idx = 1;
                }

                if (parts.Length > idx) u.Id = parts[idx++].Trim();
                if (parts.Length > idx) u.Nome = parts[idx++].Trim();
                if (parts.Length > idx) u.Cognome = parts[idx++].Trim();
                if (parts.Length > idx) u.Ruolo = parts[idx++].Trim();

                // opzionali (se presenti)
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

                // export legacy
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

                // inizializza extras (ore giornaliere previste) con un default sensato
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
        private void WireOvertimeTabEvents()
        {
            // Forziamo la barra ai valori richiesti (0-15-30).
            // Se in XAML era diverso, così la UI torna coerente.
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

            // Vecchio switch: lo lasciamo visibile ma non lo usiamo più.
           // UseExpectedHoursCheck.IsChecked = false;
           // UseExpectedHoursCheck.IsEnabled = false;

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

                App.ParametriGlobali.FestivitaRicorrenti ??= new List<(int Mese, int Giorno)>();
                foreach (var cb in _nationalHolidayCheckBoxes)
                {
                    var key = (cb.Content?.ToString() ?? "").Trim();
                    if (!NationalHolidayMap.TryGetValue(key, out var md)) continue;

                    cb.IsChecked = App.ParametriGlobali.FestivitaRicorrenti.Any(x => x.Mese == md.Mese && x.Giorno == md.Giorno);
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

            // se non è esattamente un valore valido, lo forziamo
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

            var key = (cb.Content?.ToString() ?? "").Trim();
            if (!NationalHolidayMap.TryGetValue(key, out var md)) return;

            App.ParametriGlobali.FestivitaRicorrenti ??= new List<(int Mese, int Giorno)>();
            bool enabled = cb.IsChecked == true;

            if (enabled)
            {
                if (!App.ParametriGlobali.FestivitaRicorrenti.Any(x => x.Mese == md.Mese && x.Giorno == md.Giorno))
                    App.ParametriGlobali.FestivitaRicorrenti.Add((md.Mese, md.Giorno));
            }
            else
            {
                App.ParametriGlobali.FestivitaRicorrenti.RemoveAll(x => x.Mese == md.Mese && x.Giorno == md.Giorno);
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
            // le modifiche vengono già salvate in tempo reale: qui solo feedback
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
            // ripristina dal file parametri_straordinari.json
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

        private class HolidayRow
        {
            public DateTime Date { get; set; }
            public string Description { get; set; } = "";
        }
    }
}
