using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>Construit l'objet et le corps HTML de l'email à partir des modèles.</summary>
    public static class EmailBuilder
    {
        // Sauts de ligne composes par code, pour ne dependre d aucun echappement.
        private static readonly string CRLF = new string(new char[] { (char)13, (char)10 });
        private static readonly string LF = ((char)10).ToString();

        private const string CellStyle = " style=\"border:1px solid #999; padding:4px 8px;\"";
        private const string HeadStyle = " style=\"border:1px solid #999; padding:4px 8px; background:#eef2f6; font-weight:bold;\"";

        /// <summary>Objet du message.</summary>
        public static string BuildSubject(RequestType type, string project, int count)
        {
            string prefix = RequestTypes.Libelle(type);

            // Sans référence projet, on retire le segment et son tiret.
            if (string.IsNullOrWhiteSpace(project))
                return prefix + " - " + count + " article(s)";
            return prefix + " - " + project.Trim() + " - " + count + " article(s)";
        }

        /// <summary>Corps HTML du message.</summary>
        public static string BuildBody(RequestType type, List<PartLine> lines, string project,
                                      string deadline, string conditions, string poFileName)
        {
            return BuildBody(type, lines, project, deadline, conditions, poFileName, false);
        }

        /// <summary>
        /// Corps HTML du message. En mode catalogue, le tableau ne porte que ce qui a un
        /// sens pour un article acheté sur référence : ni révision, ni matière, ni finitions,
        /// et le modèle annonce qu'aucun fichier n'accompagne la demande.
        /// </summary>
        public static string BuildBody(RequestType type, List<PartLine> lines, string project,
                                      string deadline, string conditions, string poFileName,
                                      bool catalogue)
        {
            string html = LoadTemplate(type, catalogue);
            string projectText = string.IsNullOrWhiteSpace(project) ? "-" : project.Trim();
            string deadlineText = string.IsNullOrWhiteSpace(deadline) ? "non précisé" : deadline.Trim();

            html = html.Replace("{{NB_ARTICLES}}", lines.Count.ToString());
            html = html.Replace("{{COMMANDE}}", WebUtility.HtmlEncode(projectText));
            html = html.Replace("{{DELAI}}", WebUtility.HtmlEncode(deadlineText));
            html = html.Replace("{{TABLEAU}}", BuildTable(type, lines, catalogue));
            html = html.Replace("{{COMMENTAIRE}}", BuildCommentaire(conditions));
            html = html.Replace("{{NOTES}}", BuildNotes(type, lines, catalogue));
            html = html.Replace("{{PO}}", BuildPo(type, poFileName));
            return html;
        }

        /// <summary>
        /// Avertissement sur les articles recodifiés : leur ancienne référence existe encore
        /// chez le fournisseur, et la pièce a pu évoluer entre-temps. Vide si aucun article
        /// de la demande n'est concerné.
        /// </summary>
        private static string BuildRecodification(List<PartLine> lines)
        {
            int nombre = 0;
            foreach (PartLine l in lines)
            {
                if (!string.IsNullOrWhiteSpace(l.OldRef)) nombre++;
            }
            if (nombre == 0) return "";

            string pluriel = nombre > 1 ? "s" : "";
            return "<div style=\"border-left:4px solid #b8860b; background:#fdf6e3; "
                 + "padding:10px 14px; margin:18px 0;\">"
                 + "<b>IMPORTANT &mdash; Article" + pluriel + " recodifié" + pluriel + "</b><br/>"
                 + nombre + " article" + pluriel + " de cette demande porte" + (nombre > 1 ? "nt" : "")
                 + " une <b>nouvelle référence de production</b>, qui remplace la référence "
                 + "antérieure indiquée dans la colonne <i>Ancienne réf.</i> du tableau. "
                 + "<b>Des modifications ont pu être apportées</b> depuis la version que vous "
                 + "connaissez sous l'ancienne référence. Merci de <b>revoir la gamme</b> et de ne "
                 + "pas reconduire telle quelle une préparation établie sur l'ancienne version."
                 + "</div>";
        }

        /// <summary>
        /// Mention du document joint : demande de PO sur une offre, bon de commande sur
        /// une fabrication comme sur une commande catalogue. Vide si aucun document
        /// n'accompagne la demande.
        /// </summary>
        private static string BuildPo(RequestType type, string poFileName)
        {
            if (string.IsNullOrWhiteSpace(poFileName)) return "";
            string nom = WebUtility.HtmlEncode(poFileName.Trim());
            if (type == RequestType.Offre)
            {
                return "<p>Notre <b>demande de PO</b> est jointe à ce message : " + nom + "</p>";
            }
            return "<p>Notre <b>bon de commande</b> est joint à ce message : " + nom + "</p>";
        }

        /// <summary>
        /// Commentaire general : bloc titre place juste sous le tableau, dans la meme
        /// taille que le reste du message. Vide si rien n'a ete saisi.
        /// </summary>
        private static string BuildCommentaire(string commentaire)
        {
            if (string.IsNullOrWhiteSpace(commentaire)) return "";
            string texte = WebUtility.HtmlEncode(commentaire.Trim());
            texte = texte.Replace(CRLF, "<br/>").Replace(LF, "<br/>");
            return "<div style=\"margin:18px 0; border:1px solid #b8c4cc;\">"
                 + "<div style=\"background:#eef2f6; padding:7px 13px; font-weight:bold; "
                 + "border-bottom:1px solid #b8c4cc;\">Commentaire général</div>"
                 + "<div style=\"padding:11px 13px;\">" + texte + "</div></div>";
        }

        /// <summary>
        /// Notes en couleur, regroupees en fin de message juste avant la signature :
        /// article recodifie, puis rappel sur la revision des plans en fabrication.
        ///
        /// Aucune des deux n'a de sens pour un achat catalogue : elles parlent de gamme et
        /// de preparation de fabrication, alors que le fournisseur vend une reference sur
        /// etagere et n'a jamais connu notre ancien code interne.
        /// </summary>
        private static string BuildNotes(RequestType type, List<PartLine> lines, bool catalogue)
        {
            if (catalogue) return "";

            StringBuilder sb = new StringBuilder();
            sb.Append(BuildRecodification(lines));
            if (type == RequestType.Fabrication) sb.Append(NoteRevision);
            return sb.ToString();
        }

        /// <summary>Rappel sur la revision des plans, propre a la fabrication.</summary>
        private const string NoteRevision =
            "<div style=\"border-left:4px solid #c00000; background:#fff4f4; padding:10px 14px; margin:14px 0;\">"
            + "<b>IMPORTANT &mdash; Révision des plans</b><br/>"
            + "Merci de contrôler impérativement la <b>révision indiquée dans le cartouche de "
            + "chaque plan</b> et de la comparer à celle du tableau ci-dessus. La fabrication doit "
            + "être réalisée <b>exclusivement selon la révision indiquée</b>. Nous vous prions "
            + "de nous <b>confirmer par retour de message la révision sur laquelle vous travaillez</b>, "
            + "afin de garantir que les dernières mises à jour sont bien prises en compte.</div>";

        /// <summary>Charge le modèle HTML ; si le fichier est absent, utilise le modèle intégré.</summary>
        private static string LoadTemplate(RequestType type, bool catalogue)
        {
            string fileName;
            if (type == RequestType.CommandeCatalogue) fileName = "template_commande_catalogue.html";
            else if (catalogue) fileName = "template_offre_catalogue.html";
            else if (type == RequestType.Offre) fileName = "template_offre.html";
            else fileName = "template_fabrication.html";
            string path = Path.Combine(AppContext.BaseDirectory, "templates", fileName);
            try
            {
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                LogService.Write("Modèle " + fileName + " illisible, modèle intégré utilisé : " + ex.Message);
            }
            if (type == RequestType.CommandeCatalogue) return TemplateCommandeCatalogue;
            if (catalogue) return TemplateOffreCatalogue;
            return type == RequestType.Offre ? TemplateOffre : TemplateFabrication;
        }

        /// <summary>Tableau HTML des articles. Une colonne vide pour tous les articles est omise.</summary>
        private static string BuildTable(RequestType type, List<PartLine> lines, bool catalogue)
        {
            bool showQty2 = false;
            bool showQty3 = false;
            bool showDate = false;
            bool showMaterial = false;
            bool showTreatment = false;
            bool showRef = false;
            bool showOldRef = false;
            bool showFab = false;
            bool showRev = false;
            foreach (PartLine l in lines)
            {
                if (!string.IsNullOrWhiteSpace(l.ManufacturerRef)) showFab = true;
                if (!string.IsNullOrWhiteSpace(l.EffectiveRevision)) showRev = true;
                if (RequestTypes.PlusieursQuantites(type))
                {
                    if (l.Qty2 > 0) showQty2 = true;
                    if (l.Qty3 > 0) showQty3 = true;
                }
                if (!string.IsNullOrWhiteSpace(l.RealizedDate)) showDate = true;
                if (!string.IsNullOrWhiteSpace(l.Material)) showMaterial = true;
                if (!string.IsNullOrWhiteSpace(l.Treatment)) showTreatment = true;
                if (!string.IsNullOrWhiteSpace(l.SupplierRef)) showRef = true;
                if (!string.IsNullOrWhiteSpace(l.OldRef)) showOldRef = true;
            }

            // Un article de catalogue n'a pas de plan : révision, date de réalisé, matière et
            // finitions n'ont rien à dire, même si une ligne en porte encore d'un traitement
            // précédent.
            if (catalogue)
            {
                showRev = false;
                showDate = false;
                showMaterial = false;
                showTreatment = false;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("<table style=\"border-collapse:collapse;\">");

            sb.Append("<tr>");
            sb.Append("<th" + HeadStyle + ">N° article</th>");
            if (showOldRef) sb.Append("<th" + HeadStyle + ">Ancienne réf.</th>");
            sb.Append("<th" + HeadStyle + ">Désignation</th>");
            if (showRef) sb.Append("<th" + HeadStyle + ">Votre référence</th>");
            if (showFab) sb.Append("<th" + HeadStyle + ">Réf. fabricant</th>");
            // Un article de catalogue n'a pas de plan : la colonne n'a rien à porter.
            if (!catalogue && showRev) sb.Append("<th" + HeadStyle + ">Rév. plan</th>");
            if (showDate) sb.Append("<th" + HeadStyle + ">Date de réalisé</th>");
            if (showMaterial) sb.Append("<th" + HeadStyle + ">Matière</th>");
            if (showTreatment) sb.Append("<th" + HeadStyle + ">Finitions</th>");
            if (RequestTypes.PlusieursQuantites(type))
            {
                sb.Append("<th" + HeadStyle + ">Qté 1</th>");
                if (showQty2) sb.Append("<th" + HeadStyle + ">Qté 2</th>");
                if (showQty3) sb.Append("<th" + HeadStyle + ">Qté 3</th>");
            }
            else
            {
                sb.Append("<th" + HeadStyle + ">Quantité</th>");
            }
            sb.Append("<th" + HeadStyle + ">Remarque</th>");
            sb.Append("</tr>");

            foreach (PartLine l in lines)
            {
                sb.Append("<tr>");
                sb.Append("<td" + CellStyle + ">" + Enc(l.PartNumber) + "</td>");
                if (showOldRef) sb.Append("<td" + CellStyle + ">" + Dash(l.OldRef) + "</td>");
                sb.Append("<td" + CellStyle + ">" + Dash(l.Description) + "</td>");
                if (showRef) sb.Append("<td" + CellStyle + ">" + Dash(l.SupplierRef) + "</td>");
                if (showFab) sb.Append("<td" + CellStyle + ">" + Dash(l.ManufacturerRef) + "</td>");

                // La révision du plan est mise en évidence en fabrication.
                if (!catalogue && showRev)
                {
                    string rev = Dash(l.EffectiveRevision);
                    if (type == RequestType.Fabrication)
                        sb.Append("<td" + CellStyle + "><b>" + rev + "</b></td>");
                    else
                        sb.Append("<td" + CellStyle + ">" + rev + "</td>");
                }

                if (showDate) sb.Append("<td" + CellStyle + ">" + Dash(l.RealizedDate) + "</td>");
                if (showMaterial) sb.Append("<td" + CellStyle + ">" + Dash(l.Material) + "</td>");
                if (showTreatment) sb.Append("<td" + CellStyle + ">" + Dash(l.Treatment) + "</td>");

                if (RequestTypes.PlusieursQuantites(type))
                {
                    sb.Append("<td" + CellStyle + ">" + l.Qty1 + "</td>");
                    if (showQty2) sb.Append("<td" + CellStyle + ">" + (l.Qty2 > 0 ? l.Qty2.ToString() : "") + "</td>");
                    if (showQty3) sb.Append("<td" + CellStyle + ">" + (l.Qty3 > 0 ? l.Qty3.ToString() : "") + "</td>");
                }
                else
                {
                    sb.Append("<td" + CellStyle + ">" + l.Qty1 + "</td>");
                }
                sb.Append("<td" + CellStyle + ">" + Enc(l.Remark) + "</td>");
                sb.Append("</tr>");
            }

            sb.Append("</table>");
            return sb.ToString();
        }

        /// <summary>Valeur encodée, ou un tiret si le PDM ne l'a pas fournie.</summary>
        private static string Dash(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "&mdash;";
            return WebUtility.HtmlEncode(value.Trim());
        }

        /// <summary>Encode une valeur saisie par l'utilisateur pour l'insérer dans du HTML.</summary>
        private static string Enc(string value)
        {
            if (value == null) return "";
            return WebUtility.HtmlEncode(value);
        }

        // ------------------------------------------------------------------
        // Modèles intégrés : utilisés si le dossier templates est absent,
        // afin que le programme fonctionne toujours.
        // ------------------------------------------------------------------

        /// <summary>Modèle intégré de la commande catalogue, si le fichier manque.</summary>
        private const string TemplateCommandeCatalogue =
"<html><body><div style=\"font-family:Aptos, 'Segoe UI', Calibri, Arial, sans-serif; font-size:12pt; color:#222222;\">"
+ "<p>Bonjour,</p>"
+ "<p>Nous vous passons commande des {{NB_ARTICLES}} article(s) de catalogue ci-dessous.</p>"
+ "<p>Référence commande : <b>{{COMMANDE}}</b><br/>Délai souhaité : <b>{{DELAI}}</b></p>"
+ "{{TABLEAU}}{{COMMENTAIRE}}{{PO}}"
+ "<p>Merci de nous <b>confirmer la réception de cette commande</b>, les prix et "
+ "le délai de livraison.</p>"
+ "<p>Avec nos remerciements, nous vous adressons nos meilleures salutations.</p>"
+ "{{NOTES}}</div></body></html>";

        /// <summary>Modèle intégré du mode catalogue, si le fichier manque.</summary>
        private const string TemplateOffreCatalogue =
"<html><body><div style=\"font-family:Aptos, 'Segoe UI', Calibri, Arial, sans-serif; font-size:12pt; color:#222222;\">"
+ "<p>Bonjour,</p>"
+ "<p>Nous vous prions de bien vouloir nous faire parvenir votre meilleure offre pour les "
+ "{{NB_ARTICLES}} article(s) de catalogue ci-dessous.</p>"
+ "<p>Merci d'indiquer un <b>prix unitaire pour chaque palier de quantité</b>, ainsi que le "
+ "<b>délai de livraison</b> correspondant.</p>"
+ "<p>Référence commande : <b>{{COMMANDE}}</b><br/>Délai souhaité : <b>{{DELAI}}</b></p>"
+ "{{TABLEAU}}{{COMMENTAIRE}}{{PO}}"
+ "<p>Merci de nous confirmer que les références ci-dessus correspondent bien "
+ "aux articles souhaités.</p>"
+ "<p>Dans l'attente de votre retour, nous vous adressons nos meilleures salutations.</p>"
+ "{{NOTES}}</div></body></html>";

        private const string TemplateOffre =
@"<html>
<body>
<div style=""font-family:Aptos, 'Segoe UI', Calibri, Arial, sans-serif; font-size:12pt; color:#222222;"">
<p>Bonjour,</p>
<p>Nous vous prions de bien vouloir nous faire parvenir votre meilleure offre
pour les {{NB_ARTICLES}} article(s) ci-dessous.</p>
<p>Merci d'indiquer un <b>prix unitaire pour chaque palier de quantité</b>,
ainsi que le <b>délai de livraison</b> correspondant.</p>
<p>Référence commande : <b>{{COMMANDE}}</b><br/>
Délai souhaité : <b>{{DELAI}}</b></p>
{{TABLEAU}}
{{COMMENTAIRE}}
{{PO}}
<p>Les fichiers sont joints <b>regroupés par numéro d'article</b> : une archive par article, contenant le modèle 3D (STEP AP203) et le plan (PDF et DXF) lorsqu'il existe.</p>
<p>Dans l'attente de votre retour, nous vous adressons nos meilleures salutations.</p>
{{NOTES}}
</div>
</body>
</html>";

        private const string TemplateFabrication =
@"<html>
<body>
<div style=""font-family:Aptos, 'Segoe UI', Calibri, Arial, sans-serif; font-size:12pt; color:#222222;"">
<p>Bonjour,</p>
<p>Nous vous confions la fabrication des {{NB_ARTICLES}} article(s) listés ci-dessous.</p>
<p>Référence commande : <b>{{COMMANDE}}</b><br/>
Délai souhaité : <b>{{DELAI}}</b></p>
{{TABLEAU}}
{{COMMENTAIRE}}
{{PO}}
<p>Les fichiers sont joints <b>regroupés par numéro d'article</b> : une archive par article, contenant le modèle 3D (STEP AP203) et le plan (PDF et DXF) lorsqu'il existe.</p>
<p>Avec nos remerciements et nos meilleures salutations.</p>
{{NOTES}}
</div>
</body>
</html>";
    }
}
