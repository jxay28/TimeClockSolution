using System;
using System.Collections.Generic;
using System.Linq;
using TimeClock.Core.Models;

namespace TimeClock.Core.Services
{
    /// <summary>
    /// Motore unico per il calcolo ore ordinarie/straordinarie.
    /// Regole principali:
    /// - entrata arrotondata sempre in su al blocco configurato
    /// - uscita arrotondata sempre in giù al blocco configurato
    /// - straordinario conteggiato solo a blocchi completi dopo la soglia
    /// - recupero deficit arrotondato al blocco superiore
    /// </summary>
    public sealed class WorkTimeCalculator
    {
        public IReadOnlyList<(DateTime In, DateTime Out)> BuildPairsCrossDay(IEnumerable<TimeCardEntry> entries)
        {
            var ordered = entries
                .OrderBy(e => e.DataOra)
                .ToList();

            var result = new List<(DateTime In, DateTime Out)>();
            DateTime? openIn = null;

            foreach (var punch in ordered)
            {
                if (punch.Tipo == PunchType.Entrata)
                {
                    // Se arriva una nuova ENTRATA senza uscita, manteniamo l'ultima ENTRATA come inizio valido.
                    openIn = punch.DataOra;
                    continue;
                }

                if (openIn.HasValue && punch.Tipo == PunchType.Uscita)
                {
                    if (punch.DataOra > openIn.Value)
                        result.Add((openIn.Value, punch.DataOra));

                    openIn = null;
                }
            }

            return result;
        }

        public WorkDayResult CalculateDay(
            UserProfile user,
            DateTime day,
            IEnumerable<(DateTime In, DateTime Out)> pairs,
            WorkTimePolicy policy)
        {
            var normalizedPolicy = policy ?? WorkTimePolicy.Default;
            var roundedPairs = pairs
                .Select(p => ApplyRounding(p, normalizedPolicy))
                .Where(p => p.Out >= p.In)
                .ToList();

            int totalWorkedMinutes = roundedPairs
                .Sum(p => Math.Max(0, (int)Math.Round((p.Out - p.In).TotalMinutes, MidpointRounding.AwayFromZero)));

            bool isHoliday = IsHoliday(day, normalizedPolicy);
            int expectedMinutes = CalculateExpectedDailyMinutes(user);

            int ordinaryMinutes;
            int overtimeMinutes;
            int recoveryApplied = 0;
            var notes = new List<string>();

            if (isHoliday)
            {
                ordinaryMinutes = 0;
                overtimeMinutes = totalWorkedMinutes;
            }
            else if (totalWorkedMinutes >= expectedMinutes)
            {
                ordinaryMinutes = expectedMinutes;
                int extra = Math.Max(0, totalWorkedMinutes - expectedMinutes);
                overtimeMinutes = CalculateOvertimeBlocks(extra, normalizedPolicy.OvertimeThresholdMinutes, normalizedPolicy.OvertimeBlockMinutes);
            }
            else
            {
                int deficit = Math.Max(0, expectedMinutes - totalWorkedMinutes);
                recoveryApplied = ApplyRecoveryBlocks(deficit, normalizedPolicy.DeficitRecoveryBlockMinutes);
                ordinaryMinutes = Math.Max(0, totalWorkedMinutes - recoveryApplied);
                overtimeMinutes = 0;

                if (recoveryApplied > 0)
                    notes.Add($"Recupero minimo applicato: {recoveryApplied} min");
            }

            var pairResults = roundedPairs
                .Select(p => new WorkPairResult
                {
                    In = p.In,
                    Out = p.Out,
                    DurationMinutes = Math.Max(0, (int)Math.Round((p.Out - p.In).TotalMinutes, MidpointRounding.AwayFromZero)),
                    DurationVisual = FormatMinutes(Math.Max(0, (int)Math.Round((p.Out - p.In).TotalMinutes, MidpointRounding.AwayFromZero)))
                })
                .ToList();

            return new WorkDayResult
            {
                Day = day.Date,
                IsHoliday = isHoliday,
                ExpectedMinutes = expectedMinutes,
                WorkedMinutes = totalWorkedMinutes,
                OrdinaryMinutes = ordinaryMinutes,
                OvertimeMinutes = overtimeMinutes,
                RecoveryMinutesApplied = recoveryApplied,
                Notes = notes,
                Pairs = pairResults
            };
        }

        public int CalculateExpectedDailyMinutes(UserProfile user)
        {
            if (user != null && user.OreContrattoSettimanali > 0)
                return (int)Math.Round((user.OreContrattoSettimanali / 5.0) * 60.0, MidpointRounding.AwayFromZero);

            return 8 * 60;
        }

        public static string FormatMinutes(int minutes)
        {
            var ts = TimeSpan.FromMinutes(Math.Max(0, minutes));
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
        }

        private static (DateTime In, DateTime Out) ApplyRounding((DateTime In, DateTime Out) pair, WorkTimePolicy policy)
        {
            DateTime inTime = policy.RoundEntryUp ? RoundUp(pair.In, policy.RoundingBlockMinutes) : pair.In;
            DateTime outTime = policy.RoundExitDown ? RoundDown(pair.Out, policy.RoundingBlockMinutes) : pair.Out;

            if (outTime < inTime)
                outTime = inTime;

            return (inTime, outTime);
        }

        private static DateTime RoundUp(DateTime value, int blockMinutes)
        {
            if (blockMinutes <= 0) return value;
            int minuteOfDay = value.Hour * 60 + value.Minute;
            int rounded = (int)Math.Ceiling(minuteOfDay / (double)blockMinutes) * blockMinutes;
            return value.Date.AddMinutes(rounded);
        }

        private static DateTime RoundDown(DateTime value, int blockMinutes)
        {
            if (blockMinutes <= 0) return value;
            int minuteOfDay = value.Hour * 60 + value.Minute;
            int rounded = (int)Math.Floor(minuteOfDay / (double)blockMinutes) * blockMinutes;
            return value.Date.AddMinutes(rounded);
        }

        private static int CalculateOvertimeBlocks(int extraMinutes, int thresholdMinutes, int blockMinutes)
        {
            if (extraMinutes <= 0) return 0;
            if (thresholdMinutes <= 0) return extraMinutes;
            if (extraMinutes < thresholdMinutes) return 0;
            if (blockMinutes <= 0) return extraMinutes;
            return (extraMinutes / blockMinutes) * blockMinutes;
        }

        private static int ApplyRecoveryBlocks(int deficitMinutes, int recoveryBlockMinutes)
        {
            if (deficitMinutes <= 0) return 0;
            if (recoveryBlockMinutes <= 0) return deficitMinutes;
            return (int)Math.Ceiling(deficitMinutes / (double)recoveryBlockMinutes) * recoveryBlockMinutes;
        }

        private static bool IsHoliday(DateTime day, WorkTimePolicy policy)
        {
            if (policy.AlwaysHolidayDays.Contains(day.DayOfWeek))
                return true;

            if (policy.RecurringHolidays.Any(h => h.Month == day.Month && h.Day == day.Day))
                return true;

            if (policy.AdditionalHolidayDates.Contains(day.Date))
                return true;

            return false;
        }
    }

    public sealed class WorkTimePolicy
    {
        public static WorkTimePolicy Default => new WorkTimePolicy();

        public bool RoundEntryUp { get; set; } = true;
        public bool RoundExitDown { get; set; } = true;

        public int RoundingBlockMinutes { get; set; } = 15;
        public int OvertimeThresholdMinutes { get; set; } = 15;
        public int OvertimeBlockMinutes { get; set; } = 15;
        public int DeficitRecoveryBlockMinutes { get; set; } = 15;

        public List<DayOfWeek> AlwaysHolidayDays { get; set; } = new();
        public List<(int Month, int Day)> RecurringHolidays { get; set; } = new();
        public HashSet<DateTime> AdditionalHolidayDates { get; set; } = new();
    }

    public sealed class WorkPairResult
    {
        public DateTime In { get; set; }
        public DateTime Out { get; set; }
        public int DurationMinutes { get; set; }
        public string DurationVisual { get; set; } = "00:00";
    }

    public sealed class WorkDayResult
    {
        public DateTime Day { get; set; }
        public bool IsHoliday { get; set; }
        public int ExpectedMinutes { get; set; }
        public int WorkedMinutes { get; set; }
        public int OrdinaryMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public int RecoveryMinutesApplied { get; set; }
        public List<string> Notes { get; set; } = new();
        public List<WorkPairResult> Pairs { get; set; } = new();
    }
}
