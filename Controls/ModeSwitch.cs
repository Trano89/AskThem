using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AskThem.Controls
{
    /// <summary>
    /// Interrupteur à deux positions, dessiné à la main : WinForms n'en fournit pas.
    /// Position gauche = première option, position droite = seconde option.
    /// L'option active est mise en évidence.
    /// </summary>
    public class ModeSwitch : Control
    {
        private const int TrackWidth = 48;
        private const int TrackHeight = 24;
        private const int Knob = 18;
        private const int Gap = 12;

        private bool _isRight;
        private bool _hover;

        /// <summary>Déclenché quand la position change, quelle qu'en soit la cause.</summary>
        public event EventHandler ModeChanged;

        /// <summary>Libellé de la position gauche.</summary>
        public string LeftText { get; set; }

        /// <summary>Libellé de la position droite.</summary>
        public string RightText { get; set; }

        /// <summary>Couleur de la piste lorsque l'interrupteur est à droite.</summary>
        public Color OnColor { get; set; }

        /// <summary>Vrai si l'interrupteur est en position droite.</summary>
        public bool IsRight
        {
            get { return _isRight; }
            set
            {
                if (_isRight == value) return;
                _isRight = value;
                Invalidate();
                if (ModeChanged != null) ModeChanged(this, EventArgs.Empty);
            }
        }

        public ModeSwitch()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.Selectable, true);
            TabStop = true;
            LeftText = "Gauche";
            RightText = "Droite";
            OnColor = Color.FromArgb(0, 90, 158);
            Cursor = Cursors.Hand;
            Height = 30;
        }

        /// <summary>Largeur nécessaire pour afficher les deux libellés et la piste.</summary>
        public int PreferredWidth
        {
            get
            {
                using (Font gras = new Font(Font, FontStyle.Bold))
                {
                    Size g = TextRenderer.MeasureText(LeftText, gras);
                    Size d = TextRenderer.MeasureText(RightText, gras);
                    return g.Width + Gap + TrackWidth + Gap + d.Width + 8;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Font gras = new Font(Font, FontStyle.Bold))
            {
                Size tailleGauche = TextRenderer.MeasureText(LeftText, gras);
                Size tailleDroite = TextRenderer.MeasureText(RightText, gras);

                int y = (Height - TrackHeight) / 2;
                int xTrack = tailleGauche.Width + Gap;

                // Libellé de gauche : mis en évidence s'il est actif.
                Color actif = ForeColor;
                Color inactif = Color.FromArgb(130, 138, 142);
                TextRenderer.DrawText(g, LeftText, _isRight ? Font : gras,
                    new Point(0, (Height - tailleGauche.Height) / 2),
                    _isRight ? inactif : actif);

                // Piste.
                Rectangle track = new Rectangle(xTrack, y, TrackWidth, TrackHeight);
                Color couleurPiste = _isRight ? OnColor : Color.FromArgb(176, 184, 188);
                if (_hover) couleurPiste = ControlPaint.Light(couleurPiste, 0.12f);
                using (GraphicsPath chemin = RoundedPath(track, TrackHeight / 2))
                using (SolidBrush brosse = new SolidBrush(couleurPiste))
                {
                    g.FillPath(brosse, chemin);
                }

                // Bouton mobile.
                int marge = (TrackHeight - Knob) / 2;
                int xKnob = _isRight ? track.Right - Knob - marge : track.Left + marge;
                Rectangle knob = new Rectangle(xKnob, y + marge, Knob, Knob);
                using (SolidBrush blanc = new SolidBrush(Color.White))
                using (Pen bord = new Pen(Color.FromArgb(60, 0, 0, 0)))
                {
                    g.FillEllipse(blanc, knob);
                    g.DrawEllipse(bord, knob);
                }

                // Libellé de droite.
                TextRenderer.DrawText(g, RightText, _isRight ? gras : Font,
                    new Point(track.Right + Gap, (Height - tailleDroite.Height) / 2),
                    _isRight ? actif : inactif);

                // Repère de focus clavier.
                if (Focused)
                {
                    Rectangle focus = Rectangle.Inflate(track, 3, 3);
                    ControlPaint.DrawFocusRectangle(g, focus);
                }
            }
        }

        private static GraphicsPath RoundedPath(Rectangle r, int rayon)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rayon * 2;
            p.AddArc(r.Left, r.Top, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Focus();
            IsRight = !IsRight;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Left || keyData == Keys.Right) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { IsRight = !IsRight; e.Handled = true; }
            else if (e.KeyCode == Keys.Left) { IsRight = false; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { IsRight = true; e.Handled = true; }
        }
    }
}
