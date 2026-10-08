using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AskThem.Services;

namespace AskThem.Controls
{
    /// <summary>
    /// Un grand bouton qui porte un titre et une explication.
    ///
    /// Tout est dessiné : y placer des étiquettes leur ferait peindre leur propre fond
    /// par-dessus le bouton, et le survol ne les atteindrait pas.
    /// </summary>
    public class CarteBouton : Button
    {
        private static readonly Color Bordure = Color.FromArgb(198, 204, 211);
        private static readonly Color BordureSurvol = Color.FromArgb(0, 90, 158);
        private static readonly Color Fond = Color.White;
        private static readonly Color FondSurvol = Color.FromArgb(240, 246, 251);
        private static readonly Color FondAppuye = Color.FromArgb(226, 237, 247);
        private static readonly Color Encre = Color.FromArgb(21, 24, 28);
        private static readonly Color EncreDouce = Color.FromArgb(90, 97, 105);

        // Créées une fois : en créer deux à chaque dessin, au survol, épuisait les GDI.
        private static readonly Font PoliceTitre = new Font(AppFont.Family, 12F, FontStyle.Bold);
        private static readonly Font PoliceTexte = AppFont.Get();

        private bool _survole;
        private bool _appuye;
        private bool _selectionne;

        /// <summary>Le choix en cours, repéré quand on revient à cette étape.</summary>
        public bool Selectionne
        {
            get { return _selectionne; }
            set { _selectionne = value; Invalidate(); }
        }

        /// <summary>Ligne de titre, en gras.</summary>
        public string Titre { get; set; }

        /// <summary>Ce que le choix implique, sous le titre.</summary>
        public string Explication { get; set; }

        public CarteBouton()
        {
            Titre = "";
            Explication = "";
            Height = LogicalToDeviceUnits(86);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Fond;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        private int MargeX { get { return LogicalToDeviceUnits(20); } }
        private int MargeY { get { return LogicalToDeviceUnits(14); } }
        private int Ecart { get { return LogicalToDeviceUnits(4); } }

        /// <summary>
        /// La hauteur qu'il faut pour tout lire à cette largeur. Fixée en pixels, elle
        /// rognait l'explication dès que l'écran était mis à l'échelle.
        /// </summary>
        public int HauteurPour(int largeur)
        {
            int utile = Math.Max(40, largeur - MargeX * 2);
            Size t = TextRenderer.MeasureText(Titre, PoliceTitre, new Size(utile, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size x = TextRenderer.MeasureText(Explication, PoliceTexte, new Size(utile, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return MargeY * 2 + t.Height + Ecart + x.Height;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            int h = HauteurPour(Width);
            if (Height != h) Height = h;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _survole = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _survole = false;
            _appuye = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _appuye = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _appuye = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fond = _appuye ? FondAppuye : (_survole || _selectionne ? FondSurvol : Fond);
            bool accent = _survole || Focused || _selectionne;
            Color bord = accent ? BordureSurvol : Bordure;

            using (SolidBrush b = new SolidBrush(fond))
            using (Pen p = new Pen(bord, accent ? 2f : 1f))
            {
                g.FillRectangle(b, r);
                g.DrawRectangle(p, r);
            }

            int utile = Math.Max(40, Width - MargeX * 2);
            Size t = TextRenderer.MeasureText(Titre, PoliceTitre, new Size(utile, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Rectangle rTitre = new Rectangle(MargeX, MargeY, utile, t.Height);
            TextRenderer.DrawText(g, Titre, PoliceTitre, rTitre, Encre,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            int y = MargeY + t.Height + Ecart;
            Rectangle rTexte = new Rectangle(MargeX, y, utile, Math.Max(0, Height - y - MargeY / 2));
            TextRenderer.DrawText(g, Explication, PoliceTexte, rTexte, EncreDouce,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                | TextFormatFlags.NoPrefix);
        }
    }
}
