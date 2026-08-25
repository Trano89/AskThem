using System;
using System.Collections.Generic;
using System.IO;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>
    /// Consultation de l'inventaire : ancienne référence, fournisseur imposé et
    /// référence fournisseur. Ces données ne sont pas dans le PDM.
    ///
    /// La source est un export de l'inventaire déposé sur le réseau. Ce choix évite
    /// de placer des identifiants dans une application portable, et permet de
    /// travailler même quand l'inventaire est indisponible.
    /// </summary>
    public static class InventoryService
    {
        /// <summary>Ce que l'inventaire sait d'un article.</summary>
        public class Entry
        {
            public string InternalRef = "";
            public string OldRef = "";
            public string Supplier = "";
            public string SupplierRef = "";
        }

        // Intitulés reconnus, accents et casse indifférents.
        private static readonly string[] EntetesRef = {
            "internal ref", "internal_ref", "ref interne", "reference interne",
            "code article", "n article", "article", "reference" };
        private static readonly string[] EntetesOldRef = {
            "old ref", "old_ref", "ancienne ref", "ancienne reference", "ref ancienne",
            "ancien code", "ancienne" };
        private static readonly string[] EntetesSupplier = {
            "supplier", "fournisseur", "nom fournisseur" };
        private static readonly string[] EntetesSupplierRef = {
            "supplier ref", "supplier_ref", "reference fournisseur", "ref fournisseur",
            "manufacturer ref", "manufacturer_ref", "code fournisseur" };

        /// <summary>
        /// Charge la table depuis l'export. Retourne une table vide si le fichier est
        /// absent ou illisible : l'application fonctionne sans, en le signalant.
        /// </summary>
        public static Dictionary<string, Entry> Load(AppConfig config, out string message)
        {
            Dictionary<string, Entry> table = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            message = "";

            string chemin = config == null ? null : config.InventoryExportPath;
            if (string.IsNullOrWhiteSpace(chemin))
            {
                message = "Aucun export d'inventaire configuré : anciennes références et "
                        + "références fournisseur ne seront pas renseignées.";
                return table;
            }
            if (!File.Exists(chemin))
            {
                message = "Export d'inventaire introuvable (" + chemin + ") : anciennes références "
                        + "et références fournisseur ne seront pas renseignées.";
                return table;
            }

            try
            {
                List<List<string>> rows = CsvService.ReadRows(chemin);

                int ligneEntete = -1;
                int colRef = -1;
                int limite = Math.Min(rows.Count, 20);
                for (int i = 0; i < limite; i++)
                {
                    int j = CsvService.IndexOfHeader(rows[i], EntetesRef);
                    if (j >= 0) { ligneEntete = i; colRef = j; break; }
                }
                if (ligneEntete < 0)
                {
                    message = "Export d'inventaire illisible : aucune colonne de référence interne reconnue.";
                    return table;
                }

                int colOld = CsvService.IndexOfHeader(rows[ligneEntete], EntetesOldRef);
                int colSup = CsvService.IndexOfHeader(rows[ligneEntete], EntetesSupplier);
                int colSupRef = CsvService.IndexOfHeader(rows[ligneEntete], EntetesSupplierRef);

                int avecAncienne = 0;
                for (int i = ligneEntete + 1; i < rows.Count; i++)
                {
                    string reference = Valeur(rows[i], colRef);
                    if (reference == "") continue;

                    Entry e = new Entry();
                    e.InternalRef = reference;
                    e.OldRef = Valeur(rows[i], colOld);
                    e.Supplier = Valeur(rows[i], colSup);
                    e.SupplierRef = Valeur(rows[i], colSupRef);
                    if (e.OldRef != "") avecAncienne++;
                    table[reference] = e;
                }

                message = table.Count + " article(s) lus dans l'inventaire, dont "
                        + avecAncienne + " avec une ancienne référence.";
                if (colOld < 0)
                    message += " Aucune colonne d'ancienne référence dans cet export.";
            }
            catch (Exception ex)
            {
                message = "Export d'inventaire illisible : " + ex.Message;
                LogService.Write(message);
            }
            return table;
        }

        /// <summary>Ce que l'inventaire sait de cet article, ou null s'il n'y figure pas.</summary>
        public static Entry Lookup(Dictionary<string, Entry> table, string partNumber)
        {
            if (table == null || string.IsNullOrWhiteSpace(partNumber)) return null;
            Entry e;
            if (table.TryGetValue(partNumber.Trim(), out e)) return e;
            return null;
        }

        private static string Valeur(List<string> ligne, int index)
        {
            if (index < 0 || index >= ligne.Count) return "";
            return ligne[index] == null ? "" : ligne[index].Trim();
        }
    }
}
