using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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

        /// <summary>PR_SMTP_ADDRESS : l'adresse internet d'un destinataire, quel que soit son type.</summary>
        private const string ProprieteSmtp = "http://schemas.microsoft.com/mapi/proptag/0x39FE001E";

        public static bool EstEnvoye(string sujet, DateTime depuisLocal)
        {
            return EstEnvoye(sujet, depuisLocal, "");
        }

        /// <summary>
        /// Vrai si un message portant ce sujet, adressé à ce destinataire, figure dans les
        /// éléments envoyés à partir de l'instant indiqué.
        ///
        /// Le sujet seul ne suffit pas : il ne nomme pas le fournisseur, et la même demande
        /// préparée pour trois fournisseurs porte trois fois le même objet. Celle qu'on
        /// abandonne serait archivée sur la foi de l'envoi d'une autre. On exige donc qu'un
        /// destinataire du message appartienne au domaine de messagerie de la demande — le
        /// domaine plutôt que l'adresse exacte, car l'acheteur change volontiers d'interlocuteur
        /// chez le même fournisseur avant d'envoyer.
        ///
        /// La recherche part du plus récent et s'arrête dès qu'elle dépasse l'horodatage de
        /// préparation : un dossier en attente depuis longtemps ne coûte pas un parcours de
        /// toute la boîte.
        /// </summary>
        /// <param name="destinataire">La ligne « À » de la demande. Vide : le sujet seul décide.</param>
        public static bool EstEnvoye(string sujet, DateTime depuisLocal, string destinataire)
        {
            if (string.IsNullOrWhiteSpace(sujet)) return false;

            List<string> domaines = Domaines(destinataire);
            object outlook, espace = null, dossier = null, elements = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) return false;

                outlook = Activator.CreateInstance(t);
                espace = ((dynamic)outlook).GetNamespace("MAPI");
                dossier = ((dynamic)espace).GetDefaultFolder(DossierEnvoyes);
                elements = ((dynamic)dossier).Items;
                dynamic liste = elements;

                // Du plus récent au plus ancien : la réponse est presque toujours en tête.
                try { liste.Sort("[SentOn]", true); }
                catch (Exception) { }

                // Une marge : l'horloge du poste et celle du serveur ne coïncident pas toujours.
                DateTime plancher = depuisLocal.AddMinutes(-5);

                // Chaque élément parcouru est libéré aussitôt : Exchange limite le nombre
                // d'éléments ouverts par session, et au-delà chaque lecture échoue en silence —
                // ce qui ferait conclure qu'aucun message n'est parti.
                object element = liste.GetFirst();
                int examines = 0;
                while (element != null && examines < MaxExamines)
                {
                    examines++;
                    bool trouve = false, fini = false;
                    try
                    {
                        dynamic m = element;
                        DateTime envoyeLe = (DateTime)m.SentOn;
                        if (envoyeLe < plancher)
                        {
                            fini = true;   // trié : tout le reste est plus ancien
                        }
                        else
                        {
                            string s = (string)m.Subject;
                            if (!string.IsNullOrEmpty(s)
                                && string.Equals(s.Trim(), sujet.Trim(), StringComparison.OrdinalIgnoreCase))
                                trouve = domaines.Count == 0 || AdresseAuDomaine(m, domaines);
                        }
                    }
                    catch (Exception)
                    {
                        // Les éléments envoyés peuvent contenir autre chose qu'un message :
                        // un rendez-vous n'a pas de SentOn. On passe au suivant.
                    }

                    if (trouve || fini)
                    {
                        Liberer(element);
                        return trouve;
                    }

                    object suivant = liste.GetNext();
                    Liberer(element);
                    element = suivant;
                }
                Liberer(element);
            }
            catch (Exception ex)
            {
                // Outlook fermé ou indisponible : on ne conclut pas à l'envoi. Le dossier
                // reste en attente et sera repris au prochain démarrage.
                LogService.Write("Éléments envoyés illisibles : " + ex.Message);
            }
            finally
            {
                // L'application elle-même n'est pas libérée : son enveloppe .NET peut être
                // partagée avec les messages en cours de rédaction sur ce même thread.
                Liberer(elements);
                Liberer(dossier);
                Liberer(espace);
            }
            return false;
        }

        /// <summary>Vrai si l'un des destinataires du message appartient à l'un de ces domaines.</summary>
        private static bool AdresseAuDomaine(dynamic message, List<string> domaines)
        {
            object destinataires = null;
            try
            {
                destinataires = message.Recipients;
                dynamic liste = destinataires;
                int n = (int)liste.Count;
                for (int i = 1; i <= n; i++)
                {
                    object r = null;
                    try
                    {
                        r = liste.Item(i);
                        if (AuDomaine(Adresse(r), domaines)) return true;
                    }
                    catch (Exception) { }
                    finally { Liberer(r); }
                }
            }
            catch (Exception) { }
            finally { Liberer(destinataires); }

            // Dernier recours : la ligne « À » telle qu'Outlook l'affiche.
            try { return AuDomaine((string)message.To, domaines); }
            catch (Exception) { return false; }
        }

        private static string Adresse(object destinataire)
        {
            dynamic r = destinataire;
            string adresse = "";
            try { adresse = (string)r.Address; }
            catch (Exception) { }
            if (adresse != null && adresse.Contains("@")) return adresse;

            try
            {
                object accesseur = r.PropertyAccessor;
                try { return (string)((dynamic)accesseur).GetProperty(ProprieteSmtp); }
                finally { Liberer(accesseur); }
            }
            catch (Exception) { return adresse == null ? "" : adresse; }
        }

        private static bool AuDomaine(string texte, List<string> domaines)
        {
            if (string.IsNullOrWhiteSpace(texte)) return false;
            foreach (string d in domaines)
                if (texte.IndexOf("@" + d, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Domaines de messagerie d'une ligne de destinataires (« a@x.ch; B &lt;b@y.com&gt; »).</summary>
        private static List<string> Domaines(string destinataire)
        {
            List<string> domaines = new List<string>();
            if (string.IsNullOrWhiteSpace(destinataire)) return domaines;

            foreach (string morceau in destinataire.Split(new char[] { ';', ',', ' ', '\t' },
                                                          StringSplitOptions.RemoveEmptyEntries))
            {
                int arobase = morceau.LastIndexOf('@');
                if (arobase < 0 || arobase == morceau.Length - 1) continue;
                string d = morceau.Substring(arobase + 1).Trim().TrimEnd('>', '"', '\'', ')').ToLowerInvariant();
                if (d != "" && !domaines.Contains(d)) domaines.Add(d);
            }
            return domaines;
        }

        private static void Liberer(object com)
        {
            if (com == null) return;
            try { if (Marshal.IsComObject(com)) Marshal.ReleaseComObject(com); }
            catch (Exception) { }
        }
    }
}
