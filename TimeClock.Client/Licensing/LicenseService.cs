namespace TimeClock.Client.Licensing
{
    public sealed class LicenseService
    {
        private readonly LicenseTokenValidator _validator = new();

        public string CurrentMachineId => MachineFingerprintProvider.GetMachineId();

        public LicenseValidationResult GetCurrentStatus(string? dataFolder)
        {
            string machineId = CurrentMachineId;
            string? token = LicenseTokenProvider.TryLoadToken(dataFolder, machineId);
            if (string.IsNullOrWhiteSpace(token))
            {
                return LicenseValidationResult.Invalid("Licenza non trovata per questa macchina.");
            }

            return _validator.Validate(token, dataFolder);
        }
    }
}
