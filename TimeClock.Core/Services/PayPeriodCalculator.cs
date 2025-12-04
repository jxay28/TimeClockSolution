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

        public PayPeriodCalculator(OvertimeSettings settings, IEnumerable<Holiday> holidays)
        {
            _settings = settings;
            _holidays = holidays;
        }

        /// <summary>
        /// Calcola il riepilogo per il periodo indicato.
        /// </summary>
        public PaySummary Calculate(UserProfile user, IEnumerable<TimeCardEntry> entries, int year, int month)
        {
            var groupedByDate = entries
                .Where(e => e.DataOra.Year == year && e.DataOra.Month == month)
                .OrderBy(e => e.DataOra)
                .GroupBy(e => e.DataOra.Date);

            double totaleOrdinarie = 0;
            double totaleStraordinarie = 0;

            foreach (var dayGroup in groupedByDate)
            {
                double hours = 0;
                var punches = dayGroup.ToList();
                for (int i = 0; i < punches.Count - 1; i += 2)
                {
                    if (punches[i].Tipo == PunchType.Entrata && punches[i + 1].Tipo == PunchType.Uscita)
                    {
                        TimeSpan durata = punches[i + 1].DataOra - punches[i].DataOra;
                        hours += durata.TotalHours;
                    }
                }

                bool isHoliday = _holidays.Any(h => h.Data.Date == dayGroup.Key);
                double contractHours = user.OreContrattoSettimanali / 5.0; // assumiamo 5 giorni lavorativi

                if (isHoliday)
                {
                    totaleStraordinarie += hours;
                }
                else
                {
                    if (hours > contractHours)
                    {
                        double extra = hours - contractHours;
                        double extraMinutes = extra * 60;
                        if (extraMinutes > _settings.SogliaMinuti)
                        {
                            int units = (int)Math.Ceiling(extraMinutes / _settings.UnitaArrotondamentoMinuti);
                            totaleStraordinarie += units * (_settings.UnitaArrotondamentoMinuti / 60.0);
                        }
                        totaleOrdinarie += contractHours;
                    }
                    else
                    {
                        totaleOrdinarie += hours;
                    }
                }
            }

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