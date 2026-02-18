using System;
using System.Collections.Generic;
using TimeClock.Core.Models;
using TimeClock.Core.Services;
using Xunit;

namespace TimeClock.Core.Tests
{
    public sealed class PunchPairingServiceTests
    {
        private static TimeCardEntry Punch(string ts, PunchType type) =>
            new TimeCardEntry { DataOra = DateTime.Parse(ts), Tipo = type, UserId = "u1" };

        [Fact]
        public void BuildPairs_SupportsCrossDay()
        {
            var sut = new PunchPairingService();
            var entries = new List<TimeCardEntry>
            {
                Punch("2026-02-10 22:00", PunchType.Entrata),
                Punch("2026-02-11 06:00", PunchType.Uscita)
            };

            var result = sut.BuildPairs(entries);

            Assert.Single(result.Pairs);
            Assert.Equal(DateTime.Parse("2026-02-10 22:00"), result.Pairs[0].In);
            Assert.Equal(DateTime.Parse("2026-02-11 06:00"), result.Pairs[0].Out);
            Assert.Empty(result.Notes);
        }

        [Fact]
        public void BuildPairs_DuplicateEntryKeepsLatestOpenEntry()
        {
            var sut = new PunchPairingService();
            var entries = new List<TimeCardEntry>
            {
                Punch("2026-02-10 08:00", PunchType.Entrata),
                Punch("2026-02-10 08:15", PunchType.Entrata),
                Punch("2026-02-10 12:00", PunchType.Uscita)
            };

            var result = sut.BuildPairs(entries);

            Assert.Single(result.Pairs);
            Assert.Equal(DateTime.Parse("2026-02-10 08:15"), result.Pairs[0].In);
            Assert.Equal(DateTime.Parse("2026-02-10 12:00"), result.Pairs[0].Out);
            Assert.Contains(result.Notes, n => n.Contains("Entrata duplicata"));
        }

        [Fact]
        public void BuildPairs_OrphanExitIsIgnoredWithNote()
        {
            var sut = new PunchPairingService();
            var entries = new List<TimeCardEntry>
            {
                Punch("2026-02-10 07:00", PunchType.Uscita),
                Punch("2026-02-10 08:00", PunchType.Entrata),
                Punch("2026-02-10 12:00", PunchType.Uscita)
            };

            var result = sut.BuildPairs(entries);

            Assert.Single(result.Pairs);
            Assert.Contains(result.Notes, n => n.Contains("Uscita orfana"));
        }
    }
}
