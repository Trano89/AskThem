using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
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

        /// <summary>Ce qu'on retient d'une demande tant que son envoi n'est pas constaté.</summary>
        public class Fiche
        {
            public List<string> Sujets { get; set; }
            public string Destinataire { get; set; }
            public string NomCible { get; set; }
            public DateTime PrepareeLe { get; set; }
            public string Auteur { get; set; }

            public Fiche()
            {
                Sujets = new List<string>();
                Destinataire = "";
                NomCible = "";
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
            if (string.IsNullOrWhiteSpace(dossier) || !Directory.Exists(dossier)) return;

            try
            {
                Fiche f = new Fiche();
                if (sujets != null) f.Sujets = new List<string>(sujets);
                f.Destinataire = destinataire != null ? destinataire : "";
                f.NomCible = new DirectoryInfo(dossier).Name;
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
        /// Passe en revue les demandes en attente et archive celles dont l'envoi est constaté.
        ///
        /// Renvoie le nombre de dossiers archivés. Ce qui n'est pas confirmé reste en attente,
        /// sans message d'alarme : préparer une demande et l'envoyer plus tard est un usage
        /// normal.
        /// </summary>
        public static int Reprendre(AppConfig config, Action<string> journal)
        {
            int archives = 0;
            string racine = RacineLocale();

            string[] dossiers;
            try { dossiers = Directory.GetDirectories(racine); }
            catch (Exception) { return 0; }

            foreach (string dossier in dossiers)
            {
                Fiche f = LireFiche(dossier);
                if (f == null) continue;

                int confirmes = 0;
                foreach (string sujet in f.Sujets)
                    if (EnvoiOutlook.EstEnvoye(sujet, f.PrepareeLe)) confirmes++;

                if (confirmes == 0) continue;

                if (Archiver(config, dossier, f, confirmes, journal)) archives++;
            }
            return archives;
        }

        // ------------------------------------------------------------------ interne

        private static bool Archiver(AppConfig config, string dossier, Fiche f, int confirmes, Action<string> journal)
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
                string cible = DossierLibre(Path.Combine(racineArchive, f.NomCible));

                // La fiche d'attente est un outil interne : elle n'a rien à faire dans l'archive.
                try { File.Delete(Path.Combine(dossier, NomFiche)); }
                catch (Exception) { }

                Directory.Move(dossier, cible);

                string detail = confirmes < f.Sujets.Count
                    ? " (" + confirmes + " message(s) sur " + f.Sujets.Count + " confirmé(s))"
                    : "";
                Dire(journal, "Envoi constaté : demande archivée dans " + cible + detail + ".");
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
