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

namespace TimeClock.Client
{
    public partial class MainWindow : Window
    {
        private List<UserProfile> _users = new();
        private readonly CsvRepository _repo = new();
        private readonly ClientStatusViewModel _statusVm = new();

        private string _csvFolder = string.Empty;   // ? variabile definitiva per la cartella

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _statusVm;

            // ?? Carica la cartella salvata nelle impostazioni
            _csvFolder = Properties.Settings.Default.CsvFolderPath;

            if (!string.IsNullOrWhiteSpace(_csvFolder) && Directory.Exists(_csvFolder))
            {
                SharedFolderTextBox.Text = _csvFolder;
                LoadUsers();
                SetStatus("Cartella condivisa caricata.");
            }
            else
            {
                SharedFolderTextBox.Text = "Nessuna cartella selezionata";
                _csvFolder = string.Empty;
                SetStatus("Seleziona la cartella condivisa per iniziare.");
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
                    SharedFolderTextBox.Text = _csvFolder;

                    // ?? Salvataggio preferenza
                    Properties.Settings.Default.CsvFolderPath = _csvFolder;
                    Properties.Settings.Default.Save();

                    LoadUsers();
                    SetStatus("Cartella condivisa aggiornata.");
                }
            }
        }


        // Caricamento utenti dal CSV (compatibile formato nuovo e legacy)
        private void LoadUsers()
        {
            UserComboBox.ItemsSource = null;
            _users.Clear();

            if (string.IsNullOrWhiteSpace(_csvFolder))
            {
                SetStatus("Cartella non impostata.");
                return;
            }

            var path = Path.Combine(_csvFolder, "utenti.csv");
            if (!File.Exists(path))
            {
                SetStatus("File utenti.csv non trovato.");
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
            UserComboBox.ItemsSource = _users;
            UserComboBox.DisplayMemberPath = "FullName";
            SetStatus(_users.Count > 0
                ? $"Utenti caricati: {_users.Count}."
                : "Nessun utente disponibile.");
        }


        private void UserComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
                {
                    UscitaButton.IsEnabled = true;
                    SetStatus($"{user.FullName}: dentro (attesa uscita).");
                }
                else
                {
                    EntrataButton.IsEnabled = true;
                    SetStatus($"{user.FullName}: fuori (attesa entrata).");
                }
            }
            else
            {
                SetStatus("Seleziona un utente.");
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

        private void SetStatus(string message)
        {
            _statusVm.StatusText = message;
            _statusVm.RefreshStatusCommand.Execute(null);
        }

        private void SetLastAction(string message)
        {
            _statusVm.LastActionText = $"{DateTime.Now:HH:mm:ss} - {message}";
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
