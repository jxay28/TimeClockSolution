using System;
using System.Windows.Input;
using System.Windows.Threading;

namespace TimeClock.Client.ViewModels
{
    public sealed class ClientStatusViewModel : ViewModelBase
    {
        private string _statusText = "Nessun utente selezionato";
        private string _lastActionText = "N/A";
        private string _licenseText = "Licenza non verificata";
        private ICommand? _refreshStatusCommand;

        public ClientStatusViewModel()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => UpdateTime();
            timer.Start();
            UpdateTime();
        }

        private void UpdateTime()
        {
            var now = DateTime.Now;
            CurrentTime = now.ToString("HH:mm:ss");
            CurrentDate = now.ToString("dddd, dd MMMM yyyy");
        }

        private string _currentTime = string.Empty;
        public string CurrentTime
        {
            get => _currentTime;
            set => SetProperty(ref _currentTime, value);
        }

        private string _currentDate = string.Empty;
        public string CurrentDate
        {
            get => _currentDate;
            set => SetProperty(ref _currentDate, value);
        }

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

        public string LicenseText
        {
            get => _licenseText;
            set => SetProperty(ref _licenseText, value);
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
