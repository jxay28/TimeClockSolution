using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TimeClock.Core.Services
{
    /// <summary>
    /// Scritture atomiche su file (best effort): scrive su file temporaneo nella stessa cartella,
    /// poi sostituisce il target con replace/move per ridurre il rischio di file corrotti.
    /// </summary>
    public static class SafeFileWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static void WriteAllTextAtomic(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Percorso non valido", nameof(path));

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = BuildTempPath(path);
            File.WriteAllText(tempPath, content ?? string.Empty, Utf8NoBom);
            ReplaceAtomic(tempPath, path);
        }

        public static void WriteAllLinesAtomic(string path, IEnumerable<string> lines)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Percorso non valido", nameof(path));

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = BuildTempPath(path);
            File.WriteAllLines(tempPath, lines ?? Enumerable.Empty<string>(), Utf8NoBom);
            ReplaceAtomic(tempPath, path);
        }

        private static string BuildTempPath(string finalPath)
        {
            string dir = Path.GetDirectoryName(finalPath) ?? ".";
            string file = Path.GetFileName(finalPath);
            return Path.Combine(dir, $".{file}.{Guid.NewGuid():N}.tmp");
        }

        private static void ReplaceAtomic(string tempPath, string targetPath)
        {
            try
            {
                if (File.Exists(targetPath))
                {
                    // Replace è atomico su molti filesystem locali; mantiene meglio integrità su overwrite.
                    File.Replace(tempPath, targetPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
            }
            catch
            {
                // Fallback robusto: elimina target e sposta temp.
                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                File.Move(tempPath, targetPath);
            }
        }
    }
}
