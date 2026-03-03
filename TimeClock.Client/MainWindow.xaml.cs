using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TimeClock.Core.Models;
using TimeClock.Core.Services;
using System.Windows.Input;
using TimeClock.Client.ViewModels;
using System.Windows.Threading;

namespace TimeClock.Client
{
    public partial class MainWindow : Window
    {
        private List<UserProfile> _users = new();
        private List<UserProfile> _filteredUsers = new();
        private readonly CsvRepository _repo = new();
        private readonly ClientStatusViewModel _statusVm = new();
        private string _numericFilter = string.Empty;

        private string _csvFolder = string.Empty;   // ? variabile definitiva per la cartella
        private DispatcherTimer _folderButtonTimer;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _statusVm;

            _folderButtonTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _folderButtonTimer.Tick += FolderButtonTimer_Tick;

            // ?? Carica la cartella salvata nelle impostazioni
            _csvFolder = Properties.Settings.Default.CsvFolderPath;

            if (!string.IsNullOrWhiteSpace(_csvFolder) && Directory.Exists(_csvFolder))
            {
                LoadUsers();
            }
            else
            {
                _csvFolder = string.Empty;
                _statusVm.LastActionText = "Seleziona la cartella con l'ingranaggio.";
            }
        }

        private void SelectCsvFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    _csvFolder = dialog.SelectedPath;

                    // ?? Salvataggio preferenza
                    Properties.Settings.Default.CsvFolderPath = _csvFolder;
                    Properties.Settings.Default.Save();

                    LoadUsers();
                    SetLastAction($"Cartella aggiornata: {_csvFolder}");
                }
            }
        }

        private void FolderButtonTimer_Tick(object? sender, EventArgs e)
        {
            _folderButtonTimer.Stop();
            // Trigger action programmatically
            SelectCsvFolder_Click(this, new RoutedEventArgs());
        }

        private void SelectFolder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _folderButtonTimer.Start();
        }

        private void SelectFolder_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _folderButtonTimer.Stop();
        }

        private void SelectFolder_MouseLeave(object sender, MouseEventArgs e)
        {
            _folderButtonTimer.Stop();
        }


        // Caricamento utenti dal CSV (compatibile formato nuovo e legacy)
        private void LoadUsers()
        {
            UserComboBox.ItemsSource = null;
            _users.Clear();

            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                NumericFilterTextBlock.Text = "Filtro numerico: -";
                return;
            }

            var path = Path.Combine(_csvFolder, "utenti.csv");
            if (!File.Exists(path))
            {
                NumericFilterTextBlock.Text = "Filtro numerico: -";
                return;
            }

            var list = new List<UserProfile>();

            foreach (var f in _repo.Load(path))
            {
                if (f.Length == 0 || string.IsNullOrWhiteSpace(f[0]))
                    continue;

                // Formato attuale (server):
                // 0 Id, 1 SequenceNumber, 2 Nome, 3 Cognome, 4 Ruolo, 5 DataAssunzione, 6 Ore, 7 Base, 8 Extra, ...
                // Formato legacy (vecchio client):
                // 0 Id, 1 Nome, 2 Cognome, 3 Ruolo, 4 DataAssunzione, 5 Ore, 6 Base, 7 Extra
                bool hasSequence = int.TryParse(f.ElementAtOrDefault(1), out var seq);

                int offset = hasSequence ? 1 : 0;

                list.Add(new UserProfile
                {
                    Id = f.ElementAtOrDefault(0) ?? string.Empty,
                    SequenceNumber = hasSequence ? seq : 0,
                    Nome = f.ElementAtOrDefault(1 + offset) ?? string.Empty,
                    Cognome = f.ElementAtOrDefault(2 + offset) ?? string.Empty,
                    Ruolo = f.ElementAtOrDefault(3 + offset) ?? string.Empty,
                    DataAssunzione = DateTime.TryParse(f.ElementAtOrDefault(4 + offset), out var dt) ? dt : DateTime.Today,
                    OreContrattoSettimanali = double.TryParse(
                        f.ElementAtOrDefault(5 + offset)?.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var ore) ? ore : 0,
                    CompensoOrarioBase = decimal.TryParse(
                        f.ElementAtOrDefault(6 + offset)?.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var baseSal) ? baseSal : 0,
                    CompensoOrarioExtra = decimal.TryParse(
                        f.ElementAtOrDefault(7 + offset)?.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var extraSal) ? extraSal : 0
                });
            }

            _users = list;
            ApplyNumericFilter();
            SetLastAction(_users.Count > 0
                ? $"Utenti caricati: {_users.Count}"
                : "Nessun utente disponibile");
        }


        private void UserComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
        }

        private void UpdateButtonsState()
        {
            EntrataButton.IsEnabled = false;
            UscitaButton.IsEnabled = false;
            _statusVm.ShowSummary = false;

            if (UserComboBox.SelectedItem is UserProfile user && !string.IsNullOrWhiteSpace(_csvFolder))
            {
                string userFile = Path.Combine(_csvFolder, user.Id + ".csv");
                string lastType = string.Empty;

                if (File.Exists(userFile))
                {
                    var lines = _repo.Load(userFile).ToList();
                    if (lines.Any())
                    {
                        var last = lines.Last();
                        if (last.Length > 1)
                            lastType = last[1];
                    }
                }

                if (string.Equals(lastType, "Entrata", StringComparison.OrdinalIgnoreCase))
                {
                    UscitaButton.IsEnabled = true;
                }
                else
                {
                    EntrataButton.IsEnabled = true;
                }

                _statusVm.LastActionText = GetLastActionForUser(userFile);
                UpdateMonthlySummary(user, userFile);
            }
            else
            {
                _statusVm.LastActionText = "N/A";
            }
        }

        private void UpdateMonthlySummary(UserProfile user, string userFile)
        {
            try
            {
                if (!File.Exists(userFile))
                    return;

                DateTime today = DateTime.Today;
                var currentMonthRecords = _repo.Load(userFile)
                    .Select(cols => new TimeCardEntry
                    {
                        DataOra = DateTime.TryParse(cols.ElementAtOrDefault(0), out var dt) ? dt : DateTime.MinValue,
                        Tipo = string.Equals(cols.ElementAtOrDefault(1), "Entrata", StringComparison.OrdinalIgnoreCase)
                            ? PunchType.Entrata : PunchType.Uscita,
                        UserId = user.Id
                    })
                    .Where(e => e.DataOra.Year == today.Year && e.DataOra.Month == today.Month)
                    .ToList();

                if (!currentMonthRecords.Any())
                {
                    _statusVm.MonthlyOrdinaryHours = "00:00";
                    _statusVm.MonthlyExtraHours = "00:00";
                    _statusVm.ShowSummary = true;
                    return;
                }

                var calculator = new WorkTimeCalculator();
                var pairs = calculator.BuildPairsCrossDay(currentMonthRecords);

                // Raggruppiamo le coppie per giorno di competenza (usando l'ingresso)
                var dailyPairs = pairs.GroupBy(p => p.In.Date);

                // Cerchiamo le impostazioni globali aziendali se presenti
                WorkTimePolicy policy = WorkTimePolicy.Default;
                string settingsPath = Path.Combine(_csvFolder, "settings.json");
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        string json = File.ReadAllText(settingsPath);
                        var settingsList = System.Text.Json.JsonSerializer.Deserialize<List<ParametriStraordinari>>(json);
                        if (settingsList != null && settingsList.Count > 0)
                        {
                            policy = WorkTimePolicyFactory.FromGlobalParameters(settingsList[0]);
                        }
                    }
                    catch { /* ignoriamo errori di lettura policy, usiamo default */ }
                }

                int totalOrdMinutes = 0;
                int totalExtMinutes = 0;

                foreach (var dayGroup in dailyPairs)
                {
                    var result = calculator.CalculateDay(user, dayGroup.Key, dayGroup, policy);
                    totalOrdMinutes += result.OrdinaryMinutes;
                    totalExtMinutes += result.OvertimeMinutes;
                }

                _statusVm.MonthlyOrdinaryHours = WorkTimeCalculator.FormatMinutes(totalOrdMinutes);
                _statusVm.MonthlyExtraHours = WorkTimeCalculator.FormatMinutes(totalExtMinutes);
                _statusVm.ShowSummary = true;
            }
            catch (Exception)
            {
                _statusVm.ShowSummary = false;
            }
        }


        private void EntrataButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserComboBox.SelectedItem is not UserProfile user) return;

            string path = Path.Combine(_csvFolder, user.Id + ".csv");
            string line = CsvCodec.BuildLine(new[]
            {
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                "Entrata"
            });

            try
            {
                _repo.AppendLine(path, line);
                AuditLogger.Log(_csvFolder, "punch_in", $"user={user.Id}; at={DateTime.Now:yyyy-MM-ddTHH:mm:ss}");
                UpdateButtonsState();
                SetLastAction($"Entrata registrata per {user.FullName}");
            }
            catch (IOException ex)
            {
                MessageBox.Show($"Errore scrittura: {ex.Message}");
                SetLastAction("Errore durante registrazione entrata");
            }
        }

        private void UscitaButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserComboBox.SelectedItem is not UserProfile user) return;

            string path = Path.Combine(_csvFolder, user.Id + ".csv");
            string line = CsvCodec.BuildLine(new[]
            {
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                "Uscita"
            });

            try
            {
                _repo.AppendLine(path, line);
                AuditLogger.Log(_csvFolder, "punch_out", $"user={user.Id}; at={DateTime.Now:yyyy-MM-ddTHH:mm:ss}");
                UpdateButtonsState();
                SetLastAction($"Uscita registrata per {user.FullName}");
            }
            catch (IOException ex)
            {
                MessageBox.Show($"Errore scrittura: {ex.Message}");
                SetLastAction("Errore durante registrazione uscita");
            }
        }

        private void ApplyNumericFilter()
        {
            string selectedId = (UserComboBox.SelectedItem as UserProfile)?.Id ?? string.Empty;

            _filteredUsers = _users
                .Where(u => MatchesNumericFilter(u, _numericFilter))
                .ToList();

            UserComboBox.ItemsSource = null;
            UserComboBox.ItemsSource = _filteredUsers;
            UserComboBox.DisplayMemberPath = "FullName";

            var selected = _filteredUsers.FirstOrDefault(u => u.Id == selectedId);
            if (selected != null)
                UserComboBox.SelectedItem = selected;
            else if (_filteredUsers.Count == 1)
                UserComboBox.SelectedIndex = 0;

            NumericFilterTextBlock.Text = string.IsNullOrEmpty(_numericFilter)
                ? "Filtro numerico: -"
                : $"Filtro numerico: {_numericFilter}";
        }

        private void SetLastAction(string message)
        {
            _statusVm.LastActionText = $"{DateTime.Now:dd/MM/yyyy HH:mm:ss} - {message}";
        }

        private bool MatchesNumericFilter(UserProfile user, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string sequenceDigits = $"{Math.Max(0, user.SequenceNumber):D3}";
            return sequenceDigits.Contains(filter, StringComparison.Ordinal);
        }

        private void NumericKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string digit || digit.Length != 1)
                return;

            AppendNumericFilterDigit(digit[0]);
        }

        private void NumericBackspace_Click(object sender, RoutedEventArgs e)
        {
            if (_numericFilter.Length == 0)
                return;

            _numericFilter = _numericFilter[..^1];
            ApplyNumericFilter();
            UpdateButtonsState();
        }

        private void NumericClear_Click(object sender, RoutedEventArgs e)
        {
            if (_numericFilter.Length == 0)
                return;

            _numericFilter = string.Empty;
            ApplyNumericFilter();
            UpdateButtonsState();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (TryGetDigitFromKey(e.Key, out var digit))
            {
                AppendNumericFilterDigit(digit);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back)
            {
                NumericBackspace_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete || e.Key == Key.Escape)
            {
                NumericClear_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void AppendNumericFilterDigit(char digit)
        {
            if (!char.IsDigit(digit))
                return;

            _numericFilter += digit;
            ApplyNumericFilter();
            UpdateButtonsState();
        }

        private static bool TryGetDigitFromKey(Key key, out char digit)
        {
            digit = '\0';

            if (key >= Key.D0 && key <= Key.D9)
            {
                digit = (char)('0' + (key - Key.D0));
                return true;
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                digit = (char)('0' + (key - Key.NumPad0));
                return true;
            }

            return false;
        }

        private string GetLastActionForUser(string userFile)
        {
            if (!File.Exists(userFile))
                return "Nessuna timbratura registrata";

            var lines = _repo.Load(userFile).ToList();
            if (!lines.Any())
                return "Nessuna timbratura registrata";

            var last = lines.Last();
            if (last.Length < 2)
                return "Ultima timbratura non valida";

            var tipo = last[1];
            var when = DateTime.TryParse(last[0], out var dt)
                ? dt.ToString("dd/MM/yyyy HH:mm")
                : last[0];

            return $"Ultima: {tipo} ({when})";
        }


        // Permette di trascinare la finestra
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
    }
}
