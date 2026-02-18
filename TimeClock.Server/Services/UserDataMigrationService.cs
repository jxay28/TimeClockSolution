using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Server.Services
{
    public sealed class UserDataMigrationService
    {
        private readonly CsvRepository _repo = new();

        public bool TryMigrateCsvToJsonWithBackup(string csvPath, string jsonPath, out List<UserProfile> users, out string backupPath)
        {
            users = new List<UserProfile>();
            backupPath = string.Empty;

            if (!File.Exists(csvPath) || File.Exists(jsonPath))
                return false;

            users = LoadUsersFromCsv(csvPath);
            if (!users.Any())
                return false;

            backupPath = CreateCsvBackup(csvPath);

            var options = new JsonSerializerOptions { WriteIndented = true };
            SafeFileWriter.WriteAllTextAtomic(jsonPath, JsonSerializer.Serialize(users, options));

            return true;
        }

        public List<UserProfile> LoadUsersLegacyCsv(string csvPath)
        {
            if (!File.Exists(csvPath))
                return new List<UserProfile>();

            return LoadUsersFromCsv(csvPath);
        }

        private List<UserProfile> LoadUsersFromCsv(string csvPath)
        {
            var result = new List<UserProfile>();

            foreach (var fields in _repo.Load(csvPath))
            {
                try
                {
                    var user = new UserProfile
                    {
                        Id = fields.ElementAtOrDefault(0) ?? string.Empty,
                        SequenceNumber = int.TryParse(fields.ElementAtOrDefault(1), out var seq) ? seq : 0,
                        Nome = fields.ElementAtOrDefault(2) ?? string.Empty,
                        Cognome = fields.ElementAtOrDefault(3) ?? string.Empty,
                        Ruolo = fields.ElementAtOrDefault(4) ?? string.Empty,
                        DataAssunzione = DateTime.TryParse(fields.ElementAtOrDefault(5), out var d) ? d : DateTime.MinValue,
                        OreContrattoSettimanali = double.TryParse(fields.ElementAtOrDefault(6),
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out var ore)
                            ? ore
                            : 0,
                        CompensoOrarioBase = decimal.TryParse(fields.ElementAtOrDefault(7),
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out var cb)
                            ? cb
                            : 0,
                        CompensoOrarioExtra = decimal.TryParse(fields.ElementAtOrDefault(8),
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out var ce)
                            ? ce
                            : 0,
                        OrarioIngresso1 = fields.ElementAtOrDefault(9),
                        OrarioUscita1 = fields.ElementAtOrDefault(10),
                        OrarioIngresso2 = fields.ElementAtOrDefault(11),
                        OrarioUscita2 = fields.ElementAtOrDefault(12)
                    };

                    if (!string.IsNullOrWhiteSpace(user.Id))
                        result.Add(user);
                }
                catch
                {
                    // Riga malformata: viene ignorata per non bloccare la migrazione.
                }
            }

            return result;
        }

        private static string CreateCsvBackup(string csvPath)
        {
            var folder = Path.GetDirectoryName(csvPath) ?? ".";
            var backupFolder = Path.Combine(folder, "backup");
            Directory.CreateDirectory(backupFolder);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(backupFolder, $"utenti_{timestamp}.csv.bak");
            File.Copy(csvPath, backupPath, overwrite: false);

            return backupPath;
        }
    }
}
