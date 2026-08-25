using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>Création et modification des fournisseurs, enregistrés sur le réseau.</summary>
    public class SupplierDialog : Form
    {
        private AppConfig _config;
        private List<Supplier> _suppliers;
        private bool _chargement;

        private ListBox lstSuppliers;
        private TextBox txtName;
        private TextBox txtEmails;
        private TextBox txtCc;
        private TextBox txtNote;
        private Label lblChemin;

        /// <summary>Liste telle qu'elle a été enregistrée. À relire après un OK.</summary>
        public List<Supplier> Suppliers { get { return _suppliers; } }

        public SupplierDialog(AppConfig config, List<Supplier> suppliers)
        {
            _config = config;
            _suppliers = Copy(suppliers);

            Text = "Fournisseurs";
            AppIcon.Apply(this);
            Font = new Font("Segoe UI", 9F);
            Size = new Size(780, 470);
            MinimumSize = new Size(680, 420);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            BuildRight();
            BuildLeft();
            BuildBottom();

            RefreshList();
            if (lstSuppliers.Items.Count > 0) lstSuppliers.SelectedIndex = 0;
            else LoadFields(null);
        }

        private static List<Supplier> Copy(List<Supplier> source)
        {
            List<Supplier> copie = new List<Supplier>();
            if (source == null) return copie;
            foreach (Supplier s in source)
            {
                Supplier c = new Supplier();
                c.Name = s.Name;
                c.Note = s.Note;
                c.Emails = new List<string>(s.Emails);
                c.CcEmails = new List<string>(s.CcEmails);
                copie.Add(c);
            }
            return copie;
        }

        // ------------------------------------------------------------------
        // Interface
        // ------------------------------------------------------------------

        private void BuildRight()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(14, 12, 14, 12);
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            txtName = new TextBox();
            txtName.Dock = DockStyle.Fill;
            txtName.TextChanged += new EventHandler(Field_Changed);
            txtName.Leave += new EventHandler(Field_Leave);

            txtEmails = new TextBox();
            txtEmails.Multiline = true;
            txtEmails.ScrollBars = ScrollBars.Vertical;
            txtEmails.Dock = DockStyle.Fill;
            txtEmails.PlaceholderText = "une adresse par ligne";
            txtEmails.TextChanged += new EventHandler(Field_Changed);
            txtEmails.Leave += new EventHandler(Field_Leave);

            txtCc = new TextBox();
            txtCc.Multiline = true;
            txtCc.ScrollBars = ScrollBars.Vertical;
            txtCc.Dock = DockStyle.Fill;
            txtCc.PlaceholderText = "une adresse par ligne";
            txtCc.TextChanged += new EventHandler(Field_Changed);
            txtCc.Leave += new EventHandler(Field_Leave);

            txtNote = new TextBox();
            txtNote.Dock = DockStyle.Fill;
            txtNote.PlaceholderText = "contact, spécialité, délai habituel...";
            txtNote.TextChanged += new EventHandler(Field_Changed);
            txtNote.Leave += new EventHandler(Field_Leave);

            AddRow(t, "Nom", txtName, 28);
            AddRow(t, "Destinataires", txtEmails, 104);
            AddRow(t, "Copie (Cc)", txtCc, 74);
            AddRow(t, "Note", txtNote, 28);

            Panel droite = new Panel();
            droite.Dock = DockStyle.Fill;
            droite.Controls.Add(t);
            Controls.Add(droite);
        }

        private void AddRow(TableLayoutPanel t, string caption, Control champ, int hauteur)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, hauteur + 10));

            Label l = new Label();
            l.Text = caption;
            l.AutoSize = true;
            l.Margin = new Padding(0, 6, 8, 4);

            champ.Margin = new Padding(0, 4, 0, 6);
            champ.Height = hauteur;

            t.Controls.Add(l, 0, row);
            t.Controls.Add(champ, 1, row);
        }

        private void BuildLeft()
        {
            Panel gauche = new Panel();
            gauche.Dock = DockStyle.Left;
            gauche.Width = 250;
            gauche.Padding = new Padding(12, 12, 6, 12);

            lstSuppliers = new ListBox();
            lstSuppliers.Dock = DockStyle.Fill;
            lstSuppliers.IntegralHeight = false;
            lstSuppliers.SelectedIndexChanged += new EventHandler(List_SelectionChanged);

            Panel outils = new Panel();
            outils.Dock = DockStyle.Bottom;
            outils.Height = 76;

            Button btnAdd = new Button();
            btnAdd.Text = "Ajouter";
            btnAdd.Size = new Size(110, 28);
            btnAdd.Location = new Point(0, 8);
            btnAdd.Click += new EventHandler(Add_Click);

            Button btnRemove = new Button();
            btnRemove.Text = "Supprimer";
            btnRemove.Size = new Size(110, 28);
            btnRemove.Location = new Point(118, 8);
            btnRemove.Click += new EventHandler(Remove_Click);

            Button btnImport = new Button();
            btnImport.Text = "Importer une liste…";
            btnImport.Size = new Size(228, 28);
            btnImport.Location = new Point(0, 42);
            btnImport.Click += new EventHandler(Import_Click);

            outils.Controls.Add(btnAdd);
            outils.Controls.Add(btnRemove);
            outils.Controls.Add(btnImport);

            gauche.Controls.Add(lstSuppliers);
            gauche.Controls.Add(outils);
            Controls.Add(gauche);
        }

        private void BuildBottom()
        {
            Panel bas = new Panel();
            bas.Dock = DockStyle.Bottom;
            bas.Height = 56;
            bas.Padding = new Padding(12, 10, 12, 10);

            lblChemin = new Label();
            lblChemin.Dock = DockStyle.Left;
            lblChemin.Width = 430;
            lblChemin.ForeColor = Color.Gray;
            lblChemin.TextAlign = ContentAlignment.MiddleLeft;
            string chemin = SupplierService.GetFilePath(_config);
            lblChemin.Text = chemin == null ? "Aucun chemin configuré" : "Enregistré dans " + chemin;

            Button btnSave = new Button();
            btnSave.Text = "Enregistrer et fermer";
            btnSave.Size = new Size(160, 32);
            btnSave.Dock = DockStyle.Right;
            btnSave.BackColor = Color.FromArgb(0, 90, 158);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Click += new EventHandler(Save_Click);

            Button btnCancel = new Button();
            btnCancel.Text = "Annuler";
            btnCancel.Size = new Size(100, 32);
            btnCancel.Dock = DockStyle.Right;
            btnCancel.Margin = new Padding(0, 0, 8, 0);
            btnCancel.DialogResult = DialogResult.Cancel;

            bas.Controls.Add(lblChemin);
            bas.Controls.Add(btnCancel);
            bas.Controls.Add(btnSave);
            Controls.Add(bas);
            CancelButton = btnCancel;
        }

        // ------------------------------------------------------------------
        // Comportements
        // ------------------------------------------------------------------

        private void RefreshList()
        {
            int index = lstSuppliers.SelectedIndex;
            lstSuppliers.BeginUpdate();
            lstSuppliers.Items.Clear();
            foreach (Supplier s in _suppliers) lstSuppliers.Items.Add(s);
            lstSuppliers.EndUpdate();
            if (index >= 0 && index < lstSuppliers.Items.Count) lstSuppliers.SelectedIndex = index;
        }

        private Supplier Current
        {
            get { return lstSuppliers.SelectedItem as Supplier; }
        }

        private void List_SelectionChanged(object sender, EventArgs e)
        {
            if (_chargement) return;
            LoadFields(Current);
        }

        private void LoadFields(Supplier s)
        {
            _chargement = true;
            try
            {
                bool actif = s != null;
                txtName.Enabled = actif;
                txtEmails.Enabled = actif;
                txtCc.Enabled = actif;
                txtNote.Enabled = actif;

                txtName.Text = actif ? s.Name : "";
                txtEmails.Text = actif ? string.Join(Environment.NewLine, s.Emails) : "";
                txtCc.Text = actif ? string.Join(Environment.NewLine, s.CcEmails) : "";
                txtNote.Text = actif ? s.Note : "";
            }
            finally
            {
                _chargement = false;
            }
        }

        private void Field_Changed(object sender, EventArgs e)
        {
            if (_chargement) return;
            Supplier s = Current;
            if (s == null) return;
            s.Name = txtName.Text.Trim();
            s.Emails = SupplierService.ParseAddresses(txtEmails.Text);
            s.CcEmails = SupplierService.ParseAddresses(txtCc.Text);
            s.Note = txtNote.Text.Trim();
        }

        /// <summary>
        /// Met à jour le libellé dans la liste, une fois la saisie terminée.
        /// Le faire à chaque frappe recréerait l'entrée et reprendrait le focus.
        /// </summary>
        private void Field_Leave(object sender, EventArgs e)
        {
            int i = lstSuppliers.SelectedIndex;
            if (i < 0 || i >= _suppliers.Count) return;
            _chargement = true;
            try
            {
                lstSuppliers.Items[i] = _suppliers[i];
                lstSuppliers.SelectedIndex = i;
            }
            finally
            {
                _chargement = false;
            }
        }

        private void Add_Click(object sender, EventArgs e)
        {
            Supplier s = new Supplier();
            s.Name = "Nouveau fournisseur";
            _suppliers.Add(s);
            RefreshList();
            lstSuppliers.SelectedIndex = _suppliers.Count - 1;
            txtName.Focus();
            txtName.SelectAll();
        }

        /// <summary>
        /// Reprend une liste de fournisseurs depuis un tableau CSV ou Excel, quelle que
        /// soit la disposition des colonnes : elles sont reconnues par leur intitulé.
        /// </summary>
        private void Import_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Importer une liste de fournisseurs";
                dlg.Filter = "Tableaux (*.csv;*.xlsx)|*.csv;*.xlsx"
                           + "|Classeurs Excel (*.xlsx)|*.xlsx"
                           + "|Fichiers CSV (*.csv)|*.csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    int completes;
                    string message;
                    SupplierService.ImportFromFile(_suppliers, dlg.FileName, out completes, out message);
                    RefreshList();
                    if (lstSuppliers.Items.Count > 0 && lstSuppliers.SelectedIndex < 0) lstSuppliers.SelectedIndex = 0;
                    MessageBox.Show(message, "Fournisseurs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Import impossible : " + ex.Message, "Fournisseurs",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Remove_Click(object sender, EventArgs e)
        {
            Supplier s = Current;
            if (s == null) return;
            if (MessageBox.Show("Supprimer « " + s.Name + " » de la liste ?", "Fournisseurs",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _suppliers.Remove(s);
            RefreshList();
            if (lstSuppliers.Items.Count > 0) lstSuppliers.SelectedIndex = 0;
            else LoadFields(null);
        }

        private void Save_Click(object sender, EventArgs e)
        {
            // Un fournisseur sans nom ni adresse n'a pas de sens : on refuse d'enregistrer.
            foreach (Supplier s in _suppliers)
            {
                if (string.IsNullOrWhiteSpace(s.Name))
                {
                    MessageBox.Show("Un fournisseur n'a pas de nom.", "Fournisseurs",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (s.Emails.Count == 0)
                {
                    MessageBox.Show("« " + s.Name + " » n'a aucun destinataire.", "Fournisseurs",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            string message;
            if (!SupplierService.Save(_config, _suppliers, out message))
            {
                MessageBox.Show(message, "Fournisseurs", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
