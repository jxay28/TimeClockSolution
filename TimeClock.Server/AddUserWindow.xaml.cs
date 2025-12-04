using System;
using System.Windows;
using TimeClock.Core.Models;

namespace TimeClock.Server
{
    /// <summary>
    /// Finestra per la creazione di un nuovo utente. Raccoglie i dati dai campi e restituisce un UserProfile.
    /// </summary>
    public partial class AddUserWindow : Window
    {
        public UserProfile? User { get; private set; }

        public AddUserWindow()
        {
            InitializeComponent();
            // genera automaticamente un ID utente
            IdBox.Text = Guid.NewGuid().ToString();
            AssunzionePicker.SelectedDate = DateTime.Now;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // validazione dei campi
            if (string.IsNullOrWhiteSpace(IdBox.Text) ||
                string.IsNullOrWhiteSpace(NomeBox.Text) ||
                string.IsNullOrWhiteSpace(CognomeBox.Text) ||
                string.IsNullOrWhiteSpace(RuoloBox.Text) ||
                !AssunzionePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Compila tutti i campi obbligatori");
                return;
            }
            if (!double.TryParse(OreBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var oreSett))
            {
                MessageBox.Show("Ore settimanali non valide");
                return;
            }
            if (!decimal.TryParse(SalarioBaseBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var salarioBase))
            {
                MessageBox.Show("Salario base non valido");
                return;
            }
            if (!decimal.TryParse(SalarioExtraBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var salarioExtra))
            {
                MessageBox.Show("Salario extra non valido");
                return;
            }
            // crea l'oggetto UserProfile
            User = new UserProfile
            {
                Id = IdBox.Text.Trim(),
                Nome = NomeBox.Text.Trim(),
                Cognome = CognomeBox.Text.Trim(),
                Ruolo = RuoloBox.Text.Trim(),
                DataAssunzione = AssunzionePicker.SelectedDate!.Value,
                OreContrattoSettimanali = oreSett,
                CompensoOrarioBase = salarioBase,
                CompensoOrarioExtra = salarioExtra
            };
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}