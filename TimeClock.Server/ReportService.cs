using System.Collections.Generic;
using System.IO;
using System.Linq;
using TimeClock.Core.Models;

namespace TimeClock.Server
{
    /// <summary>
    /// Genera i file di report per il commercialista e per il gestionale paghe.
    /// </summary>
    public class ReportService
    {
        public void GenerateReports(IEnumerable<UserProfile> users, IEnumerable<PaySummary> summaries, int year, int month, string outputFolder)
        {
            var reportCommLines = new List<string> { "IdUtente,CodiceFiscale,Mese,Anno,OreOrdinarie,OreStraordinarie" };
            var reportPagheLines = new List<string> { "IdUtente,AnnoMese,OreOrdinarie,OreStraordinarie,CompensoBase,CompensoStraordinario" };

            string monthStr = month.ToString("D2");

            foreach (var user in users)
            {
                var summary = summaries.FirstOrDefault(s => s.UserId == user.Id);
                if (summary == null) continue;
                // CodiceFiscale è lasciato vuoto come placeholder
                reportCommLines.Add($"{user.Id},,{monthStr},{year},{summary.OreOrdinarie:F2},{summary.OreStraordinarie:F2}");
                reportPagheLines.Add($"{user.Id},{year}{monthStr},{summary.OreOrdinarie:F2},{summary.OreStraordinarie:F2},{summary.CompensoOrdinarie:F2},{summary.CompensoStraordinarie:F2}");
            }

            File.WriteAllLines(Path.Combine(outputFolder, $"report_commercialista_{year}{monthStr}.csv"), reportCommLines);
            File.WriteAllLines(Path.Combine(outputFolder, $"report_paghe_{year}{monthStr}.csv"), reportPagheLines);
        }
    }
}