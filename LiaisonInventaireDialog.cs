using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Choisit la fiche d'inventaire correspondant à un fournisseur d'AskThem.
    ///
    /// Les noms proches sont proposés en tête, mais la liste complète reste accessible :
    /// l'inventaire contient de vraies ambiguïtés — « Idex Health &amp; Science » et
    /// « Idex Health &amp; Science, LLC » — que seul l'utilisateur peut trancher.
    /// </summary>
    public class LiaisonInventaireDialog : Form
    {
        private ListBox liste;
        private CheckBox chkToutes;
        private Label lblAide;

        private readonly List<KeyValuePair<int, string>> _candidats;
        private readonly List<KeyValuePair<int, string>> _toutes;

        /// <summary>Identifiant retenu, ou zéro si le lien est retiré.</summary>
        public int IdChoisi { get; private set; }

        public LiaisonInventaireDialog(string nomAskThem,
                                       List<KeyValuePair<int, string>> candidats,
                                       List<KeyValuePair<int, string>> toutes,
                                       int idActuel)
        {
            _candidats = candidats != null ? candidats : new List<KeyValuePair<int, string>>();
            _toutes = toutes != null ? toutes : new List<KeyValuePair<int, string>>();
            IdChoisi = idActuel;

            Text = "Lier « " + nomAskThem + " » à l'inventaire";
            Font = AppFont.Get();
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 420);
            MinimumSize = new Size(420, 320);

            lblAide = new Label();
            lblAide.Dock = DockStyle.Top;
            lblAide.Height = 44;
            lblAide.Padding = new Padding(12, 10, 12, 4);
            lblAide.Text = _candidats.Count > 0
                ? "Fiches dont le nom correspond. Vérifiez avant de valider."
                : "Aucun nom approchant : cochez ci-dessous pour voir toutes les fiches.";

            liste = new ListBox();
            liste.Dock = DockStyle.Fill;
            liste.IntegralHeight = false;
            liste.DoubleClick += new EventHandler(Valider_Click);

            chkToutes = new CheckBox();
            chkToutes.Text = "Afficher toutes les fiches de l'inventaire";
            chkToutes.Dock = DockStyle.Top;
            chkToutes.Height = 28;
            chkToutes.Padding = new Padding(12, 4, 12, 4);
            chkToutes.Checked = _candidats.Count == 0;
            chkToutes.CheckedChanged += new EventHandler(Basculer_Liste);

            Button btnValider = new Button();
            btnValider.Text = "Lier";
            btnValider.Width = AppFont.Width(btnValider.Text, 40);
            btnValider.Height = 30;
            btnValider.Click += new EventHandler(Valider_Click);

            Button btnDelier = new Button();
            btnDelier.Text = "Retirer le lien";
            btnDelier.Width = AppFont.Width(btnDelier.Text, 40);
            btnDelier.Height = 30;
            btnDelier.Click += new EventHandler(Delier_Click);

            Button btnAnnuler = new Button();
            btnAnnuler.Text = "Annuler";
            btnAnnuler.Width = AppFont.Width(btnAnnuler.Text, 40);
            btnAnnuler.Height = 30;
            btnAnnuler.DialogResult = DialogResult.Cancel;

            FlowLayoutPanel bas = new FlowLayoutPanel();
            bas.Dock = DockStyle.Bottom;
            bas.Height = 48;
            bas.Padding = new Padding(12, 8, 12, 8);
            bas.FlowDirection = FlowDirection.RightToLeft;
            bas.Controls.Add(btnValider);
            bas.Controls.Add(btnAnnuler);
            bas.Controls.Add(btnDelier);

            Controls.Add(liste);
            Controls.Add(chkToutes);
            Controls.Add(lblAide);
            Controls.Add(bas);

            AcceptButton = btnValider;
            CancelButton = btnAnnuler;

            Remplir();
        }

        private void Basculer_Liste(object sender, EventArgs e)
        {
            Remplir();
        }

        private void Remplir()
        {
            List<KeyValuePair<int, string>> source = chkToutes.Checked ? _toutes : _candidats;
            liste.BeginUpdate();
            try
            {
                liste.Items.Clear();
                foreach (KeyValuePair<int, string> f in source) liste.Items.Add(new Entree(f));
                if (liste.Items.Count == 0) return;

                // La fiche déjà liée reste sélectionnée si elle est visible.
                for (int i = 0; i < liste.Items.Count; i++)
                {
                    if (((Entree)liste.Items[i]).Id == IdChoisi) { liste.SelectedIndex = i; return; }
                }
                liste.SelectedIndex = 0;
            }
            finally
            {
                liste.EndUpdate();
            }
        }

        private void Valider_Click(object sender, EventArgs e)
        {
            Entree choix = liste.SelectedItem as Entree;
            if (choix == null)
            {
                MessageBox.Show("Choisissez une fiche, ou retirez le lien.",
                    "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            IdChoisi = choix.Id;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Delier_Click(object sender, EventArgs e)
        {
            IdChoisi = 0;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Une fiche telle qu'elle s'affiche dans la liste.</summary>
        private sealed class Entree
        {
            public int Id;
            private readonly string _nom;

            public Entree(KeyValuePair<int, string> f)
            {
                Id = f.Key;
                _nom = f.Value;
            }

            public override string ToString()
            {
                return _nom + "   (n° " + Id + ")";
            }
        }
    }
}
