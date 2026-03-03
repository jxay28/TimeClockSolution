using System;
using System.IO;

namespace TimeClock.Client.Licensing
{
    public static class LicensePublicKeyProvider
    {
        public static string? TryLoadPem(string? dataFolder)
        {
            if (!string.IsNullOrWhiteSpace(dataFolder))
            {
                string sharedPem = Path.Combine(dataFolder, "license_public_key.pem");
                if (File.Exists(sharedPem))
                {
                    string pemInData = File.ReadAllText(sharedPem);
                    if (!string.IsNullOrWhiteSpace(pemInData))
                    {
                        return pemInData;
                    }
                }
            }

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
