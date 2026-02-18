using System;
using System.Collections.Generic;
using System.Linq;
using TimeClock.Core.Models;

namespace TimeClock.Core.Services
{
    /// <summary>
    /// Calcola le ore ordinarie e straordinarie per un utente in un mese.
    /// </summary>
    public class PayPeriodCalculator
    {
        private readonly OvertimeSettings _settings;
        private readonly IEnumerable<Holiday> _holidays;
        private readonly WorkTimeCalculator _workTimeCalculator = new();

        public PayPeriodCalculator(OvertimeSettings settings, IEnumerable<Holiday> holidays)
        {
            _settings = settings;
            _holidays = holidays;
        }

        /// <summary>
        /// Calcola il riepilogo per il periodo indicato usando WorkTimeCalculator
        /// come fonte unica di verità.
        /// </summary>
        public PaySummary Calculate(UserProfile user, IEnumerable<TimeCardEntry> entries, int year, int month)
        {
            var monthEntries = entries
                .Where(e => e.DataOra.Year == year && e.DataOra.Month == month)
                .OrderBy(e => e.DataOra)
                .ToList();

            var pairs = _workTimeCalculator.BuildPairsCrossDay(monthEntries);
            var groupedByDay = pairs
                .GroupBy(p => p.In.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            var policy = new WorkTimePolicy
            {
                RoundEntryUp = true,
                RoundExitDown = true,
                RoundingBlockMinutes = _settings.UnitaArrotondamentoMinuti > 0 ? _settings.UnitaArrotondamentoMinuti : 15,
                OvertimeThresholdMinutes = _settings.SogliaMinuti > 0 ? _settings.SogliaMinuti : 15,
                OvertimeBlockMinutes = _settings.UnitaArrotondamentoMinuti > 0 ? _settings.UnitaArrotondamentoMinuti : 15,
                DeficitRecoveryBlockMinutes = _settings.SogliaMinuti > 0 ? _settings.SogliaMinuti : 15,
                AlwaysHolidayDays = new List<DayOfWeek>(),
                RecurringHolidays = _holidays
                    .Select(h => (Month: h.Data.Month, Day: h.Data.Day))
                    .Distinct()
                    .ToList(),
                AdditionalHolidayDates = _holidays.Select(h => h.Data.Date).ToHashSet()
            };

            int ordinaryMinutes = 0;
            int overtimeMinutes = 0;

            foreach (var kvp in groupedByDay)
            {
                var dayResult = _workTimeCalculator.CalculateDay(user, kvp.Key, kvp.Value, policy);
                ordinaryMinutes += dayResult.OrdinaryMinutes;
                overtimeMinutes += dayResult.OvertimeMinutes;
            }

            double totaleOrdinarie = ordinaryMinutes / 60.0;
            double totaleStraordinarie = overtimeMinutes / 60.0;

            decimal compensoBase = (decimal)totaleOrdinarie * user.CompensoOrarioBase;
            decimal compensoExtra = (decimal)totaleStraordinarie * user.CompensoOrarioExtra;

            return new PaySummary
            {
                UserId = user.Id,
                OreOrdinarie = totaleOrdinarie,
                OreStraordinarie = totaleStraordinarie,
                CompensoOrdinarie = compensoBase,
                CompensoStraordinarie = compensoExtra
            };
        }
    }
}