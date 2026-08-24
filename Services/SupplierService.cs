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
                File.WriteAllText(chemin, JsonSerializer.Serialize(suppliers, options), new UTF8Encoding(false));
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
