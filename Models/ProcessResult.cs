using System.Collections.Generic;

namespace AskThem.Models
{
    /// <summary>Résultat global d'un traitement (vérification ou génération).</summary>
    public class ProcessResult
    {
        /// <summary>Nombre total de lignes traitées.</summary>
        public int TotalLines { get; set; }

        /// <summary>Nombre d'articles au statut "OK".</summary>
        public int SuccessCount { get; set; }

        /// <summary>Nombre d'avertissements (manquant 3D, manquant 2D, introuvable).</summary>
        public int WarningCount { get; set; }

        /// <summary>Nombre d'erreurs.</summary>
        public int ErrorCount { get; set; }

        /// <summary>Dossier de sortie horodaté.</summary>
        public string OutputFolder { get; set; }

        /// <summary>Fichiers joints à l'email.</summary>
        public List<string> AttachmentPaths { get; set; }

        /// <summary>Messages du journal.</summary>
        public List<string> Messages { get; set; }

        public ProcessResult()
        {
            TotalLines = 0;
            SuccessCount = 0;
            WarningCount = 0;
            ErrorCount = 0;
            OutputFolder = "";
            AttachmentPaths = new List<string>();
            Messages = new List<string>();
        }
    }
}
