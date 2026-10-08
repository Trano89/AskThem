using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;
using AskThem.Controls;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Le suivi des demandes, et la question qui les fait avancer : « avez-vous reçu une
    /// réponse ? ».
    ///
    /// Ouverte d'elle-même quand un rappel arrive à échéance, ou depuis la barre d'outils.
    /// « Réponse reçue » clôt la demande et sa barre dans le Gantt ; « Pas encore » repousse
    /// le rappel ; « Sans suite » arrête le suivi.
    ///
    /// Chacun peut aussi voir les demandes des autres, et agir dessus — un collègue absent ne
    /// bloque pas le suivi. Le demandeur en est alors prévenu par un email automatique, qui
    /// dit qui a fait quoi.
    /// </summary>
    public class SuiviDemandesDialog : Form
    {
        private readonly AppConfig _config;
        private readonly bool _rappel;
        private List<DemandeSuivie> _demandes = new List<DemandeSuivie>();

        private DataGridView grille;
        private RadioButton optMiennes;
        private RadioButton optToutes;
        private CheckBox chkCloturees;
        private Label lblVide;
        private Label lblSelection;
        private Button btnRecue;
        private Button btnPasEncore;
        private Button btnSansSuite;
        private LinkLabel lnkDossier;

        /// <param name="rappel">Vrai si la fenêtre s'ouvre pour un rappel : seules les demandes échues de l'utilisateur sont montrées.</param>
        public SuiviDemandesDialog(AppConfig config, bool rappel)
        {
            _config = config;
            _rappel = rappel;

            // Suspendue puis reprise : c'est à la reprise que la fenêtre se met à l'échelle.
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = rappel ? "Avez-vous reçu une réponse ?" : "Suivi des demandes";
            AppIcon.Apply(this);
            Font = AppFont.Get();
            ClientSize = new Size(1180, 540);
            MinimumSize = new Size(900, 380);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = rappel;

            Construire();
            Charger();

            ResumeLayout(false);
            PerformLayout();
            Ui.TenirDansEcran(this);
        }

        private void Construire()
        {
            Label titre = Ui.Section(_rappel ? "Ces demandes attendent une réponse du fournisseur."
                                             : "Où en sont les demandes envoyées ?");
            titre.Margin = new Padding(0, 0, 0, 4);
            Label explication = Ui.Legende(_rappel
                ? "Sélectionnez celles dont vous avez reçu la réponse. « Pas encore » vous le redemandera dans "
                  + Jours() + " jours."
                : "Sélectionnez une ou plusieurs demandes, puis dites où elles en sont. Si la demande est celle "
                  + "d'un collègue, il en est prévenu par un email automatique.");
            explication.Margin = new Padding(0, 0, 0, 10);

            // « Mes demandes » ou « toutes » : deux vues d'une même liste, pas une option à cocher.
            optMiennes = new RadioButton();
            optMiennes.Text = "Mes demandes";
            optMiennes.AutoSize = true;
            optMiennes.Checked = true;
            optMiennes.Margin = new Padding(0, 0, 16, 0);
            optToutes = new RadioButton();
            optToutes.Text = "Toutes les demandes";
            optToutes.AutoSize = true;
            optToutes.Margin = new Padding(0, 0, 32, 0);
            optToutes.CheckedChanged += delegate { Charger(); };

            chkCloturees = new CheckBox();
            chkCloturees.Text = "Inclure les demandes clôturées";
            chkCloturees.AutoSize = true;
            chkCloturees.Margin = Padding.Empty;
            chkCloturees.CheckedChanged += delegate { Charger(); };

            FlowLayoutPanel filtres = Ui.Rangee();
            filtres.Margin = new Padding(0, 0, 0, 8);
            filtres.Visible = !_rappel;
            filtres.Controls.Add(optMiennes);
            filtres.Controls.Add(optToutes);
            filtres.Controls.Add(chkCloturees);

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
            grille.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grille.BackgroundColor = Theme.Surface;
            grille.GridColor = Theme.Separateur;
            grille.Margin = Padding.Empty;
            string[] colonnes = { "Statut", "Demandé par", "Type", "Fournisseur", "Réf. commande", "Articles",
                                  "Envoyée le", "Attente (j)", "Prochain rappel", "Dernière action" };
            int[] poids = { 11, 13, 10, 16, 12, 22, 10, 7, 10, 18 };
            for (int i = 0; i < colonnes.Length; i++)
            {
                DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
                c.HeaderText = colonnes[i];
                c.FillWeight = poids[i];
                c.SortMode = DataGridViewColumnSortMode.Automatic;
                grille.Columns.Add(c);
            }
            grille.CellDoubleClick += delegate { OuvrirDossier(); };
            grille.SelectionChanged += delegate { MajActions(); };

            lblVide = Ui.Legende("");
            lblVide.AutoSize = false;
            lblVide.Dock = DockStyle.Fill;
            lblVide.TextAlign = ContentAlignment.MiddleCenter;
            lblVide.Visible = false;

            Panel liste = new Panel();
            liste.Dock = DockStyle.Fill;
            liste.Margin = Padding.Empty;
            liste.Controls.Add(grille);
            liste.Controls.Add(lblVide);

            // Le pied : ce qui s'ouvre à gauche, ce qui fait avancer la demande à droite —
            // et les boutons ne s'allument que pour ce que la sélection permet.
            lnkDossier = Ui.Lien("Ouvrir le dossier", delegate { OuvrirDossier(); });
            LinkLabel lnkClasseur = Ui.Lien("Ouvrir le classeur de suivi", delegate { OuvrirClasseur(); });
            lblSelection = Ui.Legende("");
            lblSelection.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            lblSelection.Margin = new Padding(0, 6, 12, 0);

            btnSansSuite = Ui.Secondaire("Sans suite");
            btnSansSuite.Click += new EventHandler(SansSuite_Click);
            btnPasEncore = Ui.Secondaire("Pas encore");
            btnPasEncore.Click += new EventHandler(PasEncore_Click);
            btnRecue = Ui.Primaire("Réponse reçue");
            btnRecue.Click += new EventHandler(Repondue_Click);

            FlowLayoutPanel liens = Ui.Rangee();
            liens.Controls.Add(lnkDossier);
            liens.Controls.Add(lnkClasseur);
            if (_rappel)
            {
                LinkLabel lnkPlusTard = Ui.Lien("Plus tard", delegate { Close(); });
                liens.Controls.Add(lnkPlusTard);
            }

            TableLayoutPanel bas = new TableLayoutPanel();
            bas.Dock = DockStyle.Fill;
            bas.AutoSize = true;
            bas.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bas.ColumnCount = 5;
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.RowCount = 1;
            bas.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bas.Margin = new Padding(0, 12, 0, 0);
            bas.Controls.Add(liens, 0, 0);
            bas.Controls.Add(lblSelection, 1, 0);
            bas.Controls.Add(btnSansSuite, 2, 0);
            bas.Controls.Add(btnPasEncore, 3, 0);
            bas.Controls.Add(btnRecue, 4, 0);

            TableLayoutPanel cadre = new TableLayoutPanel();
            cadre.Dock = DockStyle.Fill;
            cadre.ColumnCount = 1;
            cadre.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            cadre.Padding = new Padding(18, 14, 18, 12);
            cadre.RowCount = 5;
            cadre.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cadre.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cadre.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cadre.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            cadre.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titre.Dock = DockStyle.Fill;
            explication.Dock = DockStyle.Fill;
            cadre.Controls.Add(titre, 0, 0);
            cadre.Controls.Add(explication, 0, 1);
            cadre.Controls.Add(filtres, 0, 2);
            cadre.Controls.Add(liste, 0, 3);
            cadre.Controls.Add(bas, 0, 4);
            BackColor = Theme.Fond;
            Controls.Add(cadre);
        }

        /// <summary>Échap ferme la fenêtre, comme le faisait le bouton « Fermer » retiré.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>N'offre que ce que la sélection permet, et dit combien de demandes sont choisies.</summary>
        private void MajActions()
        {
            int n = 0, envoyees = 0, preparees = 0;
            foreach (DataGridViewRow r in grille.SelectedRows)
            {
                DemandeSuivie d = r.Tag as DemandeSuivie;
                if (d == null) continue;
                n++;
                if (d.Statut == DemandeSuivie.Envoyee) envoyees++;
                else if (d.Statut == DemandeSuivie.Preparee) preparees++;
            }
            btnRecue.Enabled = envoyees > 0;
            btnPasEncore.Enabled = envoyees > 0;
            btnSansSuite.Enabled = envoyees + preparees > 0;
            lnkDossier.Enabled = n == 1;
            lblSelection.Text = n == 0 ? (_demandes.Count == 0 ? "" : "Aucune demande sélectionnée.")
                              : n == 1 ? "1 demande sélectionnée."
                              : n + " demandes sélectionnées.";
        }

        private int Jours()
        {
            return PreferencesUtilisateur.DelaiRappel(_config);
        }

        private static string Moi()
        {
            return System.Environment.UserName;
        }

        // ------------------------------------------------------------------ données

        private void Charger()
        {
            Cursor = Cursors.WaitCursor;
            List<DemandeSuivie> source;
            try { source = !_rappel && optToutes.Checked ? BaseSuivi.Lire(_config) : BaseSuivi.Miennes(_config); }
            finally { Cursor = Cursors.Default; }

            _demandes = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in source)
            {
                if (_rappel) { if (d.RappelEchu(Moi(), DateTime.Today)) _demandes.Add(d); }
                else if (chkCloturees.Checked || d.Statut == DemandeSuivie.Envoyee || d.Statut == DemandeSuivie.Preparee)
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
                string action = string.IsNullOrWhiteSpace(d.DerniereAction) ? ""
                    : d.DerniereAction + (string.IsNullOrWhiteSpace(d.DerniereActionPar) ? "" : " — " + d.DerniereActionPar);
                int i = grille.Rows.Add(d.Statut, d.Demandeur, d.Type, d.Fournisseur, d.Reference,
                    d.NbArticles + " — " + d.Articles,
                    d.EnvoyeeLe.HasValue ? d.EnvoyeeLe.Value.ToString("dd.MM.yyyy") : "pas encore partie",
                    d.EnvoyeeLe.HasValue ? attente.ToString() : "",
                    d.EnAttente && d.ProchainRappel.HasValue ? d.ProchainRappel.Value.ToString("dd.MM.yyyy") : "",
                    action);
                grille.Rows[i].Tag = d;
                if (d.RappelEchu(Moi(), DateTime.Today))
                    grille.Rows[i].DefaultCellStyle.BackColor = Theme.AttentionFond;
                else if (!d.EstA(Moi()))
                    grille.Rows[i].DefaultCellStyle.ForeColor = Theme.Texte2;
            }

            // Rien n'est choisi d'office : une action sur la première ligne, faute d'avoir
            // regardé, touchait une demande qu'on ne visait pas. Un rappel isolé, si.
            grille.ClearSelection();
            if (_rappel && grille.Rows.Count == 1) grille.Rows[0].Selected = true;

            lblVide.Text = _rappel ? "Plus aucun rappel en attente." : "Aucune demande à suivre.";
            lblVide.Visible = _demandes.Count == 0;
            grille.Visible = _demandes.Count > 0;
            MajActions();
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
            return choisies;
        }

        // ------------------------------------------------------------------ actions

        private void Repondue_Click(object sender, EventArgs e)
        {
            Agir("Réponse reçue", delegate (DemandeSuivie d) { return d.Statut == DemandeSuivie.Envoyee; },
                delegate (DemandeSuivie m)
                {
                    m.Statut = DemandeSuivie.Repondue;
                    m.ReponseLe = DateTime.Today;
                    m.ClotureLe = DateTime.Now;
                    m.ClotureePar = Qui();
                    m.ProchainRappel = null;
                });
        }

        private void PasEncore_Click(object sender, EventArgs e)
        {
            int jours = Jours();
            Agir("Rappel repoussé de " + jours + " jours", delegate (DemandeSuivie d) { return d.Statut == DemandeSuivie.Envoyee; },
                delegate (DemandeSuivie m)
                {
                    m.ProchainRappel = DateTime.Today.AddDays(jours);
                    m.NbRappels++;
                });
        }

        private void SansSuite_Click(object sender, EventArgs e)
        {
            Agir("Sans suite",
                delegate (DemandeSuivie d) { return d.Statut == DemandeSuivie.Envoyee || d.Statut == DemandeSuivie.Preparee; },
                delegate (DemandeSuivie m)
                {
                    m.Statut = m.Statut == DemandeSuivie.Preparee ? DemandeSuivie.NonEnvoyee : DemandeSuivie.SansSuite;
                    m.ClotureLe = DateTime.Now;
                    m.ClotureePar = Qui();
                    m.ProchainRappel = null;
                });
        }

        /// <summary>
        /// Applique une action aux demandes choisies. Celles d'un collègue ne sont touchées
        /// qu'après confirmation, et chacun de ces collègues reçoit un email qui dit quoi, et
        /// qui l'a fait.
        /// </summary>
        private void Agir(string action, Predicate<DemandeSuivie> possible, Action<DemandeSuivie> appliquer)
        {
            List<DemandeSuivie> choisies = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in Selection())
                if (possible(d)) choisies.Add(d);
            if (choisies.Count == 0) return;

            List<string> collegues = new List<string>();
            foreach (DemandeSuivie d in choisies)
                if (!d.EstA(Moi()) && !collegues.Contains(d.Demandeur)) collegues.Add(d.Demandeur);

            string question = "« " + action + " » pour " + choisies.Count + " demande(s) ?";
            if (collegues.Count > 0)
                question += System.Environment.NewLine + System.Environment.NewLine
                          + "Certaines appartiennent à " + string.Join(", ", collegues) + ". Un email automatique "
                          + "les préviendra de votre action.";
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string qui = Qui();
            List<string> nonPrevenus = new List<string>();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (DemandeSuivie d in choisies)
                {
                    DemandeSuivie m = d.Copie();
                    appliquer(m);
                    m.DerniereAction = action;
                    m.DerniereActionPar = qui;
                    m.DerniereActionLe = DateTime.Now;
                    BaseSuivi.Enregistrer(m);

                    if (!d.EstA(Moi()) && !Prevenir(d, m, action, qui))
                        nonPrevenus.Add(d.Demandeur);
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (nonPrevenus.Count > 0)
                MessageBox.Show(this, "L'action est enregistrée, mais l'email n'a pas pu partir vers : "
                    + string.Join(", ", nonPrevenus) + "." + System.Environment.NewLine
                    + "Son adresse est introuvable, ou Outlook a refusé l'envoi. Pensez à le prévenir.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // L'écriture dans le classeur partagé se fait en arrière-plan, dès qu'il est libre.
            SuiviEnvois.Reveiller();
            Charger();
        }

        /// <summary>Prévient le demandeur qu'un collègue a agi sur sa demande. Faux si l'email n'a pas pu partir.</summary>
        private static bool Prevenir(DemandeSuivie avant, DemandeSuivie apres, string action, string qui)
        {
            string adresse = avant.AuteurEmail;
            if (string.IsNullOrWhiteSpace(adresse)) adresse = OutlookService.ResoudreAdresse(avant.AuteurNom);
            if (string.IsNullOrWhiteSpace(adresse)) adresse = OutlookService.ResoudreAdresse(avant.Auteur);
            if (string.IsNullOrWhiteSpace(adresse)) return false;

            string message;
            return OutlookService.EnvoyerAvis(adresse, SujetAvis(avant, action), CorpsAvis(avant, apres, action, qui), out message);
        }

        public static string SujetAvis(DemandeSuivie d, string action)
        {
            return "AskThem — votre demande à " + (d.Fournisseur == "" ? "un fournisseur" : d.Fournisseur)
                 + (d.Reference == "" ? "" : " (" + d.Reference + ")") + " : " + action;
        }

        public static string CorpsAvis(DemandeSuivie avant, DemandeSuivie apres, string action, string qui)
        {
            StringBuilder h = new StringBuilder();
            h.Append("<html><body><div style=\"font-family:Aptos, 'Segoe UI', Calibri, Arial, sans-serif; font-size:11pt; color:#222222;\">");
            h.Append("<p>Bonjour ").Append(E(avant.Demandeur)).Append(",</p>");
            h.Append("<p><b>").Append(E(qui)).Append("</b> a mis à jour le suivi d'une de vos demandes : <b>")
             .Append(E(action)).Append("</b>.</p>");
            h.Append("<table style=\"border-collapse:collapse;\">");
            Ligne(h, "Type", avant.Type);
            Ligne(h, "Fournisseur", avant.Fournisseur);
            Ligne(h, "Réf. commande", avant.Reference);
            Ligne(h, "Articles", avant.NbArticles + " — " + avant.Articles);
            Ligne(h, "Envoyée le", avant.EnvoyeeLe.HasValue ? avant.EnvoyeeLe.Value.ToString("dd.MM.yyyy") : "pas encore partie");
            Ligne(h, "Statut", avant.Statut == apres.Statut ? apres.Statut : avant.Statut + " → " + apres.Statut);
            if (apres.EnAttente && apres.ProchainRappel.HasValue)
                Ligne(h, "Prochain rappel", apres.ProchainRappel.Value.ToString("dd.MM.yyyy"));
            h.Append("</table>");
            h.Append("<p style=\"color:#666666;\">Message envoyé automatiquement par AskThem. Vos demandes se consultent "
                   + "dans « Suivi des demandes… ».</p>");
            h.Append("</div></body></html>");
            return h.ToString();
        }

        private static void Ligne(StringBuilder h, string intitule, string valeur)
        {
            h.Append("<tr><td style=\"padding:3px 12px 3px 0; color:#555555;\">").Append(E(intitule))
             .Append("</td><td style=\"padding:3px 0;\">").Append(E(valeur)).Append("</td></tr>");
        }

        private static string E(string t)
        {
            return WebUtility.HtmlEncode(t ?? "");
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
                        : "Le dossier archivé est introuvable :" + System.Environment.NewLine + d.DossierArchive,
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
                    + System.Environment.NewLine + chemin, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
