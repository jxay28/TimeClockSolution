using System;
using System.Collections.Generic;
using TimeClock.Core.Models;
using TimeClock.Core.Services;
using Xunit;

namespace TimeClock.Core.Tests
{
    public sealed class WorkTimePolicyFactoryTests
    {
        [Fact]
        public void FromOvertimeSettings_MapsThresholdAndBlocks()
        {
            var settings = new OvertimeSettings
            {
                SogliaMinuti = 30,
                UnitaArrotondamentoMinuti = 15
            };

            var policy = WorkTimePolicyFactory.FromOvertimeSettings(settings, Array.Empty<Holiday>());

            Assert.True(policy.RoundEntryUp);
            Assert.True(policy.RoundExitDown);
            Assert.Equal(15, policy.RoundingBlockMinutes);
            Assert.Equal(30, policy.OvertimeThresholdMinutes);
            Assert.Equal(15, policy.OvertimeBlockMinutes);
            Assert.Equal(30, policy.DeficitRecoveryBlockMinutes);
        }

        [Fact]
        public void FromGlobalParameters_UsesFallbackWeekendWhenMissing()
        {
            var policy = WorkTimePolicyFactory.FromGlobalParameters(null);

            Assert.Contains(DayOfWeek.Saturday, policy.AlwaysHolidayDays);
            Assert.Contains(DayOfWeek.Sunday, policy.AlwaysHolidayDays);
            Assert.Equal(15, policy.OvertimeThresholdMinutes);
            Assert.Equal(15, policy.DeficitRecoveryBlockMinutes);
        }

        [Fact]
        public void FromGlobalParameters_UsesConfiguredHolidayDays()
        {
            var p = new ParametriStraordinari
            {
                SogliaMinutiStraordinario = 30,
                GiorniSempreFestivi = new List<DayOfWeek> { DayOfWeek.Sunday }
            };
            p.FestivitaRicorrenti = new List<(int Mese, int Giorno)> { (12, 25) };
            p.FestivitaAggiuntive = new List<DateTime> { new DateTime(2026, 2, 2) };

            var policy = WorkTimePolicyFactory.FromGlobalParameters(p);

            Assert.Single(policy.AlwaysHolidayDays);
            Assert.Equal(DayOfWeek.Sunday, policy.AlwaysHolidayDays[0]);
            Assert.Single(policy.RecurringHolidays);
            Assert.Contains((12, 25), policy.RecurringHolidays);
            Assert.Contains(new DateTime(2026, 2, 2), policy.AdditionalHolidayDates);
            Assert.Equal(30, policy.OvertimeThresholdMinutes);
        }
    }
}
