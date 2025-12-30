using System;

namespace TimeClock.Server.Models
{
    /// <summary>
    /// Modello utente usato dal progetto Server (UI + persistenza su utenti.json).
    ///
    /// Nota: NON è lo stesso UserProfile presente in TimeClock.Core.
    /// Evitiamo volutamente di usare il tipo del Core perché nel tuo progetto
    /// Core non contiene (o non espone) i campi di anagrafica necessari alla UI.
    /// </summary>
    public class UserProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public int SequenceNumber { get; set; }

        public string Nome { get; set; } = string.Empty;
        public string Cognome { get; set; } = string.Empty;
        public string Ruolo { get; set; } = string.Empty;

        public DateTime DataAssunzione { get; set; } = DateTime.Today;

        public double OreContrattoSettimanali { get; set; }
        public decimal CompensoOrarioBase { get; set; }
        public decimal CompensoOrarioExtra { get; set; }

        // Orari previsti (rimangono come griglia contrattuale se ti servono nel report)
        public string OrarioIngresso1 { get; set; } = string.Empty;
        public string OrarioUscita1 { get; set; } = string.Empty;
        public string OrarioIngresso2 { get; set; } = string.Empty;
        public string OrarioUscita2 { get; set; } = string.Empty;

        // Campi extra richiesti
        public double OreGiornalierePreviste { get; set; } = 8.0;
        public int GiorniLavorativiSettimanali { get; set; } = 5;

        public override string ToString()
        {
            var seq = SequenceNumber > 0 ? $"[{SequenceNumber}] " : string.Empty;
            return $"{seq}{Nome} {Cognome}".Trim();
        }
    }
}
