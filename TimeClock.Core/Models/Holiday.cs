using System;

namespace TimeClock.Core.Models
{
    /// <summary>
    /// Rappresenta una festività in cui tutte le ore sono straordinarie.
    /// </summary>
    public class Holiday
    {
        public DateTime Data { get; set; }
        public string Descrizione { get; set; } = string.Empty;
    }
}