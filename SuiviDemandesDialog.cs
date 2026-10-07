using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Les demandes de l'utilisateur, et la question qui les fait avancer : « avez-vous reçu
    /// une réponse ? ».
    ///
    /// Ouverte d'elle-même quand un rappel arrive à échéance, ou depuis la barre d'outils.
    /// « Réponse reçue » clôt la demande et sa barre dans le Gantt ; « Pas encore » repousse
    /// le rappel d'une semaine ; « Sans suite » arrête le suivi.
    /// </summary>
    public class SuiviDemandesDialog : Form
    {
        private readonly AppConfig _config;
        private readonly bool _rappel;
        private List<DemandeSuivie> _demandes = new List<DemandeSuivie>();

        private DataGridView grille;
        private CheckBox chkToutes;
        private Label lblVide;

        /// <param name="rappel">Vrai si la fenêtre s'ouvre pour un rappel : seules les demandes échues sont montrées.</param>
        public SuiviDemandesDialog(AppConfig config, bool rappel)
        {
            _config = config;
            _rappel = rappel;

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = rappel ? "Avez-vous reçu une réponse ?" : "Suivi de mes demandes";
            AppIcon.Apply(this);
            Font = AppFont.Get();
            ClientSize = new Size(1080, 520);
            MinimumSize = new Size(860, 380);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = rappel;

            Construire();
            Charger();
        }

        private void Construire()
        {
            Label titre = new Label();
            titre.Dock = DockStyle.Top;
            titre.Height = 54;
            titre.Text = _rappel
                ? "Ces demandes attendent une réponse du fournisseur. L'avez-vous reçue ?"
                  + Environment.NewLine + "« Pas encore » vous le redemandera dans " + Jours() + " jours."
                : "Vos demandes parties ou en préparation. Sélectionnez-en une ou plusieurs, puis indiquez où elles en sont.";
            titre.Padding = new Padding(0, 4, 0, 0);

            chkToutes = new CheckBox();
            chkToutes.Text = "Montrer aussi les demandes clôturées";
            chkToutes.AutoSize = true;
            chkToutes.Dock = DockStyle.Top;
            chkToutes.Visible = !_rappel;
            chkToutes.CheckedChanged += delegate { Charger(); };

            grille = new DataGridView();
            grille.Dock = DockStyle.Fill;
            grille.ReadOnly = true;
            grille.AllowUserToAddRows = false;
            grille.AllowUserToDeleteRows = false;
            grille.AllowUserToResizeRows = false;
            grille.RowHeadersVisible = false;
            grille.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grille.MultiSelect = true;
            grille.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grille.BackgroundColor = Color.White;
            string[] colonnes = { "Statut", "Type", "Fournisseur", "Réf. commande", "Articles", "Envoyée le", "Attente (j)", "Prochain rappel" };
            int[] poids = { 12, 11, 20, 14, 26, 11, 8, 11 };
            for (int i = 0; i < colonnes.Length; i++)
            {
                DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
                c.HeaderText = colonnes[i];
                c.FillWeight = poids[i];
                c.SortMode = DataGridViewColumnSortMode.Automatic;
                grille.Columns.Add(c);
            }
            grille.CellDoubleClick += delegate { OuvrirDossier(); };

            lblVide = new Label();
            lblVide.Dock = DockStyle.Fill;
            lblVide.TextAlign = ContentAlignment.MiddleCenter;
            lblVide.ForeColor = Color.FromArgb(110, 117, 125);
            lblVide.Visible = false;

            Button btnRecue = Bouton("Réponse reçue", Repondue_Click);
            Button btnPasEncore = Bouton("Pas encore", PasEncore_Click);
            Button btnSansSuite = Bouton("Sans suite", SansSuite_Click);
            Button btnDossier = Bouton("Ouvrir le dossier", delegate { OuvrirDossier(); });
            Button btnClasseur = Bouton("Ouvrir le classeur de suivi", delegate { OuvrirClasseur(); });
            Button btnFermer = Bouton(_rappel ? "Plus tard" : "Fermer", delegate { Close(); });

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Bottom;
            actions.Height = 52;
            actions.Padding = new Padding(0, 10, 0, 0);
            actions.Controls.Add(btnRecue);
            actions.Controls.Add(btnPasEncore);
            actions.Controls.Add(btnSansSuite);
            actions.Controls.Add(btnDossier);
            actions.Controls.Add(btnClasseur);
            actions.Controls.Add(btnFermer);

            Panel corps = new Panel();
            corps.Dock = DockStyle.Fill;
            corps.Padding = new Padding(18, 12, 18, 6);
            corps.Controls.Add(grille);
            corps.Controls.Add(lblVide);
            corps.Controls.Add(chkToutes);
            corps.Controls.Add(titre);
            corps.Controls.Add(actions);
            Controls.Add(corps);
            CancelButton = btnFermer;
        }

        private Button Bouton(string texte, EventHandler clic)
        {
            Button b = new Button();
            b.Text = texte;
            b.Size = new Size(AppFont.Width(texte, 32), 32);
            b.Margin = new Padding(0, 0, 8, 0);
            b.Click += clic;
            return b;
        }

        private int Jours()
        {
            return _config != null && _config.RappelJours > 0 ? _config.RappelJours : 7;
        }

        // ------------------------------------------------------------------ données

        private void Charger()
        {
            List<DemandeSuivie> miennes = BaseSuivi.Miennes(_config);
            _demandes = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in miennes)
            {
                if (_rappel) { if (d.RappelEchu(System.Environment.UserName, DateTime.Today)) _demandes.Add(d); }
                else if (chkToutes.Checked || d.Statut == DemandeSuivie.Envoyee || d.Statut == DemandeSuivie.Preparee)
                    _demandes.Add(d);
            }
            _demandes.Sort(delegate (DemandeSuivie a, DemandeSuivie b)
            {
                DateTime da = a.EnvoyeeLe ?? a.CreeeLe, db = b.EnvoyeeLe ?? b.CreeeLe;
                return da.CompareTo(db);
            });

            grille.Rows.Clear();
            foreach (DemandeSuivie d in _demandes)
            {
                int attente = d.EnvoyeeLe.HasValue
                    ? (int)((d.ReponseLe ?? d.ClotureLe ?? DateTime.Today).Date - d.EnvoyeeLe.Value.Date).TotalDays : 0;
                int i = grille.Rows.Add(d.Statut, d.Type, d.Fournisseur, d.Reference,
                    d.NbArticles + " — " + d.Articles,
                    d.EnvoyeeLe.HasValue ? d.EnvoyeeLe.Value.ToString("dd.MM.yyyy") : "pas encore partie",
                    d.EnvoyeeLe.HasValue ? attente.ToString() : "",
                    d.EnAttente && d.ProchainRappel.HasValue ? d.ProchainRappel.Value.ToString("dd.MM.yyyy") : "");
                grille.Rows[i].Tag = d;
                if (d.RappelEchu(System.Environment.UserName, DateTime.Today))
                    grille.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 204);
            }

            lblVide.Text = _rappel ? "Plus aucun rappel en attente." : "Aucune demande à suivre.";
            lblVide.Visible = _demandes.Count == 0;
            grille.Visible = _demandes.Count > 0;
            if (_rappel && _demandes.Count == 0 && Visible) Close();
        }

        private List<DemandeSuivie> Selection()
        {
            List<DemandeSuivie> choisies = new List<DemandeSuivie>();
            foreach (DataGridViewRow r in grille.SelectedRows)
            {
                DemandeSuivie d = r.Tag as DemandeSuivie;
                if (d != null) choisies.Add(d);
            }
            if (choisies.Count == 0)
                MessageBox.Show(this, "Sélectionnez une ou plusieurs demandes dans la liste.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return choisies;
        }

        // ------------------------------------------------------------------ actions

        private void Repondue_Click(object sender, EventArgs e)
        {
            foreach (DemandeSuivie d in Selection())
            {
                if (d.Statut != DemandeSuivie.Envoyee) continue;
                DemandeSuivie m = d.Copie();
                m.Statut = DemandeSuivie.Repondue;
                m.ReponseLe = DateTime.Today;
                m.ClotureLe = DateTime.Now;
                m.ClotureePar = Qui();
                m.ProchainRappel = null;
                BaseSuivi.Enregistrer(m);
            }
            Apres();
        }

        private void PasEncore_Click(object sender, EventArgs e)
        {
            foreach (DemandeSuivie d in Selection())
            {
                if (d.Statut != DemandeSuivie.Envoyee) continue;
                DemandeSuivie m = d.Copie();
                m.ProchainRappel = DateTime.Today.AddDays(Jours());
                m.NbRappels++;
                BaseSuivi.Enregistrer(m);
            }
            Apres();
        }

        private void SansSuite_Click(object sender, EventArgs e)
        {
            List<DemandeSuivie> choisies = Selection();
            if (choisies.Count == 0) return;
            if (MessageBox.Show(this, "Arrêter le suivi de " + choisies.Count + " demande(s), sans réponse du fournisseur ?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (DemandeSuivie d in choisies)
            {
                if (d.Statut != DemandeSuivie.Envoyee && d.Statut != DemandeSuivie.Preparee) continue;
                DemandeSuivie m = d.Copie();
                m.Statut = d.Statut == DemandeSuivie.Preparee ? DemandeSuivie.NonEnvoyee : DemandeSuivie.SansSuite;
                m.ClotureLe = DateTime.Now;
                m.ClotureePar = Qui();
                m.ProchainRappel = null;
                BaseSuivi.Enregistrer(m);
            }
            Apres();
        }

        private void Apres()
        {
            // L'écriture dans le classeur partagé se fait en arrière-plan, dès qu'il est libre.
            SuiviEnvois.Reveiller();
            Charger();
        }

        private static string Qui()
        {
            string nom = OutlookService.NomUtilisateur();
            return nom != "" ? nom : System.Environment.UserName;
        }

        private void OuvrirDossier()
        {
            if (grille.SelectedRows.Count == 0) return;
            DemandeSuivie d = grille.SelectedRows[0].Tag as DemandeSuivie;
            if (d == null) return;
            if (string.IsNullOrWhiteSpace(d.DossierArchive) || !Directory.Exists(d.DossierArchive))
            {
                MessageBox.Show(this, d.Statut == DemandeSuivie.Preparee
                        ? "Cette demande n'est pas encore partie : elle n'est pas archivée."
                        : "Le dossier archivé est introuvable :" + Environment.NewLine + d.DossierArchive,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Ouvrir(d.DossierArchive);
        }

        private void OuvrirClasseur()
        {
            string chemin = BaseSuivi.Chemin(_config);
            if (chemin == "" || !File.Exists(chemin))
            {
                MessageBox.Show(this, "Le classeur de suivi n'existe pas encore : il est créé à la première demande."
                    + Environment.NewLine + chemin, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Ouvrir(chemin);
        }

        private static void Ouvrir(string chemin)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(chemin);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                LogService.Write("Ouverture impossible : " + chemin + " — " + ex.Message);
            }
        }
    }
}
