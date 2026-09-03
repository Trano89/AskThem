using System;
using System.IO;

namespace AskThem.Services
{
    /// <summary>Journal sur disque. Un échec d'écriture ne doit jamais faire planter l'application.</summary>
    public static class LogService
    {
        /// <summary>Dossier du journal : %LOCALAPPDATA%\AskThem\logs.</summary>
        public static string GetLogFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AskThem",
                "logs");
        }

        /// <summary>Ajoute une ligne au journal du jour.</summary>
        /// <summary>Au-delà, un journal ne sert plus qu'à occuper le disque.</summary>
        private const int JoursConserves = 60;

        private static DateTime _dernierePurge = DateTime.MinValue;

        /// <summary>
        /// Efface les journaux trop anciens. Fait une fois par jour au plus : parcourir le
        /// dossier à chaque ligne écrite coûterait plus cher que le nettoyage lui-même.
        /// </summary>
        private static void Purger(string folder)
        {
            if ((DateTime.Now - _dernierePurge).TotalHours < 24) return;
            _dernierePurge = DateTime.Now;

            try
            {
                DateTime limite = DateTime.Now.AddDays(-JoursConserves);
                foreach (string f in Directory.GetFiles(folder, "askthem_*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < limite) File.Delete(f);
                    }
                    catch (Exception) { }   // un fichier verrouillé sera repris demain
                }
            }
            catch (Exception)
            {
                // Le nettoyage est un confort : il ne doit jamais empêcher d'écrire.
            }
        }

        public static void Write(string message)
        {
            try
            {
                string folder = GetLogFolder();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Purger(folder);
                string file = Path.Combine(folder, "askthem_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                File.AppendAllText(file, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
            }
            catch (Exception)
            {
                // Un journal indisponible ne doit jamais interrompre le traitement.
            }
        }
    }
}
