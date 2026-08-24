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
        public static void Write(string message)
        {
            try
            {
                string folder = GetLogFolder();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
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
