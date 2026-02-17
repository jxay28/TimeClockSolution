using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using TimeClock.Core.Models;
using TimeClock.Core.Services;

namespace TimeClock.Server
{
    public partial class App : Application
    {
        public static ParametriStraordinari ParametriGlobali;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string file = "parametri_straordinari.json";

            if (File.Exists(file))
            {
                ParametriGlobali = JsonSerializer.Deserialize<ParametriStraordinari>(
                    File.ReadAllText(file));
            }
            else
            {
                ParametriGlobali = new ParametriStraordinari();
                SafeFileWriter.WriteAllTextAtomic(file,
                    JsonSerializer.Serialize(ParametriGlobali, new JsonSerializerOptions { WriteIndented = true }));
            }

            NormalizzaFestivitaNazionaliFisse();
        }

        public static void SalvaParametriGlobali()
        {
            SafeFileWriter.WriteAllTextAtomic("parametri_straordinari.json",
                JsonSerializer.Serialize(ParametriGlobali, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void NormalizzaFestivitaNazionaliFisse()
        {
            ParametriGlobali ??= new ParametriStraordinari();

            var fixedList = new[]
            {
                (1, 1), (1, 6), (4, 25), (5, 1), (6, 2),
                (8, 15), (11, 1), (12, 8), (12, 25), (12, 26)
            };

            var fixedSet = fixedList.ToHashSet();
            ParametriGlobali.FestivitaRicorrenti ??= new();
            ParametriGlobali.FestivitaRicorrenti = ParametriGlobali.FestivitaRicorrenti
                .Where(f => fixedSet.Contains((f.Mese, f.Giorno)))
                .Select(f => (f.Mese, f.Giorno))
                .Distinct()
                .ToList();

            SalvaParametriGlobali();
        }
    }
}
