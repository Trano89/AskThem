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
        /// Crée et affiche le message. Retourne l'objet Outlook, afin de pouvoir
        /// enregistrer plus tard la version modifiée par l'utilisateur.
        /// </summary>
        public static object CreateMail(string to, string cc, string subject, string htmlBody, List<string> attachments)
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

        /// <summary>
        /// Enregistre le message au format .msg, dans l'état où il se trouve : si
        /// l'utilisateur l'a modifié dans Outlook, ses modifications sont capturées.
        /// Retourne false si Outlook ne rend plus le message accessible.
        /// </summary>
        public static bool SaveMessage(object mailItem, string path)
        {
            if (mailItem == null) return false;
            try
            {
                dynamic mail = mailItem;
                mail.SaveAs(path, OlMsg);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
