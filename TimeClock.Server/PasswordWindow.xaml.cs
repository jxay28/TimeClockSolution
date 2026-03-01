using System;
using System.Windows;
using System.Windows.Input;

namespace TimeClock.Server
{
    public partial class PasswordWindow : Window
    {
        private readonly string _expectedPassword;
        public bool IsAuthenticated { get; private set; } = false;

        public PasswordWindow(string expectedPassword)
        {
            InitializeComponent();
            _expectedPassword = expectedPassword;
            PwdBox.Focus();
        }

        private void Sblocca_Click(object sender, RoutedEventArgs e)
        {
            VerifyPassword();
        }

        private void Esci_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void PwdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                VerifyPassword();
            }
        }

        private void VerifyPassword()
        {
            if (PwdBox.Password == _expectedPassword)
            {
                IsAuthenticated = true;
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                ErrorText.Visibility = Visibility.Visible;
                PwdBox.Password = string.Empty;
                PwdBox.Focus();
            }
        }
    }
}
