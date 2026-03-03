namespace TimeClock.Client.Licensing
{
    public sealed class LicenseService
    {
        private readonly LicenseStorageService _storage = new();
        private readonly LicenseTokenValidator _validator = new();

        public string CurrentMachineId => MachineFingerprintProvider.GetMachineId();

        public LicenseValidationResult GetCurrentStatus()
        {
            string? token = _storage.LoadToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                return LicenseValidationResult.Invalid("Nessuna licenza attiva.");
            }

            return _validator.Validate(token);
        }

        public LicenseValidationResult TryActivate(string token)
        {
            var result = _validator.Validate(token);
            if (result.IsValid)
            {
                _storage.SaveToken(token);
            }

            return result;
        }

        public void ClearLicense()
        {
            _storage.Clear();
        }
    }
}
