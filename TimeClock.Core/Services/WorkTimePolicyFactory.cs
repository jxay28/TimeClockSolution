using System;
using System.Collections.Generic;
using System.Linq;
using TimeClock.Core.Models;

namespace TimeClock.Core.Services
{
    /// <summary>
    /// Factory unico per costruire policy di calcolo coerenti in tutti i moduli.
    /// </summary>
    public static class WorkTimePolicyFactory
    {
        public static WorkTimePolicy FromOvertimeSettings(OvertimeSettings settings, IEnumerable<Holiday> holidays)
        {
            int roundingBlock = settings?.UnitaArrotondamentoMinuti > 0
                ? settings.UnitaArrotondamentoMinuti
                : 15;

            int threshold = settings?.SogliaMinuti > 0
                ? settings.SogliaMinuti
                : 15;

            var holidayList = holidays?.ToList() ?? new List<Holiday>();

            return new WorkTimePolicy
            {
                RoundEntryUp = true,
                RoundExitDown = true,
                RoundingBlockMinutes = roundingBlock,
                OvertimeThresholdMinutes = threshold,
                OvertimeBlockMinutes = roundingBlock,
                DeficitRecoveryBlockMinutes = threshold,
                AlwaysHolidayDays = new List<DayOfWeek>(),
                RecurringHolidays = holidayList
                    .Select(h => (Month: h.Data.Month, Day: h.Data.Day))
                    .Distinct()
                    .ToList(),
                AdditionalHolidayDates = holidayList
                    .Select(h => h.Data.Date)
                    .ToHashSet()
            };
        }

        public static WorkTimePolicy FromGlobalParameters(ParametriStraordinari? p)
        {
            int block = p?.SogliaMinutiStraordinario > 0
                ? p.SogliaMinutiStraordinario
                : 15;

            return new WorkTimePolicy
            {
                RoundEntryUp = true,
                RoundExitDown = true,
                RoundingBlockMinutes = block,
                OvertimeThresholdMinutes = block,
                OvertimeBlockMinutes = block,
                DeficitRecoveryBlockMinutes = block,
                AlwaysHolidayDays = p?.GiorniSempreFestivi?.ToList() ?? new List<DayOfWeek>
                {
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday
                },
                RecurringHolidays = p?.FestivitaRicorrenti?
                    .Select(f => (Month: f.Mese, Day: f.Giorno))
                    .ToList()
                    ?? new List<(int Month, int Day)>(),
                AdditionalHolidayDates = p?.FestivitaAggiuntive?
                    .Select(d => d.Date)
                    .ToHashSet()
                    ?? new HashSet<DateTime>()
            };
        }
    }
}
