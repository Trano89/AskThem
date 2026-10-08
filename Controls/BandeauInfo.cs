using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AskThem.Services;

namespace AskThem.Controls
{
    /// <summary>Niveau d'un bilan : il décide des couleurs, jamais seul du sens.</summary>
    public enum NiveauBilan
    {
        Succes,
        Attention,
        Erreur,
        Info
    }

    /// <summary>Ce qu'un traitement a donné, dit en une phrase et quelques détails.</summary>
    public sealed class Bilan
    {
        public NiveauBilan Niveau = NiveauBilan.Info;
        public string Titre = "";
        public string Texte = "";

        /// <summary>Dossier à ouvrir, s'il y en a un.</summary>
        public string Dossier = "";

        /// <summary>Le détail complet, montré à la demande : le bandeau n'en garde que l'essentiel.</summary>
        public string Details = "";

        /// <summary>Vrai après une génération réussie : on peut passer à la demande suivante.</summary>
        public bool Genere;
    }

    /// <summary>
    /// Bandeau de bilan, en tête d'écran : le résultat d'une vérification ou d'une génération
    /// dit clairement, au lieu d'un journal qu'on ne lit pas — et que le mode guidé n'a même
    /// pas.
    /// </summary>
    public class BandeauInfo : Panel
    {
        private readonly Label _titre;
        private readonly Label _texte;
        private readonly FlowLayoutPanel _actions;
        private readonly TableLayoutPanel _grille;

        public BandeauInfo()
        {
            Visible = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            Padding = new Padding(1);
            Margin = new Padding(0, 0, 0, 8);

            _titre = Ui.Libelle("", AppFont.Section(), Theme.Texte);
            _texte = Ui.Libelle("", AppFont.Get(), Theme.Texte);
            _actions = Ui.Rangee();
            _actions.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            LinkLabel fermer = Ui.Lien("Fermer", delegate { Masquer(); });
            fermer.Margin = new Padding(16, 6, 0, 0);

            _grille = new TableLayoutPanel();
            _grille.Dock = DockStyle.Fill;
            _grille.AutoSize = true;
            _grille.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grille.ColumnCount = 3;
            _grille.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _grille.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _grille.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _grille.RowCount = 2;
            _grille.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _grille.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _grille.Padding = new Padding(12, 10, 12, 10);
            _grille.Controls.Add(_titre, 0, 0);
            _grille.Controls.Add(_texte, 0, 1);
            _grille.Controls.Add(_actions, 1, 0);
            _grille.SetRowSpan(_actions, 2);
            _grille.Controls.Add(fermer, 2, 0);
            Controls.Add(_grille);
        }

        /// <summary>
        /// La hauteur suit le texte replié à la largeur offerte : un panneau ne sait pas la
        /// calculer seul, et la dernière ligne du bilan était rognée.
        /// </summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            int largeur = proposedSize.Width > 0 && proposedSize.Width < int.MaxValue / 2
                ? proposedSize.Width : Math.Max(Width, 400);
            Size g = _grille.GetPreferredSize(new Size(Math.Max(1, largeur - Padding.Horizontal), 0));
            return new Size(largeur, g.Height + Padding.Vertical);
        }

        /// <summary>Affiche un bilan, avec des actions éventuelles (libellé, effet).</summary>
        public void Afficher(Bilan b, params Tuple<string, Action>[] actions)
        {
            if (b == null) { Masquer(); return; }
            Color texte, fond;
            switch (b.Niveau)
            {
                case NiveauBilan.Succes: texte = Theme.Succes; fond = Theme.SuccesFond; break;
                case NiveauBilan.Attention: texte = Theme.Attention; fond = Theme.AttentionFond; break;
                case NiveauBilan.Erreur: texte = Theme.Erreur; fond = Theme.ErreurFond; break;
                default: texte = Theme.Info; fond = Theme.InfoFond; break;
            }
            BackColor = texte;           // la bordure d'un pixel
            _grille.BackColor = fond;
            _titre.ForeColor = texte;
            _titre.Text = b.Titre;
            _texte.Text = b.Texte;
            _texte.Visible = !string.IsNullOrWhiteSpace(b.Texte);

            _actions.Controls.Clear();
            if (!string.IsNullOrWhiteSpace(b.Details))
            {
                string details = b.Details;
                string titre = b.Titre;
                Button bouton = Ui.Secondaire("Détails…");
                bouton.Click += delegate
                {
                    MessageBox.Show(FindForm(), details, "AskThem — " + titre,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                };
                _actions.Controls.Add(bouton);
            }
            if (!string.IsNullOrWhiteSpace(b.Dossier))
            {
                string dossier = b.Dossier;
                Button bouton = Ui.Secondaire("Ouvrir le dossier");
                bouton.Click += delegate { OuvrirDossier(dossier); };
                _actions.Controls.Add(bouton);
            }
            if (actions != null)
            {
                foreach (Tuple<string, Action> a in actions)
                {
                    if (a == null) continue;
                    Action effet = a.Item2;
                    Button bouton = Ui.Secondaire(a.Item1);
                    bouton.Click += delegate { effet(); };
                    _actions.Controls.Add(bouton);
                }
            }
            Visible = true;
        }

        public void Masquer()
        {
            Visible = false;
        }

        /// <summary>
        /// Le dossier d'une demande quitte le poste pour l'archive réseau dès que l'email est
        /// parti : il peut ne plus être là où le bilan l'a vu.
        /// </summary>
        private void OuvrirDossier(string dossier)
        {
            if (!Directory.Exists(dossier))
            {
                MessageBox.Show(FindForm(),
                    "Ce dossier n'est plus sur le poste : l'email est parti, et la demande a "
                  + "rejoint l'archive réseau.", "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Process.Start("explorer.exe", "\"" + dossier + "\""); }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "Le dossier ne s'ouvre pas : " + ex.Message, "AskThem",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
