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

        private bool _showSummary;
        public bool ShowSummary
        {
            get => _showSummary;
            set => SetProperty(ref _showSummary, value);
        }

        private string _monthlyOrdinaryHours = "00:00";
        public string MonthlyOrdinaryHours
        {
            get => _monthlyOrdinaryHours;
            set => SetProperty(ref _monthlyOrdinaryHours, value);
        }

        private string _monthlyExtraHours = "00:00";
        public string MonthlyExtraHours
        {
            get => _monthlyExtraHours;
            set => SetProperty(ref _monthlyExtraHours, value);
        }

        public ICommand RefreshStatusCommand =>
            _refreshStatusCommand ??= new RelayCommand(() =>
            {
                LastActionText = $"Refresh stato UI {DateTime.Now:HH:mm:ss}";
            });
    }
}
