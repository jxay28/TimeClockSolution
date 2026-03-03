using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace TimeClock.Client.Licensing
{
    public static class MachineFingerprintProvider
    {
        public static string GetMachineId()
        {
            string machineGuid = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                "MachineGuid",
                string.Empty) as string ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(machineGuid))
            {
                return Normalize(machineGuid);
            }

            string fallback = $"{Environment.MachineName}|{Environment.UserDomainName}";
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fallback));
            return Convert.ToHexString(hash);
        }

        private static string Normalize(string input)
        {
            return input.Trim().ToUpperInvariant();
        }
    }
}
