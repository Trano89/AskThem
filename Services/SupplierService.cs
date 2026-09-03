using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>
    /// Liste des fournisseurs, partagée sur le réseau : elle est relue à chaque
    /// démarrage et réenregistrée dès qu'elle est modifiée dans l'interface.
    /// </summary>
    public static class SupplierService
    {
        private const string FileName = "fournisseurs.json";

        /// <summary>Chemin complet du fichier de la liste.</summary>
        public static string GetFilePath(AppConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.SupplierListPath)) return null;
            return Path.Combine(config.SupplierListPath, FileName);
        }

        /// <summary>
        /// Charge la liste depuis le réseau. Retourne une liste vide si le fichier
        /// n'existe pas encore ou si le réseau est injoignable : l'application démarre
        /// toujours, même hors du réseau de l'entreprise.
        /// </summary>
        public static List<Supplier> Load(AppConfig config, out string message)
        {
            message = "";
            string chemin = GetFilePath(config);
            if (chemin == null)
            {
                message = "Aucun chemin de liste fournisseurs configuré.";
                return new List<Supplier>();
            }
            try
            {
                if (!File.Exists(chemin))
                {
                    message = "Liste fournisseurs vide : " + chemin + " n'existe pas encore.";
                    return new List<Supplier>();
                }
                string json = File.ReadAllText(chemin, Encoding.UTF8);
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.PropertyNameCaseInsensitive = true;
                options.AllowTrailingCommas = true;
                List<Supplier> liste = JsonSerializer.Deserialize<List<Supplier>>(json, options);
                if (liste == null) liste = new List<Supplier>();
                Normalize(liste);
                message = liste.Count + " fournisseur(s) chargé(s) depuis le réseau.";
                return liste;
            }
            catch (Exception ex)
            {
                message = "Liste fournisseurs illisible (" + ex.Message + ").";
                LogService.Write(message);
                return new List<Supplier>();
            }
        }

        /// <summary>Enregistre la liste sur le réseau. Retourne false et un message en cas d'échec.</summary>
        public static bool Save(AppConfig config, List<Supplier> suppliers, out string message)
        {
            message = "";
            string chemin = GetFilePath(config);
            if (chemin == null)
            {
                message = "Aucun chemin de liste fournisseurs configuré.";
                return false;
            }
            try
            {
                string dossier = Path.GetDirectoryName(chemin);
                if (!Directory.Exists(dossier)) Directory.CreateDirectory(dossier);

                Normalize(suppliers);
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = true;
                options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                // Écriture atomique : un fichier temporaire puis un remplacement, pour
                // qu'une coupure réseau ne laisse jamais la liste partagée tronquée.
                string temporaire = chemin + ".tmp";
                File.WriteAllText(temporaire, JsonSerializer.Serialize(suppliers, options), new UTF8Encoding(false));
                if (File.Exists(chemin)) File.Replace(temporaire, chemin, null);
                else File.Move(temporaire, chemin);
                message = "Liste enregistrée : " + chemin;
                return true;
            }
            catch (Exception ex)
            {
                message = "Enregistrement impossible : " + ex.Message;
                LogService.Write(message);
                return false;
            }
        }

        /// <summary>Nettoie les entrées : champs nuls, espaces, doublons d'adresses.</summary>
        private static void Normalize(List<Supplier> suppliers)
        {
            if (suppliers == null) return;
            foreach (Supplier s in suppliers)
            {
                if (s.Name == null) s.Name = "";
                s.Name = s.Name.Trim();
                if (s.Note == null) s.Note = "";
                s.Emails = Clean(s.Emails);
                s.CcEmails = Clean(s.CcEmails);
            }
            Trier(suppliers);
        }

        /// <summary>
        /// Range la liste par nom.
        ///
        /// Appelé à la lecture comme à l'écriture : toutes les listes de l'application en
        /// descendent, elles héritent donc du tri sans avoir à le refaire chacune. La
        /// comparaison suit la culture du poste, pour que les accents se rangent où on les
        /// cherche — « Éclair » près de « Eclair », et non à la fin.
        /// </summary>
        public static void Trier(List<Supplier> suppliers)
        {
            if (suppliers == null) return;
            suppliers.Sort(delegate (Supplier a, Supplier b)
            {
                string na = a == null || a.Name == null ? "" : a.Name;
                string nb = b == null || b.Name == null ? "" : b.Name;
                return string.Compare(na, nb, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        private static List<string> Clean(List<string> adresses)
        {
            List<string> propre = new List<string>();
            if (adresses == null) return propre;
            foreach (string a in adresses)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                string v = a.Trim();
                bool deja = false;
                foreach (string x in propre)
                {
                    if (string.Equals(x, v, StringComparison.OrdinalIgnoreCase)) { deja = true; break; }
                }
                if (!deja) propre.Add(v);
            }
            return propre;
        }

        // Intitulés reconnus dans un tableau de fournisseurs, accents et casse indifférents.
        private static readonly string[] EntetesNom = {
            "nom", "nom 1", "fournisseur", "raison sociale", "entreprise", "societe",
            "name", "supplier", "company" };
        private static readonly string[] EntetesEmail = {
            "e-mail", "email", "e mail", "courriel", "mail", "adresse e-mail",
            "adresse email", "e-mail 1", "email 1" };
        private static readonly string[] EntetesCc = {
            "cc", "copie", "e-mail cc", "email cc", "e-mail 2", "email 2", "mail 2" };
        private static readonly string[] EntetesNote = {
            "note", "remarque", "commentaire", "libelle", "activite", "complement" };

        /// <summary>
        /// Importe des fournisseurs depuis un tableau CSV ou Excel. Les colonnes sont
        /// reconnues par leur intitulé, dans n'importe quel ordre : seul le nom est
        /// indispensable. Un fournisseur déjà présent est complété, jamais dupliqué.
        /// </summary>
        public static int ImportFromFile(List<Supplier> suppliers, string path,
                                         out int completes, out string message)
        {
            completes = 0;
            message = "";
            List<List<string>> rows = CsvService.ReadRows(path);

            // Recherche de la ligne d'en-tête dans les premières lignes.
            int ligneEntete = -1;
            int colNom = -1;
            int limite = Math.Min(rows.Count, 20);
            for (int i = 0; i < limite; i++)
            {
                int j = CsvService.IndexOfHeader(rows[i], EntetesNom);
                if (j >= 0) { ligneEntete = i; colNom = j; break; }
            }
            if (ligneEntete < 0)
            {
                message = "Aucune colonne de nom reconnue. Attendu : Nom, Fournisseur, "
                        + "Raison sociale ou Entreprise.";
                return 0;
            }

            int colEmail = CsvService.IndexOfHeader(rows[ligneEntete], EntetesEmail);
            int colCc = CsvService.IndexOfHeader(rows[ligneEntete], EntetesCc);
            int colNote = CsvService.IndexOfHeader(rows[ligneEntete], EntetesNote);

            Dictionary<string, Supplier> connus = new Dictionary<string, Supplier>(StringComparer.OrdinalIgnoreCase);
            foreach (Supplier s in suppliers)
            {
                if (!string.IsNullOrWhiteSpace(s.Name) && !connus.ContainsKey(s.Name)) connus[s.Name] = s;
            }

            int ajoutes = 0;
            int sansAdresse = 0;
            for (int i = ligneEntete + 1; i < rows.Count; i++)
            {
                string nom = Valeur(rows[i], colNom);
                if (nom == "") continue;

                List<string> emails = ParseAddresses(Valeur(rows[i], colEmail));
                List<string> cc = ParseAddresses(Valeur(rows[i], colCc));
                string note = Valeur(rows[i], colNote);
                if (emails.Count == 0) sansAdresse++;

                Supplier deja;
                if (connus.TryGetValue(nom, out deja))
                {
                    // On complète sans écraser ce qui existe déjà.
                    foreach (string a in emails) if (!Contient(deja.Emails, a)) deja.Emails.Add(a);
                    foreach (string a in cc) if (!Contient(deja.CcEmails, a)) deja.CcEmails.Add(a);
                    if (deja.Note == "" && note != "") deja.Note = note;
                    completes++;
                    continue;
                }

                Supplier neuf = new Supplier();
                neuf.Name = nom;
                neuf.Emails = emails;
                neuf.CcEmails = cc;
                neuf.Note = note;
                suppliers.Add(neuf);
                connus[nom] = neuf;
                ajoutes++;
            }

            message = ajoutes + " fournisseur(s) ajouté(s), " + completes + " complété(s).";
            if (colEmail < 0)
                message += " Aucune colonne d'adresse e-mail dans ce fichier : les adresses restent à saisir.";
            else if (sansAdresse > 0)
                message += " " + sansAdresse + " ligne(s) sans adresse e-mail.";
            return ajoutes;
        }

        private static string Valeur(List<string> ligne, int index)
        {
            if (index < 0 || index >= ligne.Count) return "";
            return ligne[index] == null ? "" : ligne[index].Trim();
        }

        private static bool Contient(List<string> liste, string valeur)
        {
            foreach (string x in liste)
                if (string.Equals(x, valeur, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Découpe une saisie libre (points-virgules, virgules, retours à la ligne) en adresses.</summary>
        public static List<string> ParseAddresses(string texte)
        {
            List<string> resultat = new List<string>();
            if (string.IsNullOrWhiteSpace(texte)) return resultat;
            string[] morceaux = texte.Split(new char[] { ';', ',', '\r', '\n', ' ', '\t' },
                                            StringSplitOptions.RemoveEmptyEntries);
            foreach (string m in morceaux)
            {
                string v = m.Trim();
                if (v != "") resultat.Add(v);
            }
            return Clean(resultat);
        }
    }
}
