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

        private static Font _titre;
        private static Font _section;
        private static Font _grande;

        /// <summary>Titre d'écran (assistant) : Semibold 15 pt.</summary>
        public static Font Titre()
        {
            if (_titre == null) _titre = Demi(15F);
            return _titre;
        }

        /// <summary>Titre de section, de carte ou de bandeau : Semibold 11 pt.</summary>
        public static Font Section()
        {
            if (_section == null) _section = Demi(11F);
            return _section;
        }

        /// <summary>Corps agrandi, pour l'assistant : 10 pt.</summary>
        public static Font Grand()
        {
            if (_grande == null) _grande = new Font(Family, 10F, FontStyle.Regular);
            return _grande;
        }

        /// <summary>Le demi-gras de la famille s'il existe, sinon le gras.</summary>
        private static Font Demi(float taille)
        {
            string demi = Family + " Semibold";
            using (InstalledFontCollection installees = new InstalledFontCollection())
            {
                foreach (FontFamily f in installees.Families)
                    if (string.Equals(f.Name, demi, StringComparison.OrdinalIgnoreCase))
                        return new Font(f.Name, taille, FontStyle.Regular);
            }
            return new Font(Family, taille, FontStyle.Bold);
        }

        /// <summary>
        /// Largeur du texte dans la police de l'application, marge comprise, en unités
        /// logiques (à 100 %) : les fenêtres la mettent ensuite à l'échelle de l'écran avec
        /// tout le reste. Mesurée en pixels réels, elle était agrandie une seconde fois.
        /// </summary>
        public static int Width(string texte, int marge)
        {
            if (string.IsNullOrEmpty(texte)) return marge;
            int mesure = TextRenderer.MeasureText(texte, Get()).Width;
            return (int)Math.Ceiling(mesure * 96.0 / DpiSysteme()) + marge;
        }

        /// <summary>Hauteur d'une ligne de texte, en unités logiques (à 100 %).</summary>
        public static int HauteurLigne()
        {
            return (int)Math.Ceiling(Get().Height * 96.0 / DpiSysteme());
        }

        /// <summary>Convertit une mesure logique en pixels de l'écran principal.</summary>
        public static int Px(int logique)
        {
            return (int)Math.Round(logique * DpiSysteme() / 96.0);
        }

        private static int _dpi;

        /// <summary>Densité de l'écran principal, celle à laquelle les fenêtres sont construites.</summary>
        public static int DpiSysteme()
        {
            if (_dpi > 0) return _dpi;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _dpi = (int)Math.Round(g.DpiX);
            }
            catch (Exception) { }
            if (_dpi <= 0) _dpi = 96;
            return _dpi;
        }
    }
}
