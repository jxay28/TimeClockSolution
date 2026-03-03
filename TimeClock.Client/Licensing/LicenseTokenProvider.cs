using System;
using System.IO;

namespace TimeClock.Client.Licensing
{
    public static class LicenseTokenProvider
    {
        public static string? TryLoadToken(string? dataFolder, string machineId)
        {
            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                return null;
            }

            string safeMachineId = NormalizeMachineIdForFileName(machineId);
            string path = Path.Combine(dataFolder, "licenses", $"{safeMachineId}.token");
            if (!File.Exists(path))
            {
                return null;
            }

            string token = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        private static string NormalizeMachineIdForFileName(string input)
        {
            var chars = (input ?? string.Empty).Trim().ToUpperInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!(char.IsLetterOrDigit(chars[i]) || chars[i] == '-'))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}
