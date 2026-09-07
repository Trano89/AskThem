using System;
using System.Collections.Generic;
using System.IO;

namespace AskThem.Models
{
    /// <summary>
    /// Nature d'un document d'article, telle que l'inventaire la reconnaît.
    ///
    /// L'énumération est fermée côté serveur : c'est elle qui permet de demander « le plan
    /// de cet article » sans deviner. L'extension ne suffirait pas — le plan et le
    /// formulaire de contrôle sont tous deux des PDF, et ne jouent pas le même rôle.
    /// </summary>
    public static class TypeDocument
    {
        public const string Plan = "plan_pdf";
        public const string PlanDxf = "plan_dxf";
        public const string Modele = "modele_step";
        public const string Controle = "controle_pdf";
        public const string Autre = "autre";

        /// <summary>Nature déduite d'un fichier produit par AskThem.</summary>
        public static string DapresFichier(string chemin)
        {
            if (string.IsNullOrWhiteSpace(chemin)) return Autre;

            string nom = Path.GetFileName(chemin);
            string ext = Path.GetExtension(chemin).ToLowerInvariant();

            // Le formulaire de contrôle se reconnaît à son préfixe : c'est un PDF comme le
            // plan, mais il se remplit au lieu de se lire.
            if (ext == ".pdf" && nom.StartsWith("CF_", StringComparison.OrdinalIgnoreCase)) return Controle;

            switch (ext)
            {
                case ".pdf": return Plan;
                case ".dxf": return PlanDxf;
                case ".step":
                case ".stp":
                case ".stl": return Modele;
                case ".zip":
                case ".png":
                case ".jpg":
                case ".jpeg": return Autre;
                default: return Autre;
            }
        }

        /// <summary>Intitulé lisible, pour le journal et l'interface.</summary>
        public static string Libelle(string kind)
        {
            switch (kind)
            {
                case Plan: return "plan (PDF)";
                case PlanDxf: return "plan (DXF)";
                case Modele: return "modèle 3D (STEP)";
                case Controle: return "contrôle de fabrication";
                default: return "autre document";
            }
        }

        /// <summary>Vrai si cette nature accompagne une demande envoyée au fournisseur.</summary>
        public static bool PourFournisseur(string kind)
        {
            return kind == Plan || kind == PlanDxf || kind == Modele;
        }
    }

    /// <summary>
    /// Un document rattaché à un article dans l'inventaire.
    ///
    /// Le contenu n'y figure pas : on le télécharge à la demande, et l'empreinte permet de
    /// savoir sans rien transférer si ce qu'on s'apprête à déposer est déjà là.
    /// </summary>
    public class DocumentArticle
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string Revision { get; set; }

        /// <summary>
        /// Date d'émission de la révision, au format AAAA-MM-JJ.
        ///
        /// Ni déductible du fichier, ni retrouvable après coup : sans elle, le document ne
        /// peut plus être situé dans l'ordre des révisions. L'inventaire l'accepte vide pour
        /// les documents anciens, mais elle est à traiter comme obligatoire.
        /// </summary>
        public string RevisionDate { get; set; }
        public string Filename { get; set; }
        public string ContentType { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; }
        public string UploadedByUsername { get; set; }
        public DateTime UploadedAt { get; set; }
        public bool IsCurrent { get; set; }

        public DocumentArticle()
        {
            Kind = TypeDocument.Autre;
            Revision = "";
            RevisionDate = "";
            Filename = "";
            ContentType = "";
            Sha256 = "";
            UploadedByUsername = "";
            IsCurrent = true;
        }

        /// <summary>Révision telle qu'on l'affiche, jamais vide.</summary>
        public string RevisionAffichee
        {
            get { return string.IsNullOrWhiteSpace(Revision) ? "inconnue" : Revision.Trim(); }
        }

        /// <summary>Date de révision telle qu'on l'affiche, jamais vide.</summary>
        public string DateAffichee
        {
            get { return string.IsNullOrWhiteSpace(RevisionDate) ? "date inconnue" : RevisionDate; }
        }

        /// <summary>Âge du dépôt en jours, -1 si l'horodatage manque.</summary>
        public int JoursDepuisDepot
        {
            get
            {
                if (UploadedAt == default(DateTime)) return -1;
                return (int)Math.Floor((DateTime.Now - UploadedAt).TotalDays);
            }
        }

        public override string ToString()
        {
            return Filename + " — " + TypeDocument.Libelle(Kind) + " rev " + RevisionAffichee;
        }
    }

    /// <summary>
    /// Met une date de révision à la forme attendue par l'inventaire.
    ///
    /// L'inventaire refuse une date non ISO plutôt que de l'interpréter, et il a raison :
    /// « 03/04 » se lit mars ou avril selon le pays, et une date fausse rend l'ordre des
    /// révisions faux. On applique la même prudence ici — ce qu'on ne sait pas lire avec
    /// certitude n'est pas envoyé, et le journal le dit.
    /// </summary>
    public static class DateRevision
    {
        /// <summary>Formats sans ambiguïté, dans l'ordre où on les essaie.</summary>
        private static readonly string[] Formes = {
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd",
            "dd.MM.yyyy", "d.M.yyyy",          // usage suisse, jour d'abord
            "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy"
        };

        /// <summary>
        /// Rend la date au format AAAA-MM-JJ, ou une chaîne vide si elle est illisible,
        /// absente ou dans le futur.
        /// </summary>
        public static string Normaliser(string valeur, Action<string> journal)
        {
            if (string.IsNullOrWhiteSpace(valeur)) return "";

            string brut = valeur.Trim();
            DateTime d;
            if (!DateTime.TryParseExact(brut, Formes,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out d))
            {
                Dire(journal, "Date de révision illisible et donc non transmise : « " + brut
                            + " ». Attendu AAAA-MM-JJ ou JJ.MM.AAAA.");
                return "";
            }

            if (d.Date > DateTime.Today)
            {
                Dire(journal, "Date de révision dans le futur, non transmise : " + brut + ".");
                return "";
            }
            return d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Dire(Action<string> journal, string message)
        {
            if (journal == null) return;
            try { journal(message); }
            catch (Exception) { }
        }
    }

    /// <summary>Ce qu'il est advenu d'un dépôt.</summary>
    public enum ResultatDepot
    {
        Depose,
        Remplace,
        Inchange,
        MetadonneeCorrigee,
        Refuse,
        SansDroit,
        Echec
    }

    /// <summary>Ce que l'inventaire sait des documents d'un article.</summary>
    public class DocumentsArticle
    {
        public string Reference { get; set; }
        public int ArticleId { get; set; }
        public bool Trouve { get; set; }
        public List<DocumentArticle> Documents { get; set; }

        public DocumentsArticle()
        {
            Reference = "";
            Documents = new List<DocumentArticle>();
        }

        /// <summary>Le document courant de cette nature, ou null.</summary>
        public DocumentArticle De(string kind)
        {
            foreach (DocumentArticle d in Documents)
                if (d.IsCurrent && d.Kind == kind) return d;
            return null;
        }

        /// <summary>Vrai si un plan accompagne cet article.</summary>
        public bool APlan { get { return De(TypeDocument.Plan) != null; } }
    }
}
