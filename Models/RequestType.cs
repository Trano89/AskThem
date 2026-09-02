namespace AskThem.Models
{
    /// <summary>Nature de la demande, choisie au premier pas de l'assistant.</summary>
    public enum RequestType
    {
        /// <summary>Consultation de prix sur des pièces à fabriquer.</summary>
        Offre = 0,

        /// <summary>Ordre de fabrication de pièces sur mesure.</summary>
        Fabrication = 1,

        /// <summary>
        /// Commande ferme d'articles de catalogue, sur leur seule référence chez le
        /// fournisseur. Ni plan, ni modèle, ni contrôle de fabrication.
        /// </summary>
        CommandeCatalogue = 2
    }

    /// <summary>Ce que chaque type de demande implique, en un seul endroit.</summary>
    public static class RequestTypes
    {
        /// <summary>Intitulé affiché à l'utilisateur.</summary>
        public static string Libelle(RequestType type)
        {
            switch (type)
            {
                case RequestType.Fabrication: return "Demande de fabrication";
                case RequestType.CommandeCatalogue: return "Commande catalogue";
                default: return "Demande d'offre";
            }
        }

        /// <summary>Ce que le type fait au juste, en une phrase.</summary>
        public static string Description(RequestType type)
        {
            switch (type)
            {
                case RequestType.Fabrication:
                    return "Confier la fabrication de pièces sur mesure, plans et modèles 3D à l'appui.";
                case RequestType.CommandeCatalogue:
                    return "Commander des articles de catalogue sur leur référence chez le fournisseur. "
                         + "Aucun fichier n'est joint.";
                default:
                    return "Consulter un fournisseur sur le prix, avec plusieurs paliers de "
                         + "quantité. Pièces sur mesure ou articles de catalogue.";
            }
        }

        /// <summary>Vrai si la demande ne porte que des articles de catalogue.</summary>
        public static bool EstCatalogue(RequestType type)
        {
            return type == RequestType.CommandeCatalogue;
        }

        /// <summary>Vrai si la demande accepte plusieurs paliers de quantité.</summary>
        public static bool PlusieursQuantites(RequestType type)
        {
            return type == RequestType.Offre;
        }

        /// <summary>Suffixe du dossier d'archive.</summary>
        public static string Tag(RequestType type)
        {
            switch (type)
            {
                case RequestType.Fabrication: return "FAB";
                case RequestType.CommandeCatalogue: return "CDE";
                default: return "OFFRE";
            }
        }
    }
}
