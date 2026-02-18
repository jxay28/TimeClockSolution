using System;
using System.Windows.Input;

namespace TimeClock.Client.ViewModels
{
    public sealed class ClientStatusViewModel : ViewModelBase
    {
        private string _statusText = "Nessun utente selezionato";
        private string _lastActionText = "N/A";
        private ICommand? _refreshStatusCommand;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string LastActionText
        {
            get => _lastActionText;
            set => SetProperty(ref _lastActionText, value);
        }

        public ICommand RefreshStatusCommand =>
            _refreshStatusCommand ??= new RelayCommand(() =>
            {
                LastActionText = $"Refresh stato UI {DateTime.Now:HH:mm:ss}";
            });
    }
}
