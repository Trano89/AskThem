using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AskThem.Services
{
    /// <summary>
    /// Conserve un secret hors du code et hors du dépôt, chiffré par Windows (DPAPI).
    ///
    /// Le chiffrement est lié à la session Windows de l'utilisateur : le fichier
    /// obtenu est inutilisable sur un autre poste ou par un autre compte. C'est la
    /// raison pour laquelle un mot de passe ne doit jamais figurer dans le programme
    /// lui-même : il y serait lisible par quiconque obtient l'exécutable.
    /// </summary>
    public static class SecretStore
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        /// <summary>Dossier des secrets : hors du dossier de l'exécutable, donc jamais copié avec lui.</summary>
        private static string Dossier()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AskThem");
        }

        private static string Chemin(string nom)
        {
            return Path.Combine(Dossier(), nom + ".secret");
        }

        /// <summary>Chiffre et enregistre un secret. Un échec est journalisé, jamais bruyant.</summary>
        public static bool Save(string nom, string secret)
        {
            try
            {
                if (!Directory.Exists(Dossier())) Directory.CreateDirectory(Dossier());
                byte[] clair = Encoding.UTF8.GetBytes(secret == null ? "" : secret);
                byte[] chiffre = Proteger(clair, true);
                if (chiffre == null) return false;
                File.WriteAllBytes(Chemin(nom), chiffre);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write("Enregistrement du secret impossible : " + ex.Message);
                return false;
            }
        }

        /// <summary>Retourne le secret déchiffré, ou une chaîne vide s'il n'existe pas.</summary>
        public static string Load(string nom)
        {
            try
            {
                string chemin = Chemin(nom);
                if (!File.Exists(chemin)) return "";
                byte[] clair = Proteger(File.ReadAllBytes(chemin), false);
                if (clair == null)
                {
                    // Le fichier est là mais illisible : profil Windows changé, fichier
                    // abîmé. Se taire ferait dire « aucun identifiant enregistré », ce qui
                    // est faux et envoie chercher au mauvais endroit.
                    LogService.Write("Secret « " + nom + " » présent mais indéchiffrable sur ce poste : "
                                   + "il a été chiffré par un autre compte Windows, ou le fichier est abîmé. "
                                   + "Réenregistrez le mot de passe.");
                    return "";
                }
                return Encoding.UTF8.GetString(clair);
            }
            catch (Exception ex)
            {
                LogService.Write("Lecture du secret « " + nom + " » impossible : " + ex.Message);
                return "";
            }
        }

        /// <summary>Supprime le secret enregistré.</summary>
        public static void Delete(string nom)
        {
            try
            {
                string chemin = Chemin(nom);
                if (File.Exists(chemin)) File.Delete(chemin);
            }
            catch (Exception) { }
        }

        /// <summary>Vrai si un secret est déjà enregistré pour ce poste et cet utilisateur.</summary>
        public static bool Exists(string nom)
        {
            try { return File.Exists(Chemin(nom)); }
            catch (Exception) { return false; }
        }

        private static byte[] Proteger(byte[] donnees, bool chiffrer)
        {
            DATA_BLOB entree = new DATA_BLOB();
            DATA_BLOB sortie = new DATA_BLOB();
            try
            {
                entree.cbData = donnees.Length;
                entree.pbData = Marshal.AllocHGlobal(donnees.Length);
                Marshal.Copy(donnees, 0, entree.pbData, donnees.Length);

                bool ok = chiffrer
                    ? CryptProtectData(ref entree, "AskThem", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref sortie)
                    : CryptUnprotectData(ref entree, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref sortie);
                if (!ok) return null;

                byte[] resultat = new byte[sortie.cbData];
                Marshal.Copy(sortie.pbData, resultat, 0, sortie.cbData);
                return resultat;
            }
            finally
            {
                if (entree.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entree.pbData);
                if (sortie.pbData != IntPtr.Zero) Marshal.FreeHGlobal(sortie.pbData);
            }
        }
    }
}
