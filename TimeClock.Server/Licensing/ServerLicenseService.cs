using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace TimeClock.Server.Licensing
{
    public sealed class ServerLicenseService
    {
        private const string Secret = "TimeClock.Server.License.v1::2026";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public bool EnsureLicenseAtStartup(out string message)
        {
            string path = GetLicenseFilePath();

            if (File.Exists(path))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<ServerLicenseRecord>(File.ReadAllText(path));
                    if (record == null)
                    {
                        message = "File licenza server non valido.";
                        return false;
                    }

                    string expected = GenerateKeyFromToken(record.Token);
                    if (!string.Equals(expected, record.LicenseKey, StringComparison.Ordinal))
                    {
                        message = "Licenza server non valida.";
                        return false;
                    }

                    message = "Licenza server valida.";
                    return true;
                }
                catch
                {
                    message = "Impossibile leggere la licenza server.";
                    return false;
                }
            }

            string token = GenerateActivationToken();
            string key = string.Empty;
            while (true)
            {
                var wnd = new ServerLicenseActivationWindow(token);
                bool? result = wnd.ShowDialog();
                if (result != true)
                {
                    message = "Attivazione annullata.";
                    return false;
                }

                key = wnd.LicenseKey;
                string expected = GenerateKeyFromToken(token);
                if (string.Equals(expected, key, StringComparison.Ordinal))
                {
                    break;
                }

                MessageBox.Show("Key non valida per il codice mostrato.", "Licenza Server", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            var newRecord = new ServerLicenseRecord
            {
                Token = token,
                LicenseKey = key,
                GeneratedAtUtc = DateTimeOffset.UtcNow
            };

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(newRecord, JsonOptions));
            message = "Licenza server generata con successo.";
            return true;
        }

        public static string GenerateKeyFromToken(string token)
        {
            string normalized = (token ?? string.Empty).Trim();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            string hex = Convert.ToHexString(hash);
            return $"{hex[..8]}-{hex[8..16]}-{hex[16..24]}-{hex[24..32]}";
        }

        private static string GetLicenseFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "TimeClock.Server", "server_license.json");
        }

        private static string GenerateActivationToken()
        {
            string machine = (Environment.MachineName ?? "SERVER").Trim().ToUpperInvariant();
            string random = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            return $"SRV-{machine}-{DateTime.UtcNow:yyyyMMdd}-{random}";
        }
    }
}
