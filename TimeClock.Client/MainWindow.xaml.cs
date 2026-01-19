using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Client
{
    public partial class MainWindow : Window
    {
        private List<UserProfile> _users = new();
        private string _userFilter = string.Empty;
        private bool _isUpdatingFilterText;


        // CSV repository: lo usiamo SOLO per le timbrature (Id.csv)
        private readonly CsvRepository _repo = new();

        private string _csvFolder = string.Empty;

        // Opzioni JSON (case-insensitive per sicurezza)
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public MainWindow()
        {
            InitializeComponent();

            // Carica la cartella salvata nelle impostazioni
            _csvFolder = Properties.Settings.Default.CsvFolderPath;

            if (!string.IsNullOrWhiteSpace(_csvFolder) && Directory.Exists(_csvFolder))
            {
                SharedFolderTextBlock.Text = _csvFolder;
                LoadUsersFromJson();
            }
            else
            {
                SharedFolderTextBlock.Text = "Nessuna cartella selezionata";
                _csvFolder = string.Empty;
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
                    SharedFolderTextBlock.Text = _csvFolder;

                    // Salvataggio preferenza
                    Properties.Settings.Default.CsvFolderPath = _csvFolder;
                    Properties.Settings.Default.Save();

                    LoadUsersFromJson();
                }
            }
        }

        // Caricamento utenti dal JSON (utenti.json)
        private void LoadUsersFromJson()
        {
            UserComboBox.ItemsSource = null;
            _users.Clear();

            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            var path = Path.Combine(_csvFolder, "utenti.json");
            if (!File.Exists(path))
            {
                // opzionale: messaggio, oppure lasciare silenzioso
                // MessageBox.Show("File utenti.json non trovato nella cartella selezionata.");
                return;
            }

            try
            {
                var json = File.ReadAllText(path);

                // Nel tuo caso è un array JSON: [ { ... }, { ... } ]
                var users = JsonSerializer.Deserialize<List<UserProfile>>(json, _jsonOptions);

                _users = users ?? new List<UserProfile>();

                // Se vuoi ordinare per nome/cognome:
                _users = _users
                    .OrderBy(u => u.Cognome)
                    .ThenBy(u => u.Nome)
                    .ToList();

                ApplyUserFilter();

                // Mostra FullName se presente (nel tuo JSON c'è "000 - aaa aaa")
                // altrimenti mostra Nome
                // Se vuoi SOLO Nome, rimetti "Nome"
                UserComboBox.DisplayMemberPath = "FullName";

                // Se FullName a volte è vuoto, puoi invece usare il ToString() in UserProfile
                // o una proprietà calcolata; qui teniamo semplice.
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore lettura utenti.json: {ex.Message}");
            }
        }

        private void UserComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
        }

        private void UserFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFilterText)
                return;

            var sanitized = new string(UserFilterTextBox.Text.Where(char.IsDigit).ToArray());
            if (!string.Equals(UserFilterTextBox.Text, sanitized, StringComparison.Ordinal))
            {
                _isUpdatingFilterText = true;
                UserFilterTextBox.Text = sanitized;
                UserFilterTextBox.CaretIndex = sanitized.Length;
                _isUpdatingFilterText = false;
            }

            _userFilter = sanitized;
            ApplyUserFilter();
        }

        private void KeypadNumber_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string digit)
                return;

            _userFilter += digit;
            UpdateFilterTextBox();
            ApplyUserFilter();
        }

        private void KeypadClear_Click(object sender, RoutedEventArgs e)
        {
            _userFilter = string.Empty;
            UpdateFilterTextBox();
            ApplyUserFilter();
        }

        private void UpdateFilterTextBox()
        {
            _isUpdatingFilterText = true;
            UserFilterTextBox.Text = _userFilter;
            UserFilterTextBox.CaretIndex = _userFilter.Length;
            _isUpdatingFilterText = false;
        }

        private void ApplyUserFilter()
        {
            var selectedUser = UserComboBox.SelectedItem as UserProfile;
            var filteredUsers = _users.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_userFilter))
            {
                filteredUsers = filteredUsers.Where(user =>
                    (user.FullName ?? string.Empty).Contains(_userFilter, StringComparison.OrdinalIgnoreCase) ||
                    (user.Nome ?? string.Empty).Contains(_userFilter, StringComparison.OrdinalIgnoreCase) ||
                    (user.Cognome ?? string.Empty).Contains(_userFilter, StringComparison.OrdinalIgnoreCase) ||
                    (user.Id ?? string.Empty).Contains(_userFilter, StringComparison.OrdinalIgnoreCase));
            }

            var result = filteredUsers.ToList();
            UserComboBox.ItemsSource = result;

            if (selectedUser != null)
            {
                var match = result.FirstOrDefault(user => user.Id == selectedUser.Id);
                UserComboBox.SelectedItem = match;
            }

            UpdateButtonsState();
        }

        private void UpdateButtonsState()
        {
            EntrataButton.IsEnabled = false;
            UscitaButton.IsEnabled = false;

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
                    UscitaButton.IsEnabled = true;
                else
                    EntrataButton.IsEnabled = true;
            }
        }

        private void EntrataButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserComboBox.SelectedItem is not UserProfile user) return;

            string path = Path.Combine(_csvFolder, user.Id + ".csv");
            string line = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + ",Entrata";

            try
            {
                _repo.AppendLine(path, line);
                UpdateButtonsState();
            }
            catch (IOException ex)
            {
                MessageBox.Show($"Errore scrittura: {ex.Message}");
            }
        }

        private void UscitaButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserComboBox.SelectedItem is not UserProfile user) return;

            string path = Path.Combine(_csvFolder, user.Id + ".csv");
            string line = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + ",Uscita";

            try
            {
                _repo.AppendLine(path, line);
                UpdateButtonsState();
            }
            catch (IOException ex)
            {
                MessageBox.Show($"Errore scrittura: {ex.Message}");
            }
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
