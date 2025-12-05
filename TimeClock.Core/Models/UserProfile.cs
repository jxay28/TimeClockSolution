using System;

namespace TimeClock.Core.Models
{
    /// <summary>
    /// Rappresenta un dipendente con le informazioni contrattuali.
    /// </summary>
    public class UserProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string Cognome { get; set; } = string.Empty;
        public string Ruolo { get; set; } = string.Empty;
        public DateTime DataAssunzione { get; set; }
        public double OreContrattoSettimanali { get; set; }
        public decimal CompensoOrarioBase { get; set; }
        public decimal CompensoOrarioExtra { get; set; }

        public int SequenceNumber { get; set; }


    }
}