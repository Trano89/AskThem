using System;
using System.IO;
using System.Text;
using System.Text.Json;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>
    /// Les réglages propres à un utilisateur, rangés dans son profil Windows
    /// (%APPDATA%\AskThem\preferences.json) : ils le suivent, ne concernent que lui, et une
    /// mise à jour d'AskThem — qui ne remplace que l'exécutable — n'y touche pas.
    /// </summary>
    public sealed class PreferencesUtilisateur
    {
        /// <summary>Délai de rappel d'origine, en jours.</summary>
        public const int RappelParDefaut = 7;

        /// <summary>
        /// Jours entre l'envoi d'une demande et la question « avez-vous reçu une réponse ? »,
        /// puis entre deux rappels. Zéro : le réglage du poste, ou à défaut sept jours.
        /// </summary>
        public int RappelJours { get; set; }

        /// <summary>Dernière vue utilisée : vrai pour la vue complète, faux pour le mode guidé.</summary>
        public bool VueComplete { get; set; }

        public static string Chemin()
        {
            return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                                "AskThem", "preferences.json");
        }

        public static PreferencesUtilisateur Lire()
        {
            try
            {
                if (File.Exists(Chemin()))
                {
                    JsonSerializerOptions o = new JsonSerializerOptions();
                    o.PropertyNameCaseInsensitive = true;
                    PreferencesUtilisateur p = JsonSerializer.Deserialize<PreferencesUtilisateur>(
                        File.ReadAllText(Chemin(), Encoding.UTF8), o);
                    if (p != null) return p;
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Préférences illisibles, réglages d'origine utilisés : " + ex.Message);
            }
            return new PreferencesUtilisateur();
        }

        public bool Enregistrer(out string message)
        {
            message = "";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Chemin()));
                JsonSerializerOptions o = new JsonSerializerOptions();
                o.WriteIndented = true;
                string temporaire = Chemin() + ".tmp";
                File.WriteAllText(temporaire, JsonSerializer.Serialize(this, o), new UTF8Encoding(false));
                File.Move(temporaire, Chemin(), true);
                return true;
            }
            catch (Exception ex)
            {
                message = "Préférences non enregistrées : " + ex.Message;
                LogService.Write(message);
                return false;
            }
        }

        /// <summary>Le délai de rappel qui s'applique à cet utilisateur.</summary>
        public static int DelaiRappel(AppConfig config)
        {
            int perso = Lire().RappelJours;
            if (perso > 0) return perso;
            if (config != null && config.RappelJours > 0) return config.RappelJours;
            return RappelParDefaut;
        }
    }
}
