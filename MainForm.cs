using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AskThem.Controls;
using AskThem.Models;
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
        private ModeSwitch modeSwitch;
        private Label lblInfo;

        private Panel panelTools;
        private Button btnAddLine;
        private Button btnPaste;
        private Button btnImportCsv;
        private Button btnExportCsv;
        private Button btnClear;
        private Button btnInventaire;

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
        private ToolTip toolTip = new ToolTip();
        private TextBox txtProject;
        private DateTimePicker dtpDeadline;
        private CheckBox chk3D;
        private CheckBox chk2D;
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
            Log("Dossier des exports : " + _config.OutputRoot);
        }

        // ==================================================================
        // Construction de l'interface
        // ==================================================================

        private void BuildTopPanel()
        {
            panelTop = new Panel();
            panelTop.Dock = DockStyle.Top;

            // Interrupteur : gauche = offre, droite = fabrication.
            modeSwitch = new ModeSwitch();
            modeSwitch.Font = AppFont.Get();
            modeSwitch.LeftText = "Demande d'offre";
            modeSwitch.RightText = "Demande de fabrication";
            modeSwitch.Location = new Point(12, 13);
            modeSwitch.Height = 30;
            modeSwitch.Width = modeSwitch.PreferredWidth;
            modeSwitch.IsRight = false;
            modeSwitch.ModeChanged += new EventHandler(ModeSwitch_ModeChanged);

            lblInfo = new Label();
            lblInfo.Font = AppFont.Get();
            lblInfo.Text = "Saisissez ou collez (Ctrl+V depuis Excel) vos numéros d'article.";
            lblInfo.ForeColor = Color.Gray;
            lblInfo.Location = new Point(modeSwitch.Right + 32, 20);
            lblInfo.AutoSize = true;

            panelTop.Controls.Add(modeSwitch);
            panelTop.Height = Math.Max(modeSwitch.Height, lblInfo.PreferredHeight) + 30;
            panelTop.Controls.Add(lblInfo);
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
            panelTools.Height = btnAddLine.Height + 16;
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

            flow.Controls.Add(Groupe("Destinataire :", cboSupplier, btnSuppliers));
            flow.Controls.Add(Groupe("Référence commande :", txtProject, null));
            flow.Controls.Add(Groupe("Délai souhaité :", dtpDeadline, null));
            flow.Controls.Add(Groupe("", chk3D, chk2D));
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
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PerformLayout();
            AppliquerSeparateurs();
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

        /// <summary>Type de demande actuellement sélectionné.</summary>
        private RequestType CurrentType
        {
            get { return modeSwitch.IsRight ? RequestType.Fabrication : RequestType.Offre; }
        }

        private void ModeSwitch_ModeChanged(object sender, EventArgs e)
        {
            ApplyMode();
        }

        /// <summary>Affiche ou masque les quantités 2 et 3 selon le mode.</summary>
        private void ApplyMode()
        {
            bool offre = !modeSwitch.IsRight;
            colQty2.Visible = offre;
            colQty3.Visible = offre;

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

        private void BtnInventaire_Click(object sender, EventArgs e)
        {
            using (InventoryDialog dlg = new InventoryDialog(_config))
            {
                dlg.ShowDialog(this);
            }
            _config = ConfigService.Load();
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
                        Log(message);
                        string qui = api.WhoAmI();
                        if (qui != "") Log("Inventaire consulté en lecture seule par : " + qui);
                        _inventaire = api.LoadAll(out message);
                        Log(message);
                        if (_inventaire.Count > 0) return;
                    }
                    else
                    {
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

            // Une session SolidWorks deja ouverte appartient a l'utilisateur : on previent
            // avant d'y ouvrir et refermer des documents.
            if (generate && SolidWorksExporter.IsSolidWorksRunning())
            {
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
                    return;
                }
                Log("SolidWorks déjà ouvert : le traitement se déroulera dans la session existante.");
            }

            // Les valeurs de l'interface sont lues ici, sur le thread interface.
            _generateMode = generate;
            _opt3D = chk3D.Checked;
            _opt2D = chk2D.Checked;
            Supplier fournisseur = SelectedSupplier;
            _optSupplier = fournisseur == null ? "" : fournisseur.ToLine;
            _optSupplierCc = fournisseur == null ? "" : fournisseur.CcLine;
            _optSupplierName = fournisseur == null ? "" : fournisseur.Name;
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
            BuildPdmIndex();

            LoadInventory();

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
            string tag = _optType == RequestType.Offre ? "OFFRE" : "FAB";
            string identifiant = string.IsNullOrWhiteSpace(_optSupplierName) ? _optSupplier : _optSupplierName;
            string folderName = DateTime.Now.ToString("yyyy-MM-dd") + "_" + SafeName(identifiant) + "_" + tag;
            string outputFolder = DossierUnique(Path.Combine(RacineDeSortie(), folderName));
            string folder3D = Path.Combine(outputFolder, "3D_STEP");
            string folder2D = Path.Combine(outputFolder, "2D_PLANS");
            string folderZip = Path.Combine(outputFolder, "ZIP_par_article");
            Directory.CreateDirectory(folder3D);
            Directory.CreateDirectory(folder2D);
            Directory.CreateDirectory(folderZip);
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

            BuildPdmIndex();

            LoadInventory();

            SetProgress(0, "Préparation...");

            // --- Étape 2 : connexion à SolidWorks ---
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
                        ProcessOneLine(exporter, line, folder3D, folder2D, folderZip);
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

            // --- Étape 6 : pièces jointes, une archive par numéro d'article ---
            List<string> attachments = new List<string>();
            int nbArchives = 0;
            foreach (PartLine l in _work)
            {
                if (l.ZipPath != null && File.Exists(l.ZipPath)) { attachments.Add(l.ZipPath); nbArchives++; }
            }
            try
            {
                double sizeMb = ZipService.TotalSizeMb(attachments);
                bool tropNombreuses = nbArchives > _config.MaxAttachments;
                bool tropLourdes = sizeMb > _config.ZipThresholdMb;
                if (nbArchives > 1 && (tropNombreuses || tropLourdes))
                {
                    // Le destinataire retrouve quand meme un dossier par article dans l'archive.
                    string master = ZipService.ZipFolder(folderZip, folderName + "_archives");
                    attachments.Clear();
                    attachments.Add(master);
                    string raison = tropNombreuses
                        ? nbArchives + " archives > " + _config.MaxAttachments
                        : sizeMb.ToString("0.0") + " Mo > " + _config.ZipThresholdMb + " Mo";
                    Log("Regroupement (" + raison + ") : une seule archive jointe.");
                }
                else
                {
                    Log(nbArchives + " archive(s) par article jointe(s), " + sizeMb.ToString("0.0") + " Mo au total.");
                }
            }
            catch (Exception ex)
            {
                Log("ERREUR archive ZIP : " + ex.Message);
            }

            // Joint séparément : le fournisseur doit le voir sans ouvrir d'archive.
            string cheminPo = poArchive != null ? poArchive : _optPoPath;
            if (cheminPo != "" && File.Exists(cheminPo))
            {
                attachments.Add(cheminPo);
                Log("Bon de commande joint : " + Path.GetFileName(cheminPo));
            }

            // --- Avertissement groupé, avant de préparer l'email ---
            WarnAboutIssues();

            // --- Étape 8 : email Outlook (jamais en cas d'annulation) ---
            object mailOuvert = null;
            if (!_cancelRequested)
            {
                try
                {
                    string subject = EmailBuilder.BuildSubject(_optType, _optProject, _work.Count);
                    string nomPo = _optPoPath == "" ? "" : Path.GetFileName(_optPoPath);
                    string body = EmailBuilder.BuildBody(_optType, _work, _optProject, _optDeadline,
                                                         _optConditions, nomPo);
                    mailOuvert = OutlookService.CreateMail(_optSupplier, _optSupplierCc, subject, body, attachments);
                    Log("Email préparé dans Outlook. Il n'est pas envoyé automatiquement.");
                }
                catch (Exception ex)
                {
                    Log("ERREUR Outlook : " + ex.Message);
                    string folder = outputFolder;
                    UiInvoke(delegate
                    {
                        MessageBox.Show("L'email n'a pas pu être créé : " + ex.Message +
                            Environment.NewLine + Environment.NewLine +
                            "Les fichiers restent disponibles dans : " + folder,
                            "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
            }

            // --- Étape 9 : bilan ---
            ShowSummary(outputFolder);

            // --- Étape 10 : suivi silencieux de l'email, interface déjà rendue ---
            if (mailOuvert != null) WatchMail(mailOuvert, outputFolder);
        }

        /// <summary>
        /// Traite un article. Chaque document n'est ouvert QU'UNE FOIS : les propriétés sont
        /// lues et l'export réalisé dans la même ouverture. Le plan est traité en premier
        /// car il porte la révision de référence, qui nomme tous les fichiers de l'article.
        /// </summary>
        private void ProcessOneLine(SolidWorksExporter exporter, PartLine line,
                                    string folder3D, string folder2D, string folderZip)
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

                    baseName = SafeName(BuildBaseName(line));
                    if (livrer2D)
                    {
                        List<string> created = exporter.ExportDrawing(doc, folder2D, baseName);
                        line.ExportedFiles.AddRange(created);
                        foreach (string f in created) Log("Plan : " + Path.GetFileName(f));
                    }
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

                    if (baseName == null) baseName = SafeName(BuildBaseName(line));
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
                    line.ZipPath = ZipService.ZipFiles(line.ExportedFiles, zipPath);
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

        /// <summary>Nom de base des fichiers : numéro d'article, suffixé de la révision si connue.</summary>
        private static string BuildBaseName(PartLine line)
        {
            string rev = line.EffectiveRevision;
            if (string.IsNullOrWhiteSpace(rev)) return line.PartNumber;
            return line.PartNumber + "_Rev" + rev;
        }

        /// <summary>
        /// Enregistre l'email au format .msg à côté de la demande, puis continue de le
        /// réenregistrer tant qu'il reste ouvert dans Outlook : les retouches de
        /// l'utilisateur sont ainsi capturées sans jamais lui demander quoi que ce soit.
        /// Le suivi s'arrête dès qu'Outlook ne rend plus le message accessible.
        /// </summary>
        private void WatchMail(object mail, string dossier)
        {
            string chemin = Path.Combine(dossier, "Demande.msg");
            if (!OutlookService.SaveMessage(mail, chemin))
            {
                Log("L'email n'a pas pu être enregistré dans " + dossier + ".");
                return;
            }
            Log("Email enregistré : " + chemin);

            // L'interface est rendue à l'utilisateur : le suivi ne le bloque pas.
            SetBusy(false);
            UiInvoke(delegate { lblProgress.Text = "Prêt."; });

            _stopMailWatch = false;
            DateTime limite = DateTime.Now.AddMinutes(30);
            int echecs = 0;
            while (DateTime.Now < limite && !_stopMailWatch)
            {
                Thread.Sleep(4000);
                if (_stopMailWatch) break;
                if (OutlookService.SaveMessage(mail, chemin))
                {
                    echecs = 0;
                }
                else
                {
                    // Message fermé ou envoyé : la dernière version reste enregistrée.
                    echecs++;
                    if (echecs >= 2) break;
                }
            }
            Log("Email archivé dans son état final : " + chemin);
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
                modeSwitch.Enabled = !busy;
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
