using System.Collections.Generic;

namespace AskThem.Models
{
    /// <summary>Une ligne de la grille : ce que l'utilisateur saisit, et ce que le PDM renseigne.</summary>
    public class PartLine
    {
        // ---- Saisi par l'utilisateur ----

        /// <summary>Numéro d'article saisi par l'utilisateur.</summary>
        public string PartNumber { get; set; }

        /// <summary>Quantité 1 (toujours utilisée).</summary>
        public int Qty1 { get; set; }

        /// <summary>Quantité 2 (0 = non utilisée, mode Offre uniquement).</summary>
        public int Qty2 { get; set; }

        /// <summary>Quantité 3 (0 = non utilisée, mode Offre uniquement).</summary>
        public int Qty3 { get; set; }

        /// <summary>Remarque libre.</summary>
        public string Remark { get; set; }

        // ---- Renseigné par le programme depuis le PDM ----

        /// <summary>Désignation (variable Description de la carte de données).</summary>
        public string Description { get; set; }

        /// <summary>Révision du modèle 3D.</summary>
        public string Revision { get; set; }

        /// <summary>Révision du plan (variable Revision du .SLDDRW). Prioritaire dans l'email.</summary>
        public string DrawingRevision { get; set; }

        /// <summary>Date de réalisé (ReleaseDate, à défaut DrawnDate).</summary>
        public string RealizedDate { get; set; }

        /// <summary>Matière (variable Material).</summary>
        public string Material { get; set; }

        /// <summary>Finitions (variable Traitement).</summary>
        public string Treatment { get; set; }

        /// <summary>État du flux PDM lu dans les propriétés (ex. Libéré, En développement).</summary>
        public string State { get; set; }

        /// <summary>Fournisseur imposé par le PDM, pour un article catalogue.</summary>
        public string PdmSupplier { get; set; }

        /// <summary>Référence de l'article chez ce fournisseur.</summary>
        public string SupplierRef { get; set; }

        /// <summary>
        /// Référence du fabricant, quand l'inventaire la connaît. Elle n'est renseignée que
        /// sur une minorité d'articles : la colonne disparaît de l'email si aucune ligne
        /// n'en porte.
        /// </summary>
        public string ManufacturerRef { get; set; }

        /// <summary>
        /// Ancienne référence de l'article, lue dans l'inventaire. Sa présence signale
        /// que l'article a été recodifié et que la gamme mérite d'être revue.
        /// </summary>
        public string OldRef { get; set; }

        /// <summary>Type de l'article : les deux caractères YZ du code.</summary>
        public string TypeCode { get; set; }

        /// <summary>"", "OK", "Manquant 3D", "Manquant 2D", "Introuvable" ou "Erreur".</summary>
        public string Status { get; set; }

        /// <summary>Chemin trouvé du fichier .SLDPRT ou .SLDASM.</summary>
        public string Model3DPath { get; set; }

        /// <summary>Chemin trouvé du fichier .SLDDRW.</summary>
        public string DrawingPath { get; set; }

        /// <summary>Chemins des fichiers exportés pour cet article.</summary>
        public List<string> ExportedFiles { get; set; }

        /// <summary>Archive ZIP propre à cet article.</summary>
        public string ZipPath { get; set; }

        /// <summary>Révision à afficher : celle du plan si connue, sinon celle du modèle.</summary>
        public string EffectiveRevision
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DrawingRevision)) return DrawingRevision;
                return Revision == null ? "" : Revision;
            }
        }

        public PartLine()
        {
            PartNumber = "";
            Qty1 = 1;
            Qty2 = 0;
            Qty3 = 0;
            Remark = "";
            Description = "";
            Revision = "";
            DrawingRevision = "";
            RealizedDate = "";
            Material = "";
            Treatment = "";
            State = "";
            PdmSupplier = "";
            SupplierRef = "";
            ManufacturerRef = "";
            OldRef = "";
            TypeCode = "";
            Status = "";
            Model3DPath = null;
            DrawingPath = null;
            ExportedFiles = new List<string>();
            ZipPath = null;
        }
    }
}
