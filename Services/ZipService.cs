using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

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
    }
}
