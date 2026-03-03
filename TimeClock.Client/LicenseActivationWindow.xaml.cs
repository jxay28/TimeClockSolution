using System.Windows;

namespace TimeClock.Client
{
    public partial class LicenseActivationWindow : Window
    {
        public LicenseActivationWindow(string machineId, string statusMessage)
        {
            InitializeComponent();
            MachineIdTextBox.Text = machineId;
            StatusTextBlock.Text = statusMessage;
        }

        public string LicenseToken => LicenseTextBox.Text.Trim();

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LicenseToken))
            {
                MessageBox.Show(this, "Inserisci una chiave di licenza.", "Licenza", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
