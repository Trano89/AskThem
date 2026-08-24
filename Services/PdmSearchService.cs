using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AskThem.Services
{
    /// <summary>Recherche des fichiers SolidWorks dans la vue locale du coffre PDM.</summary>
    public static class PdmSearchService
    {
        /// <summary>Cherche le fichier 3D (.SLDPRT puis .SLDASM) correspondant au numéro d'article.</summary>
        public static string Find3D(string root, string partNumber)
        {
            string p = FindByExtension(root, partNumber, ".SLDPRT");
            if (p != null) return p;
            return FindByExtension(root, partNumber, ".SLDASM");
        }

        /// <summary>Cherche le dessin (.SLDDRW) correspondant au numéro d'article.</summary>
        public static string FindDrawing(string root, string partNumber)
        {
            return FindByExtension(root, partNumber, ".SLDDRW");
        }

        private static string FindByExtension(string root, string partNumber, string extension)
        {
            if (!Directory.Exists(root)) return null;
            string pattern = partNumber + extension;
            try
            {
                // EnumerateFiles évite de charger toute l'arborescence en mémoire.
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    MatchCasing = MatchCasing.CaseInsensitive
                };
                return Directory.EnumerateFiles(root, pattern, options).FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Index (cache) : la recherche récursive sur tout le coffre est lente,
        // on n'énumère donc l'arborescence qu'une seule fois par traitement.
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit l'index des fichiers SolidWorks du coffre.
        /// Clé = nom de fichier en majuscules, valeur = chemin complet.
        /// </summary>
        public static Dictionary<string, string> BuildIndex(string root, Action<string> log)
        {
            Dictionary<string, string> index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(root)) return index;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };

            string[] patterns = new string[] { "*.SLDPRT", "*.SLDASM", "*.SLDDRW" };
            foreach (string pattern in patterns)
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(root, pattern, options))
                    {
                        string name = Path.GetFileName(file);

                        // Les fichiers de verrouillage SolidWorks sont ignorés.
                        if (name.StartsWith("~", StringComparison.Ordinal)) continue;

                        string key = name.ToUpperInvariant();
                        if (index.ContainsKey(key))
                        {
                            if (log != null) log("Doublon dans le PDM : " + name + " — premier trouvé conservé.");
                            continue;
                        }
                        index[key] = file;
                    }
                }
                catch (Exception ex)
                {
                    if (log != null) log("Analyse partielle du coffre (" + pattern + ") : " + ex.Message);
                }
            }
            return index;
        }

        /// <summary>Cherche le fichier 3D dans l'index (.SLDPRT prioritaire sur .SLDASM).</summary>
        public static string Find3DInIndex(Dictionary<string, string> index, string partNumber)
        {
            string p = Lookup(index, partNumber, ".SLDPRT");
            if (p != null) return p;
            return Lookup(index, partNumber, ".SLDASM");
        }

        /// <summary>Cherche le dessin dans l'index.</summary>
        public static string FindDrawingInIndex(Dictionary<string, string> index, string partNumber)
        {
            return Lookup(index, partNumber, ".SLDDRW");
        }

        private static string Lookup(Dictionary<string, string> index, string partNumber, string extension)
        {
            if (index == null || string.IsNullOrWhiteSpace(partNumber)) return null;
            string key = (partNumber.Trim() + extension).ToUpperInvariant();
            string path;
            if (index.TryGetValue(key, out path)) return path;
            return null;
        }
    }
}
