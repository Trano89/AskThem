using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>
    /// Dépôt partagé des documents exportés depuis le coffre.
    ///
    /// Un poste équipé y publie ce qu'il produit ; un poste sans SolidWorks y lit ce dont il a
    /// besoin. Sans ce dépôt, un acheteur qui n'a ni SolidWorks ni vue du coffre ne peut joindre
    /// aucun plan à une demande de fabrication : c'est aujourd'hui le seul obstacle qui
    /// l'empêche de faire son travail depuis son poste.
    ///
    /// L'organisation est « racine / numéro d'article / empreinte ». L'empreinte est calculée
    /// sur les fichiers sources du coffre : dès que le plan ou le modèle change, elle change, et
    /// l'export précédent reste en place au lieu d'être écrasé. Une demande partie hier reste
    /// donc reconstituable, même si le plan a bougé depuis.
    /// </summary>
    public class DepotExports
    {
        private const string NomManifeste = "manifeste.json";

        private readonly string _racine;

        public DepotExports(string racine)
        {
            _racine = racine != null ? racine.Trim() : "";
        }

        /// <summary>Emplacement par défaut du dépôt, à côté des dossiers de demande.</summary>
        public static string RacineParDefaut(AppConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.ArchiveRoot)) return "";
            return Path.Combine(config.ArchiveRoot, "_exports");
        }

        /// <summary>Ce qu'un export conserve d'un article, à côté de ses fichiers.</summary>
        public class Manifeste
        {
            public string NoArticle { get; set; }
            public string Empreinte { get; set; }
            public string Designation { get; set; }
            public string RevisionPlan { get; set; }
            public string RevisionModele { get; set; }
            public string Matiere { get; set; }
            public string Traitement { get; set; }
            public string Etat { get; set; }
            public List<string> Fichiers { get; set; }
            public string ExportePar { get; set; }
            public DateTime ExporteLeUtc { get; set; }
            public string VersionAskThem { get; set; }

            public Manifeste()
            {
                NoArticle = "";
                Empreinte = "";
                Designation = "";
                RevisionPlan = "";
                RevisionModele = "";
                Matiere = "";
                Traitement = "";
                Etat = "";
                Fichiers = new List<string>();
                ExportePar = "";
                VersionAskThem = "";
            }

            /// <summary>Âge de l'export, pour dire à l'utilisateur depuis quand il dort.</summary>
            public int JoursDepuisExport
            {
                get
                {
                    if (ExporteLeUtc == default(DateTime)) return -1;
                    return (int)Math.Floor((DateTime.UtcNow - ExporteLeUtc).TotalDays);
                }
            }
        }

        // ------------------------------------------------------------------ empreinte

        /// <summary>
        /// Empreinte des fichiers sources : taille et date de dernière écriture.
        ///
        /// Ce n'est pas la révision du coffre, et c'est un compromis assumé : lire la révision
        /// exigerait d'ouvrir le document, donc SolidWorks, donc précisément ce dont le poste
        /// consommateur ne dispose pas. Taille et horodatage suffisent à détecter qu'un fichier
        /// a bougé.
        /// </summary>
        public static string Empreinte(string cheminModele, string cheminPlan)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string chemin in new string[] { cheminModele, cheminPlan })
            {
                if (string.IsNullOrWhiteSpace(chemin)) continue;
                try
                {
                    FileInfo fi = new FileInfo(chemin);
                    if (!fi.Exists) continue;
                    sb.Append(fi.Name.ToLowerInvariant());
                    sb.Append('|');
                    sb.Append(fi.Length);
                    sb.Append('|');
                    sb.Append(fi.LastWriteTimeUtc.Ticks);
                    sb.Append(';');
                }
                catch (Exception)
                {
                    // Un fichier illisible ne doit pas empêcher de calculer l'empreinte des autres.
                }
            }
            if (sb.Length == 0) return "";

            using (SHA256 sha = SHA256.Create())
            {
                byte[] somme = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                StringBuilder hex = new StringBuilder(16);
                for (int i = 0; i < 8; i++) hex.Append(somme[i].ToString("x2"));
                return hex.ToString();
            }
        }

        // ------------------------------------------------------------------ lecture

        /// <summary>Vrai si le dépôt est joignable depuis ce poste.</summary>
        public bool Accessible()
        {
            if (string.IsNullOrWhiteSpace(_racine)) return false;
            try { return Directory.Exists(_racine); }
            catch (Exception) { return false; }
        }

        public string DossierArticle(string noArticle)
        {
            if (string.IsNullOrWhiteSpace(_racine)) return "";
            return Path.Combine(_racine, NomSur(noArticle));
        }

        public string Dossier(string noArticle, string empreinte)
        {
            string racineArticle = DossierArticle(noArticle);
            if (racineArticle == "" || string.IsNullOrWhiteSpace(empreinte)) return "";
            return Path.Combine(racineArticle, NomSur(empreinte));
        }

        /// <summary>Vrai si cet article a déjà été exporté sous cette empreinte.</summary>
        public bool Existe(string noArticle, string empreinte)
        {
            string d = Dossier(noArticle, empreinte);
            if (d == "") return false;
            try { return File.Exists(Path.Combine(d, NomManifeste)); }
            catch (Exception) { return false; }
        }

        /// <summary>Le manifeste correspondant à cette empreinte, ou null.</summary>
        public Manifeste Lire(string noArticle, string empreinte)
        {
            string d = Dossier(noArticle, empreinte);
            return d == "" ? null : LireDossier(d);
        }

        /// <summary>
        /// Le manifeste le plus récent de cet article, quelle que soit son empreinte.
        ///
        /// C'est ce que lit un poste sans SolidWorks : il ne peut pas calculer l'empreinte
        /// courante, faute d'accès au coffre. Il prend donc le dernier export publié, et
        /// l'interface annonce sa date pour que l'acheteur juge lui-même de sa fraîcheur.
        /// </summary>
        public Manifeste Lire(string noArticle)
        {
            string racineArticle = DossierArticle(noArticle);
            if (racineArticle == "") return null;

            Manifeste plusRecent = null;
            try
            {
                if (!Directory.Exists(racineArticle)) return null;
                foreach (string d in Directory.GetDirectories(racineArticle))
                {
                    Manifeste m = LireDossier(d);
                    if (m == null) continue;
                    if (plusRecent == null || m.ExporteLeUtc > plusRecent.ExporteLeUtc) plusRecent = m;
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Dépôt d'exports illisible pour " + noArticle + " : " + ex.Message);
                return null;
            }
            return plusRecent;
        }

        /// <summary>Chemins complets des fichiers publiés pour ce manifeste, ceux qui existent.</summary>
        public List<string> Fichiers(Manifeste m)
        {
            List<string> chemins = new List<string>();
            if (m == null) return chemins;

            string d = Dossier(m.NoArticle, m.Empreinte);
            if (d == "") return chemins;

            foreach (string nom in m.Fichiers)
            {
                if (string.IsNullOrWhiteSpace(nom)) continue;
                string complet = Path.Combine(d, nom);
                try { if (File.Exists(complet)) chemins.Add(complet); }
                catch (Exception) { }
            }
            return chemins;
        }

        // ------------------------------------------------------------------ écriture

        /// <summary>
        /// Publie un export, de façon atomique.
        ///
        /// Les fichiers sont d'abord copiés dans un dossier temporaire voisin, puis le dossier
        /// est renommé d'un seul geste. Un acheteur qui lit le dépôt pendant une publication ne
        /// tombe donc jamais sur un export à moitié écrit.
        ///
        /// Renvoie vrai si le dépôt contient l'export à l'issue de l'appel — y compris s'il y
        /// était déjà, une même empreinte désignant un même contenu.
        /// </summary>
        public bool Publier(Manifeste m, List<string> fichiersSource)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.NoArticle) || string.IsNullOrWhiteSpace(m.Empreinte))
                return false;
            if (fichiersSource == null || fichiersSource.Count == 0) return false;

            string destination = Dossier(m.NoArticle, m.Empreinte);
            if (destination == "") return false;

            string temporaire = destination + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                if (Directory.Exists(destination)) return true;   // déjà publié, même contenu

                Directory.CreateDirectory(temporaire);

                m.Fichiers = new List<string>();
                foreach (string source in fichiersSource)
                {
                    if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;
                    string nom = Path.GetFileName(source);
                    File.Copy(source, Path.Combine(temporaire, nom), true);
                    m.Fichiers.Add(nom);
                }
                if (m.Fichiers.Count == 0) { Supprimer(temporaire); return false; }

                m.ExporteLeUtc = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(m.ExportePar)) m.ExportePar = Environment.UserName;
                if (string.IsNullOrWhiteSpace(m.VersionAskThem)) m.VersionAskThem = UpdateService.CurrentVersion();

                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = true;
                File.WriteAllText(Path.Combine(temporaire, NomManifeste),
                                  JsonSerializer.Serialize(m, options), Encoding.UTF8);

                Directory.CreateDirectory(DossierArticle(m.NoArticle));

                // Le renommage est l'instant où l'export devient visible. Si un autre poste a
                // publié la même empreinte entre-temps, son travail vaut le nôtre : on garde le
                // sien et on jette le temporaire.
                if (Directory.Exists(destination)) { Supprimer(temporaire); return true; }
                Directory.Move(temporaire, destination);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write("Publication impossible dans le dépôt pour " + m.NoArticle + " : " + ex.Message);
                Supprimer(temporaire);
                return false;
            }
        }

        // ------------------------------------------------------------------ utilitaires

        private Manifeste LireDossier(string dossier)
        {
            try
            {
                string fichier = Path.Combine(dossier, NomManifeste);
                if (!File.Exists(fichier)) return null;

                JsonSerializerOptions options = new JsonSerializerOptions();
                options.PropertyNameCaseInsensitive = true;
                options.AllowTrailingCommas = true;

                Manifeste m = JsonSerializer.Deserialize<Manifeste>(File.ReadAllText(fichier), options);
                if (m != null && m.Fichiers == null) m.Fichiers = new List<string>();
                return m;
            }
            catch (Exception ex)
            {
                LogService.Write("Manifeste illisible dans " + dossier + " : " + ex.Message);
                return null;
            }
        }

        private static void Supprimer(string dossier)
        {
            try { if (Directory.Exists(dossier)) Directory.Delete(dossier, true); }
            catch (Exception) { }
        }

        /// <summary>Rend un fragment de chemin sûr, sans jamais renvoyer une chaîne vide.</summary>
        private static string NomSur(string valeur)
        {
            if (string.IsNullOrWhiteSpace(valeur)) return "_";
            StringBuilder sb = new StringBuilder(valeur.Length);
            char[] interdits = Path.GetInvalidFileNameChars();
            foreach (char c in valeur.Trim())
                sb.Append(Array.IndexOf(interdits, c) >= 0 ? '_' : c);
            string net = sb.ToString().TrimEnd('.', ' ');
            return net.Length == 0 ? "_" : net;
        }
    }
}
