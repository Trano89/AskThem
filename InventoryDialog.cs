using System;
using System.Drawing;
using System.Windows.Forms;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Connexion à l'inventaire. Le mot de passe est saisi ici une seule fois, puis
    /// chiffré par Windows pour ce poste et cet utilisateur. Il ne figure ni dans le
    /// programme, ni dans le dépôt, ni dans config.json.
    /// </summary>
    public class InventoryDialog : Form
    {
        private AppConfig _config;
        private TextBox txtUrl;
        private TextBox txtUser;
        private TextBox txtPassword;
        private Label lblEtat;

        public InventoryDialog(AppConfig config)
        {
            _config = config;

            Text = "Connexion à l'inventaire";
            Font = new Font("Segoe UI", 9F);
            Size = new Size(640, 330);
            MinimumSize = new Size(580, 310);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(16, 14, 16, 8);
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            txtUrl = new TextBox();
            txtUrl.Dock = DockStyle.Fill;
            txtUrl.Text = _config.InventoryApiUrl;

            txtUser = new TextBox();
            txtUser.Dock = DockStyle.Fill;
            txtUser.Text = _config.InventoryUser;

            txtPassword = new TextBox();
            txtPassword.Dock = DockStyle.Fill;
            txtPassword.UseSystemPasswordChar = true;

            AddRow(t, "Adresse", txtUrl);
            AddRow(t, "Utilisateur", txtUser);
            AddRow(t, "Mot de passe", txtPassword);

            Label note = new Label();
            note.Dock = DockStyle.Bottom;
            note.Height = 64;
            note.ForeColor = Color.Gray;
            note.Text = "Le mot de passe est chiffré par Windows pour ce poste et cette session. "
                      + "Il n'est écrit ni dans le programme, ni dans config.json, ni sur le dépôt : "
                      + "un mot de passe placé dans le code serait lisible par quiconque obtient "
                      + "l'exécutable. Chaque poste le saisit une fois.";

            lblEtat = new Label();
            lblEtat.Dock = DockStyle.Bottom;
            lblEtat.Height = 42;
            lblEtat.Text = SecretStore.Exists(InventoryApiService.SecretName)
                ? "Un mot de passe est déjà enregistré sur ce poste."
                : "Aucun mot de passe enregistré sur ce poste.";

            Controls.Add(t);
            Controls.Add(lblEtat);
            Controls.Add(note);
            Controls.Add(BuildBottom());
        }

        private Panel BuildBottom()
        {
            Panel bas = new Panel();
            bas.Dock = DockStyle.Bottom;
            bas.Height = 54;
            bas.Padding = new Padding(16, 9, 16, 11);

            Button btnTest = new Button();
            btnTest.Text = "Tester et enregistrer";
            btnTest.Size = new Size(170, 32);
            btnTest.Dock = DockStyle.Right;
            btnTest.BackColor = Color.FromArgb(0, 90, 158);
            btnTest.ForeColor = Color.White;
            btnTest.FlatStyle = FlatStyle.Flat;
            btnTest.Click += new EventHandler(Test_Click);

            Button btnFermer = new Button();
            btnFermer.Text = "Fermer";
            btnFermer.Size = new Size(100, 32);
            btnFermer.Dock = DockStyle.Right;
            btnFermer.DialogResult = DialogResult.Cancel;

            Button btnOublier = new Button();
            btnOublier.Text = "Oublier le mot de passe";
            btnOublier.Size = new Size(170, 32);
            btnOublier.Dock = DockStyle.Left;
            btnOublier.Click += new EventHandler(Oublier_Click);

            bas.Controls.Add(btnOublier);
            bas.Controls.Add(btnFermer);
            bas.Controls.Add(btnTest);
            CancelButton = btnFermer;
            return bas;
        }

        private void AddRow(TableLayoutPanel t, string caption, Control champ)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            Label l = new Label();
            l.Text = caption;
            l.AutoSize = true;
            l.Margin = new Padding(0, 7, 8, 4);
            champ.Margin = new Padding(0, 4, 0, 6);
            t.Controls.Add(l, 0, row);
            t.Controls.Add(champ, 1, row);
        }

        /// <summary>Éprouve la connexion, et n'enregistre qu'en cas de succès.</summary>
        private void Test_Click(object sender, EventArgs e)
        {
            string url = txtUrl.Text.Trim();
            string user = txtUser.Text.Trim();
            string saisi = txtPassword.Text;
            string mdp = saisi;
            if (mdp == "") mdp = SecretStore.Load(InventoryApiService.SecretName);

            lblEtat.Text = "Connexion en cours…";
            Cursor = Cursors.WaitCursor;
            Application.DoEvents();
            try
            {
                using (InventoryApiService api = new InventoryApiService())
                {
                    string message;
                    if (!api.Connect(url, user, mdp, out message))
                    {
                        lblEtat.Text = message;
                        return;
                    }

                    string messageLecture;
                    api.LoadAll(out messageLecture);
                    lblEtat.Text = message + " " + messageLecture;

                    // La connexion fonctionne : on peut conserver les réglages.
                    _config.InventoryApiUrl = url;
                    _config.InventoryUser = user;
                    ConfigService.Save(_config);
                    if (saisi != "") SecretStore.Save(InventoryApiService.SecretName, saisi);
                    txtPassword.Text = "";
                }
            }
            catch (Exception ex)
            {
                lblEtat.Text = "Échec : " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void Oublier_Click(object sender, EventArgs e)
        {
            SecretStore.Delete(InventoryApiService.SecretName);
            txtPassword.Text = "";
            lblEtat.Text = "Mot de passe effacé de ce poste.";
        }
    }
}
