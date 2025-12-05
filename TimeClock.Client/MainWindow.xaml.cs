using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TimeClock.Core.Models;
using TimeClock.Core.Services;
using System.Windows.Input;

namespace TimeClock.Client
{
    public partial class MainWindow : Window
    {
        private List<UserProfile> _users = new();
        private readonly CsvRepository _repo = new();

        private string _csvFolder = string.Empty;   // ? variabile definitiva per la cartella

        public MainWindow()
        {
            InitializeComponent();

            // ?? Carica la cartella salvata nelle impostazioni
            _csvFolder = Properties.Settings.Default.CsvFolderPath;

            if (!string.IsNullOrWhiteSpace(_csvFolder) && Directory.Exists(_csvFolder))
            {
                SharedFolderTextBox.Text = _csvFolder;
                LoadUsers();
            }
            else
            {
                SharedFolderTextBox.Text = "Nessuna cartella selezionata";
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
                    SharedFolderTextBox.Text = _csvFolder;

                    // ?? Salvataggio preferenza
                    Properties.Settings.Default.CsvFolderPath = _csvFolder;
                    Properties.Settings.Default.Save();

                    LoadUsers();
                }
            }
        }


        // ?? Caricamento utenti dal CSV
        private void LoadUsers()
        {
            UserComboBox.ItemsSource = null;
            _users.Clear();

            if (string.IsNullOrWhiteSpace(_csvFolder))
                return;

            var path = Path.Combine(_csvFolder, "utenti.csv");
            if (!File.Exists(path))
                return;

            _users = _repo.Load(path)
                .Select(f => new UserProfile
                {
                    Id = f[0],
                    Nome = f[1],
                    Cognome = f[2],
                    Ruolo = f[3],
                    DataAssunzione = DateTime.Parse(f[4]),
                    OreContrattoSettimanali = double.Parse(f[5]),
                    CompensoOrarioBase = decimal.Parse(f[6]),
                    CompensoOrarioExtra = decimal.Parse(f[7])
                })
                .ToList();

            UserComboBox.ItemsSource = _users;
            UserComboBox.DisplayMemberPath = "Nome";
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
