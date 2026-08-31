using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AskThem.Services;

namespace AskThem.Config
{
    /// <summary>
    /// Réglages du rapport de contrôle, lus dans config\rapport-controle.json.
    /// Aucun nom de propriété SolidWorks n'est écrit dans le code : tout passe par ce fichier,
    /// que l'on peut corriger sur un poste sans recompiler.
    /// </summary>
    public sealed class RapportControleConfig
    {
        private const string DossierConfig = "config";
        private const string NomFichier = "rapport-controle.json";

        /// <summary>Noms de propriétés à essayer, par champ (matiere, traitement, peinture...).</summary>
        public Dictionary<string, List<string>> Proprietes { get; set; }

        /// <summary>Ce qu'on écrit quand aucune propriété n'a été trouvée.</summary>
        public string ValeurSiVide { get; set; }

        /// <summary>Exigence d'aspect, seconde ligne fixe du tableau.</summary>
        public string AspectParDefaut { get; set; }

        /// <summary>
        /// Distance maximale au bord de la feuille, en millimètres, pour qu'une note d'un
        /// caractère soit tenue pour un repère de cadre. Écarte la lettre de révision du
        /// cartouche, qui est elle aussi une note d'un seul caractère.
        /// </summary>
        public double MargeBordRepere { get; set; }

        public RapportControleConfig()
        {
            Proprietes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            ValeurSiVide = "Sans";
            AspectParDefaut = "Ébavuré, sans rayure";
            MargeBordRepere = 12.0;
        }

        /// <summary>Chemin complet du fichier, à côté de l'exécutable.</summary>
        public static string Chemin()
        {
            return Path.Combine(AppContext.BaseDirectory, DossierConfig, NomFichier);
        }

        /// <summary>
        /// Charge les réglages. Fichier absent ou illisible : valeurs par défaut, écrites
        /// sur le disque pour que l'utilisateur ait un point de départ à modifier.
        /// </summary>
        public static RapportControleConfig Load()
        {
            RapportControleConfig cfg = null;
            try
            {
                if (File.Exists(Chemin()))
                {
                    JsonSerializerOptions options = new JsonSerializerOptions();
                    options.PropertyNameCaseInsensitive = true;
                    options.AllowTrailingCommas = true;
                    options.ReadCommentHandling = JsonCommentHandling.Skip;
                    cfg = JsonSerializer.Deserialize<RapportControleConfig>(File.ReadAllText(Chemin()), options);
                }
            }
            catch (Exception ex)
            {
                LogService.Write("rapport-controle.json illisible, valeurs par défaut : " + ex.Message);
                cfg = null;
            }

            if (cfg == null)
            {
                cfg = Defaut();
                Save(cfg);
            }

            if (cfg.Proprietes == null || cfg.Proprietes.Count == 0) cfg.Proprietes = Defaut().Proprietes;
            if (string.IsNullOrWhiteSpace(cfg.ValeurSiVide)) cfg.ValeurSiVide = "Sans";
            if (string.IsNullOrWhiteSpace(cfg.AspectParDefaut)) cfg.AspectParDefaut = "Ébavuré, sans rayure";
            if (cfg.MargeBordRepere <= 0) cfg.MargeBordRepere = 12.0;

            // Le dictionnaire issu de JSON est sensible à la casse : on le refait insensible.
            Dictionary<string, List<string>> propre =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> p in cfg.Proprietes)
                if (p.Value != null) propre[p.Key] = p.Value;
            cfg.Proprietes = propre;

            return cfg;
        }

        /// <summary>Écrit le fichier. Un échec est journalisé, jamais remonté.</summary>
        public static void Save(RapportControleConfig cfg)
        {
            try
            {
                string dossier = Path.GetDirectoryName(Chemin());
                if (!string.IsNullOrEmpty(dossier)) Directory.CreateDirectory(dossier);
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = true;
                options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                File.WriteAllText(Chemin(), JsonSerializer.Serialize(cfg, options));
            }
            catch (Exception ex)
            {
                LogService.Write("Impossible d'écrire rapport-controle.json : " + ex.Message);
            }
        }

        /// <summary>Noms de propriétés à essayer pour un champ. Jamais null.</summary>
        public List<string> NomsDe(string champ)
        {
            List<string> noms;
            if (Proprietes != null && Proprietes.TryGetValue(champ, out noms) && noms != null) return noms;
            return new List<string>();
        }

        /// <summary>
        /// Réglages d'origine. Les noms viennent des propriétés réellement observées dans
        /// le coffre : Material, Traitement, Description, Revision.
        /// </summary>
        private static RapportControleConfig Defaut()
        {
            RapportControleConfig c = new RapportControleConfig();
            c.Proprietes["matiere"] = new List<string> { "Material", "Matiere", "Matière", "MATIERE", "SW-Material" };
            c.Proprietes["traitement"] = new List<string> { "Traitement", "TraitementSurface", "Finition", "Finitions", "HeatTreatment" };
            c.Proprietes["peinture"] = new List<string> { "Peinture", "RAL", "Paint" };
            c.Proprietes["durete"] = new List<string> { "Durete", "Dureté", "Hardness" };
            c.Proprietes["designation"] = new List<string> { "Description", "Designation", "Désignation" };
            c.Proprietes["revision"] = new List<string> { "Revision", "Indice", "Rev" };
            return c;
        }
    }
}
