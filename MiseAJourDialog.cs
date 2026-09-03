using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Annonce une nouvelle version, clairement, au lieu de la signaler par un petit bouton
    /// dans un bandeau — d'autant que ce bandeau n'existe pas en mode guidé.
    /// </summary>
    public class MiseAJourDialog : Form
    {
        private readonly UpdateService.UpdateInfo _info;

        /// <summary>Vrai si l'utilisateur veut installer maintenant.</summary>
        public bool Installer { get; private set; }

        public MiseAJourDialog(UpdateService.UpdateInfo info)
        {
            _info = info;

            Text = "Mise à jour d'AskThem";
            Font = AppFont.Get();
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 300);
            BackColor = Color.White;

            Construire();
        }

        private void Construire()
        {
            Label titre = new Label();
            titre.Text = "Une nouvelle version est disponible";
            titre.Font = new Font(AppFont.Family, 15F, FontStyle.Bold);
            titre.Dock = DockStyle.Top;
            titre.Height = 42;

            Label versions = new Label();
            versions.Dock = DockStyle.Top;
            versions.Height = 40;
            versions.Font = new Font(AppFont.Family, 11F);
            versions.Text = "Vous utilisez la " + _info.CurrentVersion
                          + "   →   la " + _info.LatestVersion + " est publiée.";

            Label explication = new Label();
            explication.Dock = DockStyle.Top;
            explication.Height = 92;
            explication.ForeColor = Color.FromArgb(90, 97, 105);
            explication.Text =
                "AskThem va télécharger la nouvelle version, se fermer et redémarrer tout seul, "
                + "au même endroit qu'aujourd'hui." + Environment.NewLine + Environment.NewLine
                + "Ce que vous avez saisi dans la fenêtre sera perdu : terminez ou annulez votre "
                + "demande en cours avant de continuer.";

            LinkLabel lien = new LinkLabel();
            lien.Dock = DockStyle.Top;
            lien.Height = 30;
            lien.Text = "Voir ce que la version apporte";
            lien.Visible = !string.IsNullOrWhiteSpace(_info.PageUrl);
            lien.LinkClicked += new LinkLabelLinkClickedEventHandler(Lien_Clique);

            Panel corps = new Panel();
            corps.Dock = DockStyle.Fill;
            corps.Padding = new Padding(28, 22, 28, 8);
            corps.Controls.Add(lien);
            corps.Controls.Add(explication);
            corps.Controls.Add(versions);
            corps.Controls.Add(titre);

            Button btnMaintenant = new Button();
            btnMaintenant.Text = "Mettre à jour maintenant";
            btnMaintenant.Font = new Font(AppFont.Family, 11F);
            btnMaintenant.Width = AppFont.Width(btnMaintenant.Text, 60);
            btnMaintenant.Height = 42;
            btnMaintenant.BackColor = Color.FromArgb(0, 90, 158);
            btnMaintenant.ForeColor = Color.White;
            btnMaintenant.FlatStyle = FlatStyle.Flat;
            btnMaintenant.FlatAppearance.BorderSize = 0;
            btnMaintenant.Click += new EventHandler(Maintenant_Click);

            Button btnPlusTard = new Button();
            btnPlusTard.Text = "Plus tard";
            btnPlusTard.Font = new Font(AppFont.Family, 11F);
            btnPlusTard.Width = AppFont.Width(btnPlusTard.Text, 50);
            btnPlusTard.Height = 42;
            btnPlusTard.Margin = new Padding(10, 0, 0, 0);
            btnPlusTard.DialogResult = DialogResult.Cancel;

            FlowLayoutPanel boutons = new FlowLayoutPanel();
            boutons.Dock = DockStyle.Bottom;
            boutons.Height = 66;
            boutons.Padding = new Padding(28, 12, 28, 12);
            boutons.FlowDirection = FlowDirection.RightToLeft;
            boutons.Controls.Add(btnMaintenant);
            boutons.Controls.Add(btnPlusTard);

            Controls.Add(corps);
            Controls.Add(boutons);

            AcceptButton = btnMaintenant;
            CancelButton = btnPlusTard;
        }

        private void Maintenant_Click(object sender, EventArgs e)
        {
            Installer = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Lien_Clique(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(_info.PageUrl);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("La page n'a pas pu être ouverte : " + ex.Message,
                    "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
