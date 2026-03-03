using System.Windows;

namespace TimeClock.Server
{
    public partial class ServerLicenseActivationWindow : Window
    {
        public ServerLicenseActivationWindow(string token)
        {
            InitializeComponent();
            TokenTextBox.Text = token;
        }

        public string LicenseKey => LicenseKeyTextBox.Text.Trim().ToUpperInvariant();

        private void CopyToken_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TokenTextBox.Text);
            MessageBox.Show(this, "Codice copiato negli appunti.", "Licenza", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LicenseKey))
            {
                MessageBox.Show(this, "Inserisci una key valida.", "Licenza", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
