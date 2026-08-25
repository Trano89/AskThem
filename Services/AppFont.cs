using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AskThem.Services
{
    /// <summary>
    /// Police de l'interface. Elle sert aussi à mesurer les libellés : c'est d'elle que
    /// les boutons et les colonnes tirent leur largeur, plutôt que de valeurs figées.
    ///
    /// La police des emails est distincte et définie dans les modèles : ce qui s'affiche
    /// à l'écran et ce qui part chez le fournisseur n'ont pas les mêmes contraintes.
    /// </summary>
    public static class AppFont
    {
        /// <summary>Taille unique, en points.</summary>
        public const float Size = 9F;

        private static readonly string[] Preferences = { "Segoe UI", "Aptos", "Calibri" };

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
