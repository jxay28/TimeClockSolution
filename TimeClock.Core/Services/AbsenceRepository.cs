using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TimeClock.Core.Models;

namespace TimeClock.Core.Services
{
    public class AbsenceRepository
    {
        private readonly CsvRepository _csv = new();

        public List<AbsenceRecord> Load(string path)
        {
            if (!File.Exists(path))
                return new List<AbsenceRecord>();

            return _csv.Load(path)
                .Select(r => Parse(r))
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();
        }

        public void Append(string path, AbsenceRecord record)
        {
            var line = string.Join(",", new[]
            {
                record.UserId,
                record.Data.ToString("yyyy-MM-dd"),
                record.Tipo.ToString(),
                record.Ore.ToString(CultureInfo.InvariantCulture),
                record.Note ?? string.Empty
            });

            _csv.AppendLine(path, line);
        }

        private AbsenceRecord? Parse(string[] row)
        {
            if (row.Length < 3)
                return null;

            var userId = row.ElementAtOrDefault(0) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            if (!DateTime.TryParse(row.ElementAtOrDefault(1), out var data))
                return null;

            if (!Enum.TryParse<AbsenceType>(row.ElementAtOrDefault(2), true, out var tipo))
                tipo = AbsenceType.Ferie;

            var ore = double.TryParse(row.ElementAtOrDefault(3), NumberStyles.Any, CultureInfo.InvariantCulture, out var h)
                ? h
                : 0;

            return new AbsenceRecord
            {
                UserId = userId,
                Data = data.Date,
                Tipo = tipo,
                Ore = ore,
                Note = row.ElementAtOrDefault(4)
            };
        }
    }
}
