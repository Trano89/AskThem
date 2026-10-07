using System;
using System.Collections.Generic;
using System.IO;

namespace AskThem.Services
{
    /// <summary>Crée et affiche un email dans Outlook Classic. N'envoie jamais l'email.</summary>
    public static class OutlookService
    {
        /// <summary>olMSG : format de fichier .msg d'Outlook.</summary>
        private const int OlMsg = 3;

        /// <summary>
        /// Propriété MAPI posée sur chaque message préparé par AskThem, invisible du
        /// fournisseur. Elle suit le message dans les éléments envoyés : c'est elle, et non
        /// l'objet que l'utilisateur a pu retoucher, qui prouve qu'une demande est partie.
        /// </summary>
        public const string ProprieteMarque =
            "http://schemas.microsoft.com/mapi/string/{00020329-0000-0000-C000-000000000046}/AskThemDemande";

        /// <summary>
        /// Crée et affiche le message. Retourne l'objet Outlook, afin de pouvoir
        /// enregistrer plus tard la version modifiée par l'utilisateur.
        /// </summary>
        public static object CreateMail(string to, string cc, string subject, string htmlBody, List<string> attachments)
        {
            return CreateMail(to, cc, subject, htmlBody, attachments, "");
        }

        /// <param name="marque">Identifiant du message, retrouvé ensuite dans les éléments envoyés.</param>
        public static object CreateMail(string to, string cc, string subject, string htmlBody,
                                        List<string> attachments, string marque)
        {
            Type t = Type.GetTypeFromProgID("Outlook.Application");
            if (t == null)
                throw new Exception("Outlook Classic n'est pas disponible sur ce poste.");

            dynamic outlook = Activator.CreateInstance(t);
            dynamic mail = outlook.CreateItem(0); // 0 = olMailItem

            mail.To = to;
            if (!string.IsNullOrWhiteSpace(cc)) mail.CC = cc;
            mail.Subject = subject;

            // Lire GetInspector force Outlook a inserer la signature par defaut dans le
            // corps du message. On recupere ce corps, puis on place notre contenu AVANT
            // la signature : l'affecter directement effacerait celle-ci.
            string signature = "";
            try
            {
                object inspecteur = mail.GetInspector;
                if (inspecteur != null) signature = (string)mail.HTMLBody;
            }
            catch (Exception)
            {
                signature = "";
            }
            mail.HTMLBody = MergeWithSignature(htmlBody, signature);

            if (attachments != null)
            {
                foreach (string file in attachments)
                {
                    if (File.Exists(file))
                        mail.Attachments.Add(file);
                }
            }

            // La marque est posée avant l'affichage : sans elle, l'envoi ne pourrait pas être
            // constaté, et la demande ne serait ni archivée ni suivie. On le dit tout de suite.
            if (!string.IsNullOrWhiteSpace(marque))
            {
                object accesseur = mail.PropertyAccessor;
                ((dynamic)accesseur).SetProperty(ProprieteMarque, marque);
            }

            mail.Display(false); // affiche la fenêtre, N'ENVOIE PAS
            return mail;
        }

        /// <summary>
        /// Insère le contenu au-dessus de la signature par défaut d'Outlook.
        /// Si aucune signature n'est configurée, le contenu est renvoyé tel quel.
        /// </summary>
        private static string MergeWithSignature(string contenu, string signature)
        {
            if (string.IsNullOrWhiteSpace(signature)) return contenu;

            int debut = signature.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (debut < 0) return contenu + signature;
            int fin = signature.IndexOf('>', debut);
            if (fin < 0) return contenu + signature;

            // On n'imbrique pas deux documents complets : seul l'intérieur du <body> est repris.
            return signature.Insert(fin + 1, InnerBody(contenu));
        }

        /// <summary>Contenu interne de la balise body d'un document HTML complet.</summary>
        private static string InnerBody(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            int debut = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (debut < 0) return html;
            int ouverture = html.IndexOf('>', debut);
            if (ouverture < 0) return html;
            int fermeture = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (fermeture < 0 || fermeture <= ouverture) return html.Substring(ouverture + 1);
            return html.Substring(ouverture + 1, fermeture - ouverture - 1);
        }

        /// <summary>Adresse SMTP de l'utilisateur d'Outlook, ou "".</summary>
        public static string AdresseUtilisateur()
        {
            try
            {
                if (!EnvoiOutlook.OutlookOuvert()) return "";
                Type t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) return "";
                dynamic outlook = Activator.CreateInstance(t);
                string adresse = AdresseDe(outlook.Session.CurrentUser.AddressEntry);
                if (adresse != "") return adresse;
                try { return ((string)outlook.Session.Accounts.Item(1).SmtpAddress ?? "").Trim(); }
                catch (Exception) { return ""; }
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// L'adresse d'un collègue retrouvée dans le carnet d'adresses à partir de son nom ou
        /// de son identifiant Windows, ou "".
        /// </summary>
        public static string ResoudreAdresse(string nomOuIdentifiant)
        {
            if (string.IsNullOrWhiteSpace(nomOuIdentifiant)) return "";
            if (nomOuIdentifiant.Contains("@")) return nomOuIdentifiant.Trim();
            try
            {
                Type t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) return "";
                dynamic outlook = Activator.CreateInstance(t);
                dynamic r = outlook.Session.CreateRecipient(nomOuIdentifiant.Trim());
                if (!(bool)r.Resolve()) return "";
                return AdresseDe(r.AddressEntry);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string AdresseDe(dynamic entree)
        {
            try
            {
                if (entree == null) return "";
                string type = (string)entree.Type;
                if (string.Equals(type, "EX", StringComparison.OrdinalIgnoreCase))
                {
                    dynamic exchange = entree.GetExchangeUser();
                    if (exchange != null) return ((string)exchange.PrimarySmtpAddress ?? "").Trim();
                }
                string adresse = (string)entree.Address;
                return adresse != null && adresse.Contains("@") ? adresse.Trim() : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// Envoie aussitôt un message d'information, sans l'afficher. Réservé aux avis qu'AskThem
        /// adresse à un collègue : jamais aux fournisseurs, dont les messages passent toujours
        /// par la relecture de l'utilisateur.
        /// </summary>
        public static bool EnvoyerAvis(string a, string sujet, string html, out string message)
        {
            message = "";
            try
            {
                Type t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) { message = "Outlook n'est pas disponible sur ce poste."; return false; }
                dynamic outlook = Activator.CreateInstance(t);
                dynamic mail = outlook.CreateItem(0);
                mail.To = a;
                mail.Subject = sujet;
                mail.HTMLBody = html;
                mail.Send();
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                LogService.Write("Avis non envoyé à " + a + " : " + ex.Message);
                return false;
            }
        }

        /// <summary>Nom de l'utilisateur tel qu'Outlook le connaît, ou "".</summary>
        public static string NomUtilisateur()
        {
            try
            {
                if (!EnvoiOutlook.OutlookOuvert()) return "";
                Type t = Type.GetTypeFromProgID("Outlook.Application");
                if (t == null) return "";
                dynamic outlook = Activator.CreateInstance(t);
                string nom = (string)outlook.Session.CurrentUser.Name;
                return nom == null ? "" : nom.Trim();
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
