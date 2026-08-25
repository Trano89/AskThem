using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AskThem.Services
{
    /// <summary>
    /// Police unique de l'application : Aptos en taille 12.
    ///
    /// Aptos n'est pas présente sur tous les postes — elle arrive avec les versions
    /// récentes de Microsoft 365. Demander une police absente ne provoque pas d'erreur :
    /// Windows substitue silencieusement une police de repli très datée. On choisit donc
    /// nous-mêmes le repli, pour que l'application reste lisible partout.
    /// </summary>
    public static class AppFont
    {
        /// <summary>Taille unique, en points.</summary>
        public const float Size = 12F;

        private static readonly string[] Preferences = { "Aptos", "Aptos Display", "Segoe UI", "Calibri" };

        private static string _famille;
        private static Font _normale;
        private static Font _grasse;

        /// <summary>Nom de la famille réellement retenue sur ce poste.</summary>
        public static string Family
        {
            get
            {
                if (_famille != null) return _famille;
                using (InstalledFontCollection installees = new InstalledFontCollection())
                {
                    foreach (string souhaitee in Preferences)
                    {
                        foreach (FontFamily f in installees.Families)
                        {
                            if (string.Equals(f.Name, souhaitee, StringComparison.OrdinalIgnoreCase))
                            {
                                _famille = f.Name;
                                return _famille;
                            }
                        }
                    }
                }
                _famille = SystemFonts.MessageBoxFont.FontFamily.Name;
                return _famille;
            }
        }

        /// <summary>Police normale de l'application.</summary>
        public static Font Get()
        {
            if (_normale == null) _normale = new Font(Family, Size, FontStyle.Regular);
            return _normale;
        }

        /// <summary>Même police en gras, pour les rares mises en évidence.</summary>
        public static Font Bold()
        {
            if (_grasse == null) _grasse = new Font(Family, Size, FontStyle.Bold);
            return _grasse;
        }

        /// <summary>Largeur du texte dans la police de l'application, marge comprise.</summary>
        public static int Width(string texte, int marge)
        {
            if (string.IsNullOrEmpty(texte)) return marge;
            return TextRenderer.MeasureText(texte, Get()).Width + marge;
        }
    }
}
