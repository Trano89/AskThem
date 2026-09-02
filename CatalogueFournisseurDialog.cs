using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Ce qu'un fournisseur vend, tel que l'inventaire le déclare.
    ///
    /// Le pendant du refus à la saisie : au lieu de taper des numéros et de se voir opposer
    /// un refus, on part de ce que le fournisseur propose et on choisit dedans.
    /// </summary>
    public class CatalogueFournisseurDialog : Form
    {
        private DataGridView grille;
        private TextBox txtFiltre;
        private Label lblCompte;

        private readonly List<Article> _tous = new List<Article>();
        private readonly BindingSourceLeger _affiches = new BindingSourceLeger();

        /// <summary>Numéros d'article retenus, dans l'ordre d'affichage.</summary>
        public List<string> Retenus { get; private set; }

        public CatalogueFournisseurDialog(Supplier fournisseur,
                                          Dictionary<string, InventoryService.Entry> inventaire)
        {
            Retenus = new List<string>();

            Text = "Articles vendus par « " + (fournisseur == null ? "" : fournisseur.Name) + " »";
            Font = AppFont.Get();
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1020, 560);
            MinimumSize = new Size(700, 400);

            Rassembler(fournisseur, inventaire);
            Construire();
            Filtrer();
        }

        // ------------------------------------------------------------------

        /// <summary>Un article du catalogue de ce fournisseur.</summary>
        private sealed class Article
        {
            public string Numero = "";
            public string AncienneRef = "";
            public string Designation = "";
            public string Reference = "";
            public string ReferenceFabricant = "";
            public string Prix = "";
            public string Stock = "";
            public bool Retenu = false;

            public string Cherchable
            {
                get { return (Numero + " " + AncienneRef + " " + Designation + " "
                            + Reference + " " + ReferenceFabricant).ToLowerInvariant(); }
            }
        }

        /// <summary>Liste affichée, triée par numéro d'article.</summary>
        private sealed class BindingSourceLeger : List<Article> { }

        private void Rassembler(Supplier fournisseur, Dictionary<string, InventoryService.Entry> inventaire)
        {
            if (fournisseur == null || inventaire == null) return;

            foreach (KeyValuePair<string, InventoryService.Entry> kv in inventaire)
            {
                InventoryService.Entry e = kv.Value;
                InventoryService.Fournisseur chez = e.Chez(fournisseur.InventoryId, fournisseur.Name);
                if (chez == null) continue;

                Article a = new Article();
                a.Numero = e.InternalRef;
                a.AncienneRef = e.OldRef;
                a.Designation = e.Designation;
                a.Reference = chez.Reference;
                a.ReferenceFabricant = chez.ReferenceFabricant;
                if (e.PrixUnitaire > 0)
                    a.Prix = e.PrixUnitaire.ToString("0.00", CultureInfo.InvariantCulture)
                           + (e.Monnaie == "" ? "" : " " + e.Monnaie);
                if (e.Stock != 0) a.Stock = e.Stock.ToString("0.##", CultureInfo.InvariantCulture);
                _tous.Add(a);
            }

            _tous.Sort(delegate (Article x, Article y)
            {
                return string.Compare(x.Numero, y.Numero, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void Construire()
        {
            Label aide = new Label();
            aide.Dock = DockStyle.Top;
            aide.Height = 40;
            aide.Padding = new Padding(12, 10, 12, 4);
            aide.Text = "Cochez les articles à ajouter à la demande. Les quantités se saisissent ensuite dans la grille.";

            txtFiltre = new TextBox();
            txtFiltre.Dock = DockStyle.Top;
            txtFiltre.PlaceholderText = "filtrer par numéro, désignation ou référence…";
            txtFiltre.TextChanged += new EventHandler(Filtre_Change);

            Panel haut = new Panel();
            haut.Dock = DockStyle.Top;
            haut.Height = 76;
            haut.Padding = new Padding(12, 0, 12, 8);
            haut.Controls.Add(txtFiltre);
            haut.Controls.Add(aide);

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
            grille.CellContentClick += new DataGridViewCellEventHandler(Cellule_Cliquee);
            grille.CellValueChanged += new DataGridViewCellEventHandler(Cellule_Changee);
            grille.CurrentCellDirtyStateChanged += new EventHandler(Coche_Modifiee);

            DataGridViewCheckBoxColumn coche = new DataGridViewCheckBoxColumn();
            coche.Name = "colRetenu";
            coche.HeaderText = "";
            coche.Width = 34;
            grille.Columns.Add(coche);

            Colonne("N° article", "Numero", 22, true);
            Colonne("Désignation", "Designation", 38, true);
            Colonne("Votre référence", "Reference", 24, true);
            Colonne("Réf. fabricant", "ReferenceFabricant", 20, true);
            Colonne("Ancienne réf.", "AncienneRef", 18, true);
            Colonne("Prix unitaire", "Prix", 16, true);
            Colonne("Stock", "Stock", 10, true);

            lblCompte = new Label();
            lblCompte.AutoSize = false;
            lblCompte.Dock = DockStyle.Left;
            lblCompte.Width = 420;
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
            Controls.Add(haut);

            AcceptButton = btnAjouter;
            CancelButton = btnFermer;
        }

        private void Colonne(string entete, string propriete, int poids, bool lectureSeule)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = "col" + propriete;
            c.HeaderText = entete;
            c.DataPropertyName = propriete;
            c.ReadOnly = lectureSeule;
            c.FillWeight = poids;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grille.Columns.Add(c);
        }

        // ------------------------------------------------------------------

        private void Filtre_Change(object sender, EventArgs e)
        {
            Filtrer();
        }

        private void Filtrer()
        {
            string q = txtFiltre == null ? "" : txtFiltre.Text.Trim().ToLowerInvariant();

            _affiches.Clear();
            foreach (Article a in _tous)
                if (q == "" || a.Cherchable.Contains(q)) _affiches.Add(a);

            grille.SuspendLayout();
            try
            {
                grille.Rows.Clear();
                foreach (Article a in _affiches)
                {
                    int i = grille.Rows.Add(a.Retenu, a.Numero, a.Designation, a.Reference,
                                            a.ReferenceFabricant, a.AncienneRef, a.Prix, a.Stock);
                    grille.Rows[i].Tag = a;
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
            lblCompte.Text = _tous.Count + " article(s) chez ce fournisseur"
                + (_affiches.Count != _tous.Count ? ", " + _affiches.Count + " affiché(s)" : "")
                + (retenus > 0 ? "   —   " + retenus + " retenu(s)" : "");
        }

        /// <summary>La case se valide au clic, sans attendre que la cellule perde le focus.</summary>
        private void Coche_Modifiee(object sender, EventArgs e)
        {
            if (grille.IsCurrentCellDirty && grille.CurrentCell is DataGridViewCheckBoxCell)
                grille.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void Cellule_Cliquee(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            grille.EndEdit();
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
