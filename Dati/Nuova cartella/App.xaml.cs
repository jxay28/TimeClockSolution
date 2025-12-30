using System.IO;
using System.Text.Json;
using System.Windows;
using TimeClock.Core.Models;

namespace TimeClock.Server
{
    public partial class App : Application
    {
        public static ParametriStraordinari ParametriGlobali { get; private set; } = null!;


        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string file = "parametri_straordinari.json";

            if (File.Exists(file))
            {
                ParametriGlobali = JsonSerializer.Deserialize<ParametriStraordinari>(File.ReadAllText(file));
            }
            else
            {
                ParametriGlobali = new ParametriStraordinari();
                File.WriteAllText(file,
                    JsonSerializer.Serialize(ParametriGlobali, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        public static void SalvaParametriGlobali()
        {
            File.WriteAllText("parametri_straordinari.json",
                JsonSerializer.Serialize(ParametriGlobali, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
