using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        /// Aggiunge una riga al file CSV garantendo scrittura in mutua esclusione.
        /// </summary>
        public void AppendLine(string path, string csvLine)
        {
            lock (_sync)
            {
                using var stream = new FileStream(path, FileMode.OpenOrCreate,
                    FileAccess.Write, FileShare.Read);
                stream.Seek(0, SeekOrigin.End);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.WriteLine(csvLine);
            }
        }
    }
}