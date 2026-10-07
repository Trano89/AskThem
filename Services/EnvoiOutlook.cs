using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        /// <summary>
        /// Vrai si Outlook est ouvert. Le suivi ne le démarre jamais lui-même : il le ferait
        /// toutes les deux minutes, en arrière-plan, chez quelqu'un qui vient de le fermer.
        /// </summary>
        public static bool OutlookOuvert()
        {
            try { return Process.GetProcessesByName("OUTLOOK").Length > 0; }
            catch (Exception) { return false; }
        }

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
            if (string.IsNullOrWhiteSpace(sujet) || !OutlookOuvert()) return false;

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

        /// <summary>Un message retrouvé dans les éléments envoyés, tel qu'il est parti.</summary>
        public sealed class MessageEnvoye
        {
            public DateTime EnvoyeLe;
            public string Sujet = "";
            public string Destinataires = "";
        }

        /// <summary>
        /// Le message portant cette marque dans les éléments envoyés, ou null s'il n'est pas
        /// (encore) parti.
        ///
        /// C'est la preuve de l'envoi : la marque est posée par AskThem sur le message qu'il
        /// prépare, invisible du fournisseur, et Outlook la conserve sur la copie rangée dans
        /// les éléments envoyés. Un brouillon abandonné ou supprimé n'y arrive jamais. Tous les
        /// comptes du profil sont parcourus : une demande peut partir d'une boîte partagée.
        ///
        /// Le message est enregistré au passage : c'est lui, retouches comprises, qui rejoint
        /// l'archive — et non le brouillon tel qu'AskThem l'avait préparé.
        /// </summary>
        public static MessageEnvoye Chercher(string marque, string enregistrerSous)
        {
            return Chercher(marque, enregistrerSous, DateTime.MinValue);
        }

        /// <param name="depuis">
        /// Préparation de la demande : les messages envoyés depuis sont relus un à un si le
        /// filtre d'Outlook ne trouve rien, ce qui arrive sur les comptes IMAP.
        /// </param>
        public static MessageEnvoye Chercher(string marque, string enregistrerSous, DateTime depuis)
        {
            if (string.IsNullOrWhiteSpace(marque) || !OutlookOuvert()) return null;

            // Le type de la propriété fait partie de son nom dans un filtre : sans lui
            // (0x001F, texte Unicode), Outlook ne la reconnaît pas et ne trouve rien — même
            // sur un message qui la porte.
            string filtre = "@SQL=\"" + OutlookService.ProprieteMarque + "/0x0000001F\" = '"
                          + marque.Replace("'", "''") + "'";

            object espace = null;
            List<object> dossiers = new List<object>();
            try
            {
                Type t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) return null;
                object outlook = Activator.CreateInstance(t);
                espace = ((dynamic)outlook).GetNamespace("MAPI");
                dossiers = DossiersEnvoyes(espace);
                foreach (object dossier in dossiers)
                {
                    MessageEnvoye m = ChercherDans(dossier, filtre, enregistrerSous);
                    if (m != null) return m;
                }
                if (depuis != DateTime.MinValue)
                {
                    foreach (object dossier in dossiers)
                    {
                        MessageEnvoye m = ParcourirDepuis(dossier, marque, depuis, enregistrerSous);
                        if (m != null) return m;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Éléments envoyés illisibles : " + ex.Message);
            }
            finally
            {
                foreach (object d in dossiers) Liberer(d);
                Liberer(espace);
            }
            return null;
        }

        /// <summary>Le dossier des éléments envoyés de chaque compte du profil.</summary>
        private static List<object> DossiersEnvoyes(object espace)
        {
            List<object> dossiers = new List<object>();
            object comptes = null;
            try
            {
                comptes = ((dynamic)espace).Stores;
                int n = (int)((dynamic)comptes).Count;
                for (int i = 1; i <= n; i++)
                {
                    object compte = null;
                    try
                    {
                        compte = ((dynamic)comptes).Item(i);
                        object d = ((dynamic)compte).GetDefaultFolder(DossierEnvoyes);
                        if (d != null) dossiers.Add(d);
                    }
                    catch (Exception) { }   // archive, dossiers publics : pas d'éléments envoyés
                    finally { Liberer(compte); }
                }
            }
            catch (Exception) { }
            finally { Liberer(comptes); }

            if (dossiers.Count == 0)
            {
                try { dossiers.Add(((dynamic)espace).GetDefaultFolder(DossierEnvoyes)); }
                catch (Exception) { }
            }
            return dossiers;
        }

        private static MessageEnvoye ChercherDans(object dossier, string filtre, string enregistrerSous)
        {
            object elements = null, trouves = null, element = null;
            try
            {
                elements = ((dynamic)dossier).Items;
                trouves = ((dynamic)elements).Restrict(filtre);
                if ((int)((dynamic)trouves).Count == 0) return null;
                element = ((dynamic)trouves).GetFirst();
                if (element == null) return null;
                return Decrire(element, enregistrerSous);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Liberer(element);
                Liberer(trouves);
                Liberer(elements);
            }
        }

        /// <summary>
        /// Relit un à un les messages envoyés depuis la préparation, du plus récent au plus
        /// ancien, et lit la marque sur chacun. Plus lent que le filtre, mais indépendant de
        /// ce que le compte sait filtrer.
        /// </summary>
        private static MessageEnvoye ParcourirDepuis(object dossier, string marque, DateTime depuis, string enregistrerSous)
        {
            object elements = null;
            try
            {
                elements = ((dynamic)dossier).Items;
                dynamic liste = elements;
                try { liste.Sort("[SentOn]", true); }
                catch (Exception) { }

                DateTime plancher = depuis.AddMinutes(-5);
                object element = liste.GetFirst();
                int examines = 0;
                while (element != null && examines < MaxExamines)
                {
                    examines++;
                    bool fini = false, trouve = false;
                    try
                    {
                        dynamic m = element;
                        if ((DateTime)m.SentOn < plancher) fini = true;
                        else
                        {
                            object accesseur = m.PropertyAccessor;
                            try
                            {
                                object valeur = ((dynamic)accesseur).GetProperty(OutlookService.ProprieteMarque);
                                trouve = string.Equals(valeur as string, marque, StringComparison.Ordinal);
                            }
                            catch (Exception) { }   // pas de marque sur ce message
                            finally { Liberer(accesseur); }
                        }
                    }
                    catch (Exception) { }

                    if (trouve)
                    {
                        try { return Decrire(element, enregistrerSous); }
                        finally { Liberer(element); }
                    }
                    if (fini) { Liberer(element); return null; }

                    object suivant = liste.GetNext();
                    Liberer(element);
                    element = suivant;
                }
                Liberer(element);
            }
            catch (Exception) { }
            finally { Liberer(elements); }
            return null;
        }

        private static MessageEnvoye Decrire(object element, string enregistrerSous)
        {
            {
                dynamic m = element;
                MessageEnvoye r = new MessageEnvoye();
                r.EnvoyeLe = (DateTime)m.SentOn;
                string sujet = (string)m.Subject;
                r.Sujet = sujet == null ? "" : sujet;
                r.Destinataires = ListeDestinataires(m);

                if (!string.IsNullOrWhiteSpace(enregistrerSous))
                {
                    try { m.SaveAs(enregistrerSous, 3); }
                    catch (Exception ex) { LogService.Write("Message envoyé non enregistré : " + ex.Message); }
                }
                return r;
            }
        }

        private static string ListeDestinataires(dynamic message)
        {
            List<string> liste = new List<string>();
            object destinataires = null;
            try
            {
                destinataires = message.Recipients;
                int n = (int)((dynamic)destinataires).Count;
                for (int i = 1; i <= n; i++)
                {
                    object r = null;
                    try
                    {
                        r = ((dynamic)destinataires).Item(i);
                        string nom = (string)((dynamic)r).Name;
                        string adresse = Adresse(r);
                        liste.Add(string.IsNullOrWhiteSpace(adresse) || adresse == nom ? nom : nom + " <" + adresse + ">");
                    }
                    catch (Exception) { }
                    finally { Liberer(r); }
                }
            }
            catch (Exception) { }
            finally { Liberer(destinataires); }
            return string.Join("; ", liste);
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
