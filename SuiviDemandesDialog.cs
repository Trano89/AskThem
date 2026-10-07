using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;
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
        private CheckBox chkTous;
        private CheckBox chkCloturees;
        private Label lblVide;

        /// <param name="rappel">Vrai si la fenêtre s'ouvre pour un rappel : seules les demandes échues de l'utilisateur sont montrées.</param>
        public SuiviDemandesDialog(AppConfig config, bool rappel)
        {
            _config = config;
            _rappel = rappel;

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
        }

        private void Construire()
        {
            Label titre = new Label();
            titre.Dock = DockStyle.Top;
            titre.Height = 50;
            titre.Text = _rappel
                ? "Ces demandes attendent une réponse du fournisseur. L'avez-vous reçue ?"
                  + System.Environment.NewLine + "« Pas encore » vous le redemandera dans " + Jours() + " jours."
                : "Sélectionnez une ou plusieurs demandes, puis indiquez où elles en sont. Si la demande est "
                  + "celle d'un collègue, il en est prévenu par un email automatique.";
            titre.Padding = new Padding(0, 4, 0, 0);

            chkTous = new CheckBox();
            chkTous.Text = "Voir les demandes de tous les utilisateurs";
            chkTous.AutoSize = true;
            chkTous.Margin = new Padding(0, 0, 24, 0);
            chkTous.CheckedChanged += delegate { Charger(); };

            chkCloturees = new CheckBox();
            chkCloturees.Text = "Montrer aussi les demandes clôturées";
            chkCloturees.AutoSize = true;
            chkCloturees.CheckedChanged += delegate { Charger(); };

            FlowLayoutPanel filtres = new FlowLayoutPanel();
            filtres.Dock = DockStyle.Top;
            filtres.Height = 30;
            filtres.Visible = !_rappel;
            filtres.Controls.Add(chkTous);
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
            grille.BackgroundColor = Color.White;
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
            corps.Controls.Add(filtres);
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
            try { source = !_rappel && chkTous.Checked ? BaseSuivi.Lire(_config) : BaseSuivi.Miennes(_config); }
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
                    grille.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 204);
                else if (!d.EstA(Moi()))
                    grille.Rows[i].DefaultCellStyle.ForeColor = Color.FromArgb(70, 77, 85);
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
