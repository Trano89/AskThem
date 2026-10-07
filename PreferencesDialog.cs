using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Préférences de l'utilisateur : le texte des emails.
    ///
    /// Chacun écrit ses messages à sa façon. Le texte se modifie en clair, un aperçu montre
    /// le message tel que le fournisseur le recevra, et un bouton ramène au texte d'origine.
    /// Ce qui est enregistré l'est pour l'utilisateur seul, dans son profil Windows : une
    /// mise à jour d'AskThem n'y touche pas.
    /// </summary>
    public class PreferencesDialog : Form
    {
        private readonly Dictionary<NatureTexte, string> _textes = new Dictionary<NatureTexte, string>();
        private readonly bool _demanderLivraison;
        private NatureTexte _nature = NatureTexte.Offre;
        private bool _chargement;

        private ComboBox cboNature;
        private Label lblEtat;
        private TextBox txtTexte;
        private WebBrowser apercu;
        private Timer minuterie;

        public PreferencesDialog(AppConfig config)
        {
            _demanderLivraison = config == null || config.DemanderLivraison;

            Dictionary<NatureTexte, string> perso = TextesEmail.Personnalises();
            foreach (NatureTexte n in Enum.GetValues(typeof(NatureTexte)))
                _textes[n] = perso.ContainsKey(n) ? perso[n] : TextesEmail.Origine(n);

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = "Préférences";
            AppIcon.Apply(this);
            Font = AppFont.Get();
            ClientSize = new Size(1100, 680);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;

            Construire();

            cboNature.SelectedIndex = 0;
        }

        private void Construire()
        {
            // --- en-tête ---
            Label titre = new Label();
            titre.Text = "Textes des emails";
            titre.Font = new Font(AppFont.Family, 14F, FontStyle.Bold);
            titre.Dock = DockStyle.Top;
            titre.Height = 34;

            Label explication = new Label();
            explication.Text = "Le texte s'écrit en clair : une ligne vide sépare deux paragraphes, **ainsi** "
                             + "met en gras, et les éléments entre {{ }} sont remplis par AskThem. Vos textes "
                             + "sont enregistrés pour vous seul et conservés lors des mises à jour.";
            explication.Dock = DockStyle.Top;
            explication.Height = 44;
            explication.ForeColor = Color.FromArgb(90, 97, 105);

            Label lblNature = new Label();
            lblNature.Text = "Message :";
            lblNature.AutoSize = true;
            lblNature.Location = new Point(0, 8);

            cboNature = new ComboBox();
            cboNature.DropDownStyle = ComboBoxStyle.DropDownList;
            cboNature.Location = new Point(90, 4);
            cboNature.Width = 340;
            foreach (NatureTexte n in Enum.GetValues(typeof(NatureTexte)))
                cboNature.Items.Add(TextesEmail.Libelle(n));
            cboNature.SelectedIndexChanged += new EventHandler(Nature_Change);

            lblEtat = new Label();
            lblEtat.AutoSize = true;
            lblEtat.Location = new Point(448, 8);

            Panel choix = new Panel();
            choix.Dock = DockStyle.Top;
            choix.Height = 38;
            choix.Controls.Add(lblNature);
            choix.Controls.Add(cboNature);
            choix.Controls.Add(lblEtat);

            // --- éditeur et légende ---
            txtTexte = new TextBox();
            txtTexte.Multiline = true;
            txtTexte.AcceptsReturn = true;
            txtTexte.ScrollBars = ScrollBars.Vertical;
            txtTexte.WordWrap = true;
            txtTexte.Dock = DockStyle.Fill;
            txtTexte.Font = new Font(AppFont.Family, 10.5F);
            txtTexte.TextChanged += new EventHandler(Texte_Change);

            Label legende = new Label();
            legende.Dock = DockStyle.Bottom;
            legende.Height = 176;
            legende.Padding = new Padding(0, 8, 0, 0);
            legende.ForeColor = Color.FromArgb(70, 77, 85);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Éléments remplis par AskThem :");
            foreach (string[] l in TextesEmail.Legende) sb.AppendLine(l[0] + "  —  " + l[1]);
            legende.Text = sb.ToString();

            Panel gauche = new Panel();
            gauche.Dock = DockStyle.Fill;
            gauche.Padding = new Padding(0, 0, 8, 0);
            gauche.Controls.Add(txtTexte);
            gauche.Controls.Add(legende);

            // --- aperçu ---
            Label lblApercu = new Label();
            lblApercu.Text = "Aperçu, avec des articles d'exemple :";
            lblApercu.Dock = DockStyle.Top;
            lblApercu.Height = 22;

            apercu = new WebBrowser();
            apercu.Dock = DockStyle.Fill;
            apercu.ScriptErrorsSuppressed = true;
            apercu.IsWebBrowserContextMenuEnabled = false;
            apercu.WebBrowserShortcutsEnabled = false;
            apercu.AllowWebBrowserDrop = false;

            Panel cadreApercu = new Panel();
            cadreApercu.Dock = DockStyle.Fill;
            cadreApercu.BorderStyle = BorderStyle.FixedSingle;
            cadreApercu.Controls.Add(apercu);

            Panel droite = new Panel();
            droite.Dock = DockStyle.Fill;
            droite.Padding = new Padding(8, 0, 0, 0);
            droite.Controls.Add(cadreApercu);
            droite.Controls.Add(lblApercu);

            TableLayoutPanel milieu = new TableLayoutPanel();
            milieu.Dock = DockStyle.Fill;
            milieu.ColumnCount = 2;
            milieu.RowCount = 1;
            milieu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            milieu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            milieu.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            milieu.Controls.Add(gauche, 0, 0);
            milieu.Controls.Add(droite, 1, 0);

            // --- boutons ---
            Button btnOrigine = new Button();
            btnOrigine.Text = "Rétablir le texte d'origine";
            btnOrigine.Size = new Size(AppFont.Width(btnOrigine.Text, 40), 32);
            btnOrigine.Location = new Point(0, 10);
            btnOrigine.Click += new EventHandler(Origine_Click);

            Button btnToutOrigine = new Button();
            btnToutOrigine.Text = "Tout rétablir";
            btnToutOrigine.Size = new Size(AppFont.Width(btnToutOrigine.Text, 40), 32);
            btnToutOrigine.Location = new Point(btnOrigine.Right + 8, 10);
            btnToutOrigine.Click += new EventHandler(ToutOrigine_Click);

            Button btnAnnuler = new Button();
            btnAnnuler.Text = "Annuler";
            btnAnnuler.Size = new Size(AppFont.Width(btnAnnuler.Text, 40), 32);
            btnAnnuler.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAnnuler.DialogResult = DialogResult.Cancel;

            Button btnEnregistrer = new Button();
            btnEnregistrer.Text = "Enregistrer";
            btnEnregistrer.Size = new Size(AppFont.Width(btnEnregistrer.Text, 40), 32);
            btnEnregistrer.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnEnregistrer.Click += new EventHandler(Enregistrer_Click);

            Panel bas = new Panel();
            bas.Dock = DockStyle.Bottom;
            bas.Height = 52;
            bas.Controls.Add(btnOrigine);
            bas.Controls.Add(btnToutOrigine);
            bas.Controls.Add(btnEnregistrer);
            bas.Controls.Add(btnAnnuler);
            bas.Resize += delegate
            {
                btnAnnuler.Location = new Point(bas.ClientSize.Width - btnAnnuler.Width, 10);
                btnEnregistrer.Location = new Point(btnAnnuler.Left - btnEnregistrer.Width - 8, 10);
            };

            Panel corps = new Panel();
            corps.Dock = DockStyle.Fill;
            corps.Padding = new Padding(20, 14, 20, 8);
            corps.Controls.Add(milieu);
            corps.Controls.Add(choix);
            corps.Controls.Add(explication);
            corps.Controls.Add(titre);
            corps.Controls.Add(bas);

            Controls.Add(corps);
            CancelButton = btnAnnuler;

            // L'aperçu suit la frappe, sans la ralentir.
            minuterie = new Timer();
            minuterie.Interval = 400;
            minuterie.Tick += delegate { minuterie.Stop(); Apercu(); };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { minuterie.Stop(); minuterie.Dispose(); }
            catch (Exception) { }
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------------ comportement

        private void Nature_Change(object sender, EventArgs e)
        {
            _nature = (NatureTexte)cboNature.SelectedIndex;
            _chargement = true;
            txtTexte.Text = _textes[_nature];
            _chargement = false;
            txtTexte.SelectionStart = 0;
            MajEtat();
            Apercu();
        }

        private void Texte_Change(object sender, EventArgs e)
        {
            if (_chargement) return;
            _textes[_nature] = txtTexte.Text;
            MajEtat();
            minuterie.Stop();
            minuterie.Start();
        }

        private void MajEtat()
        {
            bool origine = Pareil(_textes[_nature], TextesEmail.Origine(_nature));
            lblEtat.Text = origine ? "Texte d'origine du programme" : "Texte personnalisé";
            lblEtat.ForeColor = origine ? Color.FromArgb(90, 97, 105) : Color.FromArgb(0, 102, 51);
        }

        private static bool Pareil(string a, string b)
        {
            return (a ?? "").Replace("\r\n", "\n").Trim() == (b ?? "").Replace("\r\n", "\n").Trim();
        }

        private void Origine_Click(object sender, EventArgs e)
        {
            txtTexte.Text = TextesEmail.Origine(_nature);
            txtTexte.SelectionStart = 0;
        }

        private void ToutOrigine_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Revenir au texte d'origine pour les quatre messages ?",
                    "Préférences", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (NatureTexte n in Enum.GetValues(typeof(NatureTexte)))
                _textes[n] = TextesEmail.Origine(n);
            Nature_Change(null, null);
        }

        private void Enregistrer_Click(object sender, EventArgs e)
        {
            // Un texte sans tableau ne dit pas au fournisseur ce qu'on lui demande : refusé.
            foreach (NatureTexte n in Enum.GetValues(typeof(NatureTexte)))
            {
                string probleme = TextesEmail.Probleme(_textes[n]);
                if (probleme == "") continue;
                cboNature.SelectedIndex = (int)n;
                MessageBox.Show(this, "« " + TextesEmail.Libelle(n) + " » : " + probleme + "."
                    + Environment.NewLine + Environment.NewLine
                    + "Le tableau des articles est indispensable au fournisseur.",
                    "Préférences", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Un élément retiré n'est pas une erreur, mais son contenu disparaîtra du message :
            // on le dit une fois, avant d'enregistrer.
            StringBuilder absents = new StringBuilder();
            foreach (NatureTexte n in Enum.GetValues(typeof(NatureTexte)))
            {
                List<string> manquent = TextesEmail.BlocsAbsents(_textes[n], n);
                if (manquent.Count > 0)
                    absents.AppendLine("« " + TextesEmail.Libelle(n) + " » : " + string.Join(", ", manquent));
            }
            if (absents.Length > 0
                && MessageBox.Show(this, "Ces éléments ne figurent plus dans le texte : ce qu'ils insèrent "
                    + "n'apparaîtra pas dans les messages." + Environment.NewLine + Environment.NewLine
                    + absents.ToString() + Environment.NewLine + "Enregistrer quand même ?",
                    "Préférences", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string message;
            if (!TextesEmail.Enregistrer(_textes, out message))
            {
                MessageBox.Show(this, message, "Préférences", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // ------------------------------------------------------------------ aperçu

        private void Apercu()
        {
            try
            {
                RequestType type;
                bool catalogue;
                switch (_nature)
                {
                    case NatureTexte.OffreCatalogue: type = RequestType.Offre; catalogue = true; break;
                    case NatureTexte.Fabrication: type = RequestType.Fabrication; catalogue = false; break;
                    case NatureTexte.CommandeCatalogue: type = RequestType.CommandeCatalogue; catalogue = true; break;
                    default: type = RequestType.Offre; catalogue = false; break;
                }

                List<PartLine> lignes = Exemples(type, catalogue);
                string po = type == RequestType.Offre ? "" : "BC-2026-0412.pdf";
                string html = EmailBuilder.BuildBodyAvecTexte(_textes[_nature], type, lignes, "P-2026-031",
                    DateTime.Today.AddDays(30).ToString("dd.MM.yyyy"), "", po, catalogue,
                    catalogue ? 0 : lignes.Count, _demanderLivraison);
                apercu.DocumentText = html;
            }
            catch (Exception ex)
            {
                apercu.DocumentText = "<p>Aperçu impossible : " + System.Net.WebUtility.HtmlEncode(ex.Message) + "</p>";
            }
        }

        private static List<PartLine> Exemples(RequestType type, bool catalogue)
        {
            List<PartLine> l = new List<PartLine>();
            if (catalogue)
            {
                l.Add(Ligne("D20-00412-00", "Vis CHC M4x12 inox", 50, "912-M4X12-A2", ""));
                l.Add(Ligne("E20-00087-00", "Roulement 6001-2RS", 10, "6001-2RS", ""));
                return l;
            }

            PartLine a = Ligne("A21-00128-00", "Support de lentille", 10, "", "B");
            PartLine b = Ligne("A21-00131-01", "Platine de fixation", 4, "", "C");
            a.Material = "EN AW-6082"; a.Treatment = "Anodisé noir";
            b.Material = "Inox 1.4404";
            if (type == RequestType.Offre) { a.Qty2 = 25; a.Qty3 = 50; }
            if (type == RequestType.Fabrication) { a.ControlePath = "CF_A21-00128-00.pdf"; }
            b.OldRef = "A21-00045-00";
            l.Add(a);
            l.Add(b);
            return l;
        }

        private static PartLine Ligne(string numero, string designation, int qte, string refFournisseur, string revision)
        {
            PartLine p = new PartLine();
            p.PartNumber = numero;
            p.Description = designation;
            p.Qty1 = qte;
            p.SupplierRef = refFournisseur;
            p.DrawingRevision = revision;
            return p;
        }
    }
}
