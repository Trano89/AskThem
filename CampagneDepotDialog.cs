using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Met la base articles à jour, depuis un poste équipé.
    ///
    /// La fenêtre commence toujours par un recensement, qui n'écrit rien et répond en quelques
    /// secondes à la question « où en est notre documentation ». La production ne part
    /// qu'ensuite, sur décision explicite : personne ne lance une heure de SolidWorks par
    /// inadvertance.
    /// </summary>
    public class CampagneDepotDialog : Form
    {
        private readonly AppConfig _config;
        private readonly Dictionary<string, string> _indexPdm;

        private Label lblTitre;
        private Label lblEtat;
        private CheckBox chkPieces, chkSousEnsembles, chkAssemblagesComplets;
        private CheckBox chkFabrique, chkAcheteModifie, chkFabriqueModifie, chkEnsembles;
        private NumericUpDown numMax;
        private Label lblMax;
        private ProgressBar barre;
        private TextBox txtJournal;
        private Button btnRecenser;
        private Button btnProduire;
        private Button btnRapport;
        private Button btnFermer;

        private volatile bool _annule;
        private volatile bool _occupe;
        private Thread _fil;
        private List<CampagneDepot.Candidat> _candidats;
        private string _dernierRapport = "";

        public CampagneDepotDialog(AppConfig config, Dictionary<string, string> indexPdm)
        {
            _config = config;
            _indexPdm = indexPdm;

            Text = "Base articles — mise à jour";
            Font = AppFont.Get();
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 562);
            MinimumSize = new Size(700, 520);
            BackColor = Color.White;

            Construire();
        }

        private void Construire()
        {
            lblTitre = new Label();
            lblTitre.Text = "Mettre à jour la base articles";
            lblTitre.Font = new Font(AppFont.Family, 15F, FontStyle.Bold);
            lblTitre.Location = new Point(18, 16);
            lblTitre.AutoSize = true;

            lblEtat = new Label();
            lblEtat.Location = new Point(20, 48);
            lblEtat.Size = new Size(680, 38);
            lblEtat.ForeColor = Color.DimGray;
            lblEtat.Text = "Le recensement lit le coffre et la base, sans rien écrire. La production ne part"
                         + Environment.NewLine + "qu'ensuite, sur ce que vous avez coché ci-dessous.";

            // Deux axes, ceux de la codification : la structure dit QUOI, l'origine dit
            // lesquels ont des documents a publier. Les references de projet, categorie #,
            // ne sont jamais proposees.
            GroupBox grpStructure = new GroupBox();
            grpStructure.Text = "Structure";
            grpStructure.Location = new Point(20, 92);
            grpStructure.Size = new Size(230, 108);

            chkPieces = Case("Pièces", 14, 24, true);
            chkSousEnsembles = Case("Sous-ensembles et prémontages", 14, 50, false);
            chkAssemblagesComplets = Case("Assemblages complets", 14, 76, false);
            grpStructure.Controls.AddRange(new Control[] { chkPieces, chkSousEnsembles, chkAssemblagesComplets });

            GroupBox grpOrigine = new GroupBox();
            grpOrigine.Text = "Origine";
            grpOrigine.Location = new Point(266, 92);
            grpOrigine.Size = new Size(434, 108);

            chkFabrique = Case("Fabriqué sur plan interne", 14, 24, true);
            chkAcheteModifie = Case("Acheté puis modifié", 14, 50, true);
            chkFabriqueModifie = Case("Fabriqué puis modifié", 224, 24, true);
            chkEnsembles = Case("Ensemble d'articles", 224, 50, false);

            Label lblProjets = new Label();
            lblProjets.Text = "Les références de projet (#) et les articles non gérés sont toujours exclus.";
            lblProjets.Location = new Point(14, 78);
            lblProjets.AutoSize = true;
            lblProjets.ForeColor = Color.DimGray;

            grpOrigine.Controls.AddRange(new Control[] { chkFabrique, chkAcheteModifie,
                                                         chkFabriqueModifie, chkEnsembles, lblProjets });

            lblMax = new Label();
            lblMax.Text = "Limiter à";
            lblMax.Location = new Point(20, 214);
            lblMax.AutoSize = true;

            numMax = new NumericUpDown();
            numMax.Location = new Point(84, 211);
            numMax.Size = new Size(70, 24);
            numMax.Minimum = 0;
            numMax.Maximum = 5000;
            numMax.Value = 0;

            Label lblMaxSuite = new Label();
            lblMaxSuite.Text = "article(s)   —   0 = tous. Une première mesure sur 20 donne le coût réel.";
            lblMaxSuite.Location = new Point(162, 214);
            lblMaxSuite.AutoSize = true;
            lblMaxSuite.ForeColor = Color.DimGray;

            btnRecenser = Bouton("Recenser", 20, 246);
            btnRecenser.Click += new EventHandler(Recenser_Click);

            btnProduire = Bouton("Produire et publier", 172, 246);
            btnProduire.Width = 170;
            btnProduire.Enabled = false;
            btnProduire.Click += new EventHandler(Produire_Click);

            btnRapport = Bouton("Ouvrir le rapport", 354, 246);
            btnRapport.Width = 150;
            btnRapport.Enabled = false;
            btnRapport.Click += new EventHandler(Rapport_Click);

            barre = new ProgressBar();
            barre.Location = new Point(20, 288);
            barre.Size = new Size(680, 14);
            barre.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            txtJournal = new TextBox();
            txtJournal.Location = new Point(20, 314);
            txtJournal.Size = new Size(680, 194);
            txtJournal.Multiline = true;
            txtJournal.ReadOnly = true;
            txtJournal.ScrollBars = ScrollBars.Vertical;
            txtJournal.BackColor = Color.White;
            txtJournal.Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                              | AnchorStyles.Left | AnchorStyles.Right;

            btnFermer = Bouton("Fermer", 600, 520);
            btnFermer.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnFermer.Click += new EventHandler(Fermer_Click);

            Controls.AddRange(new Control[] { lblTitre, lblEtat, grpStructure, grpOrigine,
                                              lblMax, numMax, lblMaxSuite,
                                              btnRecenser, btnProduire, btnRapport,
                                              barre, txtJournal, btnFermer });
        }

        /// <summary>Une case du périmètre, dans son groupe.</summary>
        private CheckBox Case(string texte, int x, int y, bool cochee)
        {
            CheckBox c = new CheckBox();
            c.Text = texte;
            c.Location = new Point(x, y);
            c.AutoSize = true;
            c.Checked = cochee;
            // Changer le perimetre invalide le recensement : il ne decrit plus ce qui est coche.
            c.CheckedChanged += new EventHandler(Perimetre_Change);
            return c;
        }

        /// <summary>
        /// Un changement de périmètre périme le recensement.
        ///
        /// Sans cela, on produirait la liste établie avec les cases précédentes, ce qui
        /// donnerait le sentiment que les cases n'ont pas d'effet.
        /// </summary>
        private void Perimetre_Change(object sender, EventArgs e)
        {
            if (_occupe) return;
            if (_candidats == null || _candidats.Count == 0) return;

            _candidats = null;
            btnProduire.Enabled = false;
            Journal("Périmètre modifié : relancez un recensement.");
        }

        /// <summary>
        /// Lance un travail sur un fil STA.
        ///
        /// La campagne ouvre des centaines de documents SolidWorks en COM, ce qui exige un
        /// appartement mono-fil. Un fil du pool est MTA : tout y passe par marshalling, ce
        /// qui est au mieux lent et au pire instable sur plusieurs centaines d'appels.
        /// </summary>
        private void Lancer(ThreadStart travail)
        {
            _fil = new Thread(travail);
            _fil.SetApartmentState(ApartmentState.STA);
            _fil.IsBackground = true;
            _fil.Start();
        }

        /// <summary>Session de l'inventaire, ou null si ce n'est pas lui qui porte les documents.</summary>
        private DepotInventaire Inventaire()
        {
            if (!_config.DocumentsDansInventaire) return null;

            DepotInventaire inv = new DepotInventaire(_config);
            string motif;
            if (inv.Connecter(out motif))
            {
                Journal(motif);
                return inv;
            }
            Journal("Inventaire indisponible : " + motif);
            inv.Dispose();
            return null;
        }

        /// <summary>Références codifiées présentes dans l'index du coffre.</summary>
        private List<string> ReferencesDuCoffre()
        {
            List<string> refs = new List<string>();
            if (_indexPdm == null) return refs;
            foreach (string cle in _indexPdm.Keys)
            {
                string n = System.IO.Path.GetFileNameWithoutExtension(cle);
                if (string.IsNullOrWhiteSpace(n)) continue;
                string numero = PartNumberFormat.Normalize(n, _config.PartNumberPatterns);
                if (PartNumberFormat.IsValid(numero, _config.PartNumberPatterns) && !refs.Contains(numero))
                    refs.Add(numero);
            }
            return refs;
        }

        private Button Bouton(string texte, int x, int y)
        {
            Button b = new Button();
            b.Text = texte;
            b.Location = new Point(x, y);
            b.Size = new Size(140, 30);
            b.FlatStyle = FlatStyle.System;
            return b;
        }

        // ------------------------------------------------------------------ journal

        private void Journal(string message)
        {
            if (txtJournal.IsDisposed) return;
            if (txtJournal.InvokeRequired)
            {
                try { txtJournal.BeginInvoke(new Action<string>(Journal), message); }
                catch (Exception) { }
                return;
            }
            txtJournal.AppendText(message + Environment.NewLine);
        }

        private void Avancement(int fait, int total, string article)
        {
            if (barre.IsDisposed) return;
            if (barre.InvokeRequired)
            {
                try { barre.BeginInvoke(new Action<int, int, string>(Avancement), fait, total, article); }
                catch (Exception) { }
                return;
            }
            barre.Maximum = Math.Max(1, total);
            barre.Value = Math.Min(fait, barre.Maximum);
            lblTitre.Text = "Base articles — " + fait + "/" + total + " : " + article;
        }

        private void Occupe(bool occupe)
        {
            _occupe = occupe;
            if (InvokeRequired) { try { BeginInvoke(new Action<bool>(Occupe), occupe); } catch (Exception) { } return; }

            btnRecenser.Enabled = !occupe;
            numMax.Enabled = !occupe;
            foreach (CheckBox c in new CheckBox[] { chkPieces, chkSousEnsembles, chkAssemblagesComplets,
                                                    chkFabrique, chkAcheteModifie, chkFabriqueModifie,
                                                    chkEnsembles })
                c.Enabled = !occupe;
            btnProduire.Enabled = true;
            btnProduire.Text = occupe ? "Interrompre" : "Produire et publier";
            btnFermer.Enabled = !occupe;
            if (!occupe) _annule = false;
        }

        // ------------------------------------------------------------------ recensement

        private CampagneDepot.Options OptionsChoisies()
        {
            CampagneDepot.Options o = new CampagneDepot.Options();
            o.MaxArticles = (int)numMax.Value;

            o.Structures = new List<char>();
            if (chkPieces.Checked) o.Structures.Add(Codification.Piece);
            if (chkSousEnsembles.Checked) o.Structures.Add(Codification.SousEnsemble);
            if (chkAssemblagesComplets.Checked) o.Structures.Add(Codification.AssemblageComplet);

            o.Origines = new List<char>();
            if (chkFabrique.Checked) o.Origines.Add(Codification.Fabrique);
            if (chkAcheteModifie.Checked) o.Origines.Add(Codification.AcheteModifie);
            if (chkFabriqueModifie.Checked) o.Origines.Add(Codification.FabriqueModifie);
            if (chkEnsembles.Checked) o.Origines.Add(Codification.EnsembleArticles);

            o.InclureAssemblages = chkSousEnsembles.Checked || chkAssemblagesComplets.Checked;
            return o;
        }

        /// <summary>Vrai si au moins une case de chaque axe est cochée.</summary>
        private bool PerimetreValide()
        {
            bool structure = chkPieces.Checked || chkSousEnsembles.Checked || chkAssemblagesComplets.Checked;
            bool origine = chkFabrique.Checked || chkAcheteModifie.Checked
                        || chkFabriqueModifie.Checked || chkEnsembles.Checked;
            if (structure && origine) return true;

            MessageBox.Show(this,
                "Choisissez au moins une structure et au moins une origine : sans cela, "
              + "le recensement ne porterait sur aucun article.",
                "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private void Recenser_Click(object sender, EventArgs e)
        {
            if (!PerimetreValide()) return;
            txtJournal.Clear();
            Occupe(true);
            btnProduire.Enabled = false;

            Lancer(delegate
            {
                try
                {
                    DepotArticles depot = new DepotArticles(_config);
                    CampagneDepot campagne = new CampagneDepot(_config, depot, Journal,
                                                               delegate { return _annule; });

                    CampagneDepot.Options o = OptionsChoisies();
                    o.RecensementSeul = true;

                    using (DepotInventaire inv = Inventaire())
                    {
                        if (inv != null)
                        {
                            campagne.PublierDansInventaire(inv);
                            inv.Charger(ReferencesDuCoffre(), Journal);
                        }
                        else if (!depot.Lisible())
                        {
                            Journal("ATTENTION : aucune base documentaire n'est joignable depuis ce poste.");
                        }

                        _candidats = campagne.Recenser(_indexPdm, o);
                        CampagneDepot.Bilan bilan = campagne.Executer(_candidats, o, Avancement);
                        _dernierRapport = bilan.CheminRapport;
                        Resume(bilan, true);
                    }
                }
                catch (Exception ex)
                {
                    Journal("ERREUR : " + ex.Message);
                }
                finally
                {
                    Occupe(false);
                    ActiverSuite();
                }
            });
        }

        private void ActiverSuite()
        {
            if (InvokeRequired) { try { BeginInvoke(new Action(ActiverSuite)); } catch (Exception) { } return; }
            btnProduire.Enabled = _candidats != null && _candidats.Count > 0;
            btnRapport.Enabled = !string.IsNullOrWhiteSpace(_dernierRapport) && File.Exists(_dernierRapport);
            lblTitre.Text = "Mettre à jour la base articles";
        }

        // ------------------------------------------------------------------ production

        private void Produire_Click(object sender, EventArgs e)
        {
            if (_occupe)
            {
                // Sans retour immediat, l'utilisateur reclique en croyant que rien ne s'est
                // passe : l'arret prend le temps de finir l'article en cours.
                _annule = true;
                btnProduire.Enabled = false;
                btnProduire.Text = "Interruption…";
                lblTitre.Text = "Base articles — interruption en cours";
                Journal("Interruption demandée : l'article en cours se termine, "
                      + "puis SolidWorks sera refermé proprement.");
                return;
            }
            if (_candidats == null || _candidats.Count == 0) return;

            int aFaire = 0;
            foreach (CampagneDepot.Candidat c in _candidats)
                if (CampagneDepot.EstAFaire(c.Verdict)) aFaire++;

            if (aFaire == 0)
            {
                // Dire pourquoi, et non seulement qu'il n'y a rien : « deja a jour » et
                // « aucun de vos articles n'existe dans l'inventaire » appellent des gestes
                // tres differents.
                MessageBox.Show(this, RaisonDeNeRienFaire(),
                    "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (SolidWorksExporter.IsSolidWorksRunning())
            {
                MessageBox.Show(this,
                    "Une session SolidWorks est ouverte sur ce poste." + Environment.NewLine + Environment.NewLine
                  + "Une campagne ouvre et ferme des centaines de documents et redémarre SolidWorks "
                  + "régulièrement : votre travail en cours serait perdu." + Environment.NewLine + Environment.NewLine
                  + "Fermez SolidWorks, puis relancez la production.",
                    "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int limite = (int)numMax.Value;
            string combien = limite > 0 && limite < aFaire ? limite.ToString() : aFaire.ToString();

            if (MessageBox.Show(this,
                    combien + " article(s) vont être produits et publiés dans la base." + Environment.NewLine
                  + Environment.NewLine
                  + "SolidWorks va s'ouvrir et se fermer plusieurs fois. L'opération peut durer "
                  + "longtemps ; elle est interruptible et reprend là où elle s'est arrêtée."
                  + Environment.NewLine + Environment.NewLine + "Lancer maintenant ?",
                    "AskThem", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _annule = false;
            Occupe(true);

            Lancer(delegate
            {
                try
                {
                    DepotArticles depot = new DepotArticles(_config);
                    CampagneDepot campagne = new CampagneDepot(_config, depot, Journal,
                                                               delegate { return _annule; });

                    using (DepotInventaire inv = Inventaire())
                    {
                        if (inv != null)
                        {
                            if (!inv.PeutPublier)
                            {
                                Journal("Votre compte ne peut pas déposer de documents dans "
                                      + "l'inventaire : la campagne s'arrête ici.");
                                return;
                            }
                            campagne.PublierDansInventaire(inv);
                            inv.Charger(ReferencesDuCoffre(), Journal);
                        }
                        else
                        {
                            string raison;
                            if (!depot.Amorcer(out raison))
                            {
                                Journal("Aucune base accessible en écriture — " + raison);
                                return;
                            }
                        }

                        CampagneDepot.Bilan bilan = campagne.Executer(_candidats, OptionsChoisies(), Avancement);
                        _dernierRapport = bilan.CheminRapport;
                        Resume(bilan, false);
                    }
                }
                catch (Exception ex)
                {
                    Journal("ERREUR : " + ex.Message);
                }
                finally
                {
                    Occupe(false);
                    ActiverSuite();
                }
            });
        }

        /// <summary>Pourquoi le recensement ne laisse rien à produire.</summary>
        private string RaisonDeNeRienFaire()
        {
            int aJour = 0, horsInv = 0, sansSource = 0, ignores = 0;
            foreach (CampagneDepot.Candidat c in _candidats)
            {
                if (c.Verdict == CampagneDepot.AJour) aJour++;
                else if (c.Verdict == CampagneDepot.HorsInventaire) horsInv++;
                else if (c.Verdict == CampagneDepot.SansSource) sansSource++;
                else ignores++;
            }

            if (horsInv > 0 && aJour == 0)
                return horsInv + " article(s) du coffre n'ont pas de fiche dans l'inventaire, "
                     + "et aucun n'est à jour." + Environment.NewLine + Environment.NewLine
                     + "AskThem ne crée jamais d'article : les fiches sont à créer côté "
                     + "inventaire avant de pouvoir y publier des documents. La liste figure "
                     + "dans le rapport.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Rien à produire sur le périmètre choisi.");
            sb.AppendLine();
            if (aJour > 0) sb.AppendLine(aJour + " article(s) déjà à jour.");
            if (horsInv > 0) sb.AppendLine(horsInv + " sans fiche dans l'inventaire (à créer là-bas).");
            if (sansSource > 0) sb.AppendLine(sansSource + " sans plan ni modèle dans le coffre.");
            if (ignores > 0) sb.AppendLine(ignores + " hors périmètre.");
            sb.AppendLine();
            sb.Append("Élargissez le périmètre avec les cases, puis relancez un recensement.");
            return sb.ToString();
        }

        private void Resume(CampagneDepot.Bilan b, bool recensement)
        {
            Journal("");
            Journal("──────────────────────────────────────────────");
            Journal("Examinés ........... " + b.Candidats);
            Journal("Déjà à jour ........ " + b.AJour);
            if (!recensement)
            {
                Journal("Publiés ............ " + b.Produits);
                Journal("Remplacés .......... " + b.Remplaces);
                Journal("Non libérés ........ " + b.NonLiberes);
                Journal("Échecs ............. " + b.Echecs);
            }
            else
            {
                int aFaire = 0;
                foreach (CampagneDepot.Candidat c in _candidats)
                    if (CampagneDepot.EstAFaire(c.Verdict)) aFaire++;
                Journal("À produire ou remplacer  " + aFaire);
            }
            Journal("Sans source CAO .... " + b.SansSource);
            if (b.HorsInventaire > 0)
                Journal("Sans fiche inventaire " + b.HorsInventaire + "  (à créer côté inventaire)");
            Journal("Ignorés ............ " + b.Ignores);
            if (b.Orphelins.Count > 0)
                Journal("Archives sans source dans le coffre : " + b.Orphelins.Count + " (voir le rapport)");
            Journal("Durée .............. " + b.Duree.ToString(@"hh\:mm\:ss"));
        }

        private void Rapport_Click(object sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_dernierRapport) && File.Exists(_dernierRapport))
                    Process.Start(new ProcessStartInfo(_dernierRapport) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Le rapport n'a pas pu être ouvert : " + ex.Message,
                    "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Fermer_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_occupe)
            {
                // Fermer pendant une campagne laisserait SolidWorks piloté par personne.
                if (MessageBox.Show(this,
                        "Une campagne est en cours." + Environment.NewLine + Environment.NewLine
                      + "Elle va s'interrompre à la fin de l'article en cours, puis SolidWorks "
                      + "sera refermé proprement. Patienter et fermer ?",
                        "AskThem", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _annule = true;

                // On attend reellement la fin : rendre la main pendant qu'un fil pilote encore
                // SolidWorks permettrait de lancer une demande sur la meme instance COM, que
                // la campagne refermerait en plein milieu.
                if (_fil != null && _fil.IsAlive)
                {
                    Cursor = Cursors.WaitCursor;
                    try { _fil.Join(TimeSpan.FromMinutes(3)); }
                    finally { Cursor = Cursors.Default; }
                }
            }
            base.OnFormClosing(e);
        }
    }
}
