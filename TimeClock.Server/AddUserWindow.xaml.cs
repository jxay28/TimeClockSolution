using System;
using System.IO;
using System.Linq;
using System.Windows;
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Server
{
    public partial class AddUserWindow : Window
    {
        private readonly string _csvFolder;
        public UserProfile? User { get; private set; }

        public AddUserWindow(string csvFolder)
        {
            InitializeComponent();
            _csvFolder = csvFolder;

            // ID univoco
            IdBox.Text = Guid.NewGuid().ToString();
            AssunzionePicker.SelectedDate = DateTime.Now;

            LoadSequentialNumber();
        }

        /// <summary>
        /// Legge utenti.csv e trova il prossimo numero sequenziale.
        /// </summary>
        private void LoadSequentialNumber()
        {
            int next = 0;
            string path = Path.Combine(_csvFolder, "utenti.csv");

            if (File.Exists(path))
            {
                var repo = new CsvRepository();

                var nums = repo.Load(path)
                               .Select(r => r.ElementAtOrDefault(1)) // colonna SequenceNumber
                               .Where(v => int.TryParse(v, out _))
                               .Select(int.Parse)
                               .ToList();

                if (nums.Any())
                    next = nums.Max() + 1;
            }

            SeqNumberBox.Text = next.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // DATA ASSUNZIONE
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

            // VALIDAZIONI NUMERICHE
            if (!int.TryParse(SeqNumberBox.Text, out int seq))
            {
                MessageBox.Show("Numero sequenziale non valido.");
                return;
            }

            if (!double.TryParse(OreBox.Text.Replace(",", "."), out var ore))
            {
                MessageBox.Show("Le ore settimanali devono essere un numero.");
                return;
            }

            if (!decimal.TryParse(SalarioBaseBox.Text.Replace(",", "."), out var salarioBase))
            {
                MessageBox.Show("Il salario base deve essere un numero.");
                return;
            }

            if (!decimal.TryParse(SalarioExtraBox.Text.Replace(",", "."), out var salarioExtra))
            {
                MessageBox.Show("Il salario extra deve essere un numero.");
                return;
            }

            // CREAZIONE OGGETTO
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
                CompensoOrarioExtra = salarioExtra
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


