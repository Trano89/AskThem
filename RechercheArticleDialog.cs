using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Recherche un article, qu'il soit à fabriquer ou au catalogue.
    ///
    /// Deux sources qui ne se recouvrent pas : le coffre connaît les pièces dessinées, sans
    /// désignation ; l'inventaire connaît désignations, fournisseurs et références, y compris
    /// pour des articles qui n'ont aucun fichier. Les réunir évite d'avoir à savoir d'avance
    /// dans lequel des deux chercher.
    /// </summary>
    public class RechercheArticleDialog : Form
    {
        private DataGridView grille;
        private TextBox txtFiltre;
        private ComboBox cboType;
        private CheckBox chkFournisseur;
        private CheckBox chkAvecPlan;
        private Label lblCompte;

        private readonly AppConfig _config;
        private readonly Supplier _destinataire;
        private readonly List<Article> _tous = new List<Article>();
        private int _affiches;

        /// <summary>Numéros d'article retenus.</summary>
        public List<string> Retenus { get; private set; }

        public RechercheArticleDialog(AppConfig config, Supplier destinataire,
                                      Dictionary<string, InventoryService.Entry> inventaire,
                                      Dictionary<string, string> indexPdm)
        {
            _config = config != null ? config : new AppConfig();
            _destinataire = destinataire;
            Retenus = new List<string>();

            Text = "Rechercher un article";
            Font = AppFont.Get();
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1100, 580);
            MinimumSize = new Size(760, 420);

            Rassembler(inventaire, indexPdm);
            Construire();
            Filtrer();
        }

        // ------------------------------------------------------------------
        // Ce qu'on sait d'un article, des deux sources réunies
        // ------------------------------------------------------------------

        private sealed class Article
        {
            public string Numero = "";
            public string TypeCode = "";
            public string TypeLabel = "";
            public string Designation = "";
            public string Reference = "";
            public string ReferenceFabricant = "";
            public string AncienneRef = "";
            public string Fournisseurs = "";
            public string Coffre = "";
            public string Prix = "";
            public string Stock = "";

            public bool VenduParDestinataire;
            public bool DansLeCoffre;
            public bool ADesPlans;
            public bool Retenu;

            public string Cherchable = "";

            public void PreparerRecherche()
            {
                Cherchable = (Numero + " " + Designation + " " + AncienneRef + " "
                            + Reference + " " + ReferenceFabricant + " " + Fournisseurs).ToLowerInvariant();
            }
        }

        private void Rassembler(Dictionary<string, InventoryService.Entry> inventaire,
                                Dictionary<string, string> indexPdm)
        {
            Dictionary<string, Article> par =
                new Dictionary<string, Article>(StringComparer.OrdinalIgnoreCase);

            if (inventaire != null)
            {
                foreach (KeyValuePair<string, InventoryService.Entry> kv in inventaire)
                {
                    InventoryService.Entry e = kv.Value;
                    if (string.IsNullOrWhiteSpace(e.InternalRef)) continue;

                    Article a = Obtenir(par, e.InternalRef);
                    a.Designation = e.Designation;
                    a.AncienneRef = e.OldRef;
                    if (e.PrixUnitaire > 0)
                        a.Prix = e.PrixUnitaire.ToString("0.00", CultureInfo.InvariantCulture)
                               + (e.Monnaie == "" ? "" : " " + e.Monnaie);
                    if (e.Stock != 0) a.Stock = e.Stock.ToString("0.##", CultureInfo.InvariantCulture);

                    List<string> noms = new List<string>();
                    foreach (InventoryService.Fournisseur f in e.Fournisseurs)
                        if (f.Nom != "") noms.Add(f.Nom);
                    a.Fournisseurs = string.Join(", ", noms);

                    InventoryService.Fournisseur chez = _destinataire == null
                        ? null : e.Chez(_destinataire.InventoryId, _destinataire.Name);
                    if (chez != null)
                    {
                        a.VenduParDestinataire = true;
                        a.Reference = chez.Reference;
                        a.ReferenceFabricant = chez.ReferenceFabricant;
                    }
                    else if (e.Fournisseurs.Count > 0)
                    {
                        a.Reference = e.Fournisseurs[0].Reference;
                        a.ReferenceFabricant = e.Fournisseurs[0].ReferenceFabricant;
                    }
                }
            }

            // Le coffre : des numéros et les fichiers qui existent, rien de plus.
            if (indexPdm != null)
            {
                Dictionary<string, bool[]> fichiers =
                    new Dictionary<string, bool[]>(StringComparer.OrdinalIgnoreCase);

                foreach (string cle in indexPdm.Keys)
                {
                    string numero = Path.GetFileNameWithoutExtension(cle);
                    string ext = Path.GetExtension(cle).ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(numero)) continue;

                    bool[] presents;
                    if (!fichiers.TryGetValue(numero, out presents))
                    {
                        presents = new bool[2];
                        fichiers[numero] = presents;
                    }
                    if (ext == ".SLDPRT" || ext == ".SLDASM") presents[0] = true;
                    else if (ext == ".SLDDRW") presents[1] = true;
                }

                foreach (KeyValuePair<string, bool[]> kv in fichiers)
                {
                    Article a = Obtenir(par, kv.Key);
                    a.DansLeCoffre = true;
                    a.ADesPlans = kv.Value[1];
                    a.Coffre = (kv.Value[0] ? "3D" : "") + (kv.Value[0] && kv.Value[1] ? " + " : "")
                             + (kv.Value[1] ? "2D" : "");
                }
            }

            foreach (Article a in par.Values)
            {
                a.TypeCode = PartNumberFormat.TypeCode(a.Numero);
                a.TypeLabel = ValidationArticle.RegleDe(_config, a.Numero).Label;

                a.PreparerRecherche();
                _tous.Add(a);
            }

            _tous.Sort(delegate (Article x, Article y)
            {
                return string.Compare(x.Numero, y.Numero, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static Article Obtenir(Dictionary<string, Article> par, string numero)
        {
            string cle = numero.Trim();
            Article a;
            if (par.TryGetValue(cle, out a)) return a;
            a = new Article();
            a.Numero = cle;
            par[cle] = a;
            return a;
        }

        // ------------------------------------------------------------------
        // Interface
        // ------------------------------------------------------------------

        private void Construire()
        {
            txtFiltre = new TextBox();
            txtFiltre.Width = 300;
            txtFiltre.PlaceholderText = "numéro, désignation, référence, fournisseur…";
            txtFiltre.TextChanged += new EventHandler(Filtre_Change);

            cboType = new ComboBox();
            cboType.DropDownStyle = ComboBoxStyle.DropDownList;
            cboType.Width = 220;
            cboType.Items.Add(new ChoixType("Tous les types", null));
            if (_config.ArticleTypes != null)
            {
                List<string> codes = new List<string>(_config.ArticleTypes.Keys);
                codes.Sort();
                foreach (string code in codes)
                    cboType.Items.Add(new ChoixType(_config.ArticleTypes[code].Label + "  (" + code + ")", code));
            }
            cboType.SelectedIndex = 0;
            cboType.SelectedIndexChanged += new EventHandler(Filtre_Change);

            chkFournisseur = new CheckBox();
            chkFournisseur.AutoSize = true;
            bool aDestinataire = _destinataire != null && !string.IsNullOrWhiteSpace(_destinataire.Name);
            chkFournisseur.Text = aDestinataire
                ? "Vendus par « " + _destinataire.Name + " »"
                : "Vendus par le destinataire";
            chkFournisseur.Enabled = aDestinataire;
            chkFournisseur.CheckedChanged += new EventHandler(Filtre_Change);

            chkAvecPlan = new CheckBox();
            chkAvecPlan.AutoSize = true;
            chkAvecPlan.Text = "Avec un plan dans le coffre";
            chkAvecPlan.CheckedChanged += new EventHandler(Filtre_Change);

            FlowLayoutPanel filtres = new FlowLayoutPanel();
            filtres.Dock = DockStyle.Top;
            filtres.Height = 76;
            filtres.Padding = new Padding(12, 10, 12, 6);
            filtres.WrapContents = true;
            filtres.Controls.Add(txtFiltre);
            filtres.Controls.Add(cboType);
            filtres.Controls.Add(Espace(chkFournisseur));
            filtres.Controls.Add(Espace(chkAvecPlan));

            grille = new DataGridView();
            grille.Dock = DockStyle.Fill;
            grille.AllowUserToAddRows = false;
            grille.AllowUserToDeleteRows = false;
            grille.AutoGenerateColumns = false;
            grille.RowHeadersVisible = false;
            grille.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grille.BackgroundColor = Color.White;
            grille.BorderStyle = BorderStyle.None;
            grille.EditMode = DataGridViewEditMode.EditOnEnter;
            grille.CellValueChanged += new DataGridViewCellEventHandler(Cellule_Changee);
            grille.CurrentCellDirtyStateChanged += new EventHandler(Coche_Modifiee);
            grille.CellDoubleClick += new DataGridViewCellEventHandler(Ligne_DoubleCliquee);

            DataGridViewCheckBoxColumn coche = new DataGridViewCheckBoxColumn();
            coche.Name = "colRetenu";
            coche.HeaderText = "";
            coche.Width = 34;
            coche.FillWeight = 4;
            grille.Columns.Add(coche);

            Colonne("N° article", 18);
            Colonne("Type", 20);
            Colonne("Désignation", 34);
            Colonne("Votre référence", 22);
            Colonne("Réf. fabricant", 18);
            Colonne("Ancienne réf.", 16);
            Colonne("Fournisseur(s)", 26);
            Colonne("Coffre", 10);
            Colonne("Prix", 12);
            Colonne("Stock", 8);

            lblCompte = new Label();
            lblCompte.AutoSize = false;
            lblCompte.Dock = DockStyle.Left;
            lblCompte.Width = 460;
            lblCompte.TextAlign = ContentAlignment.MiddleLeft;

            Button btnAjouter = new Button();
            btnAjouter.Text = "Ajouter à la demande";
            btnAjouter.Width = AppFont.Width(btnAjouter.Text, 40);
            btnAjouter.Height = 30;
            btnAjouter.Click += new EventHandler(Ajouter_Click);

            Button btnFermer = new Button();
            btnFermer.Text = "Fermer";
            btnFermer.Width = AppFont.Width(btnFermer.Text, 40);
            btnFermer.Height = 30;
            btnFermer.DialogResult = DialogResult.Cancel;

            FlowLayoutPanel boutons = new FlowLayoutPanel();
            boutons.Dock = DockStyle.Right;
            boutons.FlowDirection = FlowDirection.RightToLeft;
            boutons.Width = btnAjouter.Width + btnFermer.Width + 30;
            boutons.Controls.Add(btnAjouter);
            boutons.Controls.Add(btnFermer);

            Panel bas = new Panel();
            bas.Dock = DockStyle.Bottom;
            bas.Height = 48;
            bas.Padding = new Padding(12, 8, 12, 8);
            bas.Controls.Add(boutons);
            bas.Controls.Add(lblCompte);

            Controls.Add(grille);
            Controls.Add(bas);
            Controls.Add(filtres);

            AcceptButton = btnAjouter;
            CancelButton = btnFermer;
        }

        /// <summary>Une case à cocher alignée sur la hauteur des autres filtres.</summary>
        private static Panel Espace(Control c)
        {
            Panel p = new Panel();
            p.Width = c.PreferredSize.Width + 24;
            p.Height = 26;
            c.Location = new Point(16, 4);
            p.Controls.Add(c);
            return p;
        }

        private void Colonne(string entete, int poids)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = entete;
            c.ReadOnly = true;
            c.FillWeight = poids;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grille.Columns.Add(c);
        }

        /// <summary>Une entrée de la liste des types.</summary>
        private sealed class ChoixType
        {
            public readonly string Code;
            private readonly string _libelle;

            public ChoixType(string libelle, string code)
            {
                _libelle = libelle;
                Code = code;
            }

            public override string ToString() { return _libelle; }
        }

        // ------------------------------------------------------------------
        // Filtrage
        // ------------------------------------------------------------------

        private void Filtre_Change(object sender, EventArgs e)
        {
            Filtrer();
        }

        private void Filtrer()
        {
            string q = txtFiltre == null ? "" : txtFiltre.Text.Trim().ToLowerInvariant();
            ChoixType type = cboType == null ? null : cboType.SelectedItem as ChoixType;
            string code = type == null ? null : type.Code;
            bool seulementFournisseur = chkFournisseur != null && chkFournisseur.Checked;
            bool seulementAvecPlan = chkAvecPlan != null && chkAvecPlan.Checked;

            grille.SuspendLayout();
            try
            {
                grille.Rows.Clear();
                _affiches = 0;
                foreach (Article a in _tous)
                {
                    if (code != null && a.TypeCode != code) continue;
                    if (seulementFournisseur && !a.VenduParDestinataire) continue;
                    if (seulementAvecPlan && !a.ADesPlans) continue;
                    if (q != "" && !a.Cherchable.Contains(q)) continue;

                    int i = grille.Rows.Add(a.Retenu, a.Numero, a.TypeLabel, a.Designation,
                                            a.Reference, a.ReferenceFabricant, a.AncienneRef,
                                            a.Fournisseurs, a.Coffre, a.Prix, a.Stock);
                    grille.Rows[i].Tag = a;
                    _affiches++;
                }
            }
            finally
            {
                grille.ResumeLayout();
            }
            MettreAJourCompte();
        }

        private void MettreAJourCompte()
        {
            int retenus = 0;
            foreach (Article a in _tous) if (a.Retenu) retenus++;
            lblCompte.Text = _affiches + " article(s) affiché(s) sur " + _tous.Count
                + (retenus > 0 ? "   —   " + retenus + " retenu(s)" : "");
        }

        /// <summary>La case se valide au clic, sans attendre que la cellule perde le focus.</summary>
        private void Coche_Modifiee(object sender, EventArgs e)
        {
            if (grille.IsCurrentCellDirty && grille.CurrentCell is DataGridViewCheckBoxCell)
                grille.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void Cellule_Changee(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            Article a = grille.Rows[e.RowIndex].Tag as Article;
            if (a == null) return;
            object v = grille.Rows[e.RowIndex].Cells[0].Value;
            a.Retenu = v != null && (bool)v;
            MettreAJourCompte();
        }

        /// <summary>Un double-clic sur la ligne coche ou décoche, sans viser la case.</summary>
        private void Ligne_DoubleCliquee(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            Article a = grille.Rows[e.RowIndex].Tag as Article;
            if (a == null) return;
            a.Retenu = !a.Retenu;
            grille.Rows[e.RowIndex].Cells[0].Value = a.Retenu;
            MettreAJourCompte();
        }

        private void Ajouter_Click(object sender, EventArgs e)
        {
            grille.EndEdit();
            Retenus.Clear();
            foreach (Article a in _tous)
                if (a.Retenu && a.Numero != "") Retenus.Add(a.Numero);

            if (Retenus.Count == 0)
            {
                MessageBox.Show("Aucun article coché.", "AskThem",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
