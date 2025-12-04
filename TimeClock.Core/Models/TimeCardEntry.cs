using System;

namespace TimeClock.Core.Models
{
    /// <summary>
    /// Rappresenta una singola timbratura (entrata o uscita).
    /// </summary>
    public class TimeCardEntry
    {
        public string UserId { get; set; } = string.Empty;
        public DateTime DataOra { get; set; }
        public PunchType Tipo { get; set; }
    }
}