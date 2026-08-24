using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>Lecture et écriture de config.json, à côté de l'exécutable.</summary>
    public static class ConfigService
    {
        private const string FileName = "config.json";

        /// <summary>Chemin complet du fichier de configuration.</summary>
        public static string GetConfigPath()
        {
            return Path.Combine(AppContext.BaseDirectory, FileName);
        }

        /// <summary>Charge la configuration. Si le fichier est absent ou illisible, écrit les valeurs par défaut.</summary>
        public static AppConfig Load()
        {
            AppConfig config = null;
            try
            {
                if (File.Exists(GetConfigPath()))
                {
                    string json = File.ReadAllText(GetConfigPath());
                    JsonSerializerOptions options = new JsonSerializerOptions();
                    options.PropertyNameCaseInsensitive = true;
                    options.AllowTrailingCommas = true;
                    options.ReadCommentHandling = JsonCommentHandling.Skip;
                    config = JsonSerializer.Deserialize<AppConfig>(json, options);
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Configuration illisible, valeurs par défaut appliquées : " + ex.Message);
                config = null;
            }

            if (config == null)
            {
                config = new AppConfig();
                Save(config);
            }

            // Valeurs de repli pour les clés vides ou absentes.
            if (string.IsNullOrWhiteSpace(config.PdmRoot)) config.PdmRoot = "C:\\00_LynceeTec\\";
            if (config.ZipThresholdMb <= 0) config.ZipThresholdMb = 20;
            if (config.DefaultSender == null) config.DefaultSender = "";
            if (string.IsNullOrWhiteSpace(config.OutputRoot))
            {
                config.OutputRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
            }
            return config;
        }

        /// <summary>Écrit la configuration sur le disque. Un échec est journalisé, sans exception.</summary>
        public static void Save(AppConfig config)
        {
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = true;
                File.WriteAllText(GetConfigPath(), JsonSerializer.Serialize(config, options));
            }
            catch (Exception ex)
            {
                LogService.Write("Impossible d'écrire config.json : " + ex.Message);
            }
        }
    }
}
