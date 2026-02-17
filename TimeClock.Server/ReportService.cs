using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Server
{
    /// <summary>
    /// Genera i file di report per il commercialista e per il gestionale paghe.
    /// </summary>
    public class ReportService
    {
        public void GenerateReports(IEnumerable<UserProfile> users, IEnumerable<PaySummary> summaries, int year, int month, string outputFolder)
        {
            var reportCommLines = new List<string>
            {
                CsvCodec.BuildLine(new[] { "IdUtente", "CodiceFiscale", "Mese", "Anno", "OreOrdinarie", "OreStraordinarie" })
            };
            var reportPagheLines = new List<string>
            {
                CsvCodec.BuildLine(new[] { "IdUtente", "AnnoMese", "OreOrdinarie", "OreStraordinarie", "CompensoBase", "CompensoStraordinario" })
            };

            string monthStr = month.ToString("D2");

            foreach (var user in users)
            {
                var summary = summaries.FirstOrDefault(s => s.UserId == user.Id);
                if (summary == null) continue;
                // CodiceFiscale è lasciato vuoto come placeholder
                reportCommLines.Add(CsvCodec.BuildLine(new[]
                {
                    user.Id,
                    string.Empty,
                    monthStr,
                    year.ToString(CultureInfo.InvariantCulture),
                    summary.OreOrdinarie.ToString("F2", CultureInfo.InvariantCulture),
                    summary.OreStraordinarie.ToString("F2", CultureInfo.InvariantCulture)
                }));

                reportPagheLines.Add(CsvCodec.BuildLine(new[]
                {
                    user.Id,
                    $"{year}{monthStr}",
                    summary.OreOrdinarie.ToString("F2", CultureInfo.InvariantCulture),
                    summary.OreStraordinarie.ToString("F2", CultureInfo.InvariantCulture),
                    summary.CompensoOrdinarie.ToString("F2", CultureInfo.InvariantCulture),
                    summary.CompensoStraordinarie.ToString("F2", CultureInfo.InvariantCulture)
                }));
            }

            File.WriteAllLines(Path.Combine(outputFolder, $"report_commercialista_{year}{monthStr}.csv"), reportCommLines);
            File.WriteAllLines(Path.Combine(outputFolder, $"report_paghe_{year}{monthStr}.csv"), reportPagheLines);
        }

        private bool ÈFestivo(DateTime data)
        {
            var p = App.ParametriGlobali;

            // Sabato/domenica o giorni scelti
            if (p.GiorniSempreFestivi.Contains(data.DayOfWeek))
                return true;

            // Festività ricorrenti
            foreach (var (mese, giorno) in p.FestivitaRicorrenti)
            {
                if (data.Month == mese && data.Day == giorno)
                    return true;
            }

            // Festività extra specifiche
            if (p.FestivitaAggiuntive.Contains(data.Date))
                return true;

            return false;
        }

    }
}