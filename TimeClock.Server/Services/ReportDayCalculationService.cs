using System;
using System.Collections.Generic;
using System.Linq;
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Server.Services
{
    /// <summary>
    /// Logica business del report giornaliero separata dagli eventi UI.
    /// </summary>
    public sealed class ReportDayCalculationService
    {
        private readonly WorkTimeCalculator _workTimeCalculator = new();

        public void CalculateSingleRow(
            ReportRow row,
            DateTime dataRiferimento,
            UserProfile user,
            (DateTime In, DateTime Out)? c1,
            (DateTime In, DateTime Out)? c2,
            ParametriStraordinari? parametriGlobali)
        {
            var policy = WorkTimePolicyFactory.FromGlobalParameters(parametriGlobali);

            var pairs = new List<(DateTime In, DateTime Out)>();
            if (c1.HasValue) pairs.Add(c1.Value);
            if (c2.HasValue) pairs.Add(c2.Value);

            var result = _workTimeCalculator.CalculateDay(user, dataRiferimento, pairs, policy);

            row.IsFestivo = result.IsHoliday;
            row.OreOrdinarie = Math.Round(result.OrdinaryMinutes / 60.0, 2);
            row.OreStraordinarie = Math.Round(result.OvertimeMinutes / 60.0, 2);
            row.Durata1Visual = result.Pairs.ElementAtOrDefault(0)?.DurationVisual ?? string.Empty;
            row.Durata2Visual = result.Pairs.ElementAtOrDefault(1)?.DurationVisual ?? string.Empty;
            row.Note = result.Notes.Any() ? string.Join(" | ", result.Notes) : null;
        }

        public List<(DateTime Ingresso, DateTime Uscita)> ExtractPairsFromRows(List<ReportRow> righeGiorno, DateTime dataBase)
        {
            var pairs = new List<(DateTime Ingresso, DateTime Uscita)>();

            foreach (var row in righeGiorno)
            {
                if (TryBuildPair(dataBase, row.Entrata1, row.Uscita1, out var p1))
                    pairs.Add(p1);
                if (TryBuildPair(dataBase, row.Entrata2, row.Uscita2, out var p2))
                    pairs.Add(p2);
            }

            return pairs.OrderBy(p => p.Ingresso).ToList();
        }

        public void ApplyDailyCalculationToRows(
            List<ReportRow> righeGiorno,
            DateTime dataRiferimento,
            UserProfile user,
            List<(DateTime Ingresso, DateTime Uscita)> coppieGiorno,
            ParametriStraordinari? parametriGlobali)
        {
            if (righeGiorno == null || righeGiorno.Count == 0)
                return;

            var policy = WorkTimePolicyFactory.FromGlobalParameters(parametriGlobali);
            var result = _workTimeCalculator.CalculateDay(
                user,
                dataRiferimento,
                coppieGiorno.Select(c => (c.Ingresso, c.Uscita)),
                policy);

            var ordByRow = new int[righeGiorno.Count];
            var extByRow = new int[righeGiorno.Count];

            int ordinaryRemaining = Math.Max(0, result.OrdinaryMinutes);
            int overtimeRemaining = Math.Max(0, result.OvertimeMinutes);

            for (int i = 0; i < result.Pairs.Count; i++)
            {
                int rowIndex = i / 2;
                if (rowIndex >= righeGiorno.Count)
                    break;

                int pairMinutes = Math.Max(0, result.Pairs[i].DurationMinutes);
                int ord = Math.Min(pairMinutes, ordinaryRemaining);
                ordinaryRemaining -= ord;

                int extraPotential = Math.Max(0, pairMinutes - ord);
                int ext = Math.Min(extraPotential, overtimeRemaining);
                overtimeRemaining -= ext;

                ordByRow[rowIndex] += ord;
                extByRow[rowIndex] += ext;
            }

            for (int i = 0; i < righeGiorno.Count; i++)
            {
                var row = righeGiorno[i];
                row.IsFestivo = result.IsHoliday;
                row.OreOrdinarie = Math.Round(ordByRow[i] / 60.0, 2);
                row.OreStraordinarie = Math.Round(extByRow[i] / 60.0, 2);
                row.Durata1Visual = string.Empty;
                row.Durata2Visual = string.Empty;
                row.Note = null;
            }

            for (int i = 0; i < result.Pairs.Count; i++)
            {
                int rowIndex = i / 2;
                if (rowIndex >= righeGiorno.Count)
                    break;

                if (i % 2 == 0)
                    righeGiorno[rowIndex].Durata1Visual = result.Pairs[i].DurationVisual;
                else
                    righeGiorno[rowIndex].Durata2Visual = result.Pairs[i].DurationVisual;
            }

            if (result.Notes.Any())
                righeGiorno[0].Note = string.Join(" | ", result.Notes);
        }

        private static bool TryBuildPair(DateTime dataBase, string? entrata, string? uscita, out (DateTime Ingresso, DateTime Uscita) pair)
        {
            pair = default;

            if (string.IsNullOrWhiteSpace(entrata) || string.IsNullOrWhiteSpace(uscita))
                return false;

            if (!TimeSpan.TryParse(entrata, out var tIn) || !TimeSpan.TryParse(uscita, out var tOut))
                return false;

            DateTime inDt = dataBase.Add(tIn);
            DateTime outDt = dataBase.Add(tOut);
            if (tOut < tIn)
                outDt = outDt.AddDays(1);

            if (outDt < inDt)
                return false;

            pair = (inDt, outDt);
            return true;
        }
    }
}
