using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TimeClock.Client.Licensing
{
    public sealed class LicenseStorageService
    {
        private readonly string _licensePath;

        public LicenseStorageService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "TimeClock.Client");
            _licensePath = Path.Combine(folder, "license.dat");
        }

        public string? LoadToken()
        {
            if (!File.Exists(_licensePath))
            {
                return null;
            }

            try
            {
                string base64 = File.ReadAllText(_licensePath);
                byte[] encrypted = Convert.FromBase64String(base64);
                byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }

        public void SaveToken(string token)
        {
            string? dir = Path.GetDirectoryName(_licensePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] plain = Encoding.UTF8.GetBytes(token);
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllText(_licensePath, Convert.ToBase64String(encrypted));
        }

        public void Clear()
        {
            if (File.Exists(_licensePath))
            {
                File.Delete(_licensePath);
            }
        }
    }
}
