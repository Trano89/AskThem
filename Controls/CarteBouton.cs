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

        private bool _survole;
        private bool _appuye;

        /// <summary>Ligne de titre, en gras.</summary>
        public string Titre { get; set; }

        /// <summary>Ce que le choix implique, sous le titre.</summary>
        public string Explication { get; set; }

        public CarteBouton()
        {
            Titre = "";
            Explication = "";
            Height = 86;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Fond;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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
            Color fond = _appuye ? FondAppuye : (_survole ? FondSurvol : Fond);
            Color bord = (_survole || Focused) ? BordureSurvol : Bordure;

            using (SolidBrush b = new SolidBrush(fond))
            using (Pen p = new Pen(bord, _survole || Focused ? 2f : 1f))
            {
                g.FillRectangle(b, r);
                g.DrawRectangle(p, r);
            }

            int marge = 20;
            using (Font fTitre = new Font(AppFont.Family, 13F, FontStyle.Bold))
            using (Font fTexte = new Font(AppFont.Family, 9.5F, FontStyle.Regular))
            {
                Rectangle rTitre = new Rectangle(marge, 14, Width - marge * 2, 26);
                TextRenderer.DrawText(g, Titre, fTitre, rTitre, Encre,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                Rectangle rTexte = new Rectangle(marge, 42, Width - marge * 2, Height - 52);
                TextRenderer.DrawText(g, Explication, fTexte, rTexte, EncreDouce,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                    | TextFormatFlags.NoPrefix);
            }
        }
    }
}
