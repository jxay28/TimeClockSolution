using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using TimeClock.Core.Models;
using TimeClock.Core.Services;
using TimeClock.Server.Models;
using TimeClock.Server.Properties;

namespace TimeClock.Server
{
    public partial class App : Application
    {
        public static ParametriStraordinari ParametriGlobali;
        public static string ParametriFilePath { get; private set; } = string.Empty;
        public static CompanyInfo DatiAzienda { get; set; } = new CompanyInfo();
        public static string DatiAziendaFilePath { get; private set; } = string.Empty;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ConfigureDataFolder(Settings.Default.CsvFolderPath);
            NormalizzaFestivitaNazionaliFisse();
        }

        public static void SalvaParametriGlobali()
        {
            ParametriGlobali ??= new ParametriStraordinari();
            EnsureParametriPathInitialized();
            SafeFileWriter.WriteAllTextAtomic(ParametriFilePath, JsonSerializer.Serialize(ParametriGlobali, JsonOptions));
        }

        public static void ConfigureDataFolder(string? csvFolderPath)
        {
            string previousPath = ParametriFilePath;
            string previousCompanyPath = DatiAziendaFilePath;
            ParametriFilePath = ResolveParametriPath(csvFolderPath);
            EnsureFolderExistsForFile(ParametriFilePath);
            DatiAziendaFilePath = ResolveCompanyInfoPath(csvFolderPath);
            EnsureFolderExistsForFile(DatiAziendaFilePath);

            if (!string.IsNullOrWhiteSpace(previousPath) &&
                !string.Equals(previousPath, ParametriFilePath, System.StringComparison.OrdinalIgnoreCase) &&
                File.Exists(previousPath) &&
                !File.Exists(ParametriFilePath))
            {
                File.Copy(previousPath, ParametriFilePath, overwrite: false);
            }

            if (!string.IsNullOrWhiteSpace(previousCompanyPath) &&
                !string.Equals(previousCompanyPath, DatiAziendaFilePath, System.StringComparison.OrdinalIgnoreCase) &&
                File.Exists(previousCompanyPath) &&
                !File.Exists(DatiAziendaFilePath))
            {
                File.Copy(previousCompanyPath, DatiAziendaFilePath, overwrite: false);
            }

            if (File.Exists(ParametriFilePath))
            {
                ParametriGlobali = JsonSerializer.Deserialize<ParametriStraordinari>(File.ReadAllText(ParametriFilePath))
                    ?? new ParametriStraordinari();
            }
            else
            {
                ParametriGlobali = new ParametriStraordinari();
                SafeFileWriter.WriteAllTextAtomic(ParametriFilePath, JsonSerializer.Serialize(ParametriGlobali, JsonOptions));
            }

            if (File.Exists(DatiAziendaFilePath))
            {
                DatiAzienda = JsonSerializer.Deserialize<CompanyInfo>(File.ReadAllText(DatiAziendaFilePath))
                    ?? new CompanyInfo();
            }
            else
            {
                DatiAzienda = new CompanyInfo();
                SafeFileWriter.WriteAllTextAtomic(DatiAziendaFilePath, JsonSerializer.Serialize(DatiAzienda, JsonOptions));
            }
        }

        public static void SalvaDatiAzienda()
        {
            DatiAzienda ??= new CompanyInfo();
            if (string.IsNullOrWhiteSpace(DatiAziendaFilePath))
                DatiAziendaFilePath = ResolveCompanyInfoPath(Settings.Default.CsvFolderPath);

            EnsureFolderExistsForFile(DatiAziendaFilePath);
            SafeFileWriter.WriteAllTextAtomic(DatiAziendaFilePath, JsonSerializer.Serialize(DatiAzienda, JsonOptions));
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

        private static void EnsureParametriPathInitialized()
        {
            if (!string.IsNullOrWhiteSpace(ParametriFilePath))
                return;

            ParametriFilePath = ResolveParametriPath(Settings.Default.CsvFolderPath);
            EnsureFolderExistsForFile(ParametriFilePath);
        }

        private static string ResolveParametriPath(string? csvFolderPath)
        {
            if (!string.IsNullOrWhiteSpace(csvFolderPath) && Directory.Exists(csvFolderPath))
                return Path.Combine(csvFolderPath, "parametri_straordinari.json");

            return Path.Combine(AppContext.BaseDirectory, "parametri_straordinari.json");
        }

        private static string ResolveCompanyInfoPath(string? csvFolderPath)
        {
            if (!string.IsNullOrWhiteSpace(csvFolderPath) && Directory.Exists(csvFolderPath))
                return Path.Combine(csvFolderPath, "dati_azienda.json");

            return Path.Combine(AppContext.BaseDirectory, "dati_azienda.json");
        }

        private static void EnsureFolderExistsForFile(string filePath)
        {
            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }
    }
}
