using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AskThem.Services
{
    /// <summary>Archives ZIP : une par numéro d'article, au niveau de compression choisi.</summary>
    public static class ZipService
    {
        /// <summary>Les niveaux proposés dans l'interface, du plus rapide au plus petit.</summary>
        public static readonly string[] Niveaux = { "Aucune", "Rapide", "Optimal", "Maximale" };

        /// <summary>
        /// Traduit le libellé de l'interface en niveau .NET. Tout libellé inconnu retombe
        /// sur Optimal, qui est le meilleur compromis mesuré sur les exports du coffre :
        /// « Maximale » ne gagne que trois pour cent de plus pour quatre fois le temps.
        /// </summary>
        public static CompressionLevel Niveau(string libelle)
        {
            if (libelle == null) return CompressionLevel.Optimal;
            switch (libelle.Trim().ToLowerInvariant())
            {
                case "aucune": return CompressionLevel.NoCompression;
                case "rapide": return CompressionLevel.Fastest;
                case "maximale": return CompressionLevel.SmallestSize;
                default: return CompressionLevel.Optimal;
            }
        }

        /// <summary>
        /// Compresse une liste de fichiers dans une archive, à plat (sans arborescence).
        /// Utilisé pour regrouper les fichiers d'un même numéro d'article.
        /// </summary>
        public static string ZipFiles(List<string> files, string zipPath, CompressionLevel niveau)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (string f in files)
                {
                    if (File.Exists(f))
                        zip.CreateEntryFromFile(f, Path.GetFileName(f), niveau);
                }
            }
            return zipPath;
        }

        /// <summary>
        /// Lit une entrée texte d'une archive, sans la décompresser sur le disque.
        ///
        /// Sert à relire le manifeste rangé dans l'archive d'un article : c'est lui qui porte
        /// l'empreinte des sources, donc la seule façon exacte de savoir si l'archive est
        /// encore à jour. Renvoie null si l'archive ou l'entrée manquent.
        /// </summary>
        public static string LireEntree(string zipPath, string nomEntree)
        {
            try
            {
                if (!File.Exists(zipPath)) return null;
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry entree = zip.GetEntry(nomEntree);
                    if (entree == null) return null;
                    using (StreamReader lecteur = new StreamReader(entree.Open(), Encoding.UTF8))
                        return lecteur.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Archive illisible (" + zipPath + ") : " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Extrait une archive dans un dossier, en écartant les entrées nommées dans
        /// <paramref name="exclusions"/>. Renvoie les chemins extraits.
        ///
        /// L'exclusion sert au manifeste : il décrit l'article pour nous, il n'a rien à faire
        /// dans ce qu'on envoie à un fournisseur.
        /// </summary>
        public static List<string> Extraire(string zipPath, string dossierCible, params string[] exclusions)
        {
            List<string> extraits = new List<string>();
            try
            {
                if (!File.Exists(zipPath)) return extraits;
                Directory.CreateDirectory(dossierCible);

                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entree in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entree.Name)) continue;       // dossier

                        bool ecartee = false;
                        foreach (string x in exclusions)
                            if (string.Equals(entree.Name, x, StringComparison.OrdinalIgnoreCase)) ecartee = true;
                        if (ecartee) continue;

                        string cible = Path.Combine(dossierCible, entree.Name);
                        entree.ExtractToFile(cible, true);
                        extraits.Add(cible);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Extraction impossible (" + zipPath + ") : " + ex.Message);
            }
            return extraits;
        }
    }
}
