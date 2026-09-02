using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AskThem.Config;
using AskThem.Inspection;
using AskThem.Models;
using AskThem.Pdf;
using AskThem.Services;
// Alias cible : l'interop SolidWorks expose aussi un type Environment, qui masquerait System.Environment.
using ModelDoc2 = SolidWorks.Interop.sldworks.ModelDoc2;

namespace AskThem
{
    /// <summary>Fenêtre unique de l'application. Toute l'interface est construite ici, en code.</summary>
    public class MainForm : Form
    {
        // ------------------------------------------------------------------
        // Données
        // ------------------------------------------------------------------
        private BindingList<PartLine> _lines = new BindingList<PartLine>();
        private AppConfig _config;
        private List<Supplier> _suppliers = new List<Supplier>();
        private Dictionary<string, string> _pdmIndex;
        private Dictionary<string, InventoryService.Entry> _inventaire;
        private List<PartLine> _work;
        private volatile bool _cancelRequested;
        private bool _busy;

        // Options figées au démarrage du traitement (lues sur le thread interface).
        private bool _generateMode;
        private bool _opt3D;
        private bool _opt2D;
        private bool _optControle;
        private bool _optCatalogue;
        private int _optFournisseurInventaire;
        private CompressionLevel _optCompression = CompressionLevel.Optimal;
        private ControleFabricationConfig _controleCfg;
        private IGenerateurPdf _generateurPdf;
        private string _journalControles = "";
        private string _optSupplier = "";
        private string _optSupplierCc = "";
        private string _optSupplierName = "";
        private string _optProject = "";
        private string _optDeadline = "";
        private string _optConditions = "";
        private string _optPoPath = "";
        private string _archivePath = null;
        private volatile bool _stopMailWatch;
        private UpdateService.UpdateInfo _update;
        private RequestType _optType = RequestType.Offre;

        // ------------------------------------------------------------------
        // Contrôles
        // ------------------------------------------------------------------
        private Panel panelTop;
        private ComboBox cboType;
        private Label lblInfo;

        private Panel panelTools;
        private Button btnAddLine;
        private Button btnPaste;
        private Button btnImportCsv;
        private Button btnExportCsv;
        private Button btnClear;
        private Button btnInventaire;
        private Label pastilleInventaire;
        private volatile bool inventaireConnecte;

        private DataGridView grid;
        private DataGridViewTextBoxColumn colPartNumber;
        private DataGridViewTextBoxColumn colQty1;
        private DataGridViewTextBoxColumn colQty2;
        private DataGridViewTextBoxColumn colQty3;
        private DataGridViewTextBoxColumn colRemark;

        // Volet de detail : restitue ce que la grille n'affiche plus.
        private Panel panelDetail;
        private Label lblDetailTitre;
        private Label valDescription;
        private Label valRevPlan;
        private Label valRevModele;
        private Label valDate;
        private Label valMatiere;
        private Label valFinitions;
        private Label valEtatPdm;
        private Label valStatut;
        private Label valFichiers;

        private Panel panelParams;
        private ComboBox cboSupplier;
        private Button btnSuppliers;
        private Button btnRecherche;
        private Button btnAssistant;
        private ToolTip toolTip = new ToolTip();
        private TextBox txtProject;
        private DateTimePicker dtpDeadline;
        private CheckBox chk3D;
        private CheckBox chk2D;
        private CheckBox chkControleFabrication;
        private ComboBox cboCompression;
        private ContextMenuStrip menuGrille;
        private ToolStripMenuItem mnuControleFabrication;
        private TextBox txtConditions;
        private Label lblPo;
        private TextBox txtPo;
        private Button btnPo;
        private Panel groupePo;
        private Button btnVerify;
        private Button btnGenerate;

        private Panel panelStatus;

        // Separateurs deplacables : l'utilisateur repartit l'espace comme il l'entend.
        private SplitContainer splitPrincipal;
        private SplitContainer splitCentre;
        private SplitContainer splitBas;

        // Hauteurs souhaitees au demarrage, deduites du contenu.
        private int hauteurParams;
        private int hauteurStatus;
        private int largeurDetail;
        private bool separateurDeplaceParUtilisateur;
        private Panel panelStatusLine;
        private ProgressBar progress;
        private Label lblProgress;
        private Button btnCancel;
        private Button btnUpdate;
        private TextBox txtLog;

        public MainForm()
        {
            _config = ConfigService.Load();

            // --- Fenêtre ---
            // Sans mise a l'echelle explicite, les polices suivent la densite d'ecran
            // mais pas les hauteurs de panneaux : les controles se retrouvent rognes
            // lors d'une session distante ou sur un ecran a densite differente.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Text = "AskThem " + UpdateService.CurrentVersion();
            AppIcon.Apply(this);
            Font = AppFont.Get();
            Size = new Size(1180, 760);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;

            BuildTopPanel();
            BuildToolsPanel();
            BuildGrid();
            BuildDetailPanel();
            BuildParamsPanel();
            BuildStatusPanel();

            AssembleAvecSeparateurs();

            ApplyMode();
            LoadSuppliers();
            Log("AskThem " + UpdateService.CurrentVersion() + " prêt. Coffre PDM : " + _config.PdmRoot);
            StartUpdateCheck();
            LancerVerificationInventaire();
            Log("Dossier des exports : " + _config.OutputRoot);
            _ouvrirAssistantAuDemarrage = true;
        }

        // ==================================================================
        // Construction de l'interface
        // ==================================================================

        private void BuildTopPanel()
        {
            panelTop = new Panel();
            panelTop.Dock = DockStyle.Top;

            // Trois natures de demande : un interrupteur ne pouvait plus les exprimer.
            Label lblType = new Label();
            lblType.Font = AppFont.Get();
            lblType.Text = "Type de demande :";
            lblType.Location = new Point(12, 20);
            lblType.AutoSize = true;

            cboType = new ComboBox();
            cboType.Font = AppFont.Get();
            cboType.DropDownStyle = ComboBoxStyle.DropDownList;
            cboType.Location = new Point(lblType.Right + 10, 16);
            cboType.Width = AppFont.Width(RequestTypes.Libelle(RequestType.Fabrication), 60);
            foreach (RequestType t in new RequestType[] {
                         RequestType.Offre, RequestType.Fabrication, RequestType.CommandeCatalogue })
                cboType.Items.Add(new ChoixTypeDemande(t));
            cboType.SelectedIndex = 0;
            cboType.SelectedIndexChanged += new EventHandler(TypeDemande_Change);

            lblInfo = new Label();
            lblInfo.Font = AppFont.Get();
            lblInfo.Text = "Saisissez ou collez (Ctrl+V depuis Excel) vos numéros d'article.";
            lblInfo.ForeColor = Color.Gray;
            lblInfo.Location = new Point(cboType.Right + 32, 20);
            lblInfo.AutoSize = true;

            btnAssistant = new Button();
            btnAssistant.Font = AppFont.Get();
            btnAssistant.Text = "Assistant pas à pas…";
            btnAssistant.Size = new Size(AppFont.Width(btnAssistant.Text, 34), 30);
            btnAssistant.Click += new EventHandler(BtnAssistant_Click);

            panelTop.Controls.Add(lblType);
            panelTop.Controls.Add(cboType);
            panelTop.Controls.Add(btnAssistant);
            panelTop.Height = Math.Max(cboType.Height, lblInfo.PreferredHeight) + 34;
            panelTop.Controls.Add(lblInfo);
            panelTop.Resize += new EventHandler(delegate (object s, EventArgs e)
            {
                btnAssistant.Location = new Point(
                    Math.Max(cboType.Right + 20, panelTop.Width - btnAssistant.Width - 16), 16);
            });
        }

        private void BuildToolsPanel()
        {
            panelTools = new Panel();
            panelTools.Dock = DockStyle.Top;

            btnAddLine = MakeToolButton("Ajouter ligne");
            btnAddLine.Click += new EventHandler(BtnAddLine_Click);

            btnPaste = MakeToolButton("Coller Excel");
            btnPaste.Click += new EventHandler(BtnPaste_Click);

            btnImportCsv = MakeToolButton("Importer liste");
            btnImportCsv.Click += new EventHandler(BtnImportCsv_Click);

            btnExportCsv = MakeToolButton("Exporter CSV");
            btnExportCsv.Click += new EventHandler(BtnExportCsv_Click);

            btnClear = MakeToolButton("Tout vider");
            btnClear.Click += new EventHandler(BtnClear_Click);

            btnInventaire = MakeToolButton("Inventaire…");
            btnInventaire.Click += new EventHandler(BtnInventaire_Click);

            // Les boutons s'enchaînent selon leur largeur mesurée : aucune position figée.
            int x = 12;
            foreach (Button b in new Button[] { btnAddLine, btnPaste, btnImportCsv,
                                                btnExportCsv, btnClear, btnInventaire })
            {
                b.Location = new Point(x, 8);
                panelTools.Controls.Add(b);
                x += b.Width + 8;
            }
            pastilleInventaire = new Label();
            pastilleInventaire.Size = new Size(14, 14);
            pastilleInventaire.Location = new Point(btnInventaire.Right + 8, btnInventaire.Top + (btnInventaire.Height - 14) / 2);
            using (System.Drawing.Drawing2D.GraphicsPath rond = new System.Drawing.Drawing2D.GraphicsPath())
            {
                rond.AddEllipse(0, 0, 14, 14);
                pastilleInventaire.Region = new Region(rond);
            }
            panelTools.Controls.Add(pastilleInventaire);

            panelTools.Height = btnAddLine.Height + 16;
            AfficherEtatInventaire(false, "État de la connexion inconnu.");
        }

        /// <summary>Crée un bouton de la barre d'outils (140 x 30).</summary>
        private Button MakeToolButton(string text)
        {
            Button b = new Button();
            b.Font = AppFont.Get();      // avant toute mesure : l'héritage n'a pas encore eu lieu
            b.Text = text;
            b.Width = AppFont.Width(text, 34);
            b.Height = 32;
            return b;
        }

        private void BuildGrid()
        {
            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = true;
            grid.AllowUserToDeleteRows = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowHeadersWidth = 30;
            grid.ColumnHeadersHeight = AppFont.Width("Hg", 0) > 0 ? 34 : 34;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
            grid.RowTemplate.Height = 30;
            grid.VirtualMode = false;
            grid.AutoGenerateColumns = false;
            // Un seul clic suffit pour modifier une cellule (au lieu du double-clic par defaut).
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;

            // Seules les colonnes que l'utilisateur remplit lui-meme.
            colPartNumber = MakeColumn("colPartNumber", "N° article", "PartNumber", 28, false);
            colQty1 = MakeColumn("colQty1", "Qté 1", "Qty1", 12, false);
            colQty2 = MakeColumn("colQty2", "Qté 2", "Qty2", 12, false);
            colQty3 = MakeColumn("colQty3", "Qté 3", "Qty3", 12, false);
            colRemark = MakeColumn("colRemark", "Remarque", "Remark", 36, false);

            grid.Columns.Add(colPartNumber);
            grid.Columns.Add(colQty1);
            grid.Columns.Add(colQty2);
            grid.Columns.Add(colQty3);
            grid.Columns.Add(colRemark);

            grid.DataSource = _lines;
            mnuControleFabrication = new ToolStripMenuItem("Contrôle de fabrication… (bêta)");
            mnuControleFabrication.Click += new EventHandler(MnuControleFabrication_Click);
            menuGrille = new ContextMenuStrip();
            menuGrille.Items.Add(mnuControleFabrication);
            menuGrille.Opening += new CancelEventHandler(MenuGrille_Ouverture);
            grid.ContextMenuStrip = menuGrille;
            grid.MouseDown += new MouseEventHandler(Grid_MouseDown);

            grid.CellFormatting += new DataGridViewCellFormattingEventHandler(Grid_CellFormatting);
            grid.KeyDown += new KeyEventHandler(Grid_KeyDown);
            grid.DataError += new DataGridViewDataErrorEventHandler(Grid_DataError);
            grid.SelectionChanged += new EventHandler(Grid_SelectionChanged);
            grid.CellValidating += new DataGridViewCellValidatingEventHandler(Grid_CellValidating);
            grid.CellEndEdit += new DataGridViewCellEventHandler(Grid_CellEndEdit);
        }

        /// <summary>Crée une colonne liée à une propriété de PartLine.</summary>
        private DataGridViewTextBoxColumn MakeColumn(string name, string header, string property, int weight, bool readOnly)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.DataPropertyName = property;
            c.FillWeight = weight;
            c.ReadOnly = readOnly;
            c.MinimumWidth = AppFont.Width(header, 26);
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return c;
        }

        /// <summary>Colore la ligne selon son statut.</summary>
        /// <summary>Un clic droit selectionne la ligne visee avant d'ouvrir le menu.</summary>
        private void Grid_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            DataGridView.HitTestInfo cible = grid.HitTest(e.X, e.Y);
            if (cible.RowIndex < 0 || cible.RowIndex >= _lines.Count) return;
            grid.ClearSelection();
            grid.Rows[cible.RowIndex].Selected = true;
            grid.CurrentCell = grid.Rows[cible.RowIndex].Cells[0];
        }

        /// <summary>Le menu ne s'ouvre que sur une ligne portant un numero d'article.</summary>
        private void MenuGrille_Ouverture(object sender, CancelEventArgs e)
        {
            PartLine ligne = LigneSelectionnee();
            if (_busy || ligne == null || string.IsNullOrWhiteSpace(ligne.PartNumber)) { e.Cancel = true; return; }
            mnuControleFabrication.Text = "Contrôle de fabrication… (" + ligne.PartNumber + ") — bêta";
        }

        private PartLine LigneSelectionnee()
        {
            int i = grid.CurrentCell == null ? -1 : grid.CurrentCell.RowIndex;
            if (i < 0 || i >= _lines.Count) return null;
            return _lines[i];
        }

        /// <summary>Produit le contrôle de ce seul article, puis ouvre le PDF.</summary>
        private void MnuControleFabrication_Click(object sender, EventArgs e)
        {
            PartLine ligne = LigneSelectionnee();
            if (ligne == null || string.IsNullOrWhiteSpace(ligne.PartNumber)) return;
            if (!ConfirmerSolidWorks()) return;

            _optSupplierName = "";
            _optProject = txtProject.Text.Trim();
            Supplier fournisseur = SelectedSupplier;
            if (fournisseur != null) _optSupplierName = fournisseur.Name;

            PartLine cible = ligne;
            SetBusy(true);
            Thread worker = new Thread(delegate() { RunControleSeul(cible); });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        /// <summary>
        /// Controle a la demande : sa propre session SolidWorks, son propre dossier, hors
        /// de toute demande. Le fichier est ecrit dans le dossier de sortie local.
        /// </summary>
        private void RunControleSeul(PartLine ligne)
        {
            string pdf = null;
            SolidWorksExporter exporter = new SolidWorksExporter(_config.Properties);
            try
            {
                _controleCfg = ControleFabricationConfig.Load();
                TableSymbolesGtol.Charger(LogFromWorker);
                _generateurPdf = new QuestPdfGenerateur();

                string dossier = Path.Combine(_config.OutputRoot, "ControleFabrication");
                Directory.CreateDirectory(dossier);
                _journalControles = Path.Combine(dossier, "extraction.log");

                BuildPdmIndex();
                ligne.DrawingPath = PdmSearchService.FindDrawingInIndex(_pdmIndex, ligne.PartNumber);
                if (ligne.DrawingPath == null)
                {
                    Log("Aucun plan pour " + ligne.PartNumber + " : pas de contrôle de fabrication.");
                    return;
                }

                exporter.Connect();
                ModelDoc2 doc = null;
                try
                {
                    doc = exporter.OpenDocument(ligne.DrawingPath);
                    SolidWorksExporter.DocMetadata m = exporter.ReadMetadata(doc);
                    if (ligne.DrawingRevision == "") ligne.DrawingRevision = m.Revision;
                    if (ligne.Description == "") ligne.Description = m.Description;
                    if (ligne.Material == "") ligne.Material = m.Material;
                    if (ligne.Treatment == "") ligne.Treatment = m.Treatment;

                    int avant = ligne.ExportedFiles.Count;
                    pdf = GenererControle(doc, ligne, dossier);
                    // Ce controle isole n'appartient a aucune demande : il ne rejoint pas le ZIP.
                    if (ligne.ExportedFiles.Count > avant) ligne.ExportedFiles.RemoveAt(ligne.ExportedFiles.Count - 1);
                }
                finally
                {
                    exporter.CloseDocument(doc);
                }
            }
            catch (Exception ex)
            {
                Log("ERREUR contrôle de fabrication : " + ex.Message);
            }
            finally
            {
                exporter.Dispose();
                SetBusy(false);
            }

            if (pdf != null) OuvrirFichier(pdf);
        }

        /// <summary>Ouvre un fichier avec l'application par defaut du poste.</summary>
        private void OuvrirFichier(string chemin)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(chemin);
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log("Ouverture impossible : " + ex.Message);
            }
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            PartLine line = grid.Rows[e.RowIndex].DataBoundItem as PartLine;
            if (line == null) { e.CellStyle.BackColor = Color.White; return; }

            if (line.Status == "OK")
                e.CellStyle.BackColor = Color.FromArgb(230, 245, 230);
            else if (line.Status == "Manquant 3D" || line.Status == "Manquant 2D")
                e.CellStyle.BackColor = Color.FromArgb(255, 244, 214);
            else if (line.Status == "Introuvable" || line.Status == "Erreur")
                e.CellStyle.BackColor = Color.FromArgb(255, 224, 224);
            else
                e.CellStyle.BackColor = Color.White;
        }

        /// <summary>Ctrl+V dans la grille : import depuis le presse-papiers.</summary>
        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                PasteFromClipboard();
            }
        }

        /// <summary>Une saisie non numérique ne doit pas ouvrir de boîte d'erreur.</summary>
        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        // ==================================================================
        // Volet de detail : ce que le PDM fournit, hors de la grille de saisie
        // ==================================================================

        private void BuildDetailPanel()
        {
            panelDetail = new Panel();
            panelDetail.Dock = DockStyle.Right;
            largeurDetail = 390;
            panelDetail.Width = largeurDetail;
            panelDetail.Padding = new Padding(14, 10, 14, 10);
            panelDetail.BackColor = Color.FromArgb(247, 249, 250);

            lblDetailTitre = new Label();
            lblDetailTitre.Text = "Aucune ligne sélectionnée";
            lblDetailTitre.Dock = DockStyle.Top;
            lblDetailTitre.Height = 42;
            lblDetailTitre.Font = new Font(AppFont.Family, 11F, FontStyle.Bold);
            lblDetailTitre.TextAlign = ContentAlignment.MiddleLeft;

            Label note = new Label();
            note.Text = "Lu dans le coffre PDM au moment de la génération. Rien à saisir ici.";
            note.Dock = DockStyle.Bottom;
            note.Height = 58;
            note.ForeColor = Color.Gray;

            // TableLayoutPanel plutot que des positions en pixels : suit la densite d'ecran.
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 2;
            int largeurIntitules = 0;
            foreach (string c in new string[] { "Désignation", "Rév. plan", "Rév. modèle",
                                                "Date de réalisé", "Matière", "Finitions",
                                                "État PDM", "Statut", "Fichiers" })
            {
                largeurIntitules = Math.Max(largeurIntitules, AppFont.Width(c, 14));
            }
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, largeurIntitules));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.AutoScroll = true;

            valDescription = AddDetailRow(t, "Désignation");
            valRevPlan = AddDetailRow(t, "Rév. plan");
            valRevModele = AddDetailRow(t, "Rév. modèle");
            valDate = AddDetailRow(t, "Date de réalisé");
            valMatiere = AddDetailRow(t, "Matière");
            valFinitions = AddDetailRow(t, "Finitions");
            valEtatPdm = AddDetailRow(t, "État PDM");
            valStatut = AddDetailRow(t, "Statut");
            valFichiers = AddDetailRow(t, "Fichiers");

            panelDetail.Controls.Add(t);
            panelDetail.Controls.Add(note);
            panelDetail.Controls.Add(lblDetailTitre);
        }

        /// <summary>Ajoute une ligne intitulé / valeur au volet et retourne l'étiquette de valeur.</summary>
        private Label AddDetailRow(TableLayoutPanel t, string caption)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label c = new Label();
            c.Font = AppFont.Get();
            c.Text = caption;
            c.ForeColor = Color.Gray;
            c.AutoSize = true;
            c.Margin = new Padding(0, 7, 6, 1);

            Label v = new Label();
            v.Font = AppFont.Get();
            v.Text = "—";
            v.AutoSize = true;
            v.MaximumSize = new Size(216, 0);
            v.Margin = new Padding(0, 7, 0, 1);

            t.Controls.Add(c, 0, row);
            t.Controls.Add(v, 1, row);
            return v;
        }

        /// <summary>Refuse un numéro d'article qui ne respecte aucun format accepté.</summary>
        private void Grid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex] != colPartNumber) return;
            string brut = e.FormattedValue == null ? "" : e.FormattedValue.ToString();
            if (string.IsNullOrWhiteSpace(brut)) return;   // une ligne vide reste permise

            string normalise = PartNumberFormat.Normalize(brut, _config.PartNumberPatterns);
            if (PartNumberFormat.IsValid(normalise, _config.PartNumberPatterns))
            {
                ArticleTypeRule regle = RuleFor(normalise);
                if (!regle.Allowed)
                {
                    MessageBox.Show(
                        normalise + " est un " + regle.Label.ToLowerInvariant() + "." + Environment.NewLine +
                        "Aucune demande d'offre ni de fabrication n'est possible sur ce type d'article." +
                        Environment.NewLine + "Saisissez les pièces qui le composent.",
                        "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }

                string refus;
                if (!FournisseurVendCetArticle(normalise, regle, e.RowIndex, out refus))
                {
                    MessageBox.Show(refus, "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
                return;
            }

            MessageBox.Show(
                "Numéro d'article refusé : " + normalise + Environment.NewLine + Environment.NewLine +
                "Formats acceptés : " + PartNumberFormat.Describe(_config.PartNumberPatterns) + Environment.NewLine +
                "Les tirets sont ajoutés automatiquement : vous pouvez taper le numéro sans séparateur.",
                "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }

        /// <summary>
        /// Vrai si le destinataire choisi vend cet article de catalogue.
        ///
        /// Le contrôle a lieu à la saisie plutôt qu'à l'envoi : mieux vaut refuser une
        /// ligne tout de suite, en disant qui vend l'article, que laisser constituer une
        /// demande impossible. Il ne s'applique qu'aux achats catalogue, et seulement
        /// quand l'inventaire est chargé et un destinataire choisi — sans quoi il n'y a
        /// rien à comparer, et la ligne passe.
        /// </summary>
        private bool FournisseurVendCetArticle(string numero, ArticleTypeRule regle,
                                               int ligneSaisie, out string refus)
        {
            refus = "";
            if (!regle.Catalogue) return true;
            if (_inventaire == null || _inventaire.Count == 0) return true;

            Supplier destinataire = SelectedSupplier;
            if (destinataire == null || string.IsNullOrWhiteSpace(destinataire.Name)) return true;

            InventoryService.Entry inv = InventoryService.Lookup(_inventaire, numero);
            if (inv == null || inv.Fournisseurs.Count == 0) return true;   // signalé plus tard

            if (inv.Chez(destinataire.InventoryId, destinataire.Name) != null) return true;

            // Une ligne déjà saisie qu'on ne fait que reformater ne doit pas se voir refusée
            // deux fois : seul le contenu qui change est contrôlé.
            if (ligneSaisie >= 0 && ligneSaisie < _lines.Count
                && string.Equals(_lines[ligneSaisie].PartNumber, numero, StringComparison.OrdinalIgnoreCase))
                return true;

            refus = numero + " n'est pas vendu par « " + destinataire.Name + " »."
                  + Environment.NewLine + Environment.NewLine
                  + "Dans l'inventaire, cet article est déclaré chez : " + NomsFournisseurs(inv) + "."
                  + Environment.NewLine + Environment.NewLine
                  + "Choisissez ce destinataire, ou retirez cet article de la demande.";
            Log("Refusé à la saisie : " + numero + " n'est pas vendu par « " + destinataire.Name
              + " » mais par " + NomsFournisseurs(inv) + ".");
            return false;
        }

        /// <summary>Insère les tirets après la saisie, selon le format principal.</summary>
        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex] != colPartNumber) return;
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            PartLine l = grid.Rows[e.RowIndex].DataBoundItem as PartLine;
            if (l == null) return;

            string normalise = PartNumberFormat.Normalize(l.PartNumber, _config.PartNumberPatterns);
            if (normalise != l.PartNumber)
            {
                l.PartNumber = normalise;
                grid.InvalidateRow(e.RowIndex);
            }
            UpdateDetail();
        }

        /// <summary>
        /// Normalise les numéros ajoutés en lot et écarte ceux qui ne respectent aucun
        /// format. Un SEUL message récapitule les refus.
        /// </summary>
        private void NormalizeImported(int premierIndex)
        {
            List<string> rejetes = new List<string>();
            List<string> interdits = new List<string>();
            for (int i = _lines.Count - 1; i >= premierIndex && i >= 0; i--)
            {
                PartLine l = _lines[i];
                string normalise = PartNumberFormat.Normalize(l.PartNumber, _config.PartNumberPatterns);
                if (!PartNumberFormat.IsValid(normalise, _config.PartNumberPatterns))
                {
                    rejetes.Add(l.PartNumber);
                    _lines.RemoveAt(i);
                    continue;
                }

                // Un assemblage ne peut faire l'objet d'aucune demande.
                ArticleTypeRule regle = RuleFor(normalise);
                if (!regle.Allowed)
                {
                    interdits.Add(normalise + " — " + regle.Label);
                    _lines.RemoveAt(i);
                    continue;
                }

                l.PartNumber = normalise;
                l.TypeCode = PartNumberFormat.TypeCode(normalise);
            }
            if (rejetes.Count == 0 && interdits.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            if (interdits.Count > 0)
            {
                interdits.Reverse();
                foreach (string r in interdits) Log("Écarté (type sans demande possible) : " + r);
                sb.AppendLine(interdits.Count + " article(s) écarté(s), aucune demande possible sur ce type :");
                sb.AppendLine(Summarize(interdits));
                sb.AppendLine();
            }
            if (rejetes.Count > 0)
            {
                rejetes.Reverse();
                foreach (string r in rejetes) Log("Numéro refusé (format) : " + r);
                sb.AppendLine(rejetes.Count + " numéro(s) refusé(s), format non reconnu :");
                sb.AppendLine(Summarize(rejetes));
                sb.AppendLine();
                sb.AppendLine("Format attendu : " + PartNumberFormat.Describe(_config.PartNumberPatterns));
            }
            MessageBox.Show(sb.ToString().TrimEnd(), "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            UpdateDetail();
        }

        /// <summary>Renseigne le volet avec les données de la ligne courante.</summary>
        private void UpdateDetail()
        {
            PartLine l = null;
            if (grid.CurrentRow != null) l = grid.CurrentRow.DataBoundItem as PartLine;

            if (l == null)
            {
                lblDetailTitre.Text = "Aucune ligne sélectionnée";
                valDescription.Text = "—";
                valRevPlan.Text = "—";
                valRevModele.Text = "—";
                valDate.Text = "—";
                valMatiere.Text = "—";
                valFinitions.Text = "—";
                valEtatPdm.Text = "—";
                valStatut.Text = "—";
                valStatut.ForeColor = SystemColors.ControlText;
                valFichiers.Text = "—";
                return;
            }

            lblDetailTitre.Text = string.IsNullOrWhiteSpace(l.PartNumber) ? "Nouvelle ligne" : l.PartNumber;
            valDescription.Text = OrDash(l.Description);
            valRevPlan.Text = OrDash(l.DrawingRevision);
            valRevModele.Text = OrDash(l.Revision);
            valDate.Text = OrDash(l.RealizedDate);
            valMatiere.Text = OrDash(l.Material);
            valFinitions.Text = OrDash(l.Treatment);
            valEtatPdm.Text = OrDash(l.State);
            valEtatPdm.ForeColor = IsInDevelopment(l) ? Color.FromArgb(160, 35, 40) : SystemColors.ControlText;
            valStatut.Text = string.IsNullOrWhiteSpace(l.Status) ? "non vérifié" : l.Status;
            valStatut.ForeColor = StatusColor(l.Status);
            valFichiers.Text = DescribeFiles(l);
        }

        private static string OrDash(string v)
        {
            return string.IsNullOrWhiteSpace(v) ? "—" : v.Trim();
        }

        private static Color StatusColor(string status)
        {
            if (status == "OK") return Color.FromArgb(24, 105, 60);
            if (status == "Manquant 3D" || status == "Manquant 2D") return Color.FromArgb(150, 90, 10);
            if (status == "Introuvable" || status == "Erreur") return Color.FromArgb(160, 35, 40);
            return Color.Gray;
        }

        private static string DescribeFiles(PartLine l)
        {
            List<string> parts = new List<string>();
            if (l.Model3DPath != null) parts.Add("3D : " + Path.GetFileName(l.Model3DPath));
            if (l.DrawingPath != null) parts.Add("2D : " + Path.GetFileName(l.DrawingPath));
            if (l.ZipPath != null) parts.Add("ZIP : " + Path.GetFileName(l.ZipPath));
            if (parts.Count == 0) return "—";
            return string.Join(Environment.NewLine, parts);
        }

        /// <summary>
        /// Bandeau de paramètres en disposition fluide : chaque groupe est dimensionné
        /// d'après son texte, et l'ensemble se replie tout seul quand la fenêtre rétrécit.
        /// Les positions en pixels ne survivaient pas à un changement de police.
        /// </summary>
        private void BuildParamsPanel()
        {
            panelParams = new Panel();
            panelParams.Dock = DockStyle.Bottom;
            // Largeur realiste avant toute mesure : sans elle, les groupes s'empilent
            // au lieu de se repartir, et la hauteur deduite est trois fois trop grande.
            panelParams.Width = ClientSize.Width;
            hauteurParams = 176;
            panelParams.Height = hauteurParams;
            panelParams.Padding = new Padding(12, 8, 12, 8);

            // --- Les deux actions, toujours à droite ---
            btnVerify = new Button();
            btnVerify.Text = "Vérifier";
            btnVerify.Size = new Size(AppFont.Width(btnVerify.Text, 44), 38);
            btnVerify.Click += new EventHandler(BtnVerify_Click);

            btnGenerate = new Button();
            btnGenerate.Text = "Générer la demande";
            btnGenerate.Size = new Size(AppFont.Width(btnGenerate.Text, 44), 38);
            btnGenerate.BackColor = Color.FromArgb(0, 90, 158);
            btnGenerate.ForeColor = Color.White;
            btnGenerate.FlatStyle = FlatStyle.Flat;
            btnGenerate.Click += new EventHandler(BtnGenerate_Click);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Right;
            actions.Width = btnVerify.Width + btnGenerate.Width + 24;
            actions.Padding = new Padding(12, 0, 0, 0);
            btnVerify.Location = new Point(12, 8);
            btnGenerate.Location = new Point(12 + btnVerify.Width + 10, 8);
            actions.Controls.Add(btnVerify);
            actions.Controls.Add(btnGenerate);

            // --- Les champs, qui se replient selon la largeur ---
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = true;
            flow.AutoScroll = true;

            cboSupplier = new ComboBox();
            cboSupplier.DropDownStyle = ComboBoxStyle.DropDownList;
            cboSupplier.Width = 260;
            cboSupplier.SelectedIndexChanged += new EventHandler(Supplier_Changed);

            btnSuppliers = new Button();
            btnSuppliers.Text = "Fournisseurs…";
            btnSuppliers.Size = new Size(AppFont.Width(btnSuppliers.Text, 34), 30);
            btnSuppliers.Click += new EventHandler(BtnSuppliers_Click);

            txtProject = new TextBox();
            txtProject.Width = 170;

            dtpDeadline = new DateTimePicker();
            dtpDeadline.Format = DateTimePickerFormat.Short;
            dtpDeadline.ShowCheckBox = true;
            dtpDeadline.Checked = false;
            dtpDeadline.Width = 160;

            chk3D = new CheckBox();
            chk3D.Text = "Exporter 3D (STEP AP203)";
            chk3D.AutoSize = true;
            chk3D.Checked = _config.Export3D;

            chk2D = new CheckBox();
            chk2D.Text = "Exporter 2D (PDF + DXF)";
            chk2D.AutoSize = true;
            chk2D.Checked = _config.Export2D;

            chkControleFabrication = new CheckBox();
            chkControleFabrication.Text = "Générer le contrôle de fabrication (PDF) — bêta";
            chkControleFabrication.AutoSize = true;
            chkControleFabrication.Checked = false;
            toolTip.SetToolTip(chkControleFabrication,
                "Fonction en bêta : relisez le document avant de l'envoyer au fournisseur.");

            cboCompression = new ComboBox();
            cboCompression.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCompression.Width = 130;
            cboCompression.Items.AddRange(ZipService.Niveaux);
            cboCompression.SelectedItem = ZipService.Niveaux[2];
            foreach (string n in ZipService.Niveaux)
                if (string.Equals(n, _config.ZipCompression, StringComparison.OrdinalIgnoreCase))
                    cboCompression.SelectedItem = n;
            cboCompression.SelectedIndexChanged += new EventHandler(Compression_Changee);
            toolTip.SetToolTip(cboCompression,
                "Compression des archives jointes. Sur les exports du coffre, « Maximale » ne gagne "
                + "que trois pour cent de plus qu'« Optimal » pour quatre fois le temps.");

            lblPo = new Label();
            lblPo.Text = LibellePo(true);
            lblPo.AutoSize = true;

            txtPo = new TextBox();
            txtPo.Width = 240;
            txtPo.ReadOnly = true;
            txtPo.PlaceholderText = "aucun fichier choisi";

            btnPo = new Button();
            btnPo.Text = "Parcourir…";
            btnPo.Size = new Size(AppFont.Width(btnPo.Text, 34), 30);
            btnPo.Click += new EventHandler(BtnPo_Click);

            txtConditions = new TextBox();
            txtConditions.Multiline = true;
            txtConditions.ScrollBars = ScrollBars.Vertical;
            txtConditions.Size = new Size(520, 68);
            txtConditions.PlaceholderText = "Délais de paiement, incoterms, exigences qualité, emballage...";

            btnRecherche = new Button();
            btnRecherche.Text = "Rechercher…";
            btnRecherche.Size = new Size(AppFont.Width(btnRecherche.Text, 34), 30);
            btnRecherche.Click += new EventHandler(BtnRecherche_Click);
            toolTip.SetToolTip(btnRecherche,
                "Chercher un article dans le coffre et dans l'inventaire, à fabriquer ou au catalogue.");

            flow.Controls.Add(Groupe("Destinataire :", cboSupplier, btnSuppliers));
            flow.Controls.Add(Groupe("", btnRecherche, null));
            flow.Controls.Add(Groupe("Référence commande :", txtProject, null));
            flow.Controls.Add(Groupe("Délai souhaité :", dtpDeadline, null));
            flow.Controls.Add(Groupe("", chk3D, chk2D));
            flow.Controls.Add(Groupe("", chkControleFabrication, null));
            flow.Controls.Add(Groupe("Compression des archives :", cboCompression, null));
            groupePo = Groupe(lblPo.Text, txtPo, btnPo);
            flow.Controls.Add(groupePo);
            flow.Controls.Add(Groupe("Commentaire général (bas de l'email) :", txtConditions, null));

            panelParams.Controls.Add(flow);
            panelParams.Controls.Add(actions);

            // Hauteur reelle une fois les groupes repartis sur la largeur disponible.
            flow.PerformLayout();
            int bas = 0;
            foreach (Control g in flow.Controls) bas = Math.Max(bas, g.Bottom + g.Margin.Bottom);
            if (bas > 0) hauteurParams = bas + panelParams.Padding.Vertical + 10;
        }

        /// <summary>
        /// Un groupe intitulé + champ, dimensionné d'après son contenu : c'est lui qui
        /// permet au bandeau de se replier proprement quelle que soit la police.
        /// </summary>
        private Panel Groupe(string intitule, Control champ, Control complement)
        {
            Panel g = new Panel();
            g.Margin = new Padding(0, 4, 22, 6);

            // La police doit être posée avant de mesurer : un contrôle non rattaché
            // se mesure encore avec la police par défaut, et le texte se retrouve coupé.
            champ.Font = AppFont.Get();
            if (complement != null) complement.Font = AppFont.Get();

            int y = 0;
            int largeur = LargeurReelle(champ);

            if (intitule != "")
            {
                Label l = new Label();
                l.Font = AppFont.Get();
                l.Text = intitule;
                l.AutoSize = true;
                l.Location = new Point(0, 0);
                g.Controls.Add(l);
                y = l.PreferredHeight + 4;
                largeur = Math.Max(largeur, l.PreferredSize.Width);
            }

            champ.Location = new Point(0, y);
            g.Controls.Add(champ);

            if (complement != null)
            {
                // Le complément se place à droite du champ, ou sous lui pour une case à cocher.
                if (complement is CheckBox)
                {
                    complement.Location = new Point(0, y + HauteurReelle(champ) + 6);
                    largeur = Math.Max(largeur, LargeurReelle(complement));
                    g.Height = y + HauteurReelle(champ) + 6 + HauteurReelle(complement);
                }
                else
                {
                    complement.Location = new Point(LargeurReelle(champ) + 8, y - 1);
                    largeur = LargeurReelle(champ) + 8 + LargeurReelle(complement);
                    g.Height = y + Math.Max(HauteurReelle(champ), HauteurReelle(complement));
                }
                g.Controls.Add(complement);
            }
            else
            {
                g.Height = y + HauteurReelle(champ);
            }

            g.Width = largeur;
            return g;
        }

        /// <summary>
        /// Largeur d'un contrôle, en tenant compte de l'auto-dimensionnement : tant que
        /// le contrôle n'est pas affiché, sa propriété Width n'est pas encore à jour.
        /// </summary>
        private static int LargeurReelle(Control c)
        {
            if (c.AutoSize) return Math.Max(c.Width, c.PreferredSize.Width);
            return c.Width;
        }

        private static int HauteurReelle(Control c)
        {
            if (c.AutoSize) return Math.Max(c.Height, c.PreferredSize.Height);
            return c.Height;
        }

        private void BuildStatusPanel()
        {
            panelStatus = new Panel();
            panelStatus.Dock = DockStyle.Bottom;
            hauteurStatus = 96;
            panelStatus.Height = hauteurStatus;

            progress = new ProgressBar();
            progress.Dock = DockStyle.Top;
            progress.Height = 20;

            panelStatusLine = new Panel();
            panelStatusLine.Dock = DockStyle.Top;
            panelStatusLine.Height = 34;

            lblProgress = new Label();
            lblProgress.Text = "Prêt.";
            lblProgress.Location = new Point(6, 8);
            lblProgress.AutoSize = true;

            btnCancel = new Button();
            btnCancel.Text = "Annuler";
            btnCancel.Width = 110;
            btnCancel.Height = 26;
            btnCancel.Location = new Point(panelStatusLine.Width - 122, 2);
            btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCancel.Enabled = false;
            btnCancel.Click += new EventHandler(BtnCancel_Click);

            btnUpdate = new Button();
            btnUpdate.Text = "Mettre à jour";
            btnUpdate.Width = 140;
            btnUpdate.Height = 26;
            btnUpdate.Location = new Point(panelStatusLine.Width - 270, 2);
            btnUpdate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnUpdate.Visible = false;
            btnUpdate.BackColor = Color.FromArgb(0, 120, 70);
            btnUpdate.ForeColor = Color.White;
            btnUpdate.FlatStyle = FlatStyle.Flat;
            btnUpdate.Click += new EventHandler(BtnUpdate_Click);

            panelStatusLine.Controls.Add(lblProgress);
            panelStatusLine.Controls.Add(btnUpdate);
            panelStatusLine.Controls.Add(btnCancel);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;
            txtLog.Dock = DockStyle.Fill;
            txtLog.BackColor = Color.White;

            // Le dernier ajouté est ancré au plus près du bord haut du panneau.
            panelStatus.Controls.Add(txtLog);
            panelStatus.Controls.Add(panelStatusLine);
            panelStatus.Controls.Add(progress);
        }

        // ==================================================================
        // Comportements de l'interface
        // ==================================================================

        /// <summary>
        /// Assemble la fenêtre autour de trois séparateurs déplaçables : entre la grille
        /// et le volet de détail, entre le haut et le bas, et entre les paramètres et le
        /// journal. Chacun peut être déplacé, chaque zone agrandie ou réduite.
        /// </summary>
        private void AssembleAvecSeparateurs()
        {
            grid.Dock = DockStyle.Fill;
            panelDetail.Dock = DockStyle.Fill;
            panelParams.Dock = DockStyle.Fill;
            panelStatus.Dock = DockStyle.Fill;

            splitCentre = new SplitContainer();
            splitCentre.Dock = DockStyle.Fill;
            splitCentre.Orientation = Orientation.Vertical;
            splitCentre.SplitterWidth = 6;
            splitCentre.FixedPanel = FixedPanel.Panel2;   // le volet garde sa largeur, la grille absorbe
            splitCentre.SplitterMoving += new SplitterCancelEventHandler(Separateur_Deplace);
            splitCentre.Panel1.Controls.Add(grid);
            splitCentre.Panel2.Controls.Add(panelDetail);

            splitBas = new SplitContainer();
            splitBas.Dock = DockStyle.Fill;
            splitBas.Orientation = Orientation.Horizontal;
            splitBas.SplitterWidth = 6;
            splitBas.FixedPanel = FixedPanel.Panel1;      // les paramètres gardent leur hauteur, le journal absorbe
            splitBas.SplitterMoving += new SplitterCancelEventHandler(Separateur_Deplace);
            splitBas.Panel1.Controls.Add(panelParams);
            splitBas.Panel2.Controls.Add(panelStatus);

            splitPrincipal = new SplitContainer();
            splitPrincipal.Dock = DockStyle.Fill;
            splitPrincipal.Orientation = Orientation.Horizontal;
            splitPrincipal.SplitterWidth = 6;
            splitPrincipal.FixedPanel = FixedPanel.Panel2; // le bas garde sa hauteur, la grille absorbe
            splitPrincipal.SplitterMoving += new SplitterCancelEventHandler(Separateur_Deplace);
            splitPrincipal.Panel1.Controls.Add(splitCentre);
            splitPrincipal.Panel2.Controls.Add(splitBas);

            Controls.Add(splitPrincipal);
            Controls.Add(panelTools);
            Controls.Add(panelTop);
        }

        /// <summary>
        /// Position initiale des séparateurs, appliquée à l'affichage après un agencement
        /// complet : avant cela les conteneurs n'ont pas leur taille définitive, et toutes
        /// les distances calculées seraient rabotées.
        /// </summary>
        private bool _ouvrirAssistantAuDemarrage;

        /// <summary>
        /// Ouvre l'assistant, et applique ce qu'il a recueilli.
        ///
        /// Il ne traite rien lui-même : il remplit cette fenêtre, qui reste seule à porter
        /// le pipeline. Deux chemins d'interface, un seul chemin de traitement.
        /// </summary>
        private void OuvrirAssistant()
        {
            using (AssistantForm assistant = new AssistantForm(_config, _suppliers,
                       delegate { return _inventaire; },
                       delegate { BuildPdmIndex(); return _pdmIndex; }))
            {
                if (assistant.ShowDialog(this) != DialogResult.OK) return;
                AppliquerDemande(assistant.Demande);
                if (assistant.Demande.Generer) StartProcess(true);
            }
        }

        private void BtnAssistant_Click(object sender, EventArgs e)
        {
            OuvrirAssistant();
        }

        /// <summary>Reporte dans les contrôles ce que l'assistant a recueilli.</summary>
        private void AppliquerDemande(DemandeEnCours d)
        {
            if (d == null) return;

            ChoisirType(d.Type);

            if (d.Destinataire != null)
            {
                for (int i = 0; i < cboSupplier.Items.Count; i++)
                {
                    Supplier s = cboSupplier.Items[i] as Supplier;
                    if (s != null && ReferenceEquals(s, d.Destinataire)) { cboSupplier.SelectedIndex = i; break; }
                    if (s != null && string.Equals(s.Name, d.Destinataire.Name, StringComparison.OrdinalIgnoreCase))
                    { cboSupplier.SelectedIndex = i; break; }
                }
            }

            _lines.Clear();
            foreach (PartLine l in d.Lignes) _lines.Add(l);
            if (_lines.Count == 0) _lines.Add(new PartLine());

            txtProject.Text = d.ReferenceCommande;
            dtpDeadline.Checked = d.Delai.HasValue;
            if (d.Delai.HasValue) dtpDeadline.Value = d.Delai.Value;
            txtPo.Text = d.CheminPo;
            txtConditions.Text = d.Commentaire;
            chk3D.Checked = d.Export3D;
            chk2D.Checked = d.Export2D;
            chkControleFabrication.Checked = d.ControleFabrication;

            RefreshGrid();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PerformLayout();
            AppliquerSeparateurs();

            // L'assistant s'ouvre au lancement : c'est le chemin normal. La vue complète
            // reste derrière, pour qui préfère tout voir d'un coup.
            if (_ouvrirAssistantAuDemarrage)
            {
                _ouvrirAssistantAuDemarrage = false;
                BeginInvoke(new MethodInvoker(OuvrirAssistant));
            }
        }

        /// <summary>Réapplique la répartition tant que l'utilisateur n'a rien déplacé.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!separateurDeplaceParUtilisateur) AppliquerSeparateurs();
        }

        private void AppliquerSeparateurs()
        {
            if (separateurDeplaceParUtilisateur || splitPrincipal == null) return;
            if (splitPrincipal.Height < 420 || splitCentre.Width < 640) return;

            ReglerSeparateur(splitPrincipal, 140, 160,
                splitPrincipal.Height - hauteurParams - hauteurStatus - splitBas.SplitterWidth);

            // Le bas ne connait sa hauteur qu'une fois le separateur principal applique.
            splitPrincipal.PerformLayout();
            splitBas.PerformLayout();

            ReglerSeparateur(splitCentre, 320, 220, splitCentre.Width - largeurDetail);
            ReglerSeparateur(splitBas, 90, 70, hauteurParams);
        }

        /// <summary>
        /// Dès que l'utilisateur fait glisser un séparateur, sa répartition fait foi.
        /// On écoute le glissement et non le déplacement : ce dernier est aussi émis
        /// par WinForms lors de ses propres agencements, ce qui figeait tout d'emblée.
        /// </summary>
        private void Separateur_Deplace(object sender, SplitterCancelEventArgs e)
        {
            separateurDeplaceParUtilisateur = true;
        }

        /// <summary>
        /// Place un séparateur, puis seulement ensuite ses tailles minimales : dans
        /// l'autre ordre, poser une taille minimale que la position courante viole lève
        /// une exception, et la position demandée n'est jamais appliquée.
        /// </summary>
        private static void ReglerSeparateur(SplitContainer s, int min1, int min2, int distance)
        {
            if (s == null) return;
            try
            {
                int taille = s.Orientation == Orientation.Vertical ? s.Width : s.Height;
                if (taille < min1 + min2 + s.SplitterWidth + 20) return;   // trop petit : on laisse le défaut

                // Contraintes relâchées le temps de repositionner.
                s.Panel1MinSize = 0;
                s.Panel2MinSize = 0;

                int maxi = taille - min2 - s.SplitterWidth;
                if (distance < min1) distance = min1;
                if (distance > maxi) distance = maxi;
                s.SplitterDistance = distance;

                s.Panel1MinSize = min1;
                s.Panel2MinSize = min2;
            }
            catch (Exception ex)
            {
                LogService.Write("Position de séparateur ignorée : " + ex.Message);
            }
        }

        /// <summary>Une entrée de la liste des types de demande.</summary>
        private sealed class ChoixTypeDemande
        {
            public readonly RequestType Type;
            public ChoixTypeDemande(RequestType type) { Type = type; }
            public override string ToString() { return RequestTypes.Libelle(Type); }
        }

        /// <summary>Type de demande actuellement sélectionné.</summary>
        private RequestType CurrentType
        {
            get
            {
                ChoixTypeDemande c = cboType == null ? null : cboType.SelectedItem as ChoixTypeDemande;
                return c == null ? RequestType.Offre : c.Type;
            }
        }

        /// <summary>Impose le type de demande, depuis l'assistant.</summary>
        public void ChoisirType(RequestType type)
        {
            for (int i = 0; i < cboType.Items.Count; i++)
            {
                ChoixTypeDemande c = cboType.Items[i] as ChoixTypeDemande;
                if (c != null && c.Type == type) { cboType.SelectedIndex = i; return; }
            }
        }

        private void TypeDemande_Change(object sender, EventArgs e)
        {
            ApplyMode();
        }

        /// <summary>Affiche ou masque les quantités 2 et 3 selon le mode.</summary>
        private void ApplyMode()
        {
            RequestType type = CurrentType;
            bool offre = type == RequestType.Offre;
            bool catalogue = RequestTypes.EstCatalogue(type);
            colQty2.Visible = offre;
            colQty3.Visible = offre;

            // Un achat catalogue ne livre aucun fichier : ces réglages n'ont rien à régler.
            chk3D.Enabled = !catalogue;
            chk2D.Enabled = !catalogue;
            chkControleFabrication.Enabled = !catalogue;
            if (catalogue) chkControleFabrication.Checked = false;
            lblInfo.Text = RequestTypes.Description(type);

            // Le controle accompagne une fabrication ; sur une demande d'offre il ne se
            // justifie pas, la piece n'est pas encore commandee.
            if (chkControleFabrication != null) chkControleFabrication.Checked = !offre;

            // Le document joint change de nature selon le mode : bon de commande en
            // fabrication, demande de PO en offre. Il reste facultatif en offre.
            if (groupePo != null)
            {
                foreach (Control c in groupePo.Controls)
                {
                    if (c is Label) { ((Label)c).Text = LibellePo(offre); break; }
                }
            }

            if (!offre)
            {
                // Une demande de fabrication ne comporte qu'une seule quantité.
                foreach (PartLine l in _lines)
                {
                    l.Qty2 = 0;
                    l.Qty3 = 0;
                }
                grid.Refresh();
            }
        }

        // ==================================================================
        // Mises à jour publiées sur GitHub
        // ==================================================================

        /// <summary>Recherche une nouvelle version en arrière-plan, sans bloquer le démarrage.</summary>
        private void StartUpdateCheck()
        {
            if (!_config.CheckUpdatesOnStartup) return;
            Thread t = new Thread(new ThreadStart(RunUpdateCheck));
            t.IsBackground = true;
            t.Start();
        }

        private void RunUpdateCheck()
        {
            UpdateService.UpdateInfo info = UpdateService.Check();
            _update = info;
            Log(info.Message);
            if (!info.Available) return;
            UiInvoke(delegate
            {
                btnUpdate.Text = "Mettre à jour → " + info.LatestVersion;
                btnUpdate.Visible = true;
            });
        }

        private void BtnUpdate_Click(object sender, EventArgs e)
        {
            UpdateService.UpdateInfo info = _update;
            if (info == null || !info.Available) return;

            if (MessageBox.Show(
                    info.Message + Environment.NewLine + Environment.NewLine +
                    "AskThem va télécharger la nouvelle version, se fermer et redémarrer." +
                    Environment.NewLine + "Enregistrez votre saisie avant de continuer.",
                    "Mise à jour", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            try
            {
                Cursor = Cursors.WaitCursor;
                btnUpdate.Enabled = false;
                Log("Téléchargement de la version " + info.LatestVersion + "…");
                UpdateService.DownloadAndRestart(info);
                Close();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                btnUpdate.Enabled = true;
                Log("ERREUR mise à jour : " + ex.Message);
                MessageBox.Show("La mise à jour a échoué : " + ex.Message + Environment.NewLine + Environment.NewLine +
                    "Vous pouvez la télécharger manuellement depuis " + info.PageUrl,
                    "Mise à jour", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Relit la liste des fournisseurs depuis le réseau et remplit la liste déroulante.</summary>
        private void LoadSuppliers()
        {
            string message;
            _suppliers = SupplierService.Load(_config, out message);
            Log(message);
            FillSupplierBox(null);
        }

        /// <summary>Remplit la liste déroulante et resélectionne le fournisseur indiqué.</summary>
        private void FillSupplierBox(string nomARetrouver)
        {
            cboSupplier.BeginUpdate();
            cboSupplier.Items.Clear();
            foreach (Supplier s in _suppliers) cboSupplier.Items.Add(s);
            cboSupplier.EndUpdate();

            if (nomARetrouver != null)
            {
                for (int i = 0; i < _suppliers.Count; i++)
                {
                    if (string.Equals(_suppliers[i].Name, nomARetrouver, StringComparison.OrdinalIgnoreCase))
                    {
                        cboSupplier.SelectedIndex = i;
                        return;
                    }
                }
            }
            if (cboSupplier.Items.Count > 0 && cboSupplier.SelectedIndex < 0) cboSupplier.SelectedIndex = 0;
        }

        private Supplier SelectedSupplier
        {
            get { return cboSupplier.SelectedItem as Supplier; }
        }

        private void Supplier_Changed(object sender, EventArgs e)
        {
            Supplier s = SelectedSupplier;
            if (s == null) { toolTip.SetToolTip(cboSupplier, ""); return; }
            string infos = "Destinataires : " + s.ToLine;
            if (s.CcLine != "") infos += Environment.NewLine + "Copie : " + s.CcLine;
            if (!string.IsNullOrWhiteSpace(s.Note)) infos += Environment.NewLine + s.Note;
            toolTip.SetToolTip(cboSupplier, infos);
        }

        private void BtnSuppliers_Click(object sender, EventArgs e)
        {
            Supplier avant = SelectedSupplier;
            string nom = avant == null ? null : avant.Name;
            using (SupplierDialog dlg = new SupplierDialog(_config, _suppliers))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _suppliers = dlg.Suppliers;
                Log(_suppliers.Count + " fournisseur(s) enregistré(s) sur le réseau.");
            }
            FillSupplierBox(nom);
        }

        /// <summary>Intitulé du document joint, selon le type de demande.</summary>
        private static string LibellePo(bool offre)
        {
            return offre ? "Demande de PO (PDF, facultatif) :" : "Bon de commande (PDF) :";
        }

        private void BtnPo_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = CurrentType == RequestType.Offre
                    ? "Choisir la demande de PO"
                    : "Choisir le bon de commande";
                dlg.Filter = "Bon de commande PDF (*.pdf)|*.pdf";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                if (!string.Equals(Path.GetExtension(dlg.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Le document doit être un fichier PDF.", "AskThem",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                txtPo.Text = dlg.FileName;
                Log("Document joint : " + Path.GetFileName(dlg.FileName));
            }
        }

        /// <summary>Couleur et infobulle de la pastille, selon l'état de la connexion.</summary>
        private void AfficherEtatInventaire(bool connecte, string detail)
        {
            inventaireConnecte = connecte;
            UiInvoke(delegate
            {
                pastilleInventaire.BackColor = connecte
                    ? Color.FromArgb(32, 150, 70)
                    : Color.FromArgb(180, 60, 60);
                toolTip.SetToolTip(pastilleInventaire,
                    (connecte ? "Connecté à l'inventaire. " : "Non connecté à l'inventaire. ") + detail);
            });
        }

        /// <summary>
        /// Ouvre une session avec les identifiants conservés sur ce poste. Le mot de
        /// passe survit au redémarrage : il est chiffré par Windows dans un fichier.
        /// </summary>
        private void VerifierInventaire()
        {
            string mdp = SecretStore.Load(InventoryApiService.SecretName);
            if (string.IsNullOrWhiteSpace(_config.InventoryApiUrl)
                || string.IsNullOrWhiteSpace(_config.InventoryUser)
                || string.IsNullOrWhiteSpace(mdp))
            {
                AfficherEtatInventaire(false, "Aucun identifiant enregistré sur ce poste.");
                return;
            }

            using (InventoryApiService api = new InventoryApiService())
            {
                string message;
                bool ok = api.Connect(_config.InventoryApiUrl, _config.InventoryUser, mdp, out message);
                AfficherEtatInventaire(ok, message);
                Log(message);
                if (!ok) return;

                // Chargé dès le démarrage : c'est ce qui permet de refuser un article dès
                // sa saisie, sans attendre la génération.
                _inventaire = api.LoadAll(out message);
                Log(message);
            }
        }

        private void LancerVerificationInventaire()
        {
            Thread t = new Thread(new ThreadStart(VerifierInventaire));
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>Le niveau choisi est conservé d'une session à l'autre.</summary>
        private void Compression_Changee(object sender, EventArgs e)
        {
            string choix = cboCompression.SelectedItem as string;
            if (string.IsNullOrEmpty(choix) || choix == _config.ZipCompression) return;
            _config.ZipCompression = choix;
            ConfigService.Save(_config);
            Log("Compression des archives : " + choix + ".");
        }

        private void BtnInventaire_Click(object sender, EventArgs e)
        {
            using (InventoryDialog dlg = new InventoryDialog(_config))
            {
                dlg.ShowDialog(this);
            }
            _config = ConfigService.Load();
            LancerVerificationInventaire();
        }

        /// <summary>
        /// Charge l'inventaire : par son API si une session peut s'ouvrir, sinon par
        /// l'export déposé sur le réseau. La source retenue est écrite au journal.
        /// </summary>
        private void LoadInventory()
        {
            string message;

            string mdp = SecretStore.Load(InventoryApiService.SecretName);
            if (!string.IsNullOrWhiteSpace(_config.InventoryApiUrl)
                && !string.IsNullOrWhiteSpace(_config.InventoryUser)
                && !string.IsNullOrWhiteSpace(mdp))
            {
                using (InventoryApiService api = new InventoryApiService())
                {
                    if (api.Connect(_config.InventoryApiUrl, _config.InventoryUser, mdp, out message))
                    {
                        AfficherEtatInventaire(true, message);
                        Log(message);
                        string qui = api.WhoAmI();
                        if (qui != "") Log("Inventaire consulté en lecture seule par : " + qui);
                        _inventaire = api.LoadAll(out message);
                        Log(message);
                        if (_inventaire.Count > 0) return;
                    }
                    else
                    {
                        AfficherEtatInventaire(false, message);
                        Log(message + " Repli sur l'export.");
                    }
                }
            }

            _inventaire = InventoryService.Load(_config, out message);
            Log(message);
        }

        private void BtnAddLine_Click(object sender, EventArgs e)
        {
            _lines.Add(new PartLine());
        }

        private void BtnPaste_Click(object sender, EventArgs e)
        {
            PasteFromClipboard();
        }

        private void PasteFromClipboard()
        {
            try
            {
                int avant = _lines.Count;
                int n = ClipboardImporter.ImportFromClipboard(_lines);
                NormalizeImported(avant);
                if (CurrentType == RequestType.Fabrication) ApplyMode();
                grid.Refresh();
                Log(n + " ligne(s) collée(s) depuis le presse-papiers.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Collage impossible : " + ex.Message, "AskThem",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Import d'une liste, au format CSV ou Excel.</summary>
        private void BtnImportCsv_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Importer une liste d'articles";
                dlg.Filter = "Listes d'articles (*.csv;*.xlsx)|*.csv;*.xlsx"
                           + "|Classeurs Excel (*.xlsx)|*.xlsx"
                           + "|Fichiers CSV (*.csv)|*.csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int avant = _lines.Count;
                    // Le format est déduit de l'extension ; la correspondance des
                    // colonnes est la même dans les deux cas.
                    int regroupees;
                    int n = string.Equals(Path.GetExtension(dlg.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)
                        ? XlsxService.Import(_lines, dlg.FileName, out regroupees)
                        : CsvService.Import(_lines, dlg.FileName, out regroupees);
                    NormalizeImported(avant);
                    if (CurrentType == RequestType.Fabrication) ApplyMode();
                    grid.Refresh();

                    string bilan = n + " article(s) importé(s) depuis " + Path.GetFileName(dlg.FileName) + ".";
                    if (regroupees > 0)
                    {
                        // Une nomenclature cite le même article à plusieurs niveaux.
                        bilan += " " + regroupees + " ligne(s) répétée(s) ont été regroupées, "
                               + "leurs quantités additionnées.";
                    }
                    Log(bilan);
                    if (regroupees > 0)
                    {
                        MessageBox.Show(bilan, "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Import impossible : " + ex.Message, "AskThem",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Exporter la liste en CSV";
                dlg.Filter = "Fichiers CSV (*.csv)|*.csv";
                dlg.FileName = "AskThem_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    grid.EndEdit();
                    CsvService.Export(_lines, dlg.FileName);
                    Log("Liste exportée dans " + dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Export impossible : " + ex.Message, "AskThem",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Vider toutes les lignes ?", "AskThem",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _lines.Clear();
            Log("Liste vidée.");
        }

        private void BtnVerify_Click(object sender, EventArgs e)
        {
            StartProcess(false);
        }

        private void BtnGenerate_Click(object sender, EventArgs e)
        {
            StartProcess(true);
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            _cancelRequested = true;
            lblProgress.Text = "Annulation en cours...";
            btnCancel.Enabled = false;
        }

        // ==================================================================
        // Validation
        // ==================================================================

        /// <summary>Contrôle la saisie. forGeneration ajoute le contrôle du destinataire.</summary>
        private bool ValidateInput(bool forGeneration)
        {
            grid.EndEdit();

            // 1) Suppression des lignes sans numéro d'article.
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(_lines[i].PartNumber)) _lines.RemoveAt(i);
                else _lines[i].PartNumber = _lines[i].PartNumber.Trim();
            }
            grid.Refresh();

            // 2) Liste vide.
            if (_lines.Count == 0)
            {
                MessageBox.Show("Aucun article saisi.", "AskThem",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // 3) Quantité 1 minimale.
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Qty1 < 1)
                {
                    MessageBox.Show("La quantité 1 doit être au minimum de 1 (ligne " + (i + 1) + ").",
                        "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            // 4) Doublons : simple avertissement, le traitement continue.
            List<string> duplicates = new List<string>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (PartLine l in _lines)
            {
                if (seen.ContainsKey(l.PartNumber))
                {
                    if (!duplicates.Contains(l.PartNumber)) duplicates.Add(l.PartNumber);
                }
                else seen[l.PartNumber] = true;
            }
            if (duplicates.Count > 0)
            {
                MessageBox.Show("Doublons détectés : " + string.Join(", ", duplicates), "AskThem",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 5) Destinataire obligatoire pour la génération.
            if (forGeneration)
            {
                // Facultatif en offre, obligatoire en fabrication : dans les deux cas,
                // un fichier indiqué mais disparu doit être signalé avant l'envoi.
                string po = txtPo.Text.Trim();
                if (po != "" && !File.Exists(po))
                {
                    MessageBox.Show("Le document joint est introuvable :" + Environment.NewLine + po,
                        "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                Supplier fournisseur = SelectedSupplier;
                if (fournisseur == null)
                {
                    MessageBox.Show("Choisissez un fournisseur dans la liste." + Environment.NewLine +
                        "Utilisez le bouton « Fournisseurs… » pour en créer un.",
                        "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (fournisseur.Emails.Count == 0)
                {
                    MessageBox.Show("« " + fournisseur.Name + " » n'a aucune adresse de destinataire.",
                        "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (CurrentType == RequestType.Fabrication)
                {
                    List<string> nonFabricables = new List<string>();
                    foreach (PartLine l in _lines)
                    {
                        ArticleTypeRule regle = RuleFor(l.PartNumber);
                        if (!regle.AllowFabrication) nonFabricables.Add(l.PartNumber + " — " + regle.Label);
                    }
                    if (nonFabricables.Count > 0)
                    {
                        MessageBox.Show(
                            "Une demande de fabrication ne porte que sur des pièces fabriquées." + Environment.NewLine +
                            nonFabricables.Count + " article(s) ne peuvent pas être fabriqués :" + Environment.NewLine +
                            Summarize(nonFabricables) + Environment.NewLine + Environment.NewLine +
                            "Retirez-les, ou basculez en demande d'offre.",
                            "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }

                    if (txtPo.Text.Trim() == "")
                    {
                        MessageBox.Show("Une demande de fabrication exige un bon de commande." + Environment.NewLine +
                            "Utilisez « Parcourir… » pour joindre le PDF.",
                            "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }
            return true;
        }

        // ==================================================================
        // Traitement (thread STA distinct)
        // ==================================================================

        private void StartProcess(bool generate)
        {
            if (_busy) return;
            if (!ValidateInput(generate)) return;

            // Sans session d'inventaire, la demande partira sans les anciennes références
            // ni les références fournisseur : on le dit avant, pas après.
            if (generate && !inventaireConnecte)
            {
                if (MessageBox.Show(
                        "AskThem n'est pas connecté à l'inventaire." + Environment.NewLine + Environment.NewLine +
                        "Les anciennes références et les références fournisseur ne seront pas " +
                        "renseignées dans cette demande." + Environment.NewLine +
                        "Le bouton « Inventaire… » permet de rétablir la connexion." +
                        Environment.NewLine + Environment.NewLine + "Envoyer quand même ?",
                        "AskThem", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    return;
                }
            }

            if (!VerifierHomogeneite()) return;

            // Un achat catalogue ne touche ni SolidWorks ni le coffre : la question ne se pose
            // que pour les articles dont on livre des fichiers.
            if (generate && !RequestTypes.EstCatalogue(CurrentType) && !ConfirmerSolidWorks()) return;

            // Les valeurs de l'interface sont lues ici, sur le thread interface.
            _generateMode = generate;
            _opt3D = chk3D.Checked;
            _opt2D = chk2D.Checked;
            _optControle = chkControleFabrication.Checked;
            _optCompression = ZipService.Niveau(cboCompression.SelectedItem as string);
            Supplier fournisseur = SelectedSupplier;
            _optSupplier = fournisseur == null ? "" : fournisseur.ToLine;
            _optSupplierCc = fournisseur == null ? "" : fournisseur.CcLine;
            _optSupplierName = fournisseur == null ? "" : fournisseur.Name;
            _optFournisseurInventaire = fournisseur == null ? 0 : fournisseur.InventoryId;
            _optCatalogue = RequestTypes.EstCatalogue(_optType);
            _optProject = txtProject.Text.Trim();
            _optDeadline = dtpDeadline.Checked ? dtpDeadline.Value.ToString("dd.MM.yyyy") : "";
            _optConditions = txtConditions.Text;
            _optPoPath = txtPo.Text.Trim();
            _optType = CurrentType;
            _work = new List<PartLine>(_lines);

            progress.Maximum = _work.Count > 0 ? _work.Count : 1;
            progress.Value = 0;

            _cancelRequested = false;
            _stopMailWatch = true;
            SetBusy(true);

            // SolidWorks en COM exige un thread STA.
            Thread worker = new Thread(new ThreadStart(RunProcess));
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();
        }

        private void RunProcess()
        {
            try
            {
                if (_generateMode) RunGenerate();
                else RunVerify();
            }
            catch (Exception ex)
            {
                Log("ERREUR FATALE : " + ex.Message);
                UiInvoke(delegate
                {
                    MessageBox.Show("Erreur inattendue : " + ex.Message, "AskThem",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
            finally
            {
                RefreshGrid();
                SetBusy(false);
                UiInvoke(delegate { lblProgress.Text = "Prêt."; });
            }
        }

        /// <summary>Mode vérification : aucune ouverture de SolidWorks.</summary>
        private void RunVerify()
        {
            if (!_optCatalogue) BuildPdmIndex();

            LoadInventory();

            if (_optCatalogue)
            {
                int nb = 0;
                foreach (PartLine ligne in _work)
                {
                    if (_cancelRequested) { Log("Vérification annulée."); break; }
                    TraiterArticleCatalogue(ligne);
                    if (ligne.Status == "OK") nb++;
                }
                RefreshGrid();
                Log("Vérification catalogue : " + nb + " article(s) sur " + _work.Count
                  + " avec une référence chez « " + _optSupplierName + " ».");
                AvertirCatalogue();
                return;
            }

            int total = _work.Count;
            int ok = 0;
            int warn = 0;
            int missing = 0;

            for (int i = 0; i < total; i++)
            {
                if (_cancelRequested) { Log("Vérification annulée."); break; }

                PartLine line = _work[i];
                line.Model3DPath = PdmSearchService.Find3DInIndex(_pdmIndex, line.PartNumber);
                line.DrawingPath = PdmSearchService.FindDrawingInIndex(_pdmIndex, line.PartNumber);

                InventoryService.Entry inv = InventoryService.Lookup(_inventaire, line.PartNumber);
                if (inv != null)
                {
                    line.OldRef = inv.OldRef;
                    line.SupplierRef = inv.SupplierRef;
                    line.PdmSupplier = inv.Supplier;
                }

                if (line.Model3DPath != null && line.DrawingPath != null) { line.Status = "OK"; ok++; }
                else if (line.Model3DPath != null) { line.Status = "Manquant 2D"; warn++; }
                else if (line.DrawingPath != null) { line.Status = "Manquant 3D"; warn++; }
                else { line.Status = "Introuvable"; missing++; }

                SetProgress(i + 1, "Vérification " + (i + 1) + "/" + total + " : " + line.PartNumber);
                RefreshGrid();
            }

            Log("Vérification terminée : " + ok + " OK, " + warn + " avertissement(s), " + missing + " introuvable(s).");
            WarnAboutIssues();
        }

        /// <summary>Construit l'index du coffre PDM (une seule fois par traitement).</summary>
        private void BuildPdmIndex()
        {
            Log("Analyse du coffre PDM : " + _config.PdmRoot);
            if (!Directory.Exists(_config.PdmRoot))
            {
                Log("ATTENTION : le dossier " + _config.PdmRoot + " est introuvable.");
                _pdmIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }
            DateTime start = DateTime.Now;
            _pdmIndex = PdmSearchService.BuildIndex(_config.PdmRoot, LogFromWorker);
            int seconds = (int)(DateTime.Now - start).TotalSeconds;
            Log("Index construit : " + _pdmIndex.Count + " fichier(s) SolidWorks en " + seconds + " s.");
        }

        /// <summary>
        /// Produit le controle de fabrication d'un article, a partir du plan deja ouvert.
        /// Le PDF rejoint les fichiers de l'article : il part donc dans son archive ZIP.
        /// </summary>
        private string GenererControle(ModelDoc2 plan, PartLine line, string dossier)
        {
            List<string> traces = new List<string>();
            try
            {
                ExtracteurCaracteristiques extracteur = new ExtracteurCaracteristiques(
                    _controleCfg,
                    delegate(string m) { traces.Add(m); });

                ControleFabrication controle = extracteur.Extraire(plan, line, _optSupplierName, _optProject);
                string pdf = _generateurPdf.Generer(controle, dossier);

                line.ExportedFiles.Add(pdf);
                Log("Controle de fabrication (beta) : " + Path.GetFileName(pdf)
                    + " (" + controle.Caracteristiques.Count + " caracteristique(s))");
                if (controle.ExtractionPartielle)
                    Log("ATTENTION " + line.PartNumber + " : extraction partielle, verifier le plan avant envoi.");

                EcrireJournalControle(line, controle.Caracteristiques.Count, traces, controle.Avertissements);
                return pdf;
            }
            catch (Exception ex)
            {
                // Aucune exception ne remonte : l'article suivant doit etre traite.
                Log("ERREUR controle de fabrication " + line.PartNumber + " : " + ex.Message);
                traces.Add("ECHEC : " + ex.Message);
                EcrireJournalControle(line, 0, traces, new List<string>());
                return null;
            }
        }

        /// <summary>Une ligne par article dans ControleFabrication\extraction.log.</summary>
        private void EcrireJournalControle(PartLine line, int retenues,
                                          List<string> traces, List<string> avertissements)
        {
            if (string.IsNullOrEmpty(_journalControles)) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").Append(line.PartNumber)
                  .Append("  -> ").Append(retenues).Append(" caracteristique(s)");
                foreach (string t in traces) sb.AppendLine().Append("      ").Append(t);
                foreach (string a in avertissements) sb.AppendLine().Append("      AVERTISSEMENT : ").Append(a);
                sb.AppendLine();
                File.AppendAllText(_journalControles, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("Journal des controles non ecrit : " + ex.Message);
            }
        }

        /// <summary>
        /// Renseigne un article catalogue depuis l'inventaire : ancienne référence,
        /// désignation, et surtout la référence chez le fournisseur retenu.
        ///
        /// Le statut dit ce qui manque, sans rien bloquer : c'est l'avertissement groupé,
        /// avant l'envoi, qui met l'utilisateur devant le choix.
        /// </summary>
        private void TraiterArticleCatalogue(PartLine ligne)
        {
            ligne.ExportedFiles.Clear();
            ligne.ZipPath = null;
            ligne.TypeCode = PartNumberFormat.TypeCode(ligne.PartNumber);

            // Le coffre n'est pas consulté : ce qui en venait lors d'un traitement précédent
            // n'a plus cours et ne doit pas se glisser dans le message.
            ligne.DrawingRevision = "";
            ligne.Revision = "";
            ligne.RealizedDate = "";
            ligne.Material = "";
            ligne.Treatment = "";
            ligne.State = "";
            ligne.Model3DPath = null;
            ligne.DrawingPath = null;

            InventoryService.Entry inv = InventoryService.Lookup(_inventaire, ligne.PartNumber);
            if (inv == null)
            {
                ligne.Status = "Hors inventaire";
                Log("Inconnu de l'inventaire : " + ligne.PartNumber);
                return;
            }

            ligne.OldRef = inv.OldRef;

            // Le coffre n'est pas consulté en achat catalogue : la désignation ne peut
            // venir que de l'inventaire.
            if (inv.Designation != "") ligne.Description = inv.Designation;

            if (inv.Fournisseurs.Count == 0)
            {
                ligne.Status = "Sans fournisseur";
                Log("Aucun fournisseur déclaré dans l'inventaire pour " + ligne.PartNumber + ".");
                return;
            }

            InventoryService.Fournisseur chez = inv.Chez(_optFournisseurInventaire, _optSupplierName);

            if (chez == null)
            {
                ligne.PdmSupplier = inv.Supplier;
                ligne.Status = "Autre fournisseur";
                Log(ligne.PartNumber + " n'est pas vendu par le destinataire choisi. Déclaré chez : "
                  + NomsFournisseurs(inv) + ".");
                return;
            }

            ligne.PdmSupplier = chez.Nom;
            ligne.SupplierRef = chez.Reference;
            ligne.ManufacturerRef = chez.ReferenceFabricant;
            ligne.Status = chez.Reference == "" ? "Sans référence" : "OK";
        }

        private static string NomsFournisseurs(InventoryService.Entry inv)
        {
            List<string> noms = new List<string>();
            foreach (InventoryService.Fournisseur f in inv.Fournisseurs)
                if (f.Nom != "") noms.Add(f.Nom);
            return noms.Count == 0 ? "aucun" : string.Join(", ", noms);
        }

        /// <summary>
        /// Cherche un article dans les deux sources à la fois : le coffre pour ce qui est
        /// dessiné, l'inventaire pour les désignations, fournisseurs et références.
        /// </summary>
        private void BtnRecherche_Click(object sender, EventArgs e)
        {
            // L'indexation du coffre coûte quelques dizaines de millisecondes : la refaire
            // à l'ouverture garantit une liste à jour sans qu'on ait à s'en soucier.
            Cursor = Cursors.WaitCursor;
            try
            {
                BuildPdmIndex();
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if ((_inventaire == null || _inventaire.Count == 0)
                && (_pdmIndex == null || _pdmIndex.Count == 0))
            {
                MessageBox.Show(
                    "Ni le coffre ni l'inventaire ne sont accessibles : il n'y a rien à chercher."
                    + Environment.NewLine + Environment.NewLine
                    + "Vérifiez le chemin du coffre dans config.json, et la connexion par le bouton "
                    + "« Inventaire… ».",
                    "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (RechercheArticleDialog dlg = new RechercheArticleDialog(
                       _config, SelectedSupplier, _inventaire, _pdmIndex))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                AjouterArticles(dlg.Retenus);
            }
        }

        /// <summary>
        /// Ajoute des articles à la grille, en ignorant ceux qui s'y trouvent déjà et en
        /// réutilisant la première ligne si elle est restée vide.
        /// </summary>
        private void AjouterArticles(List<string> numeros)
        {
            if (numeros == null || numeros.Count == 0) return;

            int ajoutes = 0;
            int deja = 0;
            foreach (string numero in numeros)
            {
                bool present = false;
                foreach (PartLine l in _lines)
                {
                    if (string.Equals(l.PartNumber, numero, StringComparison.OrdinalIgnoreCase))
                    {
                        present = true;
                        break;
                    }
                }
                if (present) { deja++; continue; }

                PartLine vide = null;
                foreach (PartLine l in _lines)
                    if (string.IsNullOrWhiteSpace(l.PartNumber)) { vide = l; break; }

                if (vide != null) vide.PartNumber = numero;
                else
                {
                    PartLine nouvelle = new PartLine();
                    nouvelle.PartNumber = numero;
                    _lines.Add(nouvelle);
                }
                ajoutes++;
            }

            RefreshGrid();
            Log(ajoutes + " article(s) ajouté(s) depuis le catalogue du fournisseur"
              + (deja > 0 ? ", " + deja + " déjà présent(s)" : "") + ".");
        }

        /// <summary>Vrai si cet article s'achète au catalogue, sans plan ni modèle.</summary>
        private bool EstCatalogue(PartLine ligne)
        {
            return RuleFor(ligne.PartNumber).Catalogue;
        }

        /// <summary>
        /// Les articles doivent tous être de la nature annoncée par le type de demande.
        ///
        /// Un achat catalogue et une pièce sur mesure n'appellent ni les mêmes fichiers ni la
        /// même façon de choisir le fournisseur : les mélanger produirait un message faux.
        /// </summary>
        private bool VerifierHomogeneite()
        {
            RequestType type = CurrentType;
            bool attenduCatalogue = RequestTypes.EstCatalogue(type);

            List<string> intrus = new List<string>();
            foreach (PartLine l in _lines)
            {
                if (string.IsNullOrWhiteSpace(l.PartNumber)) continue;
                if (EstCatalogue(l) != attenduCatalogue) intrus.Add(l.PartNumber);
            }
            if (intrus.Count == 0) return true;

            string quoi = attenduCatalogue
                ? "ne sont pas des articles de catalogue"
                : "sont des articles de catalogue";
            string remede = attenduCatalogue
                ? "Retirez-les, ou choisissez « " + RequestTypes.Libelle(RequestType.Offre) + " »."
                : "Retirez-les, ou choisissez « " + RequestTypes.Libelle(RequestType.CommandeCatalogue) + " ».";

            MessageBox.Show(
                intrus.Count + " article(s) " + quoi + ", alors que la demande est une « "
                + RequestTypes.Libelle(type) + " »." + Environment.NewLine + Environment.NewLine
                + Extrait(intrus) + Environment.NewLine + Environment.NewLine + remede,
                "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Log("Demande refusée : " + intrus.Count + " article(s) " + quoi + " pour une « "
              + RequestTypes.Libelle(type) + " ».");
            return false;
        }

        /// <summary>Quelques numéros d'article, pour un message lisible.</summary>
        private static string Extrait(List<string> numeros)
        {
            int max = 5;
            if (numeros.Count <= max) return string.Join(", ", numeros);
            return string.Join(", ", numeros.GetRange(0, max)) + " … et " + (numeros.Count - max) + " autre(s)";
        }

        /// <summary>
        /// Une session SolidWorks deja ouverte appartient a l'utilisateur : on previent
        /// avant d'y ouvrir et refermer des documents. Vrai si l'on peut poursuivre.
        /// </summary>
        private bool ConfirmerSolidWorks()
        {
            if (!SolidWorksExporter.IsSolidWorksRunning()) return true;

            DialogResult reponse = MessageBox.Show(
                "SolidWorks est déjà ouvert sur ce poste." + Environment.NewLine + Environment.NewLine +
                "Les documents seront ouverts et refermés dans votre session pendant le traitement, " +
                "et un document que vous avez déjà ouvert pourrait être refermé." + Environment.NewLine + Environment.NewLine +
                "Enregistrez votre travail avant de continuer." + Environment.NewLine + Environment.NewLine +
                "Continuer malgré tout ?",
                "AskThem", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (reponse != DialogResult.Yes)
            {
                Log("Traitement abandonné : SolidWorks est déjà ouvert.");
                return false;
            }
            Log("SolidWorks déjà ouvert : le traitement se déroulera dans la session existante.");
            return true;
        }

        /// <summary>Passerelle de journalisation utilisable par les services.</summary>
        private void LogFromWorker(string message)
        {
            Log(message);
        }

        /// <summary>Mode génération : export SolidWorks, récapitulatif et email Outlook.</summary>
        private void RunGenerate()
        {
            int total = _work.Count;

            // --- Étape 1 : dossier de la demande, directement dans l'archive réseau ---
            string tag = RequestTypes.Tag(_optType);
            string identifiant = string.IsNullOrWhiteSpace(_optSupplierName) ? _optSupplier : _optSupplierName;
            string folderName = DateTime.Now.ToString("yyyy-MM-dd") + "_" + SafeName(identifiant) + "_" + tag;
            string outputFolder = DossierUnique(Path.Combine(RacineDeSortie(), folderName));
            string folder3D = Path.Combine(outputFolder, "3D_STEP");
            string folder2D = Path.Combine(outputFolder, "2D_PLANS");
            string folderZip = Path.Combine(outputFolder, "ZIP_par_article");
            string folderControles = Path.Combine(outputFolder, "ControleFabrication");
            Directory.CreateDirectory(folder3D);
            Directory.CreateDirectory(folder2D);
            Directory.CreateDirectory(folderZip);
            if (_optControle) Directory.CreateDirectory(folderControles);
            _archivePath = outputFolder;
            Log("Dossier de la demande : " + outputFolder);

            // Le bon de commande fait partie du dossier : il est archivé avec la demande.
            string poArchive = null;
            if (_optPoPath != "" && File.Exists(_optPoPath))
            {
                try
                {
                    poArchive = Path.Combine(outputFolder, Path.GetFileName(_optPoPath));
                    File.Copy(_optPoPath, poArchive, true);
                    Log("Bon de commande archivé : " + Path.GetFileName(poArchive));
                }
                catch (Exception ex)
                {
                    poArchive = null;
                    Log("ERREUR copie du bon de commande : " + ex.Message);
                }
            }

            if (_optControle)
            {
                _controleCfg = ControleFabricationConfig.Load();
                TableSymbolesGtol.Charger(LogFromWorker);
                _generateurPdf = new QuestPdfGenerateur();
                _journalControles = Path.Combine(folderControles, "extraction.log");
            }

            if (!_optCatalogue) BuildPdmIndex();

            LoadInventory();

            SetProgress(0, "Préparation...");

            // Un achat catalogue ne se cherche pas dans le coffre : tout ce qu'il faut est
            // dans l'inventaire. Ni SolidWorks, ni index PDM, ni contrôle de fabrication.
            if (_optCatalogue)
            {
                for (int i = 0; i < total; i++)
                {
                    if (_cancelRequested) { Log("Annulation demandée."); break; }
                    PartLine ligne = _work[i];
                    SetProgress(i, "Article " + (i + 1) + "/" + total + " : " + ligne.PartNumber);
                    TraiterArticleCatalogue(ligne);
                    SetProgress(i + 1, "Article " + (i + 1) + "/" + total + " : " + ligne.PartNumber);
                }
                RefreshGrid();
            }
            else

            // --- Étape 2 : connexion à SolidWorks ---
            {
            SolidWorksExporter exporter = new SolidWorksExporter(_config.Properties);
            bool connected = false;
            try
            {
                try
                {
                    exporter.Connect();
                    connected = true;
                    Log("SolidWorks démarré en arrière-plan.");
                }
                catch (Exception ex)
                {
                    Log("ERREUR : " + ex.Message);
                    UiInvoke(delegate
                    {
                        MessageBox.Show("SolidWorks n'a pas pu être démarré. Vérifiez qu'il est installé et fermez toute boîte de dialogue ouverte.",
                            "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                    return;
                }

                // --- Étape 3 : boucle sur les articles ---
                for (int i = 0; i < total; i++)
                {
                    if (_cancelRequested) { Log("Annulation demandée."); break; }

                    PartLine line = _work[i];
                    SetProgress(i, "Traitement " + (i + 1) + "/" + total + " : " + line.PartNumber);
                    try
                    {
                        ProcessOneLine(exporter, line, folder3D, folder2D, folderZip, folderControles);
                    }
                    catch (Exception ex)
                    {
                        // Un échec unitaire n'interrompt jamais le lot.
                        line.Status = "Erreur";
                        Log("ERREUR " + line.PartNumber + " : " + ex.Message);
                    }
                    finally
                    {
                        SetProgress(i + 1, "Traitement " + (i + 1) + "/" + total + " : " + line.PartNumber);
                        RefreshGrid();
                    }
                }
            }
            finally
            {
                // --- Étape 4 : fermeture de SolidWorks, même en cas d'annulation ---
                exporter.Dispose();
                if (connected) Log("SolidWorks fermé.");
            }
            }

            // --- Étape 6 : répartition des archives sur un ou plusieurs messages ---
            List<LotEnvoi> lots = RepartitionEnvois.Repartir(
                _work, _config.ZipThresholdMb, _config.MaxAttachments, LogFromWorker);

            double totalMb = 0;
            int nbArchives = 0;
            foreach (LotEnvoi lot in lots)
            {
                totalMb += lot.TailleMb;
                nbArchives += lot.PiecesJointes.Count;
            }

            if (lots.Count > 1)
            {
                Log(nbArchives + " archive(s) pour " + totalMb.ToString("0.0") + " Mo : au-delà de "
                  + _config.ZipThresholdMb + " Mo ou de " + _config.MaxAttachments
                  + " pièces jointes par message, la demande part en " + lots.Count + " emails.");
            }
            else
            {
                Log(nbArchives + " archive(s) par article jointe(s), " + totalMb.ToString("0.0") + " Mo au total.");
            }

            // Joint séparément au premier message : le fournisseur doit le voir sans ouvrir
            // d'archive, et il n'a pas à le recevoir en plusieurs exemplaires.
            string cheminPo = poArchive != null ? poArchive : _optPoPath;
            bool poJoignable = cheminPo != "" && File.Exists(cheminPo);
            if (poJoignable) Log("Bon de commande joint : " + Path.GetFileName(cheminPo));

            // --- Avertissement groupé, avant de préparer les emails ---
            WarnAboutIssues();

            // --- Étape 8 : emails Outlook (jamais en cas d'annulation) ---
            List<object> mailsOuverts = new List<object>();
            List<string> cheminsMsg = new List<string>();
            if (!_cancelRequested)
            {
                string nomPo = _optPoPath == "" ? "" : Path.GetFileName(_optPoPath);
                for (int i = 0; i < lots.Count; i++)
                {
                    LotEnvoi lot = lots[i];
                    try
                    {
                        string subject = EmailBuilder.BuildSubject(_optType, _optProject, lot.Lignes.Count)
                                       + Numerotation(i + 1, lots.Count);
                        string body = EmailBuilder.BuildBody(_optType, lot.Lignes, _optProject, _optDeadline,
                                                             _optConditions, i == 0 ? nomPo : "", _optCatalogue);

                        List<string> pieces = new List<string>(lot.PiecesJointes);
                        if (poJoignable && i == 0) pieces.Add(cheminPo);

                        object mail = OutlookService.CreateMail(_optSupplier, _optSupplierCc, subject, body, pieces);
                        mailsOuverts.Add(mail);
                        cheminsMsg.Add(Path.Combine(outputFolder, NomMessage(i + 1, lots.Count)));
                        Log("Email " + (i + 1) + "/" + lots.Count + " préparé : " + lot.Lignes.Count
                          + " article(s), " + lot.TailleMb.ToString("0.0") + " Mo.");
                    }
                    catch (Exception ex)
                    {
                        Log("ERREUR Outlook (message " + (i + 1) + "/" + lots.Count + ") : " + ex.Message);
                        string folder = outputFolder;
                        string message = ex.Message;
                        UiInvoke(delegate
                        {
                            MessageBox.Show("L'email n'a pas pu être créé : " + message +
                                Environment.NewLine + Environment.NewLine +
                                "Les fichiers restent disponibles dans : " + folder,
                                "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        });
                    }
                }
                if (mailsOuverts.Count > 0)
                    Log("Aucun message n'est envoyé automatiquement.");
            }

            // --- Étape 9 : bilan ---
            ShowSummary(outputFolder);

            // --- Étape 10 : suivi silencieux des emails, interface déjà rendue ---
            WatchMails(mailsOuverts, cheminsMsg, outputFolder);
        }

        /// <summary>Suffixe de sujet quand la demande part en plusieurs messages.</summary>
        private static string Numerotation(int rang, int total)
        {
            return total <= 1 ? "" : " (" + rang + "/" + total + ")";
        }

        /// <summary>Nom du .msg archivé, distinct pour chaque message d'une même demande.</summary>
        private static string NomMessage(int rang, int total)
        {
            return total <= 1 ? "Demande.msg" : "Demande_" + rang + "sur" + total + ".msg";
        }

        /// <summary>
        /// Traite un article. Chaque document n'est ouvert QU'UNE FOIS : les propriétés sont
        /// lues et l'export réalisé dans la même ouverture. Le plan est traité en premier
        /// car sa révision prime sur celle du modèle dans le tableau de l'email.
        /// </summary>
        private void ProcessOneLine(SolidWorksExporter exporter, PartLine line,
                                    string folder3D, string folder2D, string folderZip,
                                    string folderControles)
        {
            line.ExportedFiles.Clear();
            line.ZipPath = null;
            line.TypeCode = PartNumberFormat.TypeCode(line.PartNumber);

            // Le type de l'article décide de ce qu'on livre : un article catalogue
            // se commande par sa référence fournisseur, sans 3D ni plan.
            ArticleTypeRule regle = RuleFor(line.PartNumber);
            bool livrer3D = _opt3D && regle.Export3D;
            bool livrer2D = _opt2D && regle.Export2D;

            line.Model3DPath = PdmSearchService.Find3DInIndex(_pdmIndex, line.PartNumber);
            line.DrawingPath = PdmSearchService.FindDrawingInIndex(_pdmIndex, line.PartNumber);

            if (line.Model3DPath == null && line.DrawingPath == null)
            {
                line.Status = "Introuvable";
                Log("Introuvable dans le PDM : " + line.PartNumber);
                return;
            }

            string baseName = null;

            // --- Le plan : une seule ouverture pour lire les propriétés et exporter ---
            if (line.DrawingPath != null)
            {
                ModelDoc2 doc = null;
                try
                {
                    doc = exporter.OpenDocument(line.DrawingPath);
                    SolidWorksExporter.DocMetadata m = exporter.ReadMetadata(doc);
                    line.DrawingRevision = m.Revision;
                    line.Description = m.Description;
                    line.RealizedDate = m.Date;
                    line.Material = m.Material;
                    line.Treatment = m.Treatment;
                    line.State = m.State;
                    line.PdmSupplier = m.Supplier;
                    line.SupplierRef = m.SupplierRef;

                    baseName = SafeName(line.PartNumber);
                    if (livrer2D)
                    {
                        List<string> created = exporter.ExportDrawing(doc, folder2D, baseName);
                        line.ExportedFiles.AddRange(created);
                        foreach (string f in created) Log("Plan : " + Path.GetFileName(f));
                    }

                    // Le controle est tire du plan deja ouvert : le document n'est jamais
                    // rouvert. Un echec ici ne touche ni l'export PDF/DXF ni les autres articles.
                    if (_optControle && regle.Export2D) GenererControle(doc, line, folderControles);
                }
                finally
                {
                    exporter.CloseDocument(doc);
                }
            }

            // --- Le modèle 3D : une seule ouverture également ---
            if (line.Model3DPath != null)
            {
                ModelDoc2 doc = null;
                try
                {
                    doc = exporter.OpenDocument(line.Model3DPath);
                    SolidWorksExporter.DocMetadata m = exporter.ReadMetadata(doc);
                    line.Revision = m.Revision;
                    // Le plan reste prioritaire : le modèle ne comble que les manques.
                    if (line.Description == "") line.Description = m.Description;
                    if (line.RealizedDate == "") line.RealizedDate = m.Date;
                    if (line.Material == "") line.Material = m.Material;
                    if (line.Treatment == "") line.Treatment = m.Treatment;
                    if (line.State == "") line.State = m.State;
                    if (line.PdmSupplier == "") line.PdmSupplier = m.Supplier;
                    if (line.SupplierRef == "") line.SupplierRef = m.SupplierRef;

                    if (baseName == null) baseName = SafeName(line.PartNumber);
                    if (livrer3D)
                    {
                        string step = exporter.ExportStep(doc, folder3D, baseName);
                        line.ExportedFiles.Add(step);
                        Log("STEP : " + Path.GetFileName(step));
                    }
                }
                finally
                {
                    exporter.CloseDocument(doc);
                }
            }

            // --- Ce que l'inventaire sait de cet article ---
            InventoryService.Entry inv = InventoryService.Lookup(_inventaire, line.PartNumber);
            if (inv != null)
            {
                line.OldRef = inv.OldRef;
                if (line.SupplierRef == "") line.SupplierRef = inv.SupplierRef;
                if (line.PdmSupplier == "") line.PdmSupplier = inv.Supplier;
            }

            // --- Une archive par numéro d'article ---
            if (line.ExportedFiles.Count > 0)
            {
                try
                {
                    string zipPath = Path.Combine(folderZip, (baseName == null ? SafeName(line.PartNumber) : baseName) + ".zip");
                    line.ZipPath = ZipService.ZipFiles(line.ExportedFiles, zipPath, _optCompression);
                    Log("Archive : " + Path.GetFileName(line.ZipPath) + " (" + line.ExportedFiles.Count + " fichier(s))");
                }
                catch (Exception ex)
                {
                    Log("ERREUR archive " + line.PartNumber + " : " + ex.Message);
                }
            }

            // --- Statut : on ne réclame que ce que le type impose de livrer ---
            if (livrer3D && line.Model3DPath == null) line.Status = "Manquant 3D";
            else if (livrer2D && line.DrawingPath == null) line.Status = "Manquant 2D";
            else line.Status = "OK";
        }

        /// <summary>
        /// Règle du type d'article, déduite des caractères YZ du code. Un type inconnu
        /// reçoit la règle par défaut, et l'utilisateur en est informé dans le journal.
        /// </summary>
        private ArticleTypeRule RuleFor(string partNumber)
        {
            string type = PartNumberFormat.TypeCode(partNumber);
            ArticleTypeRule regle;
            if (type != "" && _config.ArticleTypes != null && _config.ArticleTypes.TryGetValue(type, out regle))
                return regle;

            // Type non déclaré : rien ne part tant que sa règle n'a pas été écrite
            // dans config.json. Mieux vaut refuser que livrer au hasard.
            return ArticleTypeRule.Create(
                type == "" ? "type indéterminé" : "type " + type + " non déclaré",
                false, false, false, false, false);
        }

        /// <summary>Vrai si l'état PDM lu est renseigné et ne fait pas partie des états libérés.</summary>
        private bool IsInDevelopment(PartLine line)
        {
            if (string.IsNullOrWhiteSpace(line.State)) return false;
            string etat = line.State.Trim();
            foreach (string libere in _config.ReleasedStates)
            {
                if (string.Equals(etat, libere, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// Un SEUL message pour tous les articles à problème : aucun article n'est signalé
        /// individuellement. Le détail complet reste dans le journal et le récapitulatif.
        /// </summary>
        private void WarnAboutIssues()
        {
            List<string> sansPlan = new List<string>();
            List<string> enDeveloppement = new List<string>();
            List<string> mauvaisFournisseur = new List<string>();
            List<string> sansReference = new List<string>();
            int etatsLus = 0;
            int referencesLues = 0;

            if (_optCatalogue) { AvertirCatalogue(); return; }

            foreach (PartLine l in _work)
            {
                if (string.IsNullOrWhiteSpace(l.Status)) continue;   // non traité
                if (l.Status == "Introuvable") continue;             // déjà compté ailleurs
                if (!string.IsNullOrWhiteSpace(l.State)) etatsLus++;
                if (IsInDevelopment(l)) enDeveloppement.Add(l.PartNumber + " — " + l.State.Trim());

                ArticleTypeRule regle = RuleFor(l.PartNumber);

                // On ne réclame un plan que pour les types qui doivent en avoir un.
                if (regle.Export2D && l.DrawingPath == null) sansPlan.Add(l.PartNumber);

                if (!string.IsNullOrWhiteSpace(l.SupplierRef)) referencesLues++;
                if (!regle.SupplierImposed) continue;

                // Article catalogue : la référence fournisseur doit accompagner la demande.
                if (string.IsNullOrWhiteSpace(l.SupplierRef))
                    sansReference.Add(l.PartNumber + " — " + regle.Label);

                if (!string.IsNullOrWhiteSpace(l.PdmSupplier) && !SameSupplier(l.PdmSupplier, _optSupplierName))
                    mauvaisFournisseur.Add(l.PartNumber + " — imposé : " + l.PdmSupplier.Trim());
            }

            // Si aucune référence n'a été lue nulle part, la source n'est pas dans les
            // propriétés des fichiers : on le dit une fois plutôt que d'énumérer tous
            // les articles catalogue à chaque demande.
            if (referencesLues == 0 && sansReference.Count > 0)
            {
                Log("Aucune référence fournisseur dans les propriétés des fichiers ("
                  + sansReference.Count + " article(s) catalogue concernés). "
                  + "Ces données se trouvent dans l'inventaire, pas dans le PDM.");
                sansReference.Clear();
            }

            if (etatsLus == 0 && _generateMode)
            {
                Log("Aucun état PDM lisible dans les propriétés des fichiers : le contrôle "
                  + "« libéré / en développement » n'a pas pu s'appliquer.");
            }

            if (sansPlan.Count == 0 && enDeveloppement.Count == 0
                && mauvaisFournisseur.Count == 0 && sansReference.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            if (mauvaisFournisseur.Count > 0)
            {
                sb.AppendLine("Fournisseur imposé par le PDM — " + mauvaisFournisseur.Count + " article(s) :");
                sb.AppendLine(Summarize(mauvaisFournisseur));
                foreach (string x in mauvaisFournisseur) Log("Fournisseur imposé : " + x);
                sb.AppendLine("Ces articles ne peuvent être commandés qu'à leur fournisseur.");
                sb.AppendLine();
            }
            if (sansReference.Count > 0)
            {
                sb.AppendLine("Sans référence fournisseur — " + sansReference.Count + " article(s) :");
                sb.AppendLine(Summarize(sansReference));
                foreach (string x in sansReference) Log("Sans référence fournisseur : " + x);
                sb.AppendLine();
            }
            if (enDeveloppement.Count > 0)
            {
                sb.AppendLine("Non libérés dans le PDM — " + enDeveloppement.Count + " article(s) :");
                sb.AppendLine(Summarize(enDeveloppement));
                foreach (string x in enDeveloppement) Log("Non libéré : " + x);
                sb.AppendLine();
            }
            if (sansPlan.Count > 0)
            {
                sb.AppendLine("Sans plan 2D — " + sansPlan.Count + " article(s) :");
                sb.AppendLine(Summarize(sansPlan));
                foreach (string x in sansPlan) Log("Sans plan : " + x);
            }
            sb.AppendLine();
            sb.Append("Le détail complet figure dans le journal, en bas de la fenêtre.");

            string message = sb.ToString();
            UiInvoke(delegate
            {
                MessageBox.Show(message, "AskThem — points à vérifier",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        /// <summary>
        /// Points à vérifier sur une demande d'achat catalogue. Rien n'est bloqué : chaque
        /// cas est nommé, avec les fournisseurs qui vendent réellement l'article, et
        /// l'utilisateur décide.
        /// </summary>
        private void AvertirCatalogue()
        {
            List<string> horsInventaire = new List<string>();
            List<string> sansFournisseur = new List<string>();
            List<string> autreFournisseur = new List<string>();
            List<string> sansReference = new List<string>();

            foreach (PartLine l in _work)
            {
                switch (l.Status)
                {
                    case "Hors inventaire": horsInventaire.Add(l.PartNumber); break;
                    case "Sans fournisseur": sansFournisseur.Add(l.PartNumber); break;
                    case "Autre fournisseur": autreFournisseur.Add(l.PartNumber); break;
                    case "Sans référence": sansReference.Add(l.PartNumber); break;
                }
            }

            if (_optFournisseurInventaire == 0)
            {
                string nom = _optSupplierName == "" ? "Le destinataire choisi" : "« " + _optSupplierName + " »";
                Log(nom + " n'est lié à aucune fiche de l'inventaire : aucune référence "
                  + "fournisseur ne peut être renseignée.");
                UiInvoke(delegate
                {
                    MessageBox.Show(
                        nom + " n'est lié à aucune fiche de l'inventaire." + Environment.NewLine + Environment.NewLine
                        + "Sans ce lien, AskThem ne sait pas quelle référence l'article porte chez lui, "
                        + "et la demande partira sans aucune référence." + Environment.NewLine + Environment.NewLine
                        + "Le bouton « Fournisseurs… » permet de faire le lien.",
                        "AskThem — points à vérifier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
            }

            if (horsInventaire.Count == 0 && sansFournisseur.Count == 0
                && autreFournisseur.Count == 0 && sansReference.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            if (autreFournisseur.Count > 0)
            {
                sb.AppendLine("Non vendus par ce fournisseur — " + autreFournisseur.Count + " article(s) :");
                sb.AppendLine(Summarize(autreFournisseur));
                sb.AppendLine("Le journal indique, pour chacun, qui les vend.");
                sb.AppendLine();
            }
            if (sansFournisseur.Count > 0)
            {
                sb.AppendLine("Aucun fournisseur dans l'inventaire — " + sansFournisseur.Count + " article(s) :");
                sb.AppendLine(Summarize(sansFournisseur));
                sb.AppendLine();
            }
            if (horsInventaire.Count > 0)
            {
                sb.AppendLine("Inconnus de l'inventaire — " + horsInventaire.Count + " article(s) :");
                sb.AppendLine(Summarize(horsInventaire));
                sb.AppendLine();
            }
            if (sansReference.Count > 0)
            {
                sb.AppendLine("Sans référence chez ce fournisseur — " + sansReference.Count + " article(s) :");
                sb.AppendLine(Summarize(sansReference));
                sb.AppendLine();
            }
            sb.Append("Ces articles partiront sans référence. Le détail figure dans le journal.");

            string message = sb.ToString();
            UiInvoke(delegate
            {
                MessageBox.Show(message, "AskThem — points à vérifier",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        /// <summary>
        /// Comparaison indulgente de deux noms de fournisseur : le PDM et la liste
        /// d'AskThem ne les écrivent pas forcément à l'identique.
        /// </summary>
        private static bool SameSupplier(string pdm, string choisi)
        {
            if (string.IsNullOrWhiteSpace(pdm) || string.IsNullOrWhiteSpace(choisi)) return true;
            string a = pdm.Trim().ToLowerInvariant();
            string b = choisi.Trim().ToLowerInvariant();
            return a == b || a.Contains(b) || b.Contains(a);
        }

        /// <summary>Liste abrégée : au plus 20 éléments, puis un décompte du reste.</summary>
        private static string Summarize(List<string> items)
        {
            const int max = 20;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < max; i++)
            {
                sb.Append("    • ");
                sb.AppendLine(items[i]);
            }
            if (items.Count > max)
                sb.AppendLine("    … et " + (items.Count - max) + " autre(s).");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Enregistre chaque email au format .msg à côté de la demande, puis continue de les
        /// réenregistrer tant qu'ils restent ouverts dans Outlook : les retouches de
        /// l'utilisateur sont ainsi capturées sans jamais lui demander quoi que ce soit.
        /// Le suivi d'un message s'arrête dès qu'Outlook ne le rend plus accessible ; les
        /// autres continuent d'être suivis.
        /// </summary>
        private void WatchMails(List<object> mails, List<string> chemins, string dossier)
        {
            if (mails == null || mails.Count == 0)
            {
                SetBusy(false);
                UiInvoke(delegate { lblProgress.Text = "Prêt."; });
                return;
            }

            // Suivis encore ouverts : chaque message quitte la liste quand Outlook cesse
            // de le rendre accessible, c'est-à-dire quand il est fermé ou envoyé.
            List<int> actifs = new List<int>();
            int[] echecs = new int[mails.Count];
            for (int i = 0; i < mails.Count; i++)
            {
                if (OutlookService.SaveMessage(mails[i], chemins[i]))
                {
                    actifs.Add(i);
                    Log("Email enregistré : " + chemins[i]);
                }
                else
                {
                    Log("L'email n'a pas pu être enregistré dans " + dossier + ".");
                }
            }

            // L'interface est rendue à l'utilisateur : le suivi ne le bloque pas.
            SetBusy(false);
            UiInvoke(delegate { lblProgress.Text = "Prêt."; });
            if (actifs.Count == 0) return;

            _stopMailWatch = false;
            DateTime limite = DateTime.Now.AddMinutes(30);
            while (DateTime.Now < limite && !_stopMailWatch && actifs.Count > 0)
            {
                Thread.Sleep(4000);
                if (_stopMailWatch) break;

                for (int k = actifs.Count - 1; k >= 0; k--)
                {
                    int i = actifs[k];
                    if (OutlookService.SaveMessage(mails[i], chemins[i]))
                    {
                        echecs[i] = 0;
                    }
                    else
                    {
                        // Message fermé ou envoyé : la dernière version reste enregistrée.
                        echecs[i]++;
                        if (echecs[i] >= 2)
                        {
                            Log("Email archivé dans son état final : " + chemins[i]);
                            actifs.RemoveAt(k);
                        }
                    }
                }
            }

            foreach (int i in actifs)
                Log("Email archivé dans son état final : " + chemins[i]);
        }

        /// <summary>
        /// Racine où écrire la demande : l'archive réseau si elle est joignable,
        /// sinon le dossier local, pour ne jamais perdre un traitement.
        /// </summary>
        private string RacineDeSortie()
        {
            if (!string.IsNullOrWhiteSpace(_config.ArchiveRoot) && Directory.Exists(_config.ArchiveRoot))
                return _config.ArchiveRoot;

            Log("Archive réseau inaccessible (" + _config.ArchiveRoot + ") : "
              + "la demande est écrite en local, dans " + _config.OutputRoot + ".");
            return _config.OutputRoot;
        }

        /// <summary>Évite d'écraser une demande du même jour pour le même fournisseur.</summary>
        private static string DossierUnique(string souhaite)
        {
            string cible = souhaite;
            int suffixe = 2;
            while (Directory.Exists(cible))
            {
                cible = souhaite + "_" + suffixe;
                suffixe++;
            }
            return cible;
        }

        /// <summary>
        /// Bilan du traitement. Rien ne s'affiche quand tout s'est bien passé :
        /// le journal en bas de fenêtre suffit. Seule une annulation est signalée.
        /// </summary>
        private void ShowSummary(string outputFolder)
        {
            int ok = 0;
            int warn = 0;
            int err = 0;
            foreach (PartLine l in _work)
            {
                if (l.Status == "OK") ok++;
                else if (l.Status == "Erreur") err++;
                else if (l.Status != "") warn++;
            }
            Log("Terminé : " + ok + " article(s) exporté(s), " + warn + " avertissement(s), " + err + " erreur(s).");
            Log("Demande enregistrée dans : " + outputFolder);

            if (!_cancelRequested) return;

            string dossier = outputFolder;
            UiInvoke(delegate
            {
                MessageBox.Show("Traitement annulé. Les fichiers déjà extraits sont conservés dans : " + dossier,
                    "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        /// <summary>Remplace les caractères interdits dans un nom de fichier.</summary>
        private static string SafeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        // ==================================================================
        // Accès à l'interface depuis le thread de traitement
        // ==================================================================

        /// <summary>Exécute une action sur le thread de l'interface.</summary>
        private void UiInvoke(Action action)
        {
            if (InvokeRequired)
            {
                try { this.Invoke(action); }
                catch (Exception) { } // la fenêtre a pu être fermée pendant le traitement
            }
            else
            {
                action();
            }
        }

        /// <summary>Ajoute un message au journal de la fenêtre et au fichier journal.</summary>
        private void Log(string message)
        {
            string text = DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine;
            UiInvoke(delegate
            {
                txtLog.AppendText(text);
                try
                {
                    txtLog.SelectionStart = txtLog.TextLength;
                    txtLog.ScrollToCaret();
                }
                catch (Exception) { }
            });
            LogService.Write(message);
        }

        /// <summary>Active ou désactive l'interface pendant un traitement.</summary>
        private void SetBusy(bool busy)
        {
            _busy = busy;
            UiInvoke(delegate
            {
                panelTools.Enabled = !busy;
                grid.Enabled = !busy;
                panelDetail.Enabled = !busy;
                btnVerify.Enabled = !busy;
                btnGenerate.Enabled = !busy;
                cboType.Enabled = !busy;
                btnSuppliers.Enabled = !busy;
                btnInventaire.Enabled = !busy;
                btnCancel.Enabled = busy;
                Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            });
        }

        private void SetProgress(int value, string text)
        {
            UiInvoke(delegate
            {
                int v = value;
                if (v < progress.Minimum) v = progress.Minimum;
                if (v > progress.Maximum) v = progress.Maximum;
                progress.Value = v;
                lblProgress.Text = text;
            });
        }

        /// <summary>Rafraîchit l'affichage de la grille après modification des lignes.</summary>
        private void RefreshGrid()
        {
            UiInvoke(delegate
            {
                try { _lines.ResetBindings(); }
                catch (Exception) { }
                UpdateDetail();
            });
        }
    }
}
