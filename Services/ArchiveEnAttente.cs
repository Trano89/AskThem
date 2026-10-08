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
    /// Le dossier de demande est d'abord écrit sur le poste. Il ne rejoint l'archive du réseau,
    /// et la demande n'entre dans le suivi, qu'une fois le message retrouvé dans les éléments
    /// envoyés grâce à la marque qu'AskThem y a posée. Une demande générée puis abandonnée ne
    /// laisse donc aucune trace sur le partage : l'archive redevient la liste de ce qui est
    /// réellement parti chez un fournisseur. Et c'est le message tel qu'il est parti —
    /// retouché ou non par l'utilisateur — qui y est conservé.
    ///
    /// La reprise est rejouée au démarrage puis régulièrement : un partage indisponible, un
    /// poste éteint ou un envoi différé ne font perdre aucun dossier.
    /// </summary>
    public static class ArchiveEnAttente
    {
        private const string NomFiche = "attente.json";

        /// <summary>Demande archivée dont la copie locale n'a pas pu être effacée.</summary>
        private const string NomFicheArchivee = "archivee.json";

        /// <summary>Demande jamais envoyée : plus suivie, laissée sur le poste.</summary>
        private const string NomFicheAbandonnee = "abandonnee.json";

        /// <summary>Au-delà, un message préparé et toujours pas parti est tenu pour abandonné.</summary>
        private const int JoursAvantAbandon = 30;

        /// <summary>
        /// La reprise tourne au démarrage, à intervalles réguliers et après une demande :
        /// deux reprises simultanées archiveraient deux fois le même dossier.
        /// </summary>
        private static readonly object Verrou = new object();

        /// <summary>Ce qu'on retient d'une demande tant que son envoi n'est pas constaté.</summary>
        public class Fiche
        {
            public List<string> Sujets { get; set; }
            public string Destinataire { get; set; }
            public string NomCible { get; set; }
            public string SousDossier { get; set; }
            public DateTime PrepareeLe { get; set; }
            public string Auteur { get; set; }

            /// <summary>Marque de chaque message de la demande, dans l'ordre.</summary>
            public List<string> Marques { get; set; }

            /// <summary>Marques déjà retrouvées dans les éléments envoyés.</summary>
            public List<string> Envoyes { get; set; }

            /// <summary>Dossier de la demande sur le réseau, une fois archivée.</summary>
            public string DossierArchive { get; set; }

            /// <summary>La demande telle que la base des demandes la connaît.</summary>
            public DemandeSuivie Demande { get; set; }

            public Fiche()
            {
                Sujets = new List<string>();
                Destinataire = "";
                NomCible = "";
                SousDossier = "";
                Auteur = "";
                Marques = new List<string>();
                Envoyes = new List<string>();
                DossierArchive = "";
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
        /// Sans fiche, un dossier resterait indéfiniment sur le poste : c'est elle qui dit quel
        /// message chercher dans les éléments envoyés, et sous quel nom archiver ensuite.
        /// </summary>
        public static void Deposer(string dossier, List<string> sujets, string destinataire,
                                   string sousDossier, List<string> marques, DemandeSuivie demande)
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
                if (marques != null) f.Marques = new List<string>(marques);
                f.Demande = demande;
                EcrireFiche(dossier, f);
            }
            catch (Exception ex)
            {
                LogService.Write("Fiche d'attente non écrite pour " + dossier + " : " + ex.Message);
            }
        }

        /// <summary>
        /// Passe en revue les demandes en attente : constate les envois, archive, et tient la
        /// base des demandes à jour. Renvoie le nombre de demandes dont l'envoi vient d'être
        /// constaté.
        /// </summary>
        public static int Reprendre(AppConfig config, Action<string> journal)
        {
            lock (Verrou)
            {
                int constates = 0;
                string[] dossiers;
                try { dossiers = Directory.GetDirectories(RacineLocale()); }
                catch (Exception) { return 0; }

                foreach (string dossier in dossiers)
                {
                    try
                    {
                        Fiche f = LireFiche(dossier);
                        if (f == null) continue;
                        bool envoi = f.Marques.Count > 0
                            ? TraiterMarquee(config, dossier, f, journal)
                            : TraiterAncienne(config, dossier, f, journal);
                        if (envoi) constates++;
                    }
                    catch (Exception ex)
                    {
                        LogService.Write("Reprise de " + Path.GetFileName(dossier) + " : " + ex.Message);
                    }
                }
                return constates;
            }
        }

        // ------------------------------------------------------------------ demandes marquées

        /// <summary>
        /// Une demande préparée par cette version : ses messages se reconnaissent à leur marque.
        /// Vrai si un envoi vient d'être constaté.
        /// </summary>
        private static bool TraiterMarquee(AppConfig config, string dossier, Fiche f, Action<string> journal)
        {
            if (f.Demande == null) f.Demande = DemandeDeSecours(f);
            DemandeSuivie d = f.Demande;

            List<string> nouveaux = new List<string>();
            for (int i = 0; i < f.Marques.Count; i++)
            {
                string marque = f.Marques[i];
                string msg = Path.Combine(dossier, NomMessage(i + 1, f.Marques.Count));
                if (f.Envoyes.Contains(marque))
                {
                    Rattraper(marque, msg, f);
                    continue;
                }

                EnvoiOutlook.MessageEnvoye m = EnvoiOutlook.Chercher(marque, msg, f.PrepareeLe);
                if (m == null) continue;

                f.Envoyes.Add(marque);
                nouveaux.Add(msg);
                if (!d.EnvoyeeLe.HasValue || m.EnvoyeLe < d.EnvoyeeLe.Value) d.EnvoyeeLe = m.EnvoyeLe;
                d.SujetEnvoye = Ajouter(d.SujetEnvoye, m.Sujet, " | ");
                d.DestinatairesEnvoyes = Ajouter(d.DestinatairesEnvoyes, m.Destinataires, "; ");
            }

            bool change = nouveaux.Count > 0;
            if (change)
            {
                // Un collègue a pu agir sur la demande entre-temps : on part de ce que la base
                // en sait, et l'on n'y ajoute que ce que l'envoi apprend.
                DemandeSuivie actuelle = BaseSuivi.Trouver(config, d.Id);
                if (actuelle != null)
                {
                    actuelle.EnvoyeeLe = d.EnvoyeeLe;
                    actuelle.SujetEnvoye = d.SujetEnvoye;
                    actuelle.DestinatairesEnvoyes = d.DestinatairesEnvoyes;
                    d = actuelle;
                    f.Demande = d;
                }
                d.MessagesEnvoyes = f.Envoyes.Count;
                if (d.Statut == DemandeSuivie.Preparee || d.Statut == DemandeSuivie.NonEnvoyee)
                {
                    d.Statut = DemandeSuivie.Envoyee;
                    int jours = PreferencesUtilisateur.DelaiRappel(config);
                    d.ProchainRappel = d.EnvoyeeLe.Value.Date.AddDays(jours);
                }
                Dire(journal, "Envoi constaté : « " + f.NomCible + " » est parti ("
                            + f.Envoyes.Count + " message(s) sur " + f.Marques.Count + ").");
            }

            // L'archive suit l'envoi : le dossier entier au premier message parti, puis
            // chaque message qui part ensuite. Un partage injoignable est réessayé plus tard.
            if (f.Envoyes.Count > 0)
            {
                if (f.DossierArchive == "")
                {
                    string cible = Archiver(config, dossier, f, journal);
                    if (cible != null)
                    {
                        f.DossierArchive = cible;
                        d.DossierArchive = cible;
                        change = true;
                    }
                }
                else
                {
                    foreach (string msg in nouveaux)
                    {
                        try
                        {
                            string vers = Path.Combine(f.DossierArchive, Path.GetFileName(msg));
                            if (!File.Exists(vers)) File.Copy(msg, vers);
                        }
                        catch (Exception ex)
                        {
                            LogService.Write("Message non ajouté à l'archive " + f.DossierArchive + " : " + ex.Message);
                        }
                    }
                }
            }

            bool toutParti = f.Envoyes.Count == f.Marques.Count;
            bool ancien = DateTime.Now - f.PrepareeLe > TimeSpan.FromDays(JoursAvantAbandon);

            // Jamais parti : la demande sort du suivi, sans rien laisser sur le réseau.
            if (f.Envoyes.Count == 0 && ancien)
            {
                d.Statut = DemandeSuivie.NonEnvoyee;
                BaseSuivi.Enregistrer(d);
                Renommer(dossier, NomFiche, NomFicheAbandonnee);
                Dire(journal, "« " + f.NomCible + " » n'est jamais parti en " + JoursAvantAbandon
                            + " jours : demande classée non envoyée.");
                return false;
            }

            if (change)
            {
                EcrireFiche(dossier, f);
                BaseSuivi.Enregistrer(d);
            }

            // Tout est parti et archivé, ou ce qui manque ne partira plus : la copie locale
            // n'a plus de raison d'être. Tant qu'un message parti manque à l'archive, elle reste,
            // et l'on retente de l'enregistrer au passage suivant.
            if (f.DossierArchive != "" && (toutParti || ancien) && (TousArchives(f) || ancien)) Effacer(dossier);
            return nouveaux.Count > 0;
        }

        /// <summary>Nom du message enregistré tel qu'il est parti.</summary>
        /// <summary>
        /// Un message parti dont la copie n'a pas pu être enregistrée — Outlook occupé, archive
        /// injoignable — est repris dans les éléments envoyés : c'est lui, tel que l'utilisateur
        /// l'a envoyé après retouches, que l'archive doit garder, jamais le brouillon préparé.
        /// </summary>
        private static void Rattraper(string marque, string msgLocal, Fiche f)
        {
            string nom = Path.GetFileName(msgLocal);
            string dansArchive = f.DossierArchive == "" ? "" : Path.Combine(f.DossierArchive, nom);
            if (dansArchive != "" && File.Exists(dansArchive)) return;

            if (!File.Exists(msgLocal))
            {
                if (EnvoiOutlook.Chercher(marque, msgLocal, f.PrepareeLe) == null || !File.Exists(msgLocal)) return;
                LogService.Write("Message envoyé enregistré au second essai : " + f.NomCible + " — " + nom);
            }
            if (dansArchive == "") return;
            try { File.Copy(msgLocal, dansArchive); }
            catch (Exception ex) { LogService.Write("Message non ajouté à l'archive " + f.DossierArchive + " : " + ex.Message); }
        }

        /// <summary>Vrai si chaque message parti a sa copie dans l'archive.</summary>
        private static bool TousArchives(Fiche f)
        {
            if (f.DossierArchive == "") return false;
            for (int i = 0; i < f.Marques.Count; i++)
            {
                if (!f.Envoyes.Contains(f.Marques[i])) continue;
                if (!File.Exists(Path.Combine(f.DossierArchive, NomMessage(i + 1, f.Marques.Count)))) return false;
            }
            return true;
        }

        private static string NomMessage(int numero, int total)
        {
            return total > 1 ? "Message envoyé (" + numero + " sur " + total + ").msg" : "Message envoyé.msg";
        }

        private static string Ajouter(string existant, string ajout, string separateur)
        {
            if (string.IsNullOrWhiteSpace(ajout)) return existant ?? "";
            if (string.IsNullOrWhiteSpace(existant)) return ajout.Trim();
            if (existant.IndexOf(ajout.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return existant;
            return existant + separateur + ajout.Trim();
        }

        // ------------------------------------------------------------------ demandes anciennes

        /// <summary>
        /// Une demande préparée par une version antérieure, sans marque : seul l'objet et le
        /// domaine du destinataire permettent de la retrouver. Ces dossiers disparaissent avec
        /// les dernières demandes en attente ; toutes les nouvelles sont marquées.
        /// </summary>
        private static bool TraiterAncienne(AppConfig config, string dossier, Fiche f, Action<string> journal)
        {
            int confirmes = 0;
            foreach (string sujet in f.Sujets)
                if (EnvoiOutlook.EstEnvoye(sujet, f.PrepareeLe, f.Destinataire)) confirmes++;
            if (confirmes == 0) return false;

            string cible = Archiver(config, dossier, f, journal);
            if (cible == null) return false;

            DemandeSuivie d = DemandeDeSecours(f);
            d.Statut = DemandeSuivie.Envoyee;
            d.EnvoyeeLe = f.PrepareeLe;
            d.MessagesEnvoyes = confirmes;
            d.DossierArchive = cible;
            int jours = PreferencesUtilisateur.DelaiRappel(config);
            d.ProchainRappel = DateTime.Today.AddDays(jours);
            BaseSuivi.Enregistrer(d);

            Effacer(dossier);
            return true;
        }

        /// <summary>Ce qu'on peut dire d'une demande dont la fiche ne porte pas le détail.</summary>
        private static DemandeSuivie DemandeDeSecours(Fiche f)
        {
            DemandeSuivie d = new DemandeSuivie();
            d.Id = DemandeSuivie.NouvelId();
            d.Type = f.SousDossier == "" ? RequestTypes.SousDossier(RequestType.Offre) : f.SousDossier;
            d.CreeeLe = f.PrepareeLe;
            d.Auteur = string.IsNullOrWhiteSpace(f.Auteur) ? Environment.UserName : f.Auteur;
            d.Poste = Environment.MachineName;
            d.Destinataires = f.Destinataire;
            d.NbMessages = Math.Max(f.Sujets.Count, f.Marques.Count);

            // « 2026-09-16_HER Precision_FAB » : le fournisseur est entre la date et la nature.
            Match m = Regex.Match(f.NomCible ?? "", @"^\d{4}-\d{2}-\d{2}_(.+)_[A-Z]+(_\d+)?$");
            d.Fournisseur = m.Success ? m.Groups[1].Value : "";
            return d;
        }

        // ------------------------------------------------------------------ archivage

        /// <summary>
        /// Recopie la demande sur le réseau et renvoie son dossier, ou null si le partage est
        /// injoignable. La copie locale est gardée : d'autres messages peuvent encore partir.
        ///
        /// Un déplacement ne franchit pas les volumes : le dossier vit sur le poste, l'archive
        /// sur le partage. On copie donc dans un dossier provisoire du partage, qu'un simple
        /// renommage — sur le même volume, d'un seul geste — rend visible une fois complet.
        /// </summary>
        private static string Archiver(AppConfig config, string dossier, Fiche f, Action<string> journal)
        {
            string racineArchive = config != null ? config.ArchiveRoot : null;
            if (string.IsNullOrWhiteSpace(racineArchive) || !Directory.Exists(racineArchive))
            {
                Dire(journal, "Envoi constaté pour « " + f.NomCible + " » mais l'archive réseau est "
                            + "injoignable : la demande sera archivée plus tard.");
                return null;
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

                Dire(journal, "Demande archivée dans " + cible + ".");
                return cible;
            }
            catch (Exception ex)
            {
                Dire(journal, "Archivage impossible pour « " + f.NomCible + " » : " + ex.Message
                            + " — nouvel essai plus tard.");
                return null;
            }
        }

        /// <summary>Efface la copie locale d'une demande archivée, ou la marque comme telle.</summary>
        private static void Effacer(string dossier)
        {
            try
            {
                Directory.Delete(dossier, true);
            }
            catch (Exception ex)
            {
                // Si elle résiste (un fichier ouvert), sa fiche change de nom pour qu'on ne la
                // traite pas une seconde fois.
                Renommer(dossier, NomFiche, NomFicheArchivee);
                LogService.Write("Copie locale de « " + Path.GetFileName(dossier) + " » conservée : " + ex.Message);
            }
        }

        private static void Renommer(string dossier, string de, string vers)
        {
            try
            {
                string source = Path.Combine(dossier, de);
                if (File.Exists(source)) File.Move(source, Path.Combine(dossier, vers), true);
            }
            catch (Exception) { }
        }

        /// <summary>Copie récursive, sans les fiches internes : elles n'ont rien à faire dans l'archive.</summary>
        private static void Copier(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string fichier in Directory.GetFiles(source))
            {
                string nom = Path.GetFileName(fichier);
                if (string.Equals(nom, NomFiche, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nom, NomFicheArchivee, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nom, NomFicheAbandonnee, StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(fichier, Path.Combine(destination, nom), false);
            }
            foreach (string sousDossier in Directory.GetDirectories(source))
                Copier(sousDossier, Path.Combine(destination, Path.GetFileName(sousDossier)));
        }

        // ------------------------------------------------------------------ fiche

        private static void EcrireFiche(string dossier, Fiche f)
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = true;
            string chemin = Path.Combine(dossier, NomFiche);
            string temporaire = chemin + ".tmp";
            File.WriteAllText(temporaire, JsonSerializer.Serialize(f, options), Encoding.UTF8);
            File.Move(temporaire, chemin, true);
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
                if (f.Marques == null) f.Marques = new List<string>();
                if (f.Envoyes == null) f.Envoyes = new List<string>();
                if (f.DossierArchive == null) f.DossierArchive = "";
                return f.Sujets.Count == 0 && f.Marques.Count == 0 ? null : f;
            }
            catch (Exception ex)
            {
                LogService.Write("Fiche d'attente illisible dans " + dossier + " : " + ex.Message);
                return null;
            }
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
