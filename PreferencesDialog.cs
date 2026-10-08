using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AskThem.Controls;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Préférences : le texte des emails et le délai des rappels, propres à l'utilisateur ;
    /// le découpage des envois, propre au poste.
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
        private NumericUpDown numRappel;
        private ComboBox cboCompression;
        private NumericUpDown numTailleMax;
        private NumericUpDown numPiecesMax;
        private readonly AppConfig _config;

        public PreferencesDialog(AppConfig config)
        {
            _demanderLivraison = config == null || config.DemanderLivraison;
            _config = config;

            Dictionary<NatureTexte, string> perso = TextesEmail.Personnalises();
            foreach (NatureTexte n in Enum.GetValues(typeof(NatureTexte)))
                _textes[n] = perso.ContainsKey(n) ? perso[n] : TextesEmail.Origine(n);

            // Suspendue puis reprise : c'est à la reprise que la fenêtre se met à l'échelle.
            SuspendLayout();
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

            ResumeLayout(false);
            PerformLayout();
            Ui.TenirDansEcran(this);
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

            Label lblNature = Ui.Corps("Message :");
            lblNature.Anchor = AnchorStyles.Left;
            lblNature.Margin = new Padding(0, 0, 8, 0);

            cboNature = new ComboBox();
            cboNature.DropDownStyle = ComboBoxStyle.DropDownList;
            cboNature.Width = 340;
            cboNature.Margin = new Padding(0, 0, 16, 0);
            foreach (NatureTexte n in Enum.GetValues(typeof(NatureTexte)))
                cboNature.Items.Add(TextesEmail.Libelle(n));
            cboNature.SelectedIndexChanged += new EventHandler(Nature_Change);

            lblEtat = Ui.Legende("");
            lblEtat.Anchor = AnchorStyles.Left;
            lblEtat.Margin = new Padding(0, 0, 16, 0);

            // Revenir à l'origine se fait rarement, et se rattrape : des liens suffisent.
            LinkLabel lnkOrigine = Ui.Lien("Rétablir ce texte", new EventHandler(Origine_Click));
            lnkOrigine.Anchor = AnchorStyles.Left;
            lnkOrigine.Margin = new Padding(0, 0, 16, 0);
            LinkLabel lnkToutOrigine = Ui.Lien("Tout rétablir", new EventHandler(ToutOrigine_Click));
            lnkToutOrigine.Anchor = AnchorStyles.Left;
            lnkToutOrigine.Margin = Padding.Empty;

            FlowLayoutPanel choix = new FlowLayoutPanel();
            choix.Dock = DockStyle.Top;
            choix.AutoSize = true;
            choix.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            choix.WrapContents = true;
            choix.Padding = new Padding(0, 4, 0, 8);
            choix.Controls.Add(lblNature);
            choix.Controls.Add(cboNature);
            choix.Controls.Add(lblEtat);
            choix.Controls.Add(lnkOrigine);
            choix.Controls.Add(lnkToutOrigine);

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

            // --- boutons : l'action principale à droite ---
            Button btnAnnuler = Ui.Secondaire("Annuler");
            btnAnnuler.DialogResult = DialogResult.Cancel;
            Button btnEnregistrer = Ui.Primaire("Enregistrer");
            btnEnregistrer.Click += new EventHandler(Enregistrer_Click);

            TableLayoutPanel bas = new TableLayoutPanel();
            bas.Dock = DockStyle.Bottom;
            bas.AutoSize = true;
            bas.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bas.ColumnCount = 3;
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.RowCount = 1;
            bas.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bas.Padding = new Padding(0, 12, 0, 0);
            bas.Controls.Add(new Label(), 0, 0);
            bas.Controls.Add(btnAnnuler, 1, 0);
            bas.Controls.Add(btnEnregistrer, 2, 0);

            // --- onglets : les textes, les rappels ---
            TabPage pageTextes = new TabPage("Textes des emails");
            pageTextes.Padding = new Padding(12, 10, 12, 8);
            pageTextes.Controls.Add(milieu);
            pageTextes.Controls.Add(choix);
            pageTextes.Controls.Add(explication);
            pageTextes.Controls.Add(titre);

            TabControl onglets = new TabControl();
            onglets.Dock = DockStyle.Fill;
            onglets.TabPages.Add(pageTextes);
            onglets.TabPages.Add(PageRappels());
            onglets.TabPages.Add(PageEnvoi());

            Panel corps = new Panel();
            corps.Dock = DockStyle.Fill;
            corps.Padding = new Padding(16, 12, 16, 8);
            corps.Controls.Add(onglets);
            corps.Controls.Add(bas);

            Controls.Add(corps);
            CancelButton = btnAnnuler;
            AcceptButton = null;     // Entrée sert à aller à la ligne dans le texte

            // L'aperçu suit la frappe, sans la ralentir.
            minuterie = new Timer();
            minuterie.Interval = 400;
            minuterie.Tick += delegate { minuterie.Stop(); Apercu(); };
        }

        /// <summary>Une page de réglages : un titre, une explication, puis des rangées.</summary>
        private static TableLayoutPanel Formulaire(TabPage page, string titre, string explication)
        {
            page.Padding = new Padding(20, 18, 20, 8);
            page.AutoScroll = true;

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Top;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            Label lTitre = Ui.Section(titre);
            lTitre.Margin = new Padding(0, 0, 0, 6);
            Label lExplication = Ui.Legende(explication);
            lExplication.Margin = new Padding(0, 0, 0, 18);
            Large(t, lTitre);
            Large(t, lExplication);
            page.Controls.Add(t);
            return t;
        }

        /// <summary>Une rangée sur toute la largeur.</summary>
        private static void Large(TableLayoutPanel t, Control c)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 2);
        }

        /// <summary>Une rangée : l'intitulé, puis les contrôles côte à côte.</summary>
        private static void Reglage(TableLayoutPanel t, string intitule, params Control[] controles)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l = Ui.Corps(intitule);
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 0, 16, 12);
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.WrapContents = false;
            f.Margin = new Padding(0, 0, 0, 12);
            foreach (Control c in controles)
            {
                c.Anchor = AnchorStyles.Left;
                f.Controls.Add(c);
            }
            t.Controls.Add(l, 0, r);
            t.Controls.Add(f, 1, r);
        }

        private static Label Unite(string texte)
        {
            Label l = Ui.Corps(texte);
            l.Margin = new Padding(6, 0, 16, 0);
            return l;
        }

        /// <summary>Le délai des rappels, propre à cet utilisateur.</summary>
        private TabPage PageRappels()
        {
            TabPage page = new TabPage("Rappels");
            TableLayoutPanel t = Formulaire(page, "Rappel « Avez-vous reçu une réponse ? »",
                "Après l'envoi d'une demande, AskThem vous demande si le fournisseur a répondu. "
              + "« Pas encore » repose la question après le même délai. Ce réglage vaut pour vous "
              + "seul, sur tous vos postes, et reste en place lors des mises à jour.");

            numRappel = new NumericUpDown();
            numRappel.Minimum = 1;
            numRappel.Maximum = 90;
            numRappel.Width = 70;
            numRappel.Value = Math.Max(1, Math.Min(90, PreferencesUtilisateur.DelaiRappel(_config)));

            LinkLabel origine = Ui.Lien("Rétablir " + PreferencesUtilisateur.RappelParDefaut + " jours",
                delegate { numRappel.Value = PreferencesUtilisateur.RappelParDefaut; });
            origine.Margin = Padding.Empty;
            Reglage(t, "Délai avant rappel", numRappel, Unite("jours"), origine);

            Label note = Ui.Legende("Les rappels déjà programmés gardent leur date ; le nouveau délai vaut "
                                  + "pour les prochains envois et pour chaque « Pas encore ».");
            Large(t, note);
            return page;
        }

        /// <summary>
        /// Le découpage des envois, propre au poste : il dépend de la messagerie, pas de la
        /// demande. Ces réglages encombraient l'écran principal, où personne ne les changeait.
        /// </summary>
        private TabPage PageEnvoi()
        {
            TabPage page = new TabPage("Envoi");
            TableLayoutPanel t = Formulaire(page, "Pièces jointes",
                "Une demande trop lourde pour un seul email est répartie sur plusieurs messages. "
              + "Ces réglages valent pour ce poste, pour tous ses utilisateurs.");

            numTailleMax = new NumericUpDown();
            numTailleMax.Minimum = 1;
            numTailleMax.Maximum = 200;
            numTailleMax.Width = 70;
            numTailleMax.Value = Math.Max(1, Math.Min(200, _config == null ? 20 : _config.ZipThresholdMb));
            Reglage(t, "Taille par email, au plus", numTailleMax, Unite("Mo"));

            numPiecesMax = new NumericUpDown();
            numPiecesMax.Minimum = 1;
            numPiecesMax.Maximum = 200;
            numPiecesMax.Width = 70;
            numPiecesMax.Value = Math.Max(1, Math.Min(200, _config == null ? 25 : _config.MaxAttachments));
            Reglage(t, "Pièces jointes par email, au plus", numPiecesMax, Unite("fichiers"));

            cboCompression = new ComboBox();
            cboCompression.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCompression.Width = 140;
            cboCompression.Items.AddRange(ZipService.Niveaux);
            cboCompression.SelectedItem = ZipService.Niveaux[2];
            if (_config != null)
                foreach (string n in ZipService.Niveaux)
                    if (n == _config.ZipCompression) cboCompression.SelectedItem = n;
            Reglage(t, "Compression des archives", cboCompression);

            Label note = Ui.Legende("Une compression plus forte allège les messages, mais prend plus de temps "
                                  + "à la préparation.");
            Large(t, note);
            return page;
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

            PreferencesUtilisateur prefs = PreferencesUtilisateur.Lire();
            prefs.RappelJours = (int)numRappel.Value;
            if (!prefs.Enregistrer(out message))
            {
                MessageBox.Show(this, message, "Préférences", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Les réglages du poste ne s'écrivent que s'ils ont changé : le fichier est
            // partagé par tous les utilisateurs du poste.
            if (_config != null)
            {
                int taille = (int)numTailleMax.Value;
                int pieces = (int)numPiecesMax.Value;
                string compression = cboCompression.SelectedItem as string;
                if (string.IsNullOrEmpty(compression)) compression = _config.ZipCompression;
                if (taille != _config.ZipThresholdMb || pieces != _config.MaxAttachments
                    || compression != _config.ZipCompression)
                {
                    _config.ZipThresholdMb = taille;
                    _config.MaxAttachments = pieces;
                    _config.ZipCompression = compression;
                    try { ConfigService.Save(_config); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Les réglages d'envoi n'ont pas pu être enregistrés : " + ex.Message,
                            "Préférences", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
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
