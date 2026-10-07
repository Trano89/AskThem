using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>
    /// Retient les dossiers de demande jusqu'à ce que l'envoi soit constaté.
    ///
    /// Le dossier de demande est d'abord écrit sur le poste. Il ne rejoint l'archive du réseau
    /// qu'une fois le message retrouvé dans les éléments envoyés. Une demande préparée puis
    /// abandonnée ne laisse donc aucune trace sur le partage, et l'archive redevient ce qu'elle
    /// prétend être : la liste de ce qui est réellement parti chez un fournisseur.
    ///
    /// La reprise est rejouée à chaque démarrage : un partage indisponible, un poste éteint ou
    /// un envoi différé ne font perdre aucun dossier.
    /// </summary>
    public static class ArchiveEnAttente
    {
        private const string NomFiche = "attente.json";

        /// <summary>
        /// Une demande archivée dont la copie locale n'a pas pu être effacée : elle ne doit pas
        /// être archivée une seconde fois.
        /// </summary>
        private const string NomFicheArchivee = "archivee.json";

        /// <summary>
        /// La reprise tourne au démarrage, à la fin de chaque suivi d'emails et parfois en même
        /// temps : deux reprises simultanées archiveraient deux fois le même dossier.
        /// </summary>
        private static readonly object Verrou = new object();

        /// <summary>Nom d'un dossier de demande : date, destinataire, nature, suffixe éventuel.</summary>
        private static readonly Regex NomDeDemande =
            new Regex(@"^\d{4}-\d{2}-\d{2}_.+_(OFFRE|FAB|CDE)(_\d+)?$", RegexOptions.Compiled);

        /// <summary>Ce qu'on retient d'une demande tant que son envoi n'est pas constaté.</summary>
        public class Fiche
        {
            public List<string> Sujets { get; set; }
            public string Destinataire { get; set; }
            public string NomCible { get; set; }
            public string SousDossier { get; set; }
            public DateTime PrepareeLe { get; set; }
            public string Auteur { get; set; }

            public Fiche()
            {
                Sujets = new List<string>();
                Destinataire = "";
                NomCible = "";
                SousDossier = "";
                Auteur = "";
            }
        }

        /// <summary>Où les demandes patientent : sur le poste, pas sur le réseau.</summary>
        public static string RacineLocale()
        {
            string racine = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AskThem", "en-attente");
            try { Directory.CreateDirectory(racine); }
            catch (Exception) { }
            return racine;
        }

        /// <summary>
        /// Marque un dossier comme en attente d'envoi.
        ///
        /// Sans fiche, un dossier resterait indéfiniment sur le poste : c'est elle qui dit quoi
        /// chercher dans les éléments envoyés, et sous quel nom archiver ensuite.
        /// </summary>
        public static void Deposer(string dossier, List<string> sujets, string destinataire)
        {
            Deposer(dossier, sujets, destinataire, "");
        }

        /// <summary>
        /// Même chose, en rangeant la demande par nature une fois archivée.
        ///
        /// Les demandes s'accumulent : offres, commandes et fabrications dans un même dossier
        /// deviennent vite illisibles.
        /// </summary>
        public static void Deposer(string dossier, List<string> sujets, string destinataire,
                                   string sousDossier)
        {
            if (string.IsNullOrWhiteSpace(dossier) || !Directory.Exists(dossier)) return;

            try
            {
                Fiche f = new Fiche();
                if (sujets != null) f.Sujets = new List<string>(sujets);
                f.Destinataire = destinataire != null ? destinataire : "";
                f.NomCible = new DirectoryInfo(dossier).Name;
                f.SousDossier = sousDossier != null ? sousDossier : "";
                f.PrepareeLe = DateTime.Now;
                f.Auteur = Environment.UserName;

                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = true;
                File.WriteAllText(Path.Combine(dossier, NomFiche),
                                  JsonSerializer.Serialize(f, options), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogService.Write("Fiche d'attente non écrite pour " + dossier + " : " + ex.Message);
            }
        }

        /// <summary>
        /// Remplace les sujets retenus pour une demande en attente.
        ///
        /// L'utilisateur retouche l'objet du message avant de l'envoyer ; c'est cette
        /// dernière version qu'on retrouvera dans les éléments envoyés. Sans cette mise à
        /// jour, la demande partirait sans jamais être archivée.
        /// </summary>
        public static void MettreAJourSujets(string dossier, List<string> sujets)
        {
            if (string.IsNullOrWhiteSpace(dossier) || sujets == null || sujets.Count == 0) return;
            try
            {
                Fiche f = LireFiche(dossier);
                if (f == null) return;

                f.Sujets = new List<string>(sujets);
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = true;
                File.WriteAllText(Path.Combine(dossier, NomFiche),
                                  JsonSerializer.Serialize(f, options), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogService.Write("Sujets d'attente non mis à jour pour " + dossier + " : " + ex.Message);
            }
        }

        /// <summary>
        /// Passe en revue les demandes en attente et archive celles dont l'envoi est constaté.
        ///
        /// Renvoie le nombre de dossiers archivés. Ce qui n'est pas confirmé reste en attente,
        /// sans message d'alarme : préparer une demande et l'envoyer plus tard est un usage
        /// normal.
        /// </summary>
        public static int Reprendre(AppConfig config, Action<string> journal)
        {
            lock (Verrou)
            {
                int archives = 0;
                string racine = RacineLocale();

                string[] dossiers;
                try { dossiers = Directory.GetDirectories(racine); }
                catch (Exception) { return 0; }

                foreach (string dossier in dossiers)
                {
                    Fiche f = LireFiche(dossier);
                    if (f == null)
                    {
                        Fiche orpheline = Orpheline(dossier);
                        if (orpheline != null
                            && Archiver(config, dossier, orpheline, 0, journal, " (envoi constaté par une version précédente)"))
                            archives++;
                        continue;
                    }

                    int confirmes = 0;
                    foreach (string sujet in f.Sujets)
                        if (EnvoiOutlook.EstEnvoye(sujet, f.PrepareeLe, f.Destinataire)) confirmes++;

                    if (confirmes == 0) continue;

                    if (Archiver(config, dossier, f, confirmes, journal, "")) archives++;
                }
                return archives;
            }
        }

        /// <summary>
        /// Une demande dont l'envoi a déjà été constaté, mais restée sur le poste.
        ///
        /// Jusqu'à la version 1.5.12, la fiche d'attente était effacée AVANT de déplacer le
        /// dossier vers le réseau — et ce déplacement échouait toujours, le poste et le partage
        /// n'étant pas sur le même volume. Le dossier restait donc ici, sans fiche, alors que
        /// le message était bel et bien parti. Seul l'archivage efface la fiche : un dossier de
        /// demande qui a perdu la sienne mais garde son message enregistré est un envoi constaté.
        /// Un dossier sans message n'a jamais été proposé à l'envoi, et reste où il est.
        /// </summary>
        private static Fiche Orpheline(string dossier)
        {
            try
            {
                if (File.Exists(Path.Combine(dossier, NomFiche))) return null;
                if (File.Exists(Path.Combine(dossier, NomFicheArchivee))) return null;

                string nom = new DirectoryInfo(dossier).Name;
                Match m = NomDeDemande.Match(nom);
                if (!m.Success) return null;
                if (Directory.GetFiles(dossier, "*.msg").Length == 0) return null;

                Fiche f = new Fiche();
                f.NomCible = nom;
                f.SousDossier = m.Groups[1].Value == "FAB" ? RequestTypes.SousDossier(RequestType.Fabrication)
                              : m.Groups[1].Value == "CDE" ? RequestTypes.SousDossier(RequestType.CommandeCatalogue)
                              : RequestTypes.SousDossier(RequestType.Offre);
                return f;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ interne

        /// <summary>
        /// Recopie la demande sur le réseau, puis efface la copie locale.
        ///
        /// Un déplacement ne franchit pas les volumes : le dossier vit sur le poste, l'archive
        /// sur le partage. On copie donc dans un dossier provisoire du partage, qu'un simple
        /// renommage — sur le même volume, d'un seul geste — rend visible une fois complet.
        /// La fiche d'attente ne disparaît qu'avec la copie locale : tant que l'archive n'est
        /// pas faite, la demande reste à reprendre.
        /// </summary>
        private static bool Archiver(AppConfig config, string dossier, Fiche f, int confirmes,
                                     Action<string> journal, string precision)
        {
            string racineArchive = config != null ? config.ArchiveRoot : null;

            if (string.IsNullOrWhiteSpace(racineArchive) || !Directory.Exists(racineArchive))
            {
                Dire(journal, "Envoi constaté pour « " + f.NomCible + " » mais l'archive réseau est "
                            + "injoignable : le dossier reste en attente et sera archivé plus tard.");
                return false;
            }

            try
            {
                string racineNature = string.IsNullOrWhiteSpace(f.SousDossier)
                    ? racineArchive
                    : Path.Combine(racineArchive, f.SousDossier.Trim());
                Directory.CreateDirectory(racineNature);

                string cible = DossierLibre(Path.Combine(racineNature, f.NomCible));
                string provisoire = cible + ".copie-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                try
                {
                    Copier(dossier, provisoire);
                    Directory.Move(provisoire, cible);
                }
                catch (Exception)
                {
                    try { if (Directory.Exists(provisoire)) Directory.Delete(provisoire, true); }
                    catch (Exception) { }
                    throw;
                }

                string detail = confirmes > 0 && confirmes < f.Sujets.Count
                    ? " (" + confirmes + " message(s) sur " + f.Sujets.Count + " confirmé(s))"
                    : "";
                Dire(journal, "Envoi constaté : demande archivée dans " + cible + detail + precision + ".");

                // L'archive est faite : la copie locale n'a plus de raison d'être. Si elle
                // résiste (un fichier ouvert), sa fiche change de nom pour qu'on ne l'archive
                // pas une seconde fois.
                try
                {
                    Directory.Delete(dossier, true);
                }
                catch (Exception ex)
                {
                    try
                    {
                        string fiche = Path.Combine(dossier, NomFiche);
                        if (File.Exists(fiche)) File.Move(fiche, Path.Combine(dossier, NomFicheArchivee), true);
                        else File.WriteAllText(Path.Combine(dossier, NomFicheArchivee), "{}", Encoding.UTF8);
                    }
                    catch (Exception) { }
                    LogService.Write("Copie locale de « " + f.NomCible + " » conservée : " + ex.Message);
                }
                return true;
            }
            catch (Exception ex)
            {
                Dire(journal, "Archivage impossible pour « " + f.NomCible + " » : " + ex.Message
                            + " — le dossier reste en attente.");
                return false;
            }
        }

        private static Fiche LireFiche(string dossier)
        {
            try
            {
                string chemin = Path.Combine(dossier, NomFiche);
                if (!File.Exists(chemin)) return null;

                JsonSerializerOptions options = new JsonSerializerOptions();
                options.PropertyNameCaseInsensitive = true;
                options.AllowTrailingCommas = true;

                Fiche f = JsonSerializer.Deserialize<Fiche>(File.ReadAllText(chemin), options);
                if (f == null) return null;
                if (f.Sujets == null) f.Sujets = new List<string>();
                return f.Sujets.Count == 0 ? null : f;
            }
            catch (Exception ex)
            {
                LogService.Write("Fiche d'attente illisible dans " + dossier + " : " + ex.Message);
                return null;
            }
        }

        /// <summary>Copie récursive, sans les fiches internes : elles n'ont rien à faire dans l'archive.</summary>
        private static void Copier(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string fichier in Directory.GetFiles(source))
            {
                string nom = Path.GetFileName(fichier);
                if (string.Equals(nom, NomFiche, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nom, NomFicheArchivee, StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(fichier, Path.Combine(destination, nom), false);
            }
            foreach (string sousDossier in Directory.GetDirectories(source))
                Copier(sousDossier, Path.Combine(destination, Path.GetFileName(sousDossier)));
        }

        /// <summary>N'écrase jamais une demande déjà archivée du même jour.</summary>
        private static string DossierLibre(string souhaite)
        {
            string cible = souhaite;
            int suffixe = 2;
            while (Directory.Exists(cible))
            {
                cible = souhaite + "_" + suffixe;
                suffixe++;
            }
            return cible;
        }

        private static void Dire(Action<string> journal, string message)
        {
            LogService.Write(message);
            if (journal != null)
            {
                try { journal(message); }
                catch (Exception) { }
            }
        }
    }
}
