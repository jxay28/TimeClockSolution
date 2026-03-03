namespace TimeClock.Client.Licensing
{
    public sealed class LicenseValidationResult
    {
        public static LicenseValidationResult Invalid(string message)
        {
            return new LicenseValidationResult(false, message, null);
        }

        public static LicenseValidationResult Valid(LicensePayload payload)
        {
            return new LicenseValidationResult(true, "Licenza valida.", payload);
        }

        public LicenseValidationResult(bool isValid, string message, LicensePayload? payload)
        {
            IsValid = isValid;
            Message = message;
            Payload = payload;
        }

        public bool IsValid { get; }
        public string Message { get; }
        public LicensePayload? Payload { get; }
    }
}
