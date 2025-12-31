using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Server
{
    public partial class AddUserWindow : Window
    {
        private readonly string _dataFolder;
        public UserProfile? User { get; private set; }

        public AddUserWindow(string csvFolder)
        {
            InitializeComponent();
            _dataFolder = csvFolder;

            // ID univoco
            IdBox.Text = Guid.NewGuid().ToString();
            AssunzionePicker.SelectedDate = DateTime.Now;

            LoadSequentialNumber();

            
        }

        /// <summary>
        /// Calcola il prossimo numero sequenziale guardando prima utenti.json,
        /// se non esiste prova a leggere utenti.csv per migrazione.
        /// </summary>
        private void LoadSequentialNumber()
        {
            int next = 0;

            if (string.IsNullOrWhiteSpace(_dataFolder))
            {
                SeqNumberBox.Text = next.ToString();
                return;
            }

            string jsonPath = Path.Combine(_dataFolder, "utenti.json");
            string csvPath = Path.Combine(_dataFolder, "utenti.csv");

            // 1) JSON master
            if (File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    var list = JsonSerializer.Deserialize<List<UserProfile>>(json) ?? new List<UserProfile>();
                    if (list.Any())
                        next = list.Max(u => u.SequenceNumber) + 1;
                }
                catch
                {
                    // in caso di problemi, next resta 0 e si passerà a CSV
                }
            }
            // 2) fallback CSV (prima migrazione)
            else if (File.Exists(csvPath))
            {
                try
                {
                    var repo = new CsvRepository();
                    var nums = repo.Load(csvPath)
                                   .Select(r => r.ElementAtOrDefault(1))
                                   .Where(v => !string.IsNullOrWhiteSpace(v) && int.TryParse(v, out _))
                                   .Select(v => int.Parse(v!))
                                   .ToList();
                    if (nums.Any())
                        next = nums.Max() + 1;
                }
                catch
                {
                    // se fallisce, resta 0
                }
            }

            SeqNumberBox.Text = next.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Data assunzione
            DateTime dataAss;
            if (AssunzionePicker.SelectedDate == null)
            {
                MessageBox.Show("Data non selezionata. Impostata la data di oggi.");
                dataAss = DateTime.Today;
            }
            else
            {
                dataAss = AssunzionePicker.SelectedDate.Value;
            }

            // Numero sequenziale
            if (!int.TryParse(SeqNumberBox.Text, out int seq))
            {
                MessageBox.Show("Numero sequenziale non valido.");
                return;
            }

            // Ore settimanali
            if (!double.TryParse(
                    OreBox.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var ore))
            {
                MessageBox.Show("Le ore settimanali devono essere un numero.");
                return;
            }

            // Salario base
            if (!decimal.TryParse(
                    SalarioBaseBox.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var salarioBase))
            {
                MessageBox.Show("Il salario base deve essere un numero.");
                return;
            }

            // Salario extra
            if (!decimal.TryParse(
                    SalarioExtraBox.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var salarioExtra))
            {
                MessageBox.Show("Il salario extra deve essere un numero.");
                return;
            }

            

            
            

            // CREAZIONE OGGETTO UTENTE COMPLETO
            User = new UserProfile
            {
                Id = IdBox.Text,
                SequenceNumber = seq,
                Nome = NomeBox.Text.Trim(),
                Cognome = CognomeBox.Text.Trim(),
                Ruolo = RuoloBox.Text.Trim(),
                DataAssunzione = dataAss,
                OreContrattoSettimanali = ore,
                CompensoOrarioBase = salarioBase,
                CompensoOrarioExtra = salarioExtra,
               
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
