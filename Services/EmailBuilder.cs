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
        private const string CellStyle = " style=\"border:1px solid #999; padding:4px 8px;\"";
        private const string HeadStyle = " style=\"border:1px solid #999; padding:4px 8px; background:#eef2f6; font-weight:bold;\"";

        /// <summary>Objet du message.</summary>
        public static string BuildSubject(RequestType type, string project, int count)
        {
            string prefix = "Demande de fabrication";
            if (type == RequestType.Offre) prefix = "Demande d'offre";

            // Sans référence projet, on retire le segment et son tiret.
            if (string.IsNullOrWhiteSpace(project))
                return prefix + " - " + count + " article(s)";
            return prefix + " - " + project.Trim() + " - " + count + " article(s)";
        }

        /// <summary>Corps HTML du message.</summary>
        public static string BuildBody(RequestType type, List<PartLine> lines, string project,
                                      string deadline, string conditions, string poFileName)
        {
            string html = LoadTemplate(type);
            string projectText = string.IsNullOrWhiteSpace(project) ? "-" : project.Trim();
            string deadlineText = string.IsNullOrWhiteSpace(deadline) ? "non précisé" : deadline.Trim();

            html = html.Replace("{{NB_ARTICLES}}", lines.Count.ToString());
            html = html.Replace("{{PROJET}}", WebUtility.HtmlEncode(projectText));
            html = html.Replace("{{DELAI}}", WebUtility.HtmlEncode(deadlineText));
            html = html.Replace("{{TABLEAU}}", BuildTable(type, lines));
            html = html.Replace("{{CONDITIONS}}", BuildConditions(conditions));
            html = html.Replace("{{PO}}", BuildPo(poFileName));
            return html;
        }

        /// <summary>Mention du bon de commande joint. Vide si aucun n'accompagne la demande.</summary>
        private static string BuildPo(string poFileName)
        {
            if (string.IsNullOrWhiteSpace(poFileName)) return "";
            return "<p>Notre <b>bon de commande</b> est joint à ce message : "
                 + WebUtility.HtmlEncode(poFileName.Trim()) + "</p>";
        }

        /// <summary>
        /// Bloc de conditions générales ajouté en fin de message. Vide si rien n'a été saisi.
        /// Les retours à la ligne saisis par l'utilisateur sont conservés.
        /// </summary>
        private static string BuildConditions(string conditions)
        {
            if (string.IsNullOrWhiteSpace(conditions)) return "";
            string texte = WebUtility.HtmlEncode(conditions.Trim());
            texte = texte.Replace("\r\n", "<br/>").Replace("\n", "<br/>");
            return "<div style=\"margin-top:20px; padding-top:12px; border-top:1px solid #cccccc; "
                 + "font-size:13px; color:#333333;\">" + texte + "</div>";
        }

        /// <summary>Charge le modèle HTML ; si le fichier est absent, utilise le modèle intégré.</summary>
        private static string LoadTemplate(RequestType type)
        {
            string fileName = type == RequestType.Offre ? "template_offre.html" : "template_fabrication.html";
            string path = Path.Combine(AppContext.BaseDirectory, "templates", fileName);
            try
            {
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                LogService.Write("Modèle " + fileName + " illisible, modèle intégré utilisé : " + ex.Message);
            }
            return type == RequestType.Offre ? TemplateOffre : TemplateFabrication;
        }

        /// <summary>Tableau HTML des articles. Une colonne vide pour tous les articles est omise.</summary>
        private static string BuildTable(RequestType type, List<PartLine> lines)
        {
            bool showQty2 = false;
            bool showQty3 = false;
            bool showDate = false;
            bool showMaterial = false;
            bool showTreatment = false;
            foreach (PartLine l in lines)
            {
                if (type == RequestType.Offre)
                {
                    if (l.Qty2 > 0) showQty2 = true;
                    if (l.Qty3 > 0) showQty3 = true;
                }
                if (!string.IsNullOrWhiteSpace(l.RealizedDate)) showDate = true;
                if (!string.IsNullOrWhiteSpace(l.Material)) showMaterial = true;
                if (!string.IsNullOrWhiteSpace(l.Treatment)) showTreatment = true;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("<table style=\"border-collapse:collapse; font-size:13px;\">");

            sb.Append("<tr>");
            sb.Append("<th" + HeadStyle + ">N° article</th>");
            sb.Append("<th" + HeadStyle + ">Désignation</th>");
            sb.Append("<th" + HeadStyle + ">Rév. plan</th>");
            if (showDate) sb.Append("<th" + HeadStyle + ">Date de réalisé</th>");
            if (showMaterial) sb.Append("<th" + HeadStyle + ">Matière</th>");
            if (showTreatment) sb.Append("<th" + HeadStyle + ">Finitions</th>");
            if (type == RequestType.Offre)
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
                sb.Append("<td" + CellStyle + ">" + Dash(l.Description) + "</td>");

                // La révision du plan est mise en évidence en fabrication.
                string rev = Dash(l.EffectiveRevision);
                if (type == RequestType.Fabrication)
                    sb.Append("<td" + CellStyle + "><b>" + rev + "</b></td>");
                else
                    sb.Append("<td" + CellStyle + ">" + rev + "</td>");

                if (showDate) sb.Append("<td" + CellStyle + ">" + Dash(l.RealizedDate) + "</td>");
                if (showMaterial) sb.Append("<td" + CellStyle + ">" + Dash(l.Material) + "</td>");
                if (showTreatment) sb.Append("<td" + CellStyle + ">" + Dash(l.Treatment) + "</td>");

                if (type == RequestType.Offre)
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

        private const string TemplateOffre =
@"<html>
<body style=""font-family:Segoe UI, Arial, sans-serif; font-size:14px; color:#222;"">
<p>Bonjour,</p>
<p>Nous vous prions de bien vouloir nous faire parvenir votre meilleure offre
pour les {{NB_ARTICLES}} article(s) ci-dessous.</p>
<p>Merci d'indiquer un <b>prix unitaire pour chaque palier de quantité</b>,
ainsi que le <b>délai de livraison</b> correspondant.</p>
<p>Référence projet : <b>{{PROJET}}</b><br/>
Délai souhaité : <b>{{DELAI}}</b></p>
{{TABLEAU}}
{{CONDITIONS}}
<p>Les fichiers sont joints <b>regroupés par numéro d'article</b> : une archive par article, contenant le modèle 3D (STEP AP203) et le plan (PDF et DXF) lorsqu'il existe.</p>
<p>Dans l'attente de votre retour, nous vous adressons nos meilleures salutations.</p>
</body>
</html>";

        private const string TemplateFabrication =
@"<html>
<body style=""font-family:Segoe UI, Arial, sans-serif; font-size:14px; color:#222;"">
<p>Bonjour,</p>
<p>Nous vous confions la fabrication des {{NB_ARTICLES}} article(s) listés ci-dessous.</p>
<p>Référence projet : <b>{{PROJET}}</b><br/>
Délai souhaité : <b>{{DELAI}}</b></p>
{{TABLEAU}}
<div style=""border-left:4px solid #c00; background:#fff4f4; padding:10px 14px; margin:18px 0;"">
<b>IMPORTANT — Révision des plans</b><br/>
Merci de contrôler impérativement la <b>révision indiquée dans le cartouche de chaque plan</b>
et de la comparer à celle du tableau ci-dessus. La fabrication doit être réalisée
<b>exclusivement selon la révision indiquée</b>. Nous vous prions de nous
<b>confirmer par retour de message la révision sur laquelle vous travaillez</b>,
afin de garantir que les dernières mises à jour sont bien prises en compte.
</div>
{{CONDITIONS}}
{{PO}}
<p>Les fichiers sont joints <b>regroupés par numéro d'article</b> : une archive par article, contenant le modèle 3D (STEP AP203) et le plan (PDF et DXF) lorsqu'il existe.</p>
<p>Avec nos remerciements et nos meilleures salutations.</p>
</body>
</html>";
    }
}
