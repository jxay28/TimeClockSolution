using System;
using System.IO;

namespace TimeClock.Client.Licensing
{
    public static class LicensePublicKeyProvider
    {
        public static string? TryLoadPem()
        {
            string configured = Properties.Settings.Default.LicensePublicKeyPem ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            string filePath = Path.Combine(AppContext.BaseDirectory, "license_public_key.pem");
            if (!File.Exists(filePath))
            {
                return null;
            }

            string pem = File.ReadAllText(filePath);
            return string.IsNullOrWhiteSpace(pem) ? null : pem;
        }
    }
}
