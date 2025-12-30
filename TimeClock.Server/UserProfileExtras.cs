using System;
using System.Runtime.CompilerServices;

using TimeClock.Core.Models;

namespace TimeClock.Server
{
    /// <summary>
    /// Storage non-invasivo per estendere UserProfile (che arriva da TimeClock.Core).
    /// Non potendo aggiungere proprietà alla classe (non è partial), le teniamo qui.
    /// </summary>
    public static class UserProfileExtras
    {
        private sealed class Extras
        {
            public double OreGiornalierePreviste { get; set; }
            public int GiorniLavorativiSettimana { get; set; }
        }

        private static readonly ConditionalWeakTable<UserProfile, Extras> _table = new();

        public static void SetOreGiornalierePreviste(this UserProfile user, double ore)
        {
            if (user == null) return;
            var ex = _table.GetOrCreateValue(user);
            ex.OreGiornalierePreviste = ore;
        }

        public static double GetOreGiornalierePreviste(this UserProfile user, double fallbackOreGiornaliere)
        {
            if (user == null) return fallbackOreGiornaliere;
            if (_table.TryGetValue(user, out var ex) && ex.OreGiornalierePreviste > 0)
                return ex.OreGiornalierePreviste;
            return fallbackOreGiornaliere;
        }

        public static void SetGiorniLavorativiSettimana(this UserProfile user, int giorni)
        {
            if (user == null) return;
            var ex = _table.GetOrCreateValue(user);
            ex.GiorniLavorativiSettimana = giorni;
        }

        public static int GetGiorniLavorativiSettimana(this UserProfile user, int fallbackGiorni)
        {
            if (user == null) return fallbackGiorni;
            if (_table.TryGetValue(user, out var ex) && ex.GiorniLavorativiSettimana > 0)
                return ex.GiorniLavorativiSettimana;
            return fallbackGiorni;
        }

        public static double CalcolaOreSettimanali(this UserProfile user, double fallbackOreSettimanali)
        {
            if (user == null) return fallbackOreSettimanali;
            // Default “storico”: ore settimanali già presenti nel profilo
            var baseOreSett = user.OreContrattoSettimanali > 0 ? user.OreContrattoSettimanali : fallbackOreSettimanali;
            // Default “storico”: 5 giorni lavorativi
            var fallbackGiorni = 5;
            var oreGiornoFallback = baseOreSett > 0 ? baseOreSett / fallbackGiorni : 0;
            var oreGiorno = user.GetOreGiornalierePreviste(oreGiornoFallback);
            var giorni = user.GetGiorniLavorativiSettimana(fallbackGiorni);
            if (oreGiorno > 0 && giorni > 0)
                return oreGiorno * giorni;
            return baseOreSett;
        }

        // DTO usata SOLO per la persistenza in utenti.json
        public sealed class Persisted
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
            public string? OrarioIngresso1 { get; set; }
            public string? OrarioUscita1 { get; set; }
            public string? OrarioIngresso2 { get; set; }
            public string? OrarioUscita2 { get; set; }

            // Nuovi campi (non presenti in TimeClock.Core)
            public double OreGiornalierePreviste { get; set; }
            public int GiorniLavorativiSettimana { get; set; }
        }
    }
}
