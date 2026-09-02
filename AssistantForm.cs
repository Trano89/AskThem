using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Ce que l'assistant a récolté. La fenêtre complète l'applique ensuite : le pipeline
    /// n'existe qu'à un seul endroit, l'assistant ne fait que remplir.
    /// </summary>
    public class DemandeEnCours
    {
        public RequestType Type = RequestType.Offre;
        public Supplier Destinataire;
        public List<PartLine> Lignes = new List<PartLine>();
        public string ReferenceCommande = "";
        public DateTime? Delai;
        public string CheminPo = "";
        public string Commentaire = "";
        public bool Export3D = true;
        public bool Export2D = true;
        public bool ControleFabrication;
        public bool Generer;
    }

    /// <summary>
    /// Conduit l'utilisateur d'un bout à l'autre d'une demande, un pas après l'autre.
    ///
    /// La fenêtre complète reste accessible et fait exactement le même travail : celle-ci
    /// ne décide de rien, elle guide. Tout ce qu'elle recueille est repassé à la fenêtre
    /// principale, qui reste seule à porter le traitement.
    /// </summary>
    public class AssistantForm : Form
    {
        private const int NbEtapes = 5;

        private readonly AppConfig _config;
        private readonly List<Supplier> _fournisseurs;
        private readonly Dictionary<string, InventoryService.Entry> _inventaire;
        private readonly Dictionary<string, string> _pdm;

        private int _etape;
        private readonly DemandeEnCours _demande = new DemandeEnCours();
        private readonly BindingList<PartLine> _lignes = new BindingList<PartLine>();

        private Label lblTitre;
        private Label lblSousTitre;
        private Label lblProgression;
        private Panel corps;
        private Button btnPrecedent;
        private Button btnSuivant;
        private Button btnVueComplete;

        // Étape 2
        private ListBox lstFournisseurs;
        private Label lblLien;

        // Étape 3
        private DataGridView grille;
        private Label lblCompteArticles;

        // Étape 4
        private TextBox txtReference;
        private DateTimePicker dtpDelai;
        private TextBox txtPo;
        private TextBox txtCommentaire;
        private CheckBox chk3D;
        private CheckBox chk2D;
        private CheckBox chkControle;
        private Panel voletAvance;

        // Étape 5
        private Label lblRecap;

        /// <summary>Ce que l'utilisateur a construit, une fois la fenêtre fermée.</summary>
        public DemandeEnCours Demande { get { return _demande; } }

        /// <summary>Vrai si l'utilisateur a demandé à passer à la fenêtre complète.</summary>
        public bool VueComplete { get; private set; }

        public AssistantForm(AppConfig config, List<Supplier> fournisseurs,
                             Dictionary<string, InventoryService.Entry> inventaire,
                             Dictionary<string, string> pdm)
        {
            _config = config != null ? config : new AppConfig();
            _fournisseurs = fournisseurs != null ? fournisseurs : new List<Supplier>();
            _inventaire = inventaire;
            _pdm = pdm;

            Text = "AskThem — nouvelle demande";
            Font = AppFont.Get();
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(940, 620);
            MinimumSize = new Size(760, 520);
            MaximizeBox = false;

            Construire();
            AllerA(0);
        }

        // ==================================================================
        // Ossature
        // ==================================================================

        private void Construire()
        {
            lblTitre = new Label();
            lblTitre.Font = new Font(AppFont.Family, 17F, FontStyle.Bold);
            lblTitre.Dock = DockStyle.Top;
            lblTitre.Height = 42;

            lblSousTitre = new Label();
            lblSousTitre.Dock = DockStyle.Top;
            lblSousTitre.Height = 26;
            lblSousTitre.ForeColor = Color.FromArgb(90, 97, 105);

            lblProgression = new Label();
            lblProgression.Dock = DockStyle.Top;
            lblProgression.Height = 24;
            lblProgression.ForeColor = Color.FromArgb(120, 127, 135);

            Panel entete = new Panel();
            entete.Dock = DockStyle.Top;
            entete.Height = 104;
            entete.Padding = new Padding(28, 18, 28, 6);
            entete.Controls.Add(lblSousTitre);
            entete.Controls.Add(lblTitre);
            entete.Controls.Add(lblProgression);

            corps = new Panel();
            corps.Dock = DockStyle.Fill;
            corps.Padding = new Padding(28, 8, 28, 8);
            corps.AutoScroll = true;

            btnPrecedent = GrandBouton("← Précédent", 170);
            btnPrecedent.Click += new EventHandler(Precedent_Click);

            btnSuivant = GrandBouton("Suivant →", 220);
            btnSuivant.BackColor = Color.FromArgb(0, 90, 158);
            btnSuivant.ForeColor = Color.White;
            btnSuivant.FlatStyle = FlatStyle.Flat;
            btnSuivant.FlatAppearance.BorderSize = 0;
            btnSuivant.Click += new EventHandler(Suivant_Click);

            btnVueComplete = new Button();
            btnVueComplete.Text = "Vue complète";
            btnVueComplete.Width = AppFont.Width(btnVueComplete.Text, 34);
            btnVueComplete.Height = 34;
            btnVueComplete.Click += new EventHandler(VueComplete_Click);
            toolTipVue.SetToolTip(btnVueComplete,
                "L'écran complet, tous les réglages sur une seule page. Ce que vous avez déjà saisi y est repris.");

            FlowLayoutPanel droite = new FlowLayoutPanel();
            droite.Dock = DockStyle.Right;
            droite.FlowDirection = FlowDirection.RightToLeft;
            droite.Width = btnSuivant.Width + btnPrecedent.Width + 30;
            droite.Controls.Add(btnSuivant);
            droite.Controls.Add(btnPrecedent);

            Panel bas = new Panel();
            bas.Dock = DockStyle.Bottom;
            bas.Height = 66;
            bas.Padding = new Padding(28, 14, 28, 14);
            bas.Controls.Add(droite);
            bas.Controls.Add(btnVueComplete);

            Controls.Add(corps);
            Controls.Add(bas);
            Controls.Add(entete);
        }

        private readonly ToolTip toolTipVue = new ToolTip();

        private Button GrandBouton(string texte, int largeur)
        {
            Button b = new Button();
            b.Text = texte;
            b.Font = new Font(AppFont.Family, 11F, FontStyle.Regular);
            b.Width = Math.Max(largeur, AppFont.Width(texte, 60));
            b.Height = 40;
            b.Margin = new Padding(8, 0, 0, 0);
            return b;
        }

        // ==================================================================
        // Navigation
        // ==================================================================

        private void AllerA(int etape)
        {
            if (etape < 0) etape = 0;
            if (etape > NbEtapes - 1) etape = NbEtapes - 1;
            _etape = etape;

            corps.Controls.Clear();
            lblProgression.Text = "Étape " + (_etape + 1) + " sur " + NbEtapes;
            btnPrecedent.Visible = _etape > 0;
            btnSuivant.Text = _etape == NbEtapes - 1 ? "Générer la demande" : "Suivant →";

            switch (_etape)
            {
                case 0: EtapeType(); break;
                case 1: EtapeDestinataire(); break;
                case 2: EtapeArticles(); break;
                case 3: EtapeDetails(); break;
                default: EtapeRecapitulatif(); break;
            }
        }

        private void Precedent_Click(object sender, EventArgs e)
        {
            Recolter();
            AllerA(_etape - 1);
        }

        private void Suivant_Click(object sender, EventArgs e)
        {
            Recolter();
            if (!EtapeValide()) return;

            if (_etape == NbEtapes - 1)
            {
                _demande.Generer = true;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            AllerA(_etape + 1);
        }

        private void VueComplete_Click(object sender, EventArgs e)
        {
            Recolter();
            VueComplete = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Reprend dans la demande ce que l'étape affichée contient.</summary>
        private void Recolter()
        {
            switch (_etape)
            {
                case 1:
                    _demande.Destinataire = lstFournisseurs == null
                        ? _demande.Destinataire : lstFournisseurs.SelectedItem as Supplier;
                    break;
                case 2:
                    _demande.Lignes = new List<PartLine>();
                    foreach (PartLine l in _lignes)
                        if (!string.IsNullOrWhiteSpace(l.PartNumber)) _demande.Lignes.Add(l);
                    break;
                case 3:
                    _demande.ReferenceCommande = txtReference.Text.Trim();
                    _demande.Delai = dtpDelai.Checked ? (DateTime?)dtpDelai.Value : null;
                    _demande.CheminPo = txtPo.Text.Trim();
                    _demande.Commentaire = txtCommentaire.Text;
                    _demande.Export3D = chk3D.Checked;
                    _demande.Export2D = chk2D.Checked;
                    _demande.ControleFabrication = chkControle.Checked;
                    break;
            }
        }

        /// <summary>Ce qui manque pour passer à l'étape suivante, dit sur-le-champ.</summary>
        private bool EtapeValide()
        {
            if (_etape == 1 && _demande.Destinataire == null)
            {
                Prevenir("Choisissez un destinataire.");
                return false;
            }
            if (_etape == 2)
            {
                if (_demande.Lignes.Count == 0)
                {
                    Prevenir("Ajoutez au moins un article.");
                    return false;
                }
                string faute = ArticleDeMauvaisType();
                if (faute != null)
                {
                    Prevenir(faute);
                    return false;
                }
            }
            if (_etape == 3 && _demande.Type == RequestType.Fabrication
                && string.IsNullOrWhiteSpace(_demande.CheminPo))
            {
                Prevenir("Une demande de fabrication exige un bon de commande au format PDF.");
                return false;
            }
            return true;
        }

        /// <summary>Un article dont la nature ne correspond pas au type choisi, s'il y en a.</summary>
        private string ArticleDeMauvaisType()
        {
            bool attenduCatalogue = RequestTypes.EstCatalogue(_demande.Type);
            List<string> intrus = new List<string>();
            foreach (PartLine l in _demande.Lignes)
                if (EstCatalogue(l.PartNumber) != attenduCatalogue) intrus.Add(l.PartNumber);
            if (intrus.Count == 0) return null;

            return intrus.Count + " article(s) ne correspondent pas à une « "
                 + RequestTypes.Libelle(_demande.Type) + " » : "
                 + string.Join(", ", intrus.GetRange(0, Math.Min(4, intrus.Count)))
                 + (intrus.Count > 4 ? "…" : "");
        }

        private bool EstCatalogue(string numero)
        {
            string type = PartNumberFormat.TypeCode(numero);
            ArticleTypeRule regle;
            if (type != "" && _config.ArticleTypes != null
                && _config.ArticleTypes.TryGetValue(type, out regle)) return regle.Catalogue;
            return false;
        }

        private void Prevenir(string message)
        {
            MessageBox.Show(message, "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ==================================================================
        // Étape 1 — la nature de la demande
        // ==================================================================

        private void EtapeType()
        {
            lblTitre.Text = "Que voulez-vous faire ?";
            lblSousTitre.Text = "Le reste de l'assistant s'adapte à ce choix.";
            btnSuivant.Visible = false;

            FlowLayoutPanel cartes = new FlowLayoutPanel();
            cartes.Dock = DockStyle.Fill;
            cartes.FlowDirection = FlowDirection.TopDown;
            cartes.WrapContents = false;

            foreach (RequestType t in new RequestType[] {
                         RequestType.Offre, RequestType.Fabrication, RequestType.CommandeCatalogue })
            {
                cartes.Controls.Add(Carte(t));
            }
            corps.Controls.Add(cartes);
        }

        /// <summary>Un grand bouton par nature de demande, avec ce qu'elle implique.</summary>
        private Panel Carte(RequestType type)
        {
            Button b = new Button();
            b.Tag = type;
            b.Width = 820;
            b.Height = 92;
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(170, 176, 183);
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = Color.White;
            b.Text = "";
            b.Click += new EventHandler(Carte_Click);

            Label titre = new Label();
            titre.Text = RequestTypes.Libelle(type);
            titre.Font = new Font(AppFont.Family, 13F, FontStyle.Bold);
            titre.Location = new Point(22, 16);
            titre.AutoSize = true;
            titre.Click += new EventHandler(delegate (object s, EventArgs e) { Choisir(type); });

            Label detail = new Label();
            detail.Text = RequestTypes.Description(type);
            detail.ForeColor = Color.FromArgb(90, 97, 105);
            detail.Location = new Point(24, 46);
            detail.Size = new Size(760, 36);
            detail.Click += new EventHandler(delegate (object s, EventArgs e) { Choisir(type); });

            b.Controls.Add(titre);
            b.Controls.Add(detail);

            Panel p = new Panel();
            p.Width = 830;
            p.Height = 104;
            p.Controls.Add(b);
            return p;
        }

        private void Carte_Click(object sender, EventArgs e)
        {
            Button b = sender as Button;
            if (b == null || !(b.Tag is RequestType)) return;
            Choisir((RequestType)b.Tag);
        }

        private void Choisir(RequestType type)
        {
            _demande.Type = type;
            btnSuivant.Visible = true;
            AllerA(1);
        }

        // ==================================================================
        // Étape 2 — le destinataire
        // ==================================================================

        private void EtapeDestinataire()
        {
            lblTitre.Text = "À qui l'envoyez-vous ?";
            lblSousTitre.Text = RequestTypes.EstCatalogue(_demande.Type)
                ? "Seuls les articles vendus par ce fournisseur pourront être commandés."
                : "Le destinataire du message.";
            btnSuivant.Visible = true;

            // L'étiquette d'abord : choisir une ligne déclenche aussitôt son rafraîchissement.
            lblLien = new Label();
            lblLien.Dock = DockStyle.Bottom;
            lblLien.Height = 34;
            lblLien.ForeColor = Color.FromArgb(90, 97, 105);

            lstFournisseurs = new ListBox();
            lstFournisseurs.Dock = DockStyle.Fill;
            lstFournisseurs.Font = new Font(AppFont.Family, 12F);
            lstFournisseurs.ItemHeight = 30;
            lstFournisseurs.IntegralHeight = false;
            foreach (Supplier f in _fournisseurs) lstFournisseurs.Items.Add(f);
            lstFournisseurs.SelectedIndexChanged += new EventHandler(Fournisseur_Change);

            if (_demande.Destinataire != null)
                lstFournisseurs.SelectedItem = _demande.Destinataire;
            else if (lstFournisseurs.Items.Count > 0)
                lstFournisseurs.SelectedIndex = 0;

            Button btnGerer = GrandBouton("Gérer les fournisseurs…", 260);
            btnGerer.Dock = DockStyle.Bottom;
            btnGerer.Click += new EventHandler(Gerer_Click);

            corps.Controls.Add(lstFournisseurs);
            corps.Controls.Add(lblLien);
            corps.Controls.Add(btnGerer);
            Fournisseur_Change(null, null);
        }

        private void Fournisseur_Change(object sender, EventArgs e)
        {
            if (lblLien == null || lstFournisseurs == null) return;
            Supplier f = lstFournisseurs.SelectedItem as Supplier;
            if (f == null) { lblLien.Text = ""; return; }

            string adresses = f.ToLine == "" ? "aucune adresse" : f.ToLine;
            string lien = f.InventoryId != 0
                ? "lié à l'inventaire (fiche n° " + f.InventoryId + ")"
                : "non lié à l'inventaire — le rapprochement se fera sur le nom";
            lblLien.Text = adresses + "   —   " + lien;
        }

        private void Gerer_Click(object sender, EventArgs e)
        {
            using (SupplierDialog dlg = new SupplierDialog(_config, _fournisseurs))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _fournisseurs.Clear();
                _fournisseurs.AddRange(dlg.Suppliers);
            }
            AllerA(1);
        }

        // ==================================================================
        // Étape 3 — les articles
        // ==================================================================

        private void EtapeArticles()
        {
            lblTitre.Text = "Quels articles ?";
            lblSousTitre.Text = RequestTypes.EstCatalogue(_demande.Type)
                ? "Articles de catalogue, achetés sur leur référence chez le fournisseur."
                : "Pièces sur mesure, dont les plans et modèles seront joints.";

            if (_lignes.Count == 0)
            {
                foreach (PartLine l in _demande.Lignes) _lignes.Add(l);
                if (_lignes.Count == 0) _lignes.Add(new PartLine());
            }

            grille = new DataGridView();
            grille.Dock = DockStyle.Fill;
            grille.Font = new Font(AppFont.Family, 11F);
            grille.AutoGenerateColumns = false;
            grille.AllowUserToAddRows = true;
            grille.RowTemplate.Height = 34;
            grille.ColumnHeadersHeight = 38;
            grille.BackgroundColor = Color.White;
            grille.BorderStyle = BorderStyle.None;
            grille.EditMode = DataGridViewEditMode.EditOnEnter;
            grille.SelectionMode = DataGridViewSelectionMode.CellSelect;

            Colonne("N° article", "PartNumber", 34);
            Colonne("Qté 1", "Qty1", 12);
            if (RequestTypes.PlusieursQuantites(_demande.Type))
            {
                Colonne("Qté 2", "Qty2", 12);
                Colonne("Qté 3", "Qty3", 12);
            }
            Colonne("Remarque", "Remark", 34);

            grille.DataSource = _lignes;
            grille.DataError += new DataGridViewDataErrorEventHandler(Grille_Erreur);
            _lignes.ListChanged += new ListChangedEventHandler(Lignes_Change);

            Button btnRecherche = GrandBouton("Rechercher un article…", 260);
            btnRecherche.Click += new EventHandler(Recherche_Click);

            Button btnColler = GrandBouton("Coller depuis Excel", 220);
            btnColler.Click += new EventHandler(Coller_Click);

            Button btnVider = GrandBouton("Tout vider", 150);
            btnVider.Click += new EventHandler(Vider_Click);

            FlowLayoutPanel outils = new FlowLayoutPanel();
            outils.Dock = DockStyle.Top;
            outils.Height = 52;
            outils.Controls.Add(btnRecherche);
            outils.Controls.Add(btnColler);
            outils.Controls.Add(btnVider);

            lblCompteArticles = new Label();
            lblCompteArticles.Dock = DockStyle.Bottom;
            lblCompteArticles.Height = 28;
            lblCompteArticles.ForeColor = Color.FromArgb(90, 97, 105);

            corps.Controls.Add(grille);
            corps.Controls.Add(lblCompteArticles);
            corps.Controls.Add(outils);
            Lignes_Change(null, null);
        }

        private void Colonne(string entete, string propriete, int poids)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = entete;
            c.DataPropertyName = propriete;
            c.FillWeight = poids;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grille.Columns.Add(c);
        }

        private void Grille_Erreur(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void Lignes_Change(object sender, ListChangedEventArgs e)
        {
            int n = 0;
            foreach (PartLine l in _lignes) if (!string.IsNullOrWhiteSpace(l.PartNumber)) n++;
            lblCompteArticles.Text = n + " article(s) dans la demande.";
        }

        private void Recherche_Click(object sender, EventArgs e)
        {
            using (RechercheArticleDialog dlg = new RechercheArticleDialog(
                       _config, _demande.Destinataire, _inventaire, _pdm))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (string numero in dlg.Retenus) AjouterLigne(numero);
            }
        }

        private void Coller_Click(object sender, EventArgs e)
        {
            int avant = _lignes.Count;
            int n = ClipboardImporter.ImportFromClipboard(_lignes);
            if (n == 0 && _lignes.Count == avant)
            {
                Prevenir("Le presse-papiers ne contient aucun numéro d'article reconnaissable.");
                return;
            }
            grille.Refresh();
            Lignes_Change(null, null);
        }

        private void Vider_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Vider la liste des articles ?", "AskThem",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _lignes.Clear();
            _lignes.Add(new PartLine());
        }

        private void AjouterLigne(string numero)
        {
            AjouterLigne(numero, 1, "");
        }

        private void AjouterLigne(string numero, int quantite, string remarque)
        {
            if (string.IsNullOrWhiteSpace(numero)) return;
            foreach (PartLine l in _lignes)
                if (string.Equals(l.PartNumber, numero, StringComparison.OrdinalIgnoreCase)) return;

            foreach (PartLine l in _lignes)
            {
                if (string.IsNullOrWhiteSpace(l.PartNumber))
                {
                    l.PartNumber = numero;
                    l.Qty1 = quantite > 0 ? quantite : 1;
                    l.Remark = remarque;
                    grille.Refresh();
                    Lignes_Change(null, null);
                    return;
                }
            }
            PartLine nouvelle = new PartLine();
            nouvelle.PartNumber = numero;
            nouvelle.Qty1 = quantite > 0 ? quantite : 1;
            nouvelle.Remark = remarque;
            _lignes.Add(nouvelle);
        }

        // ==================================================================
        // Étape 4 — les détails
        // ==================================================================

        private void EtapeDetails()
        {
            lblTitre.Text = "Détails de la demande";
            lblSousTitre.Text = "Tout est facultatif, sauf le bon de commande en fabrication.";

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Top;
            t.ColumnCount = 2;
            t.AutoSize = true;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppFont.Width("Commentaire général", 30)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            txtReference = new TextBox();
            txtReference.Dock = DockStyle.Fill;
            txtReference.Font = new Font(AppFont.Family, 11F);
            txtReference.Text = _demande.ReferenceCommande;

            dtpDelai = new DateTimePicker();
            dtpDelai.Font = new Font(AppFont.Family, 11F);
            dtpDelai.Format = DateTimePickerFormat.Short;
            dtpDelai.ShowCheckBox = true;
            dtpDelai.Checked = _demande.Delai.HasValue;
            if (_demande.Delai.HasValue) dtpDelai.Value = _demande.Delai.Value;
            dtpDelai.Width = 200;

            txtPo = new TextBox();
            txtPo.Dock = DockStyle.Fill;
            txtPo.ReadOnly = true;
            txtPo.Font = new Font(AppFont.Family, 11F);
            txtPo.Text = _demande.CheminPo;
            txtPo.PlaceholderText = "aucun fichier choisi";

            Button btnParcourir = new Button();
            btnParcourir.Text = "Parcourir…";
            btnParcourir.Width = AppFont.Width(btnParcourir.Text, 30);
            btnParcourir.Height = 30;
            btnParcourir.Dock = DockStyle.Right;
            btnParcourir.Click += new EventHandler(Parcourir_Click);

            Panel lignePo = new Panel();
            lignePo.Dock = DockStyle.Fill;
            lignePo.Height = 32;
            lignePo.Controls.Add(txtPo);
            lignePo.Controls.Add(btnParcourir);

            txtCommentaire = new TextBox();
            txtCommentaire.Dock = DockStyle.Fill;
            txtCommentaire.Multiline = true;
            txtCommentaire.ScrollBars = ScrollBars.Vertical;
            txtCommentaire.Font = new Font(AppFont.Family, 11F);
            txtCommentaire.Text = _demande.Commentaire;
            txtCommentaire.PlaceholderText = "Délais de paiement, incoterms, exigences qualité, emballage…";

            Ligne(t, "Référence commande", txtReference, 34);
            Ligne(t, "Délai souhaité", dtpDelai, 34);
            Ligne(t, _demande.Type == RequestType.Offre ? "Demande de PO" : "Bon de commande", lignePo, 36);
            Ligne(t, "Commentaire général", txtCommentaire, 92);

            corps.Controls.Add(ConstruireVoletAvance());
            corps.Controls.Add(t);
        }

        private void Ligne(TableLayoutPanel t, string intitule, Control champ, int hauteur)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, hauteur));

            Label l = new Label();
            l.Text = intitule;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;

            t.Controls.Add(l, 0, r);
            t.Controls.Add(champ, 1, r);
        }

        /// <summary>
        /// Ce qu'on ne règle presque jamais, replié par défaut : c'est précisément ce qui
        /// encombrait l'écran unique.
        /// </summary>
        private Control ConstruireVoletAvance()
        {
            chk3D = new CheckBox();
            chk3D.Text = "Exporter le modèle 3D (STEP AP203)";
            chk3D.AutoSize = true;
            chk3D.Location = new Point(24, 8);
            chk3D.Checked = _demande.Export3D;

            chk2D = new CheckBox();
            chk2D.Text = "Exporter le plan (PDF + DXF)";
            chk2D.AutoSize = true;
            chk2D.Location = new Point(24, 36);
            chk2D.Checked = _demande.Export2D;

            chkControle = new CheckBox();
            chkControle.Text = "Générer le contrôle de fabrication (PDF) — bêta";
            chkControle.AutoSize = true;
            chkControle.Location = new Point(24, 64);
            chkControle.Checked = _demande.ControleFabrication;

            bool catalogue = RequestTypes.EstCatalogue(_demande.Type);
            chk3D.Enabled = !catalogue;
            chk2D.Enabled = !catalogue;
            chkControle.Enabled = !catalogue;
            if (catalogue) { chk3D.Checked = false; chk2D.Checked = false; chkControle.Checked = false; }

            voletAvance = new Panel();
            voletAvance.Dock = DockStyle.Bottom;
            voletAvance.Height = 0;
            voletAvance.Visible = false;
            voletAvance.Controls.Add(chk3D);
            voletAvance.Controls.Add(chk2D);
            voletAvance.Controls.Add(chkControle);

            CheckBox bascule = new CheckBox();
            bascule.Appearance = Appearance.Button;
            bascule.TextAlign = ContentAlignment.MiddleCenter;
            bascule.Text = catalogue ? "Options avancées (sans objet pour un achat catalogue)" : "Options avancées";
            bascule.Width = AppFont.Width(bascule.Text, 40);
            bascule.Height = 32;
            bascule.Dock = DockStyle.Bottom;
            bascule.CheckedChanged += new EventHandler(delegate (object s, EventArgs e)
            {
                voletAvance.Visible = bascule.Checked;
                voletAvance.Height = bascule.Checked ? 100 : 0;
            });

            Panel hote = new Panel();
            hote.Dock = DockStyle.Bottom;
            hote.Height = 140;
            hote.Controls.Add(voletAvance);
            hote.Controls.Add(bascule);
            return hote;
        }

        private void Parcourir_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Document PDF (*.pdf)|*.pdf";
                dlg.Title = _demande.Type == RequestType.Offre
                    ? "Choisir la demande de PO" : "Choisir le bon de commande";
                if (dlg.ShowDialog(this) == DialogResult.OK) txtPo.Text = dlg.FileName;
            }
        }

        // ==================================================================
        // Étape 5 — le récapitulatif
        // ==================================================================

        private void EtapeRecapitulatif()
        {
            lblTitre.Text = "Vérifiez avant d'envoyer";
            lblSousTitre.Text = "Rien ne part sans votre accord : le message s'ouvrira dans Outlook.";

            lblRecap = new Label();
            lblRecap.Dock = DockStyle.Fill;
            lblRecap.Font = new Font(AppFont.Family, 11F);
            lblRecap.Text = Recapitulatif();

            corps.Controls.Add(lblRecap);
        }

        private string Recapitulatif()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(RequestTypes.Libelle(_demande.Type));
            sb.AppendLine();
            sb.AppendLine("Destinataire   : " + (_demande.Destinataire == null
                ? "aucun" : _demande.Destinataire.Name + "   " + _demande.Destinataire.ToLine));
            sb.AppendLine("Articles       : " + _demande.Lignes.Count);
            sb.AppendLine("Référence      : " + (_demande.ReferenceCommande == "" ? "—" : _demande.ReferenceCommande));
            sb.AppendLine("Délai souhaité : " + (_demande.Delai.HasValue
                ? _demande.Delai.Value.ToString("dd.MM.yyyy") : "non précisé"));
            string intitulePo = _demande.Type == RequestType.Offre ? "Demande de PO  : " : "Bon de commande: ";
            sb.AppendLine(intitulePo
                + (_demande.CheminPo == "" ? "aucun" : Path.GetFileName(_demande.CheminPo)));

            if (!RequestTypes.EstCatalogue(_demande.Type))
            {
                sb.AppendLine();
                sb.AppendLine("Fichiers joints: "
                    + (_demande.Export3D ? "3D " : "") + (_demande.Export2D ? "plan " : "")
                    + (_demande.ControleFabrication ? "+ contrôle de fabrication" : ""));
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("Aucun fichier n'accompagne une commande de catalogue.");
            }

            sb.AppendLine();
            int max = 12;
            int i = 0;
            foreach (PartLine l in _demande.Lignes)
            {
                if (i++ >= max) { sb.AppendLine("   … et " + (_demande.Lignes.Count - max) + " autre(s)"); break; }
                sb.AppendLine("   " + l.PartNumber + "   ×" + l.Qty1
                    + (string.IsNullOrWhiteSpace(l.Remark) ? "" : "   " + l.Remark));
            }
            return sb.ToString();
        }
    }
}
