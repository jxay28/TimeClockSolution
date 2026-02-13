using System;
using System.IO;
using System.Text;

namespace TimeClock.Core.Services
{
    public static class AuditLogger
    {
        private static readonly object _sync = new();

        public static void Log(string folder, string action, string details)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return;

            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "audit_log.csv");
            string line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ},{Escape(action)},{Escape(details)}";

            lock (_sync)
            {
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                stream.Seek(0, SeekOrigin.End);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.WriteLine(line);
            }
        }

        private static string Escape(string value)
        {
            value ??= string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return '"' + value.Replace("\"", "\"\"") + '"';
            }

            return value;
        }
    }
}
