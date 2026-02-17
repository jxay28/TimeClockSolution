using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TimeClock.Core.Services
{
    /// <summary>
    /// Repository generico per operazioni sui file CSV.
    /// Gestisce la concorrenza usando lock e FileShare.
    /// </summary>
    public class CsvRepository
    {
        private readonly object _sync = new();

        /// <summary>
        /// Legge tutte le righe dal file CSV e restituisce i campi suddivisi.
        /// </summary>
        public IEnumerable<string[]> Load(string path)
        {
            if (!File.Exists(path))
                yield break;
            using var stream = new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var fields = CsvCodec.ParseLine(line);
                yield return fields;
            }
        }

        /// <summary>
        /// Aggiunge una riga al file CSV con mutua esclusione intra-processo,
        /// lock file inter-processo e retry progressivo su errori I/O temporanei.
        /// </summary>
        public void AppendLine(string path, string csvLine)
        {
            const int maxAttempts = 6;
            int[] delaysMs = { 50, 100, 200, 300, 500, 800 };

            Exception? lastError = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    lock (_sync)
                    {
                        var lockPath = path + ".lock";

                        // Lock inter-processo: un solo writer per volta sullo stesso file.
                        using var lockStream = new FileStream(lockPath, FileMode.OpenOrCreate,
                            FileAccess.ReadWrite, FileShare.None);

                        using var stream = new FileStream(path, FileMode.OpenOrCreate,
                            FileAccess.Write, FileShare.Read);
                        stream.Seek(0, SeekOrigin.End);
                        using var writer = new StreamWriter(stream, Encoding.UTF8);
                        writer.WriteLine(csvLine);
                        writer.Flush();
                        stream.Flush(flushToDisk: true);
                    }

                    return;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }

                if (attempt < maxAttempts)
                {
                    Thread.Sleep(delaysMs[Math.Min(attempt - 1, delaysMs.Length - 1)]);
                }
            }

            throw new IOException($"Impossibile scrivere su '{path}' dopo {maxAttempts} tentativi.", lastError);
        }
    }
}