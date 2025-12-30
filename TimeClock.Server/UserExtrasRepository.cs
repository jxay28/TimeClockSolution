using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TimeClock.Core.Models;

namespace TimeClock.Server
{
    /// <summary>
    /// Gestisce i campi aggiuntivi dell'anagrafica utente che NON esistono nel modello UserProfile del Core.
    /// File: utenti_extras.json (nella cartella dati).
    /// </summary>
    public sealed class UserExtrasRepository
    {
        public sealed class UserExtraData
        {
            public string UserId { get; set; } = string.Empty;
            public double OreGiornalierePreviste { get; set; }
            public int GiorniLavorativiSettimana { get; set; }
        }

        private readonly string _filePath;
        private readonly Dictionary<string, UserExtraData> _byId = new(StringComparer.OrdinalIgnoreCase);

        public UserExtrasRepository(string dataFolder)
        {
            _filePath = Path.Combine(dataFolder ?? string.Empty, "utenti_extras.json");
            Load();
        }

        public void Load()
        {
            _byId.Clear();

            if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
                return;

            try
            {
                string json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<UserExtraData>>(json) ?? new List<UserExtraData>();

                foreach (var item in list)
                {
                    if (string.IsNullOrWhiteSpace(item.UserId))
                        continue;

                    _byId[item.UserId] = item;
                }
            }
            catch
            {
                // File corrotto? Non blocchiamo l'app: consideriamo nessun extra.
            }
        }

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(_filePath))
                return;

            var list = _byId.Values
                .OrderBy(x => x.UserId)
                .ToList();

            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_filePath, json);
        }

        public bool TryGet(string userId, out UserExtraData data)
        {
            return _byId.TryGetValue(userId, out data!);
        }

        public void Set(string userId, double oreGiornalierePreviste, int giorniLavorativiSettimana)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return;

            if (giorniLavorativiSettimana < 1) giorniLavorativiSettimana = 1;
            if (giorniLavorativiSettimana > 7) giorniLavorativiSettimana = 7;
            if (oreGiornalierePreviste < 0) oreGiornalierePreviste = 0;

            _byId[userId] = new UserExtraData
            {
                UserId = userId,
                OreGiornalierePreviste = oreGiornalierePreviste,
                GiorniLavorativiSettimana = giorniLavorativiSettimana
            };
        }

        public int GetGiorniLavorativiOrFallback(UserProfile user, int fallback = 5)
        {
            if (user != null && TryGet(user.Id, out var ex) && ex.GiorniLavorativiSettimana >= 1 && ex.GiorniLavorativiSettimana <= 7)
                return ex.GiorniLavorativiSettimana;

            return fallback;
        }

        public double GetOreGiornaliereOrFallback(UserProfile user, double fallbackOreGiornaliere = 8.0, int fallbackGiorni = 5)
        {
            if (user == null)
                return fallbackOreGiornaliere;

            if (TryGet(user.Id, out var ex) && ex.OreGiornalierePreviste > 0)
                return ex.OreGiornalierePreviste;

            // Fallback storico: ore settimanali / giorni lavorativi (di default 5)
            int giorni = GetGiorniLavorativiOrFallback(user, fallbackGiorni);
            if (user.OreContrattoSettimanali > 0 && giorni > 0)
                return user.OreContrattoSettimanali / giorni;

            return fallbackOreGiornaliere;
        }
    }
}
