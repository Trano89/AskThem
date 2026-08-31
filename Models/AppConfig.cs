using System.Collections.Generic;

namespace AskThem.Models
{
    /// <summary>
    /// Correspondance entre une donnée métier et les noms de propriétés SolidWorks
    /// susceptibles de la porter. Le premier nom non vide trouvé est retenu.
    /// Les valeurs par défaut correspondent aux cartes de données du coffre 00_LynceeTec.
    /// </summary>
    public class PropertyNames
    {
        public List<string> Description { get; set; }
        public List<string> Revision { get; set; }
        public List<string> Date { get; set; }
        public List<string> Material { get; set; }
        public List<string> Treatment { get; set; }
        public List<string> State { get; set; }
        public List<string> Supplier { get; set; }
        public List<string> SupplierRef { get; set; }

        public PropertyNames()
        {
            Description = new List<string> { "Description", "Désignation", "DESIGNATION", "Designation" };
            Revision = new List<string> { "Revision", "Rev", "REVISION", "Indice" };
            Date = new List<string> { "ReleaseDate", "DrawnDate", "Date" };
            Material = new List<string> { "Material", "Matière", "Matiere", "MATERIAL", "Matériau" };
            Treatment = new List<string> { "Traitement", "TRAITEMENT", "Finition", "Finitions", "Surface" };
            State = new List<string> { "Etat", "État", "State", "Statut", "WorkflowState", "Workflow State" };
            Supplier = new List<string> { "Fournisseur", "Supplier", "Vendor", "Fabricant", "Manufacturer" };
            SupplierRef = new List<string> { "RefFournisseur", "Référence fournisseur", "Reference fournisseur",
                                             "SupplierRef", "Supplier Ref", "ManufacturerPartNumber", "MPN",
                                             "CodeFournisseur", "Article fournisseur" };
        }
    }

    /// <summary>Configuration de l'application, lue depuis config.json.</summary>
    public class AppConfig
    {
        /// <summary>Racine de la vue locale du coffre PDM.</summary>
        public string PdmRoot { get; set; }

        /// <summary>Dossier racine des exports. Vide = dossier Téléchargements.</summary>
        public string OutputRoot { get; set; }

        /// <summary>Volume, en Mo, au-delà duquel les archives par article sont regroupées.</summary>
        public int ZipThresholdMb { get; set; }

        /// <summary>Nombre d'archives au-delà duquel elles sont regroupées en une seule.</summary>
        public int MaxAttachments { get; set; }

        /// <summary>
        /// Niveau de compression des archives : Aucune, Rapide, Optimal ou Maximale.
        /// Réglable dans le bandeau d'options, et conservé d'une session à l'autre.
        /// </summary>
        public string ZipCompression { get; set; }

        /// <summary>Expéditeur par défaut (réservé).</summary>
        public string DefaultSender { get; set; }

        /// <summary>Case "Exporter 3D" cochée au démarrage.</summary>
        public bool Export3D { get; set; }

        /// <summary>Case "Exporter 2D" cochée au démarrage.</summary>
        public bool Export2D { get; set; }

        /// <summary>Noms des propriétés à lire dans les fichiers SolidWorks.</summary>
        public PropertyNames Properties { get; set; }

        /// <summary>
        /// Racine où chaque demande est archivée, un dossier par demande nommé
        /// avec la date et le destinataire. Vide = pas d'archivage.
        /// </summary>
        public string ArchiveRoot { get; set; }

        /// <summary>
        /// Formats de numéro d'article acceptés, décrits par les longueurs de groupes.
        /// "3-5-2" décrit XYZ-AAAAA-BB. Le PREMIER sert à insérer les tirets
        /// automatiquement quand ils ne sont pas saisis. Liste vide = aucun contrôle.
        /// </summary>
        public List<string> PartNumberPatterns { get; set; }

        /// <summary>Dossier réseau où est enregistrée la liste des fournisseurs.</summary>
        public string SupplierListPath { get; set; }

        /// <summary>Adresse de l'API de l'inventaire. Vide = consultation par export seulement.</summary>
        public string InventoryApiUrl { get; set; }

        /// <summary>
        /// Utilisateur de l'inventaire. Le mot de passe n'est jamais enregistré ici :
        /// il est chiffré par Windows, hors du programme et hors du dépôt.
        /// </summary>
        public string InventoryUser { get; set; }

        /// <summary>
        /// Export de l'inventaire (CSV ou Excel) contenant la référence interne,
        /// l'ancienne référence, le fournisseur et sa référence. Vide = pas de consultation.
        /// </summary>
        public string InventoryExportPath { get; set; }

        /// <summary>Rechercher une nouvelle version au démarrage.</summary>
        public bool CheckUpdatesOnStartup { get; set; }

        /// <summary>
        /// Règles par type d'article, indexées sur les deux caractères YZ du code.
        /// Un type absent de cette table est traité par la règle par défaut : autorisé,
        /// 3D et 2D livrés, fournisseur libre — et signalé dans le journal.
        /// </summary>
        public Dictionary<string, ArticleTypeRule> ArticleTypes { get; set; }

        /// <summary>
        /// Valeurs d'état considérées comme libérées. Tout autre état non vide déclenche
        /// un avertissement groupé avant la création de l'email.
        /// </summary>
        public List<string> ReleasedStates { get; set; }

        public AppConfig()
        {
            PdmRoot = "C:\\00_LynceeTec\\";
            OutputRoot = "";
            ZipThresholdMb = 20;
            MaxAttachments = 25;
            ZipCompression = "Optimal";
            DefaultSender = "";
            Export3D = true;
            Export2D = true;
            Properties = new PropertyNames();
            ReleasedStates = new List<string> { "Libéré", "Libere", "Released", "Approuvé", "Approved" };
            ArchiveRoot = "P:\\PRODUCTION\\3) Document fournisseur";
            SupplierListPath = "P:\\PRODUCTION\\14) Documents techniques\\AskThem_Liste fournisseurs";
            InventoryApiUrl = "http://inventaire.lynceetec.local/api/v1";
            InventoryUser = "";
            InventoryExportPath = "P:\\PRODUCTION\\14) Documents techniques\\AskThem_Liste fournisseurs\\inventaire.xlsx";
            CheckUpdatesOnStartup = true;
            PartNumberPatterns = new List<string> { "3-5-2" };

            //                                   intitulé                              autorisé fabric.  3D     2D   fourn. figé
            ArticleTypes = new Dictionary<string, ArticleTypeRule>();
            ArticleTypes["21"] = ArticleTypeRule.Create("Pièce de détail",              true,   true,  true,  true,  false);
            ArticleTypes["20"] = ArticleTypeRule.Create("Article catalogue",            true,   false, false, false, true);
            ArticleTypes["22"] = ArticleTypeRule.Create("Catalogue modifié à l'achat",  true,   true,  true,  true,  true);
            ArticleTypes["24"] = ArticleTypeRule.Create("Catalogue modifié après livraison", true, false, true,  true,  true);
            ArticleTypes["13"] = ArticleTypeRule.Create("Assemblage",                   false,  false, false, false, false);
        }
    }
}
