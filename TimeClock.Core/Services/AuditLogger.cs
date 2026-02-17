using System;
using System.IO;
using System.Text;
using System.Threading;

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
            string line = CsvCodec.BuildLine(new[]
            {
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                action,
                details
            });

            const int maxAttempts = 6;
            int[] delaysMs = { 50, 100, 200, 300, 500, 800 };

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    lock (_sync)
                    {
                        string lockPath = path + ".lock";
                        using var lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                        stream.Seek(0, SeekOrigin.End);
                        using var writer = new StreamWriter(stream, Encoding.UTF8);
                        writer.WriteLine(line);
                        writer.Flush();
                        stream.Flush(flushToDisk: true);
                    }

                    return;
                }
                catch (IOException)
                {
                    if (attempt == maxAttempts) return;
                    Thread.Sleep(delaysMs[Math.Min(attempt - 1, delaysMs.Length - 1)]);
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt == maxAttempts) return;
                    Thread.Sleep(delaysMs[Math.Min(attempt - 1, delaysMs.Length - 1)]);
                }
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
