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
        private string _sharedFolder = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _sharedFolder = dlg.SelectedPath;
                SharedFolderTextBox.Text = _sharedFolder;
                LoadUsers();
            }
        }

        private void LoadUsers()
        {
            UserComboBox.ItemsSource = null;
            _users.Clear();
            if (string.IsNullOrWhiteSpace(_sharedFolder))
                return;

            var path = Path.Combine(_sharedFolder, "utenti.csv");
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
            if (UserComboBox.SelectedItem is UserProfile user && !string.IsNullOrWhiteSpace(_sharedFolder))
            {
                string userFile = Path.Combine(_sharedFolder, user.Id + ".csv");
                string lastType = string.Empty;
                var lines = _repo.Load(userFile).ToList();
                if (lines.Any())
                {
                    var last = lines.Last();
                    if (last.Length > 1)
                        lastType = last[1];
                }
                if (string.Equals(lastType, "Entrata", StringComparison.OrdinalIgnoreCase))
                {
                    UscitaButton.IsEnabled = true;
                }
                else
                {
                    EntrataButton.IsEnabled = true;
                }
            }
        }

        private void EntrataButton_Click(object sender, RoutedEventArgs e)
        {
            if (UserComboBox.SelectedItem is not UserProfile user) return;
            string path = Path.Combine(_sharedFolder, user.Id + ".csv");
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
            string path = Path.Combine(_sharedFolder, user.Id + ".csv");
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

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            // Permette di trascinare la finestra cliccando sullo sfondo
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

