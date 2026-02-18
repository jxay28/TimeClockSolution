using System;
using System.Collections.Generic;
using System.Linq;
using TimeClock.Core.Models;

namespace TimeClock.Core.Services
{
    public sealed class PairingResult
    {
        public List<(DateTime In, DateTime Out)> Pairs { get; } = new();
        public List<string> Notes { get; } = new();
    }

    /// <summary>
    /// Pairing formale delle timbrature con supporto cross-day ed edge case.
    /// </summary>
    public sealed class PunchPairingService
    {
        public PairingResult BuildPairs(IEnumerable<TimeCardEntry> entries)
        {
            var ordered = entries?
                .OrderBy(e => e.DataOra)
                .ToList()
                ?? new List<TimeCardEntry>();

            var result = new PairingResult();
            DateTime? openIn = null;

            foreach (var punch in ordered)
            {
                if (punch.Tipo == PunchType.Entrata)
                {
                    if (openIn.HasValue)
                    {
                        result.Notes.Add($"Entrata duplicata alle {punch.DataOra:yyyy-MM-dd HH:mm}: sostituita entrata precedente.");
                    }

                    openIn = punch.DataOra;
                    continue;
                }

                if (!openIn.HasValue)
                {
                    result.Notes.Add($"Uscita orfana alle {punch.DataOra:yyyy-MM-dd HH:mm}: ignorata.");
                    continue;
                }

                if (punch.DataOra <= openIn.Value)
                {
                    result.Notes.Add($"Uscita non valida alle {punch.DataOra:yyyy-MM-dd HH:mm}: ignorata.");
                    continue;
                }

                result.Pairs.Add((openIn.Value, punch.DataOra));
                openIn = null;
            }

            if (openIn.HasValue)
            {
                result.Notes.Add($"Entrata senza uscita alle {openIn.Value:yyyy-MM-dd HH:mm}: ignorata.");
            }

            return result;
        }
    }
}
