using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AskThem.Services;

namespace AskThem.Controls
{
    /// <summary>
    /// Bascule entre le mode guidé et la vue complète : deux moitiés, celle qui est active
    /// est pleine. Dessiné, pour que le survol et l'état actif ne dépendent d'aucun enfant.
    /// </summary>
    public class SelecteurMode : Control
    {
        private static readonly Color Actif = Color.FromArgb(0, 90, 158);
        private static readonly Color Fond = Color.FromArgb(233, 237, 241);
        private static readonly Color Bordure = Color.FromArgb(198, 204, 211);
        private static readonly Color Encre = Color.FromArgb(21, 24, 28);

        private bool _complet;
        private int _survole = -1;

        public string TexteGauche { get; set; }
        public string TexteDroite { get; set; }

        /// <summary>Vrai quand la vue complète est choisie.</summary>
        public bool Complet
        {
            get { return _complet; }
            set
            {
                if (_complet == value) return;
                _complet = value;
                Invalidate();
                if (ModeChange != null) ModeChange(this, EventArgs.Empty);
            }
        }

        public event EventHandler ModeChange;

        public SelecteurMode()
        {
            TexteGauche = "Guidé";
            TexteDroite = "Vue complète";
            Height = 32;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        /// <summary>Largeur juste nécessaire aux deux intitulés.</summary>
        public int LargeurUtile
        {
            get { return AppFont.Width(TexteGauche, 34) + AppFont.Width(TexteDroite, 34); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int moitie = e.X < Width / 2 ? 0 : 1;
            if (moitie != _survole) { _survole = moitie; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _survole = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Complet = e.X >= Width / 2;
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? SystemColors.Control : Parent.BackColor);

            Rectangle tout = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(Fond))
            using (Pen p = new Pen(Bordure))
            {
                g.FillRectangle(b, tout);
                g.DrawRectangle(p, tout);
            }

            int milieu = Width / 2;
            Rectangle gauche = new Rectangle(1, 1, milieu - 1, Height - 3);
            Rectangle droite = new Rectangle(milieu, 1, Width - milieu - 2, Height - 3);

            Moitie(g, gauche, TexteGauche, !_complet, _survole == 0);
            Moitie(g, droite, TexteDroite, _complet, _survole == 1);
        }

        private void Moitie(Graphics g, Rectangle r, string texte, bool actif, bool survole)
        {
            if (actif)
            {
                using (SolidBrush b = new SolidBrush(Actif)) g.FillRectangle(b, r);
            }
            else if (survole)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(219, 226, 233))) g.FillRectangle(b, r);
            }

            using (Font f = new Font(AppFont.Family, 9.5F, actif ? FontStyle.Bold : FontStyle.Regular))
            {
                TextRenderer.DrawText(g, texte, f, r, actif ? Color.White : Encre,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix);
            }
        }
    }
}
