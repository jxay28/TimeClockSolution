using System;

namespace TimeClock.Core.Models
{
    public class UserProfile
    {
        public string Id { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }

        public string Nome { get; set; } = string.Empty;
        public string Cognome { get; set; } = string.Empty;
        public string Ruolo { get; set; } = string.Empty;

        public DateTime DataAssunzione { get; set; }

        public double OreContrattoSettimanali { get; set; }

        public decimal CompensoOrarioBase { get; set; }
        public decimal CompensoOrarioExtra { get; set; }

        // Nuovi campi per orari previsti
        public string? OrarioIngresso1 { get; set; }
        public string? OrarioUscita1 { get; set; }
        public string? OrarioIngresso2 { get; set; }
        public string? OrarioUscita2 { get; set; }
        public string FullName => $"{SequenceNumber:D3} - {Nome} {Cognome}";
        public override string ToString() => FullName;

    }
}
