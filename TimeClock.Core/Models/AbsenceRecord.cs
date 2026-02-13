using System;

namespace TimeClock.Core.Models
{
    public class AbsenceRecord
    {
        public string UserId { get; set; } = string.Empty;
        public DateTime Data { get; set; }
        public AbsenceType Tipo { get; set; } = AbsenceType.Ferie;
        public double Ore { get; set; }
        public string? Note { get; set; }
    }
}
