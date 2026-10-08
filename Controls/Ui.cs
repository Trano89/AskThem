using System;
using System.Drawing;
using System.Windows.Forms;
using AskThem.Services;

namespace AskThem.Controls
{
    /// <summary>
    /// Les couleurs de l'application, en un seul endroit.
    ///
    /// Un fond neutre, une seule couleur d'accent pour l'action principale, et des couleurs
    /// d'état qui ne sont jamais le seul signal : chaque état porte aussi un texte.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Fond = Color.FromArgb(244, 245, 247);
        public static readonly Color Surface = Color.White;
        public static readonly Color Bordure = Color.FromArgb(209, 213, 219);
        public static readonly Color Separateur = Color.FromArgb(229, 231, 235);
        public static readonly Color Texte = Color.FromArgb(31, 35, 40);
        public static readonly Color Texte2 = Color.FromArgb(91, 100, 112);
        public static readonly Color Desactive = Color.FromArgb(154, 161, 170);

        public static readonly Color Accent = Color.FromArgb(0, 90, 158);
        public static readonly Color AccentSurvol = Color.FromArgb(0, 75, 132);
        public static readonly Color AccentPresse = Color.FromArgb(0, 61, 107);
        public static readonly Color AccentPale = Color.FromArgb(232, 241, 250);

        public static readonly Color Succes = Color.FromArgb(30, 123, 69);
        public static readonly Color SuccesFond = Color.FromArgb(231, 244, 236);
        public static readonly Color Attention = Color.FromArgb(138, 90, 0);
        public static readonly Color AttentionFond = Color.FromArgb(255, 244, 214);
        public static readonly Color Erreur = Color.FromArgb(179, 38, 30);
        public static readonly Color ErreurFond = Color.FromArgb(253, 236, 236);
        public static readonly Color Info = Color.FromArgb(0, 90, 158);
        public static readonly Color InfoFond = Color.FromArgb(232, 241, 250);
    }

    /// <summary>
    /// Les briques de l'interface. Leur taille découle toujours du texte qu'elles portent :
    /// c'est ce qui les garde lisibles à 100, 125 ou 150 %, et en bureau à distance — des
    /// hauteurs fixées en pixels rognaient les libellés dès que l'écran était mis à l'échelle.
    /// </summary>
    public static class Ui
    {
        /// <summary>Bouton ordinaire, au rendu natif de Windows.</summary>
        public static Button Secondaire(string texte)
        {
            Button b = new Button();
            b.Text = texte;
            b.Font = AppFont.Get();
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(10, 3, 10, 3);
            b.MinimumSize = new Size(88, 0);
            b.Margin = new Padding(8, 0, 0, 0);
            b.UseVisualStyleBackColor = true;
            return b;
        }

        /// <summary>L'action principale de l'écran : une seule par écran, toujours à droite.</summary>
        public static Button Primaire(string texte)
        {
            Button b = Secondaire(texte);
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = Theme.Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Theme.AccentPresse;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentSurvol;
            b.FlatAppearance.MouseDownBackColor = Theme.AccentPresse;
            b.EnabledChanged += delegate { b.BackColor = b.Enabled ? Theme.Accent : Theme.Desactive; };
            return b;
        }

        /// <summary>Action rare ou réversible, présentée comme un lien.</summary>
        public static LinkLabel Lien(string texte, EventHandler clic)
        {
            LinkLabel l = new LinkLabel();
            l.Text = texte;
            l.Font = AppFont.Get();
            l.AutoSize = true;
            l.LinkColor = Theme.Accent;
            l.ActiveLinkColor = Theme.AccentPresse;
            l.VisitedLinkColor = Theme.Accent;
            l.LinkBehavior = LinkBehavior.HoverUnderline;
            l.Margin = new Padding(0, 6, 16, 0);
            if (clic != null) l.LinkClicked += delegate { clic(l, EventArgs.Empty); };
            return l;
        }

        public static Label Titre(string texte) { return Libelle(texte, AppFont.Titre(), Theme.Texte); }
        public static Label Section(string texte) { return Libelle(texte, AppFont.Section(), Theme.Texte); }
        public static Label Corps(string texte) { return Libelle(texte, AppFont.Get(), Theme.Texte); }
        public static Label Legende(string texte) { return Libelle(texte, AppFont.Get(), Theme.Texte2); }

        /// <summary>Un libellé qui se replie sur plusieurs lignes quand la place manque.</summary>
        public static Label Libelle(string texte, Font police, Color couleur)
        {
            Label l = new Label();
            l.Text = texte;
            l.Font = police;
            l.ForeColor = couleur;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            l.Margin = new Padding(0, 0, 0, 4);
            return l;
        }

        /// <summary>Une pile verticale qui prend la hauteur de son contenu.</summary>
        public static TableLayoutPanel Pile()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Dock = DockStyle.Top;
            t.Margin = Padding.Empty;
            return t;
        }

        /// <summary>Ajoute une ligne à hauteur automatique.</summary>
        public static void Ajouter(TableLayoutPanel t, Control c)
        {
            t.RowCount = t.RowCount + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, 0, t.RowCount - 1);
        }

        /// <summary>Une rangée de boutons, de gauche à droite, à la hauteur de son contenu.</summary>
        public static FlowLayoutPanel Rangee()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.WrapContents = false;
            f.Margin = Padding.Empty;
            f.Padding = Padding.Empty;
            return f;
        }

        /// <summary>
        /// La fenêtre tient dans l'écran où elle s'ouvre : mise à l'échelle à 150 %, une
        /// fenêtre pensée pour 100 % dépasse un écran portable, boutons du bas compris.
        /// </summary>
        public static void TenirDansEcran(Form f)
        {
            f.Load += delegate
            {
                Control repere = f.Owner != null ? (Control)f.Owner : f;
                Rectangle zone = Screen.FromControl(repere).WorkingArea;
                f.MinimumSize = new Size(Math.Min(f.MinimumSize.Width, zone.Width),
                                         Math.Min(f.MinimumSize.Height, zone.Height));
                if (f.Width <= zone.Width && f.Height <= zone.Height) return;

                int w = Math.Min(f.Width, zone.Width);
                int h = Math.Min(f.Height, zone.Height);
                Rectangle centre = f.Owner != null ? f.Owner.Bounds : zone;
                int x = centre.Left + (centre.Width - w) / 2;
                int y = centre.Top + (centre.Height - h) / 2;
                x = Math.Max(zone.Left, Math.Min(x, zone.Right - w));
                y = Math.Max(zone.Top, Math.Min(y, zone.Bottom - h));
                f.Bounds = new Rectangle(x, y, w, h);
            };
        }

        /// <summary>Ligne de séparation d'un pixel.</summary>
        public static Panel Separateur()
        {
            Panel p = new Panel();
            p.Height = 1;
            p.Dock = DockStyle.Top;
            p.BackColor = Theme.Separateur;
            p.Margin = Padding.Empty;
            return p;
        }
    }
}
