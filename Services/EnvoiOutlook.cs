using System;

namespace AskThem.Services
{
    /// <summary>
    /// Constate qu'un message est réellement parti.
    ///
    /// Une fenêtre de rédaction fermée ne dit rien : l'utilisateur a pu envoyer comme il a pu
    /// renoncer. La seule preuve est la présence du message dans les éléments envoyés. C'est
    /// ce qui distingue une demande faite d'une demande abandonnée, et donc ce qui décide si
    /// elle mérite une place dans l'archive du réseau.
    /// </summary>
    public static class EnvoiOutlook
    {
        /// <summary>olFolderSentMail.</summary>
        private const int DossierEnvoyes = 5;

        /// <summary>Au-delà, on considère que le message cherché n'est pas là : inutile de remonter des années.</summary>
        private const int MaxExamines = 300;

        /// <summary>
        /// Vrai si un message portant ce sujet figure dans les éléments envoyés, à partir de
        /// l'instant indiqué.
        ///
        /// La recherche part du plus récent et s'arrête dès qu'elle dépasse l'horodatage de
        /// préparation : un dossier en attente depuis longtemps ne coûte pas un parcours de
        /// toute la boîte.
        /// </summary>
        public static bool EstEnvoye(string sujet, DateTime depuisLocal)
        {
            if (string.IsNullOrWhiteSpace(sujet)) return false;

            try
            {
                Type t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) return false;

                dynamic outlook = Activator.CreateInstance(t);
                dynamic espace = outlook.GetNamespace("MAPI");
                dynamic dossier = espace.GetDefaultFolder(DossierEnvoyes);
                dynamic elements = dossier.Items;

                // Du plus récent au plus ancien : la réponse est presque toujours en tête.
                try { elements.Sort("[SentOn]", true); }
                catch (Exception) { }

                // Une marge : l'horloge du poste et celle du serveur ne coïncident pas toujours.
                DateTime plancher = depuisLocal.AddMinutes(-5);

                dynamic element = elements.GetFirst();
                int examines = 0;

                while (element != null && examines < MaxExamines)
                {
                    examines++;
                    try
                    {
                        DateTime envoyeLe = (DateTime)element.SentOn;
                        if (envoyeLe < plancher) return false;   // trié : tout le reste est plus ancien

                        string s = (string)element.Subject;
                        if (!string.IsNullOrEmpty(s)
                            && string.Equals(s.Trim(), sujet.Trim(), StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch (Exception)
                    {
                        // Les éléments envoyés peuvent contenir autre chose qu'un message :
                        // un rendez-vous n'a pas de SentOn. On passe au suivant.
                    }
                    element = elements.GetNext();
                }
            }
            catch (Exception ex)
            {
                // Outlook fermé ou indisponible : on ne conclut pas à l'envoi. Le dossier
                // reste en attente et sera repris au prochain démarrage.
                LogService.Write("Éléments envoyés illisibles : " + ex.Message);
            }
            return false;
        }
    }
}
