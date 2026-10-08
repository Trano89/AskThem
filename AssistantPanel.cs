using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using AskThem.Controls;
using AskThem.Models;
using AskThem.Services;

namespace AskThem
{
    /// <summary>
    /// Ce que l'assistant a récolté. La fenêtre complète l'applique ensuite : le pipeline
    /// n'existe qu'à un seul endroit, l'assistant ne fait que remplir.
    /// </summary>
    public class DemandeEnCours
    {
        public RequestType Type = RequestType.Offre;
        public Supplier Destinataire;
        public List<PartLine> Lignes = new List<PartLine>();
        public string ReferenceCommande = "";
        public DateTime? Delai;
        public string CheminPo = "";
        public string Commentaire = "";
        public bool Export3D = true;
        public bool Export2D = true;
        public bool ControleFabrication;
        public bool DemanderLivraison = true;
        public bool Generer;
    }

    /// <summary>
    /// Conduit l'utilisateur d'un bout à l'autre d'une demande, un pas après l'autre.
    ///
    /// La fenêtre complète reste accessible et fait exactement le même travail : celle-ci
    /// ne décide de rien, elle guide. Tout ce qu'elle recueille est repassé à la fenêtre
    /// principale, qui reste seule à porter le traitement.
    ///
    /// Chaque écran suit la même ossature : où l'on en est, la question posée, la réponse,
    /// et en bas les deux seuls gestes utiles — revenir, ou continuer. Les messages
    /// s'affichent au pied de l'écran plutôt que dans une fenêtre à fermer.
    /// </summary>
    public class AssistantPanel : Panel
    {
        private static readonly string[] NomsEtapes =
            { "Type de demande", "Destinataire", "Articles", "Détails", "Vérification" };

        private static int NbEtapes { get { return NomsEtapes.Length; } }

        /// <summary>Largeur de lecture : au-delà, les champs s'étirent sans rien gagner.</summary>
        private const int LargeurMax = 1040;

        /// <summary>Largeur des cartes de l'étape 1.</summary>
        private const int LargeurCartes = 720;

        private readonly AppConfig _config;
        private readonly List<Supplier> _fournisseurs;
        private readonly Func<Dictionary<string, InventoryService.Entry>> _inventaire;
        private readonly Func<Dictionary<string, string>> _pdm;

        private int _etape;

        /// <summary>Vrai dès qu'un type a été choisi : l'étape 1 peut alors se passer d'un clic.</summary>
        private bool _typeChoisi;

        /// <summary>Type pour lequel la case de contrôle a été réglée.</summary>
        private RequestType? _typeControle;
        private readonly DemandeEnCours _demande = new DemandeEnCours();
        private readonly BindingList<PartLine> _lignes = new BindingList<PartLine>();

        // Ossature
        private TableLayoutPanel cadre;
        private Jalons jalons;
        private Label lblProgression;
        private Label lblTitre;
        private Label lblSousTitre;
        private BandeauInfo bandeau;
        private Panel corps;
        private LinkLabel lnkSecondaire;
        private EventHandler _actionSecondaire;
        private Label lblMessage;
        private Button btnPrecedent;
        private Button btnSuivant;

        // Étape 1
        private FlowLayoutPanel cartes;

        // Étape 2
        private TextBox txtFiltre;
        private ListBox lstFournisseurs;
        private Label lblLien;

        // Étape 3
        private DataGridView grille;
        private Label lblCompteArticles;

        // Étape 4
        private TextBox txtReference;
        private CheckBox chkDelai;
        private DateTimePicker dtpDelai;
        private TextBox txtPo;
        private LinkLabel lnkRetirerPo;
        private TextBox txtCommentaire;
        private CheckBox chk3D;
        private CheckBox chk2D;
        private CheckBox chkControle;
        private CheckBox chkLivraison;

        // Bande d'état, pendant une génération
        private TableLayoutPanel panneauEtat;
        private ProgressBar progression;
        private Label lblEtat;
        private Button btnAnnuler;

        /// <summary>Ce que l'utilisateur a construit.</summary>
        public DemandeEnCours Demande { get { return _demande; } }

        /// <summary>Appelé quand l'utilisateur veut lancer la demande qu'il vient de composer.</summary>
        public event EventHandler Generer;

        /// <summary>Appelé quand la liste des fournisseurs a été modifiée depuis ce panneau.</summary>
        public event EventHandler FournisseursChanges;

        /// <summary>Appelé quand l'utilisateur veut vérifier sans envoyer.</summary>
        public event EventHandler Verifier;

        /// <summary>Appelé quand l'utilisateur veut arrêter le traitement en cours.</summary>
        public event EventHandler Annuler;

        /// <summary>
        /// Appelé quand l'utilisateur passe à la demande suivante, une fois la précédente
        /// préparée : la fenêtre principale vide alors ses propres champs.
        /// </summary>
        public event EventHandler NouvelleDemande;

        /// <summary>
        /// Les deux sources d'articles sont demandées au moment de s'en servir, et non
        /// retenues à la construction : au lancement, l'inventaire se charge encore en
        /// arrière-plan et le coffre n'est pas indexé. Les figer ici donnerait une
        /// recherche vide.
        /// </summary>
        public AssistantPanel(AppConfig config, List<Supplier> fournisseurs,
                             Func<Dictionary<string, InventoryService.Entry>> inventaire,
                             Func<Dictionary<string, string>> pdm)
        {
            _config = config != null ? config : new AppConfig();
            _fournisseurs = fournisseurs != null ? fournisseurs : new List<Supplier>();
            _inventaire = inventaire;
            _pdm = pdm;

            // Les réglages retenus d'une session à l'autre valent aussi pour le mode guidé :
            // partir de valeurs écrites en dur annulait, dès le premier passage, le choix
            // enregistré par l'utilisateur dans la vue complète.
            _demande.Export3D = _config.Export3D;
            _demande.Export2D = _config.Export2D;
            _demande.DemanderLivraison = _config.DemanderLivraison;

            Font = AppFont.Get();
            Dock = DockStyle.Fill;
            BackColor = Theme.Fond;

            Construire();
        }

        private bool _etapeConstruite;

        /// <summary>
        /// La première étape se construit une fois la fenêtre mise à l'échelle : construite
        /// avant, ses marges, déjà adaptées à l'écran, l'auraient été une seconde fois.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!_etapeConstruite) AllerA(_etape);
        }

        // ==================================================================
        // Ce que la fenêtre principale pilote
        // ==================================================================

        /// <summary>Montre où en est le traitement, et permet de l'arrêter.</summary>
        public void Occupe(bool occupe)
        {
            panneauEtat.Visible = occupe;
            btnAnnuler.Enabled = occupe;
            btnPrecedent.Enabled = !occupe;
            btnSuivant.Enabled = !occupe && SuivantPossible();
            lnkSecondaire.Enabled = !occupe;
            corps.Enabled = !occupe;
            if (occupe)
            {
                bandeau.Masquer();
                Effacer();
            }
            else
            {
                progression.Value = 0;
            }
        }

        /// <summary>Avancement, repris de la fenêtre qui traite.</summary>
        public void Avancement(int valeur, int maximum, string texte)
        {
            if (maximum > 0) progression.Maximum = maximum;
            progression.Value = Math.Max(0, Math.Min(valeur, progression.Maximum));
            lblEtat.Text = texte;
        }

        /// <summary>
        /// Le résultat d'une vérification ou d'une génération, en tête d'écran. Après une
        /// génération réussie, il propose de passer à la demande suivante.
        /// </summary>
        public void AfficherBilan(Bilan b)
        {
            if (b == null) { bandeau.Masquer(); return; }
            bandeau.Afficher(b);
            // Une fois la demande préparée, l'action principale devient la suivante :
            // « Générer » restait offert, et un second clic préparait le même email deux fois.
            _prete = b.Genere;
            if (_prete)
            {
                btnSuivant.Text = "Nouvelle demande";
                btnSuivant.Enabled = true;
            }
        }

        /// <summary>Vrai quand la demande affichée vient d'être préparée.</summary>
        private bool _prete;

        /// <summary>
        /// Reprend une demande venue d'ailleurs, pour que la bascule entre les deux vues ne
        /// perde rien dans un sens comme dans l'autre.
        /// </summary>
        public void Charger(DemandeEnCours d)
        {
            if (d == null) return;

            _demande.Type = d.Type;
            _demande.Destinataire = d.Destinataire;
            _demande.ReferenceCommande = d.ReferenceCommande;
            _demande.Delai = d.Delai;
            _demande.CheminPo = d.CheminPo;
            _demande.Commentaire = d.Commentaire;
            _demande.Export3D = d.Export3D;
            _demande.Export2D = d.Export2D;
            _demande.ControleFabrication = d.ControleFabrication;
            _demande.DemanderLivraison = d.DemanderLivraison;

            _demande.Lignes = new List<PartLine>(d.Lignes);
            _lignes.Clear();
            foreach (PartLine l in d.Lignes) _lignes.Add(l);
            if (_lignes.Count == 0) _lignes.Add(new PartLine());

            // Une demande déjà entamée dans la vue complète a un type : l'étape 1 n'a
            // plus à l'exiger d'un clic.
            if (d.Destinataire != null || d.Lignes.Count > 0) _typeChoisi = true;

            AllerA(_etape);
        }

        /// <summary>
        /// Recommence une demande vierge. Le type et les cases restent : on enchaîne
        /// souvent des demandes de même nature.
        /// </summary>
        public void Reinitialiser()
        {
            _demande.Destinataire = null;
            _demande.Lignes.Clear();
            _demande.ReferenceCommande = "";
            _demande.Delai = null;
            _demande.CheminPo = "";
            _demande.Commentaire = "";
            _demande.Generer = false;
            _lignes.Clear();
            _lignes.Add(new PartLine());
            bandeau.Masquer();
            AllerA(0);
        }

        private void RepartirAZero()
        {
            Reinitialiser();
            if (NouvelleDemande != null) NouvelleDemande(this, EventArgs.Empty);
        }

        /// <summary>Reprend ce qui est affiché, pour que la vue complète le retrouve.</summary>
        public void Synchroniser()
        {
            // L'article en cours de saisie doit entrer dans la ligne avant d'être recueilli.
            try { if (grille != null && !grille.IsDisposed) grille.EndEdit(); }
            catch (Exception) { }
            Recolter();
        }

        // ==================================================================
        // Ossature
        // ==================================================================

        /// <summary>
        /// Tout se range dans une grille dont chaque rangée prend la hauteur de son contenu :
        /// des hauteurs fixées en pixels rognaient titres et boutons dès que l'écran était
        /// mis à l'échelle.
        /// </summary>
        private void Construire()
        {
            jalons = new Jalons(NbEtapes);
            jalons.Dock = DockStyle.Top;
            jalons.Margin = new Padding(0, 0, 0, 10);

            lblProgression = Ui.Legende("");
            lblTitre = Ui.Titre("");
            lblTitre.Margin = new Padding(0, 2, 0, 2);
            lblSousTitre = Ui.Legende("");

            TableLayoutPanel entete = Ui.Pile();
            entete.Dock = DockStyle.Fill;
            Ui.Ajouter(entete, jalons);
            Ui.Ajouter(entete, lblProgression);
            Ui.Ajouter(entete, lblTitre);
            Ui.Ajouter(entete, lblSousTitre);

            bandeau = new BandeauInfo();
            bandeau.Dock = DockStyle.Fill;
            bandeau.Margin = new Padding(0, 8, 0, 0);

            corps = new Panel();
            corps.Dock = DockStyle.Fill;
            corps.Margin = new Padding(0, 14, 0, 8);
            corps.AutoScroll = true;
            corps.Resize += new EventHandler(Corps_Resize);

            // Une génération dure des minutes : sans cette bande, le mode guidé n'en montrait
            // rien et n'offrait aucun moyen d'arrêter.
            progression = new ProgressBar();
            progression.Dock = DockStyle.Fill;
            progression.Height = 14;
            progression.Margin = new Padding(0, 0, 0, 4);
            lblEtat = Ui.Legende("");
            btnAnnuler = Ui.Secondaire("Annuler");
            btnAnnuler.Enabled = false;
            btnAnnuler.Click += new EventHandler(Annuler_Click);

            panneauEtat = new TableLayoutPanel();
            panneauEtat.Dock = DockStyle.Fill;
            panneauEtat.AutoSize = true;
            panneauEtat.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panneauEtat.ColumnCount = 2;
            panneauEtat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panneauEtat.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panneauEtat.RowCount = 2;
            panneauEtat.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panneauEtat.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panneauEtat.Margin = new Padding(0, 0, 0, 8);
            panneauEtat.Controls.Add(progression, 0, 0);
            panneauEtat.Controls.Add(lblEtat, 0, 1);
            panneauEtat.Controls.Add(btnAnnuler, 1, 0);
            panneauEtat.SetRowSpan(btnAnnuler, 2);
            panneauEtat.Visible = false;

            // Le pied : une action secondaire à gauche, le message du moment au milieu, et
            // les deux gestes de navigation à droite — l'action principale tout au bout.
            lnkSecondaire = Ui.Lien("", new EventHandler(Secondaire_Click));
            lnkSecondaire.Visible = false;

            lblMessage = Ui.Libelle("", AppFont.Get(), Theme.Erreur);
            lblMessage.Margin = new Padding(0, 6, 12, 0);

            btnPrecedent = Ui.Secondaire("Précédent");
            btnPrecedent.Click += new EventHandler(Precedent_Click);
            btnSuivant = Ui.Primaire("Suivant");
            btnSuivant.MinimumSize = new Size(120, 0);
            btnSuivant.Click += new EventHandler(Suivant_Click);

            TableLayoutPanel bas = new TableLayoutPanel();
            bas.Dock = DockStyle.Fill;
            bas.AutoSize = true;
            bas.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bas.ColumnCount = 4;
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bas.RowCount = 1;
            bas.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bas.Margin = new Padding(0, 12, 0, 0);
            bas.Controls.Add(lnkSecondaire, 0, 0);
            bas.Controls.Add(lblMessage, 1, 0);
            bas.Controls.Add(btnPrecedent, 2, 0);
            bas.Controls.Add(btnSuivant, 3, 0);

            Panel trait = Ui.Separateur();
            trait.Dock = DockStyle.Fill;

            cadre = new TableLayoutPanel();
            cadre.ColumnCount = 1;
            cadre.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            cadre.Padding = new Padding(28, 20, 28, 16);
            cadre.RowCount = 6;
            cadre.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // en-tête
            cadre.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // bilan
            cadre.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // l'étape
            cadre.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // traitement en cours
            cadre.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));   // trait
            cadre.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // pied
            cadre.Controls.Add(entete, 0, 0);
            cadre.Controls.Add(bandeau, 0, 1);
            cadre.Controls.Add(corps, 0, 2);
            cadre.Controls.Add(panneauEtat, 0, 3);
            cadre.Controls.Add(trait, 0, 4);
            cadre.Controls.Add(bas, 0, 5);
            Controls.Add(cadre);
            Centrer();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Centrer();
        }

        /// <summary>Le contenu reste à largeur de lecture, centré, sur un grand écran.</summary>
        private void Centrer()
        {
            if (cadre == null) return;
            int w = Math.Min(ClientSize.Width, LogicalToDeviceUnits(LargeurMax));
            cadre.SetBounds(Math.Max(0, (ClientSize.Width - w) / 2), 0, Math.Max(0, w), ClientSize.Height);
        }

        private void Corps_Resize(object sender, EventArgs e)
        {
            AjusterCartes();
        }

        private int D(int valeur)
        {
            return LogicalToDeviceUnits(valeur);
        }

        /// <summary>Une colonne qui occupe l'étape ; ses rangées se règlent une à une.</summary>
        private static TableLayoutPanel Colonne()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.Margin = Padding.Empty;
            return t;
        }

        /// <summary>Ajoute une rangée : à la hauteur du contenu, ou qui prend la place restante.</summary>
        private static void Rang(TableLayoutPanel t, Control c, bool remplit)
        {
            t.RowCount = t.RowCount + 1;
            t.RowStyles.Add(remplit ? new RowStyle(SizeType.Percent, 100F) : new RowStyle(SizeType.AutoSize));
            if (remplit) c.Dock = DockStyle.Fill;
            t.Controls.Add(c, 0, t.RowCount - 1);
        }

        // ==================================================================
        // Messages
        // ==================================================================

        /// <summary>
        /// Ce qui bloque, dit au pied de l'écran, près du bouton qu'on vient de presser :
        /// une fenêtre à fermer coupait le geste pour une phrase.
        /// </summary>
        private void Prevenir(string message)
        {
            lblMessage.ForeColor = Theme.Erreur;
            lblMessage.Text = message == null ? "" : message.Replace(Environment.NewLine, " ");
        }

        /// <summary>Une information, sans alarme.</summary>
        private void Informer(string message)
        {
            lblMessage.ForeColor = Theme.Texte2;
            lblMessage.Text = message == null ? "" : message.Replace(Environment.NewLine, " ");
        }

        private void Effacer()
        {
            lblMessage.Text = "";
        }

        // ==================================================================
        // Navigation
        // ==================================================================

        private void AllerA(int etape)
        {
            if (etape < 0) etape = 0;
            if (etape > NbEtapes - 1) etape = NbEtapes - 1;
            _etape = etape;
            _etapeConstruite = true;

            // Les contrôles de l'étape précédente sont détruits, pas seulement retirés :
            // une grille ou une liste oubliée garde ses abonnements et ses ressources.
            List<Control> anciens = new List<Control>();
            foreach (Control c in corps.Controls) anciens.Add(c);
            corps.Controls.Clear();
            foreach (Control c in anciens) c.Dispose();
            cartes = null;
            grille = null;
            lstFournisseurs = null;

            Effacer();
            bandeau.Masquer();
            _prete = false;
            jalons.Etape = _etape;
            lblProgression.Text = "Étape " + (_etape + 1) + " sur " + NbEtapes + "  ·  " + NomsEtapes[_etape];
            btnPrecedent.Visible = _etape > 0;
            btnSuivant.Visible = true;
            btnSuivant.Text = _etape == NbEtapes - 1 ? "Générer la demande" : "Suivant";
            DefinirSecondaire(null, null);

            corps.SuspendLayout();
            switch (_etape)
            {
                case 0: EtapeType(); break;
                case 1: EtapeDestinataire(); break;
                case 2: EtapeArticles(); break;
                case 3: EtapeDetails(); break;
                default: EtapeRecapitulatif(); break;
            }
            corps.ResumeLayout(true);
            MajSuivant();
        }

        /// <summary>L'action secondaire de l'étape, en lien au pied de l'écran.</summary>
        private void DefinirSecondaire(string texte, EventHandler action)
        {
            _actionSecondaire = action;
            lnkSecondaire.Text = texte == null ? "" : texte;
            lnkSecondaire.Visible = action != null;
        }

        private void Secondaire_Click(object sender, EventArgs e)
        {
            if (_actionSecondaire != null) _actionSecondaire(sender, e);
        }

        /// <summary>« Suivant » n'est offert que lorsqu'il mène quelque part.</summary>
        private bool SuivantPossible()
        {
            if (_etape == 0) return _typeChoisi;
            if (_etape == 1) return lstFournisseurs != null && lstFournisseurs.SelectedItem is Supplier;
            return true;
        }

        private void MajSuivant()
        {
            btnSuivant.Visible = _etape != 0 || _typeChoisi;
            btnSuivant.Enabled = !panneauEtat.Visible && SuivantPossible();
        }

        private void Precedent_Click(object sender, EventArgs e)
        {
            Recolter();
            AllerA(_etape - 1);
        }

        private void Suivant_Click(object sender, EventArgs e)
        {
            if (_prete)
            {
                RepartirAZero();
                return;
            }
            Recolter();
            if (!EtapeValide()) return;

            if (_etape == NbEtapes - 1)
            {
                _demande.Generer = true;
                if (Generer != null) Generer(this, EventArgs.Empty);
                return;
            }
            AllerA(_etape + 1);
        }

        private void Verifier_Click(object sender, EventArgs e)
        {
            Recolter();
            if (!EtapeValide()) return;
            if (Verifier != null) Verifier(this, EventArgs.Empty);
        }

        private void Annuler_Click(object sender, EventArgs e)
        {
            if (Annuler != null) Annuler(this, EventArgs.Empty);
        }

        /// <summary>Reprend dans la demande ce que l'étape affichée contient.</summary>
        private void Recolter()
        {
            switch (_etape)
            {
                case 1:
                    if (lstFournisseurs != null)
                    {
                        Supplier choisi = lstFournisseurs.SelectedItem as Supplier;
                        // Une liste filtrée sans sélection ne fait pas oublier le choix déjà fait.
                        if (choisi != null) _demande.Destinataire = choisi;
                    }
                    break;
                case 2:
                    try { if (grille != null) grille.EndEdit(); }
                    catch (Exception) { }
                    _demande.Lignes = new List<PartLine>();
                    foreach (PartLine l in _lignes)
                        if (!string.IsNullOrWhiteSpace(l.PartNumber)) _demande.Lignes.Add(l);
                    break;
                case 3:
                    _demande.ReferenceCommande = txtReference.Text.Trim();
                    _demande.Delai = chkDelai.Checked ? (DateTime?)dtpDelai.Value.Date : null;
                    _demande.CheminPo = txtPo.Text.Trim();
                    _demande.Commentaire = txtCommentaire.Text;
                    _demande.Export3D = chk3D.Checked;
                    _demande.Export2D = chk2D.Checked;
                    _demande.ControleFabrication = chkControle.Checked;
                    _demande.DemanderLivraison = chkLivraison.Checked;
                    break;
            }
        }

        /// <summary>Ce qui manque pour passer à l'étape suivante, dit sur-le-champ.</summary>
        private bool EtapeValide()
        {
            if (_etape == 1 && _demande.Destinataire == null)
            {
                Prevenir("Choisissez un destinataire dans la liste.");
                return false;
            }
            if (_etape == 2)
            {
                if (_demande.Lignes.Count == 0)
                {
                    Prevenir("Ajoutez au moins un article.");
                    return false;
                }
                string faute = ArticleDeMauvaisType();
                if (faute != null)
                {
                    Prevenir(faute);
                    return false;
                }
            }
            if (_etape == 3 && _demande.Type == RequestType.Fabrication
                && string.IsNullOrWhiteSpace(_demande.CheminPo))
            {
                Prevenir("Une demande de fabrication exige le bon de commande, au format PDF.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Ce qui empêche cette liste d'articles, s'il y a quelque chose.
        ///
        /// Une offre porte aussi bien du catalogue que du sur mesure ; c'est le mélange des
        /// deux qui est refusé. Une fabrication ne porte que du sur mesure, une commande
        /// catalogue que du catalogue.
        /// </summary>
        private string ArticleDeMauvaisType()
        {
            List<string> catalogue = new List<string>();
            List<string> surMesure = new List<string>();
            foreach (PartLine l in _demande.Lignes)
            {
                if (EstCatalogue(l.PartNumber)) catalogue.Add(l.PartNumber);
                else surMesure.Add(l.PartNumber);
            }

            if (catalogue.Count > 0 && surMesure.Count > 0)
                return "Cette demande mélange " + catalogue.Count + " article(s) de catalogue et "
                     + surMesure.Count + " pièce(s) sur mesure : faites-en deux demandes séparées.";

            if (_demande.Type == RequestType.CommandeCatalogue && surMesure.Count > 0)
                return Quelques(surMesure) + " ne sont pas des articles de catalogue.";

            // Même critère que la vue complète au lancement : la règle du type. Le mode guidé
            // en jugeait sur « catalogue ou non », et acceptait des articles que la génération
            // refusait ensuite, une fois toutes les étapes remplies.
            if (_demande.Type == RequestType.Fabrication)
            {
                List<string> nonFabricables = new List<string>();
                foreach (PartLine l in _demande.Lignes)
                    if (!ValidationArticle.RegleDe(_config, l.PartNumber).AllowFabrication)
                        nonFabricables.Add(l.PartNumber);
                if (nonFabricables.Count > 0)
                    return Quelques(nonFabricables) + " ne se fabriquent pas : retirez-les, "
                         + "ou faites-en une demande d'offre.";
            }

            return null;
        }

        private static string Quelques(List<string> articles)
        {
            return string.Join(", ", articles.GetRange(0, Math.Min(4, articles.Count)))
                 + (articles.Count > 4 ? " et " + (articles.Count - 4) + " autre(s)" : "");
        }

        /// <summary>Vrai si tous les articles saisis sont des achats catalogue.</summary>
        private bool ToutEnCatalogue()
        {
            bool auMoinsUn = false;
            foreach (PartLine l in _lignes)
            {
                if (string.IsNullOrWhiteSpace(l.PartNumber)) continue;
                auMoinsUn = true;
                if (!EstCatalogue(l.PartNumber)) return false;
            }
            return auMoinsUn;
        }

        private bool EstCatalogue(string numero)
        {
            return ValidationArticle.EstCatalogue(_config, numero);
        }

        // ==================================================================
        // Étape 1 — la nature de la demande
        // ==================================================================

        private void EtapeType()
        {
            lblTitre.Text = "Que voulez-vous demander ?";
            lblSousTitre.Text = "Choisissez le type de demande : la suite de l'assistant s'y adapte.";

            cartes = new FlowLayoutPanel();
            cartes.Dock = DockStyle.Top;
            cartes.AutoSize = true;
            cartes.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            cartes.FlowDirection = FlowDirection.TopDown;
            cartes.WrapContents = false;
            cartes.Margin = Padding.Empty;

            foreach (RequestType t in new RequestType[] {
                         RequestType.Offre, RequestType.Fabrication, RequestType.CommandeCatalogue })
            {
                cartes.Controls.Add(Carte(t));
            }
            corps.Controls.Add(cartes);
            AjusterCartes();
        }

        /// <summary>Un grand bouton par nature de demande, avec ce qu'elle implique.</summary>
        private Control Carte(RequestType type)
        {
            CarteBouton b = new CarteBouton();
            b.Tag = type;
            b.Titre = RequestTypes.Libelle(type);
            b.Explication = RequestTypes.Description(type);
            b.Selectionne = _typeChoisi && _demande.Type == type;
            b.Margin = new Padding(0, 0, 0, D(10));
            b.Click += new EventHandler(Carte_Click);
            return b;
        }

        /// <summary>Les cartes suivent la largeur disponible, sans dépasser une largeur de lecture.</summary>
        private void AjusterCartes()
        {
            if (cartes == null || cartes.IsDisposed) return;
            int dispo = corps.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - D(4);
            int largeur = Math.Max(D(280), Math.Min(D(LargeurCartes), dispo));
            foreach (Control c in cartes.Controls) c.Width = largeur;
        }

        private void Carte_Click(object sender, EventArgs e)
        {
            Control b = sender as Control;
            if (b == null || !(b.Tag is RequestType)) return;
            _demande.Type = (RequestType)b.Tag;
            _typeChoisi = true;
            AllerA(1);
        }

        // ==================================================================
        // Étape 2 — le destinataire
        // ==================================================================

        private void EtapeDestinataire()
        {
            lblTitre.Text = "À qui l'envoyez-vous ?";
            lblSousTitre.Text = RequestTypes.EstCatalogue(_demande.Type)
                ? "Seuls les articles vendus par ce fournisseur pourront être commandés."
                : "Le fournisseur qui recevra la demande.";
            DefinirSecondaire("Gérer les fournisseurs…", new EventHandler(Gerer_Click));

            TableLayoutPanel t = Colonne();

            txtFiltre = new TextBox();
            txtFiltre.PlaceholderText = "Rechercher un fournisseur";
            txtFiltre.Margin = new Padding(0, 0, 0, D(8));
            txtFiltre.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtFiltre.TextChanged += new EventHandler(Filtre_Change);
            Rang(t, txtFiltre, false);

            // Le nom et les adresses sur deux lignes : on reconnaît un fournisseur à l'un
            // comme à l'autre. Aucun n'est présélectionné : un envoi au premier de la liste,
            // faute d'avoir regardé, partait chez le mauvais destinataire.
            lstFournisseurs = new ListBox();
            lstFournisseurs.DrawMode = DrawMode.OwnerDrawFixed;
            lstFournisseurs.ItemHeight = Math.Min(255, AppFont.Get().Height * 2 + D(14));
            lstFournisseurs.IntegralHeight = false;
            lstFournisseurs.BorderStyle = BorderStyle.FixedSingle;
            lstFournisseurs.Margin = Padding.Empty;
            lstFournisseurs.DrawItem += new DrawItemEventHandler(Fournisseur_Dessin);
            lstFournisseurs.SelectedIndexChanged += new EventHandler(Fournisseur_Change);
            lstFournisseurs.DoubleClick += new EventHandler(Fournisseur_DoubleClic);
            Rang(t, lstFournisseurs, true);

            lblLien = Ui.Legende("");
            lblLien.Margin = new Padding(0, D(6), 0, 0);
            Rang(t, lblLien, false);

            if (_fournisseurs.Count == 0)
            {
                Label vide = Ui.Legende("Aucun fournisseur enregistré : « Gérer les fournisseurs… », en bas, permet d'en créer un.");
                vide.Margin = new Padding(0, 0, 0, D(8));
                Rang(t, vide, false);
            }

            corps.Controls.Add(t);
            Remplir("");
            Fournisseur_Change(null, null);
            // Pas de focus d'office dans le filtre : son invite « Rechercher… » s'effacerait.
            lstFournisseurs.Select();
        }

        /// <summary>La liste, réduite à ce qui correspond au filtre ; le choix fait reste choisi.</summary>
        private void Remplir(string filtre)
        {
            if (lstFournisseurs == null) return;
            Supplier choisi = lstFournisseurs.SelectedItem as Supplier;
            if (choisi == null) choisi = _demande.Destinataire;

            lstFournisseurs.BeginUpdate();
            lstFournisseurs.Items.Clear();
            string f = (filtre ?? "").Trim();
            foreach (Supplier s in _fournisseurs)
            {
                if (f != "" && (s.Name ?? "").IndexOf(f, StringComparison.CurrentCultureIgnoreCase) < 0
                            && (s.ToLine ?? "").IndexOf(f, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                lstFournisseurs.Items.Add(s);
            }
            lstFournisseurs.EndUpdate();

            if (choisi != null && lstFournisseurs.Items.Contains(choisi))
                lstFournisseurs.SelectedItem = choisi;
            else if (f != "" && lstFournisseurs.Items.Count == 1)
                lstFournisseurs.SelectedIndex = 0;     // un seul résultat : c'est lui qu'on cherchait
        }

        private void Filtre_Change(object sender, EventArgs e)
        {
            Remplir(txtFiltre.Text);
            Fournisseur_Change(null, null);
        }

        private void Fournisseur_Dessin(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Supplier f = lstFournisseurs.Items[e.Index] as Supplier;
            bool choisi = (e.State & DrawItemState.Selected) != 0;

            using (SolidBrush fond = new SolidBrush(choisi ? Theme.AccentPale : Theme.Surface))
                e.Graphics.FillRectangle(fond, e.Bounds);
            if (choisi)
            {
                using (SolidBrush barre = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(barre, e.Bounds.X, e.Bounds.Y, D(3), e.Bounds.Height);
            }
            using (Pen trait = new Pen(Theme.Separateur))
                e.Graphics.DrawLine(trait, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            if (f == null) return;
            int x = e.Bounds.X + D(12);
            int w = e.Bounds.Width - D(24);
            int h = e.Bounds.Height / 2;
            string nom = string.IsNullOrWhiteSpace(f.Name) ? f.ToLine : f.Name;
            string adresses = f.ToLine == "" ? "aucune adresse" : f.ToLine;
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(e.Graphics, nom, AppFont.Section(),
                new Rectangle(x, e.Bounds.Y + D(4), w, h), Theme.Texte, flags | TextFormatFlags.Bottom);
            TextRenderer.DrawText(e.Graphics, adresses, AppFont.Get(),
                new Rectangle(x, e.Bounds.Y + h, w, h - D(4)), Theme.Texte2, flags | TextFormatFlags.Top);
        }

        private void Fournisseur_Change(object sender, EventArgs e)
        {
            if (lblLien == null || lstFournisseurs == null) return;
            Supplier f = lstFournisseurs.SelectedItem as Supplier;
            if (f == null)
            {
                lblLien.Text = lstFournisseurs.Items.Count == 0 && _fournisseurs.Count > 0
                    ? "Aucun fournisseur ne correspond à cette recherche."
                    : "";
            }
            else
            {
                lblLien.Text = f.InventoryId != 0
                    ? "Lié à l'inventaire (fiche n° " + f.InventoryId + ")."
                    : "Non lié à l'inventaire : le rapprochement se fera sur le nom.";
                Effacer();
            }
            MajSuivant();
        }

        private void Fournisseur_DoubleClic(object sender, EventArgs e)
        {
            if (lstFournisseurs.SelectedItem is Supplier) Suivant_Click(sender, e);
        }

        private void Gerer_Click(object sender, EventArgs e)
        {
            Recolter();
            using (SupplierDialog dlg = new SupplierDialog(_config, _fournisseurs))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                _fournisseurs.Clear();
                _fournisseurs.AddRange(dlg.Suppliers);
            }
            // La fiche retenue a pu être remplacée par sa version modifiée : on la retrouve
            // par son nom, plutôt que de garder une fiche qui n'est plus dans la liste.
            if (_demande.Destinataire != null && !_fournisseurs.Contains(_demande.Destinataire))
            {
                Supplier meme = null;
                foreach (Supplier s in _fournisseurs)
                    if (string.Equals(s.Name, _demande.Destinataire.Name, StringComparison.OrdinalIgnoreCase)) { meme = s; break; }
                _demande.Destinataire = meme;
            }
            if (FournisseursChanges != null) FournisseursChanges(this, EventArgs.Empty);
            AllerA(1);
        }

        // ==================================================================
        // Étape 3 — les articles
        // ==================================================================

        private void EtapeArticles()
        {
            lblTitre.Text = "Quels articles ?";
            lblSousTitre.Text = RequestTypes.EstCatalogue(_demande.Type)
                ? "Articles de catalogue, achetés sur leur référence chez le fournisseur."
                : (_demande.Type == RequestType.Fabrication
                    ? "Pièces sur mesure : leurs plans et modèles seront joints."
                    : "Pièces sur mesure ou articles de catalogue, mais pas les deux à la fois.");
            DefinirSecondaire("Tout vider", new EventHandler(Vider_Click));

            if (_lignes.Count == 0)
            {
                foreach (PartLine l in _demande.Lignes) _lignes.Add(l);
                if (_lignes.Count == 0) _lignes.Add(new PartLine());
            }

            TableLayoutPanel t = Colonne();

            Button btnRecherche = Ui.Secondaire("Rechercher un article…");
            btnRecherche.Margin = new Padding(0, 0, D(8), 0);
            btnRecherche.Click += new EventHandler(Recherche_Click);
            Button btnColler = Ui.Secondaire("Coller depuis Excel");
            btnColler.Margin = new Padding(0, 0, D(8), 0);
            btnColler.Click += new EventHandler(Coller_Click);
            Button btnImporter = Ui.Secondaire("Importer une liste…");
            btnImporter.Margin = new Padding(0, 0, D(8), 0);
            btnImporter.Click += new EventHandler(Importer_Click);

            FlowLayoutPanel outils = Ui.Rangee();
            outils.Margin = new Padding(0, 0, 0, D(8));
            outils.Controls.Add(btnRecherche);
            outils.Controls.Add(btnColler);
            outils.Controls.Add(btnImporter);
            Rang(t, outils, false);

            grille = new DataGridView();
            grille.AutoGenerateColumns = false;
            grille.AllowUserToAddRows = true;
            grille.AllowUserToResizeRows = false;
            grille.RowHeadersWidth = D(28);
            grille.RowTemplate.Height = AppFont.Get().Height + D(12);
            grille.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grille.BackgroundColor = Theme.Surface;
            grille.BorderStyle = BorderStyle.FixedSingle;
            grille.GridColor = Theme.Separateur;
            grille.EditMode = DataGridViewEditMode.EditOnEnter;
            grille.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grille.Margin = Padding.Empty;

            Colonne("N° article", "PartNumber", 34);
            Colonne("Qté 1", "Qty1", 12);
            if (RequestTypes.PlusieursQuantites(_demande.Type))
            {
                Colonne("Qté 2", "Qty2", 12);
                Colonne("Qté 3", "Qty3", 12);
            }
            Colonne("Remarque", "Remark", 34);

            grille.DataSource = _lignes;
            grille.DataError += new DataGridViewDataErrorEventHandler(Grille_Erreur);
            grille.CellValidating += new DataGridViewCellValidatingEventHandler(Grille_Validation);
            grille.CellEndEdit += new DataGridViewCellEventHandler(Grille_FinSaisie);
            // L'étape est reconstruite à chaque passage : sans le retrait préalable, chaque
            // visite ajoutait un abonnement de plus, et chaque modification de la liste
            // déclenchait autant de mises à jour.
            _lignes.ListChanged -= new ListChangedEventHandler(Lignes_Change);
            _lignes.ListChanged += new ListChangedEventHandler(Lignes_Change);
            Rang(t, grille, true);

            lblCompteArticles = Ui.Legende("");
            lblCompteArticles.Margin = new Padding(0, D(6), 0, 0);
            Rang(t, lblCompteArticles, false);

            corps.Controls.Add(t);
            Lignes_Change(null, null);
        }

        private void Colonne(string entete, string propriete, int poids)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = entete;
            c.DataPropertyName = propriete;
            c.FillWeight = poids;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grille.Columns.Add(c);
        }

        private void Grille_Erreur(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            Prevenir("Valeur refusée : une quantité est un nombre entier.");
        }

        /// <summary>
        /// Le même contrôle que dans la vue complète : format, type d'article autorisé, et
        /// fournisseur qui vend bien l'article. Les deux vues appellent le même service.
        /// </summary>
        private void Grille_Validation(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex != 0) return;
            string brut = e.FormattedValue == null ? "" : e.FormattedValue.ToString();
            if (string.IsNullOrWhiteSpace(brut)) return;

            string normalise = PartNumberFormat.Normalize(brut, _config.PartNumberPatterns);
            if (e.RowIndex >= 0 && e.RowIndex < _lignes.Count
                && string.Equals(_lignes[e.RowIndex].PartNumber, normalise, StringComparison.OrdinalIgnoreCase))
                return;

            Dictionary<string, InventoryService.Entry> inventaire =
                _inventaire == null ? null : _inventaire();

            string refus = ValidationArticle.Verifier(_config, normalise, _demande.Destinataire, inventaire);
            if (refus == null) return;

            Prevenir(refus);
            e.Cancel = true;
        }

        /// <summary>Insère les tirets après la saisie, comme dans la vue complète.</summary>
        private void Grille_FinSaisie(object sender, DataGridViewCellEventArgs e)
        {
            Effacer();
            if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= _lignes.Count) return;
            PartLine ligne = _lignes[e.RowIndex];
            string normalise = PartNumberFormat.Normalize(ligne.PartNumber, _config.PartNumberPatterns);
            if (normalise == ligne.PartNumber) return;
            ligne.PartNumber = normalise;
            grille.Refresh();
        }

        private void Lignes_Change(object sender, ListChangedEventArgs e)
        {
            if (lblCompteArticles == null || lblCompteArticles.IsDisposed) return;
            int n = 0;
            foreach (PartLine l in _lignes) if (!string.IsNullOrWhiteSpace(l.PartNumber)) n++;
            lblCompteArticles.Text = n == 0 ? "Aucun article pour l'instant : saisissez un numéro, ou utilisez les boutons ci-dessus."
                                   : n == 1 ? "1 article dans la demande."
                                   : n + " articles dans la demande.";
        }

        private void Recherche_Click(object sender, EventArgs e)
        {
            Dictionary<string, InventoryService.Entry> inventaire = null;
            Dictionary<string, string> pdm = null;

            Cursor = Cursors.WaitCursor;
            try
            {
                if (_inventaire != null) inventaire = _inventaire();
                if (_pdm != null) pdm = _pdm();
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            bool riens = (inventaire == null || inventaire.Count == 0)
                      && (pdm == null || pdm.Count == 0);
            if (riens)
            {
                Prevenir("Ni le coffre ni l'inventaire ne répondent : il n'y a rien à chercher. "
                    + "Vérifiez le chemin du coffre et la connexion à l'inventaire (menu Outils).");
                return;
            }

            using (RechercheArticleDialog dlg = new RechercheArticleDialog(
                       _config, _demande.Destinataire, inventaire, pdm))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                foreach (string numero in dlg.Retenus) AjouterLigne(numero);
            }
        }

        private void Coller_Click(object sender, EventArgs e)
        {
            RetirerLignesVides();
            int avant = _lignes.Count;
            int n;
            try
            {
                n = ClipboardImporter.ImportFromClipboard(_lignes);
            }
            catch (Exception ex)
            {
                // Excel retient parfois le presse-papiers un instant : ce n'est pas une panne.
                Prevenir("Le presse-papiers est momentanément indisponible (" + ex.Message + "). Réessayez.");
                if (_lignes.Count == 0) _lignes.Add(new PartLine());
                return;
            }

            if (n == 0 && _lignes.Count == avant)
            {
                if (_lignes.Count == 0) _lignes.Add(new PartLine());
                Prevenir("Le presse-papiers ne contient aucun numéro d'article reconnaissable.");
                return;
            }
            Normaliser(avant, (_lignes.Count - avant) + " article(s) collé(s).");
        }

        /// <summary>
        /// Une liste CSV ou Excel, comme dans la vue complète : le mode guidé n'offrait que
        /// le collage, et une nomenclature exportée obligeait à changer de vue.
        /// </summary>
        private void Importer_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Importer une liste d'articles";
                dlg.Filter = "Listes d'articles (*.csv;*.xlsx)|*.csv;*.xlsx"
                           + "|Classeurs Excel (*.xlsx)|*.xlsx"
                           + "|Fichiers CSV (*.csv)|*.csv";
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

                RetirerLignesVides();
                int avant = _lignes.Count;
                try
                {
                    int regroupees;
                    int n = string.Equals(Path.GetExtension(dlg.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)
                        ? XlsxService.Import(_lignes, dlg.FileName, out regroupees)
                        : CsvService.Import(_lignes, dlg.FileName, out regroupees);
                    string bilan = n + " article(s) importé(s) depuis " + Path.GetFileName(dlg.FileName) + ".";
                    if (regroupees > 0)
                        bilan += " " + regroupees + " ligne(s) répétée(s) regroupée(s), quantités additionnées.";
                    Normaliser(avant, bilan);
                }
                catch (Exception ex)
                {
                    if (_lignes.Count == 0) _lignes.Add(new PartLine());
                    Prevenir("Import impossible : " + ex.Message);
                }
            }
        }

        private void RetirerLignesVides()
        {
            try { if (grille != null) grille.EndEdit(); }
            catch (Exception) { }
            for (int i = _lignes.Count - 1; i >= 0; i--)
                if (string.IsNullOrWhiteSpace(_lignes[i].PartNumber)) _lignes.RemoveAt(i);
        }

        /// <summary>
        /// Met en forme les numéros ajoutés depuis l'indice donné, et écarte ceux qui ne
        /// peuvent faire l'objet d'aucune demande — comme l'import de la vue complète.
        /// </summary>
        private void Normaliser(int premier, string bilan)
        {
            int refuses = 0;
            for (int i = _lignes.Count - 1; i >= premier && i >= 0; i--)
            {
                PartLine l = _lignes[i];
                string normalise = PartNumberFormat.Normalize(l.PartNumber, _config.PartNumberPatterns);
                if (!PartNumberFormat.IsValid(normalise, _config.PartNumberPatterns)
                    || !ValidationArticle.RegleDe(_config, normalise).Allowed)
                {
                    _lignes.RemoveAt(i);
                    refuses++;
                    continue;
                }
                l.PartNumber = normalise;
                l.TypeCode = PartNumberFormat.TypeCode(normalise);
            }
            if (_lignes.Count == 0) _lignes.Add(new PartLine());
            if (grille != null) grille.Refresh();
            Lignes_Change(null, null);

            if (refuses > 0)
                Prevenir(bilan + " " + refuses + " numéro(s) écarté(s) : format non reconnu, "
                       + "ou type sans demande possible.");
            else
                Informer(bilan);
        }

        private void Vider_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(FindForm(), "Retirer tous les articles de la demande ?", "AskThem",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _lignes.Clear();
            _lignes.Add(new PartLine());
            Effacer();
        }

        private void AjouterLigne(string numero)
        {
            if (string.IsNullOrWhiteSpace(numero)) return;
            numero = PartNumberFormat.Normalize(numero, _config.PartNumberPatterns);
            foreach (PartLine l in _lignes)
                if (string.Equals(l.PartNumber, numero, StringComparison.OrdinalIgnoreCase)) return;

            foreach (PartLine l in _lignes)
            {
                if (string.IsNullOrWhiteSpace(l.PartNumber))
                {
                    l.PartNumber = numero;
                    l.Qty1 = 1;
                    l.Remark = "";
                    if (grille != null) grille.Refresh();
                    Lignes_Change(null, null);
                    return;
                }
            }
            PartLine nouvelle = new PartLine();
            nouvelle.PartNumber = numero;
            nouvelle.Qty1 = 1;
            nouvelle.Remark = "";
            _lignes.Add(nouvelle);
        }

        // ==================================================================
        // Étape 4 — les détails
        // ==================================================================

        private void EtapeDetails()
        {
            bool fabrication = _demande.Type == RequestType.Fabrication;
            lblTitre.Text = "Quelques précisions";
            lblSousTitre.Text = fabrication
                ? "Le bon de commande est obligatoire ; le reste est facultatif."
                : "Tout est facultatif.";

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Top;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.Margin = Padding.Empty;

            txtReference = new TextBox();
            txtReference.Text = _demande.ReferenceCommande;
            txtReference.PlaceholderText = "Projet, affaire ou commande";
            Champ(t, "Référence", txtReference);

            // Une case qui dit ce qu'elle fait, plutôt que la case cachée dans le sélecteur
            // de date, que personne ne remarquait.
            chkDelai = new CheckBox();
            chkDelai.Text = "Indiquer un délai souhaité";
            chkDelai.AutoSize = true;
            chkDelai.Checked = _demande.Delai.HasValue;
            chkDelai.Margin = new Padding(0, D(3), D(12), 0);
            dtpDelai = new DateTimePicker();
            dtpDelai.Format = DateTimePickerFormat.Short;
            dtpDelai.Width = D(130);
            dtpDelai.Value = _demande.Delai.HasValue ? _demande.Delai.Value : DateTime.Today.AddDays(14);
            dtpDelai.Enabled = chkDelai.Checked;
            dtpDelai.Margin = Padding.Empty;
            chkDelai.CheckedChanged += delegate { dtpDelai.Enabled = chkDelai.Checked; };
            FlowLayoutPanel delai = Ui.Rangee();
            delai.Controls.Add(chkDelai);
            delai.Controls.Add(dtpDelai);
            Champ(t, "Délai", delai);

            txtPo = new TextBox();
            txtPo.ReadOnly = true;
            txtPo.Text = _demande.CheminPo;
            txtPo.PlaceholderText = fabrication ? "à joindre : bon de commande (PDF)" : "aucun document";
            txtPo.Dock = DockStyle.Fill;
            txtPo.Margin = new Padding(0, 0, D(8), 0);
            txtPo.TextChanged += delegate { lnkRetirerPo.Visible = txtPo.Text.Trim() != ""; Effacer(); };
            Button btnParcourir = Ui.Secondaire("Choisir…");
            btnParcourir.Margin = Padding.Empty;
            btnParcourir.Click += new EventHandler(Parcourir_Click);
            // Retirer le document est un geste à part : la question Oui/Non qui le cachait
            // derrière « Parcourir… » ne se comprenait qu'en la lisant deux fois.
            lnkRetirerPo = Ui.Lien("Retirer", delegate { txtPo.Text = ""; });
            lnkRetirerPo.Margin = new Padding(D(12), D(6), 0, 0);
            lnkRetirerPo.Visible = txtPo.Text.Trim() != "";

            TableLayoutPanel po = new TableLayoutPanel();
            po.Dock = DockStyle.Fill;
            po.AutoSize = true;
            po.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            po.ColumnCount = 3;
            po.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            po.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            po.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            po.RowCount = 1;
            po.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            po.Margin = Padding.Empty;
            po.Controls.Add(txtPo, 0, 0);
            po.Controls.Add(btnParcourir, 1, 0);
            po.Controls.Add(lnkRetirerPo, 2, 0);
            string intitulePo = _demande.Type == RequestType.Offre ? "Demande de PO"
                              : (fabrication ? "Bon de commande *" : "Bon de commande");
            Champ(t, intitulePo, po);

            txtCommentaire = new TextBox();
            txtCommentaire.Multiline = true;
            txtCommentaire.AcceptsReturn = true;
            txtCommentaire.ScrollBars = ScrollBars.Vertical;
            txtCommentaire.Height = AppFont.Get().Height * 4 + D(10);
            txtCommentaire.Text = _demande.Commentaire;
            txtCommentaire.PlaceholderText = "Conditions de paiement, incoterms, exigences qualité, emballage…";
            Champ(t, "Commentaire", txtCommentaire);

            // Les options restent visibles : repliées sous « Options avancées », on ne
            // savait plus ce qui partait avec la demande.
            chk3D = Case("Le modèle 3D (STEP)", _demande.Export3D);
            chk2D = Case("Le plan (PDF et DXF)", _demande.Export2D);
            chkControle = Case("Le formulaire de contrôle de fabrication (bêta)", _demande.ControleFabrication);
            chkLivraison = Case("Demander le délai et les frais de livraison", _demande.DemanderLivraison);

            // Un achat catalogue ne livre aucun fichier : ces cases disparaissent, sans être
            // décochées — forcer l'état le reportait ensuite sur les demandes suivantes.
            bool catalogue = RequestTypes.EstCatalogue(_demande.Type) || ToutEnCatalogue();
            bool controlePossible = fabrication && !catalogue;

            // Cochée d'office quand on arrive en fabrication ; ensuite, c'est le choix de
            // l'utilisateur qui compte. Chaque retour à cette étape la recochait.
            bool typeChange = !_typeControle.HasValue || _typeControle.Value != _demande.Type;
            _typeControle = _demande.Type;
            chkControle.Checked = controlePossible && (typeChange || _demande.ControleFabrication);

            FlowLayoutPanel fichiers = new FlowLayoutPanel();
            fichiers.FlowDirection = FlowDirection.TopDown;
            fichiers.WrapContents = false;
            fichiers.AutoSize = true;
            fichiers.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            fichiers.Margin = Padding.Empty;
            if (!catalogue)
            {
                fichiers.Controls.Add(chk3D);
                fichiers.Controls.Add(chk2D);
                if (controlePossible) fichiers.Controls.Add(chkControle);
                Champ(t, "Joindre", fichiers);
            }

            FlowLayoutPanel livraison = new FlowLayoutPanel();
            livraison.AutoSize = true;
            livraison.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            livraison.Margin = Padding.Empty;
            livraison.Controls.Add(chkLivraison);
            Champ(t, "Livraison", livraison);

            corps.Controls.Add(t);
        }

        /// <summary>Une rangée du formulaire : l'intitulé à gauche, aligné sur la première ligne du champ.</summary>
        private void Champ(TableLayoutPanel t, string intitule, Control champ)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label l = Ui.Legende(intitule);
            l.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            l.Margin = new Padding(0, D(4), D(16), D(12));

            if (champ is TextBox) champ.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            champ.Margin = new Padding(0, 0, 0, D(12));

            t.Controls.Add(l, 0, r);
            t.Controls.Add(champ, 1, r);
        }

        private CheckBox Case(string texte, bool coche)
        {
            CheckBox c = new CheckBox();
            c.Text = texte;
            c.AutoSize = true;
            c.Checked = coche;
            c.Margin = new Padding(0, D(3), 0, D(3));
            return c;
        }

        private void Parcourir_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Document PDF (*.pdf)|*.pdf";
                dlg.Title = _demande.Type == RequestType.Offre
                    ? "Choisir la demande de PO" : "Choisir le bon de commande";
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK) txtPo.Text = dlg.FileName;
            }
        }

        // ==================================================================
        // Étape 5 — le récapitulatif
        // ==================================================================

        private void EtapeRecapitulatif()
        {
            lblTitre.Text = "Tout est prêt ?";
            lblSousTitre.Text = "Rien ne part sans vous : chaque email s'ouvre dans Outlook, à relire puis à envoyer.";
            DefinirSecondaire("Vérifier les articles sans envoyer", new EventHandler(Verifier_Click));

            TableLayoutPanel t = Colonne();

            TableLayoutPanel recap = new TableLayoutPanel();
            recap.Dock = DockStyle.Top;
            recap.AutoSize = true;
            recap.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            recap.ColumnCount = 3;
            recap.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            recap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            recap.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            recap.Margin = new Padding(0, 0, 0, D(12));

            Fait(recap, "Demande", RequestTypes.Libelle(_demande.Type), 0);
            Fait(recap, "Destinataire", _demande.Destinataire == null ? "aucun"
                : _demande.Destinataire.Name + (_demande.Destinataire.ToLine == "" ? "" : "  ·  " + _demande.Destinataire.ToLine), 1);
            Fait(recap, "Articles", _demande.Lignes.Count == 1 ? "1 article" : _demande.Lignes.Count + " articles", 2);
            Fait(recap, "Référence", _demande.ReferenceCommande == "" ? "—" : _demande.ReferenceCommande, 3);
            Fait(recap, "Délai", _demande.Delai.HasValue ? _demande.Delai.Value.ToString("dd.MM.yyyy") : "non précisé", -1);
            Fait(recap, _demande.Type == RequestType.Offre ? "Demande de PO" : "Bon de commande",
                _demande.CheminPo == "" ? "aucun" : Path.GetFileName(_demande.CheminPo), -1);

            bool catalogue = RequestTypes.EstCatalogue(_demande.Type) || ToutEnCatalogue();
            List<string> joints = new List<string>();
            if (!catalogue)
            {
                if (_demande.Export3D) joints.Add("modèle 3D");
                if (_demande.Export2D) joints.Add("plan");
                if (_demande.ControleFabrication && _demande.Type == RequestType.Fabrication) joints.Add("contrôle de fabrication");
            }
            Fait(recap, "Pièces jointes", catalogue ? "aucune : achat sur catalogue"
                : (joints.Count == 0 ? "aucune" : string.Join(", ", joints)), -1);
            Fait(recap, "Livraison", _demande.DemanderLivraison ? "délai et frais demandés" : "non demandée", -1);
            if (!string.IsNullOrWhiteSpace(_demande.Commentaire))
                Fait(recap, "Commentaire", _demande.Commentaire.Trim().Replace(Environment.NewLine, " "), -1);
            Rang(t, recap, false);

            ListView articles = new ListView();
            articles.View = View.Details;
            articles.FullRowSelect = true;
            articles.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            articles.Columns.Add("N° article", D(200));
            articles.Columns.Add("Qté", D(70), HorizontalAlignment.Right);
            articles.Columns.Add("Remarque", D(400));
            foreach (PartLine l in _demande.Lignes)
            {
                ListViewItem it = new ListViewItem(l.PartNumber);
                string qte = l.Qty1.ToString();
                if (RequestTypes.PlusieursQuantites(_demande.Type))
                {
                    if (l.Qty2 > 0) qte += " / " + l.Qty2;
                    if (l.Qty3 > 0) qte += " / " + l.Qty3;
                }
                it.SubItems.Add(qte);
                it.SubItems.Add(l.Remark ?? "");
                articles.Items.Add(it);
            }
            Rang(t, articles, true);

            corps.Controls.Add(t);
        }

        /// <summary>Une ligne du récapitulatif, avec un lien vers l'étape qui la règle.</summary>
        private void Fait(TableLayoutPanel t, string intitule, string valeur, int etape)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label l = Ui.Legende(intitule);
            l.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            l.Margin = new Padding(0, 0, D(16), D(6));
            Label v = Ui.Corps(valeur);
            v.Margin = new Padding(0, 0, D(12), D(6));
            t.Controls.Add(l, 0, r);
            t.Controls.Add(v, 1, r);

            if (etape >= 0)
            {
                int cible = etape;
                LinkLabel modifier = Ui.Lien("Modifier", delegate { AllerA(cible); });
                modifier.Margin = new Padding(0, 0, 0, D(6));
                t.Controls.Add(modifier, 2, r);
            }
        }

        /// <summary>
        /// Où l'on en est, d'un coup d'œil : un trait par étape, plein pour celles qui
        /// sont faites ou en cours.
        /// </summary>
        private sealed class Jalons : Control
        {
            private readonly int _nombre;
            private int _etape;

            public Jalons(int nombre)
            {
                _nombre = Math.Max(1, nombre);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Height = 4;
                TabStop = false;
            }

            public int Etape
            {
                get { return _etape; }
                set { _etape = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Parent == null ? Theme.Fond : Parent.BackColor);
                g.SmoothingMode = SmoothingMode.None;
                int ecart = LogicalToDeviceUnits(6);
                int largeur = Math.Min(LogicalToDeviceUnits(64), (Width - ecart * (_nombre - 1)) / _nombre);
                if (largeur <= 0) return;
                for (int i = 0; i < _nombre; i++)
                {
                    using (SolidBrush b = new SolidBrush(i <= _etape ? Theme.Accent : Theme.Bordure))
                        g.FillRectangle(b, i * (largeur + ecart), 0, largeur, Height);
                }
            }
        }
    }
}
