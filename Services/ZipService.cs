using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AskThem.Services
{
    /// <summary>Archives ZIP : une par article, regroupées si elles sont trop nombreuses ou trop lourdes.</summary>
    public static class ZipService
    {
        /// <summary>Calcule la taille totale d'une liste de fichiers, en méga-octets.</summary>
        public static double TotalSizeMb(List<string> files)
        {
            long total = 0;
            foreach (string f in files)
                if (File.Exists(f)) total += new FileInfo(f).Length;
            return total / 1024.0 / 1024.0;
        }

        /// <summary>
        /// Compresse une liste de fichiers dans une archive, à plat (sans arborescence).
        /// Utilisé pour regrouper les fichiers d'un même numéro d'article.
        /// </summary>
        public static string ZipFiles(List<string> files, string zipPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (string f in files)
                {
                    if (File.Exists(f))
                        zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
                }
            }
            return zipPath;
        }

        /// <summary>Compresse le dossier dans une archive ZIP placée à côté. Retourne le chemin du ZIP.</summary>
        public static string ZipFolder(string folder, string zipName)
        {
            string zipPath = Path.Combine(Path.GetDirectoryName(folder), zipName + ".zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(folder, zipPath, CompressionLevel.Optimal, false);
            return zipPath;
        }
    }
}
