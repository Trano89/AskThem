using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>Les quatre messages dont le texte peut être adapté.</summary>
    public enum NatureTexte
    {
        Offre,
        OffreCatalogue,
        Fabrication,
        CommandeCatalogue
    }

    /// <summary>
    /// Le texte des emails : celui d'origine, et celui que chaque utilisateur s'est fait.
    ///
    /// Le texte s'écrit en clair, sans balise : un paragraphe par bloc de lignes, une ligne
    /// vide entre deux paragraphes, **ceci** en gras, et des jetons {{…}} là où le programme
    /// insère ce qu'il calcule — le tableau, le bon de commande, l'annonce des pièces jointes.
    /// Il est converti en HTML au moment de construire le message.
    ///
    /// Les textes personnalisés sont rangés dans le profil Windows de l'utilisateur
    /// (%APPDATA%\AskThem), et non à côté de l'exécutable : ils sont propres à chacun,
    /// le suivent d'un poste à l'autre, et une mise à jour — qui remplace l'exécutable —
    /// ne les touche pas.
    /// </summary>
    public static class TextesEmail
    {
        /// <summary>Jetons insérés à leur place dans une ligne.</summary>
        public static readonly string[] JetonsEnLigne = { "{{NB_ARTICLES}}", "{{COMMANDE}}", "{{DELAI}}" };

        /// <summary>Jetons qui occupent une ligne à eux seuls : le programme y insère un bloc.</summary>
        public static readonly string[] JetonsBlocs =
            { "{{TABLEAU}}", "{{LIVRAISON}}", "{{FICHIERS}}", "{{PO}}", "{{COMMENTAIRE}}", "{{NOTES}}" };

        /// <summary>Ce que chaque jeton devient dans le message.</summary>
        public static readonly string[][] Legende =
        {
            new string[] { "{{NB_ARTICLES}}", "nombre d'articles de la demande" },
            new string[] { "{{COMMANDE}}", "référence commande saisie" },
            new string[] { "{{DELAI}}", "délai souhaité saisi" },
            new string[] { "{{TABLEAU}}", "tableau des articles (obligatoire)" },
            new string[] { "{{LIVRAISON}}", "demande du délai et des frais de livraison, si la case est cochée" },
            new string[] { "{{FICHIERS}}", "annonce des pièces jointes et du contrôle de fabrication" },
            new string[] { "{{PO}}", "mention du bon de commande ou de la demande de PO joint" },
            new string[] { "{{COMMENTAIRE}}", "commentaire général saisi" },
            new string[] { "{{NOTES}}", "encadrés importants : articles recodifiés, révision des plans" },
        };

        /// <summary>Intitulé d'une nature, tel que l'utilisateur la connaît.</summary>
        public static string Libelle(NatureTexte n)
        {
            switch (n)
            {
                case NatureTexte.OffreCatalogue: return "Demande d'offre — articles de catalogue";
                case NatureTexte.Fabrication: return "Demande de fabrication";
                case NatureTexte.CommandeCatalogue: return "Commande catalogue";
                default: return "Demande d'offre — pièces sur mesure";
            }
        }

        /// <summary>La nature du message d'une demande.</summary>
        public static NatureTexte NatureDe(RequestType type, bool catalogue)
        {
            if (type == RequestType.CommandeCatalogue) return NatureTexte.CommandeCatalogue;
            if (type == RequestType.Fabrication) return NatureTexte.Fabrication;
            return catalogue ? NatureTexte.OffreCatalogue : NatureTexte.Offre;
        }

        // ------------------------------------------------------------------ textes d'origine

        /// <summary>Le texte d'origine du programme.</summary>
        public static string Origine(NatureTexte n)
        {
            switch (n)
            {
                case NatureTexte.OffreCatalogue: return OrigineOffreCatalogue;
                case NatureTexte.Fabrication: return OrigineFabrication;
                case NatureTexte.CommandeCatalogue: return OrigineCommandeCatalogue;
                default: return OrigineOffre;
            }
        }

        private static readonly string OrigineOffre = Lignes(
            "Bonjour,",
            "",
            "Nous vous prions de bien vouloir nous faire parvenir votre meilleure offre pour les "
          + "{{NB_ARTICLES}} article(s) ci-dessous, avec un **prix unitaire pour chaque palier de quantité**.",
            "{{LIVRAISON}}",
            "",
            "Référence commande : **{{COMMANDE}}**",
            "Délai souhaité : **{{DELAI}}**",
            "",
            "{{TABLEAU}}",
            "{{COMMENTAIRE}}",
            "{{PO}}",
            "{{FICHIERS}}",
            "",
            "Dans l'attente de votre retour, nous vous adressons nos meilleures salutations.",
            "{{NOTES}}");

        private static readonly string OrigineOffreCatalogue = Lignes(
            "Bonjour,",
            "",
            "Nous vous prions de bien vouloir nous faire parvenir votre meilleure offre pour les "
          + "{{NB_ARTICLES}} article(s) de catalogue ci-dessous, avec un **prix unitaire pour chaque "
          + "palier de quantité**.",
            "{{LIVRAISON}}",
            "",
            "Référence commande : **{{COMMANDE}}**",
            "Délai souhaité : **{{DELAI}}**",
            "",
            "{{TABLEAU}}",
            "{{COMMENTAIRE}}",
            "{{PO}}",
            "",
            "Si l'une des références ci-dessus ne correspond pas à l'article attendu, nous vous "
          + "serions reconnaissants de nous le signaler.",
            "",
            "Dans l'attente de votre retour, nous vous adressons nos meilleures salutations.",
            "{{NOTES}}");

        private static readonly string OrigineFabrication = Lignes(
            "Bonjour,",
            "",
            "Nous vous confions la fabrication des {{NB_ARTICLES}} article(s) listés ci-dessous.",
            "",
            "Référence commande : **{{COMMANDE}}**",
            "Délai souhaité : **{{DELAI}}**",
            "",
            "{{TABLEAU}}",
            "{{COMMENTAIRE}}",
            "{{PO}}",
            "{{FICHIERS}}",
            "{{LIVRAISON}}",
            "",
            "Avec nos remerciements et nos meilleures salutations.",
            "{{NOTES}}");

        private static readonly string OrigineCommandeCatalogue = Lignes(
            "Bonjour,",
            "",
            "Nous vous passons commande des {{NB_ARTICLES}} article(s) de catalogue ci-dessous.",
            "",
            "Référence commande : **{{COMMANDE}}**",
            "Délai souhaité : **{{DELAI}}**",
            "",
            "{{TABLEAU}}",
            "{{COMMENTAIRE}}",
            "{{PO}}",
            "",
            "Nous attendons votre **confirmation de commande**, avec les prix appliqués.",
            "{{LIVRAISON}}",
            "",
            "Avec nos remerciements, nous vous adressons nos meilleures salutations.",
            "{{NOTES}}");

        private static string Lignes(params string[] lignes)
        {
            return string.Join("\r\n", lignes);
        }

        // ------------------------------------------------------------------ textes de l'utilisateur

        /// <summary>Le fichier des textes personnalisés de l'utilisateur courant.</summary>
        public static string Chemin()
        {
            return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                                "AskThem", "textes-email.json");
        }

        /// <summary>Les textes personnalisés, par nature. Vide si l'utilisateur n'en a aucun.</summary>
        public static Dictionary<NatureTexte, string> Personnalises()
        {
            Dictionary<NatureTexte, string> textes = new Dictionary<NatureTexte, string>();
            try
            {
                if (!File.Exists(Chemin())) return textes;
                Dictionary<string, string> lus = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(Chemin(), Encoding.UTF8));
                if (lus == null) return textes;
                foreach (KeyValuePair<string, string> kv in lus)
                {
                    NatureTexte n;
                    if (Enum.TryParse(kv.Key, out n) && !string.IsNullOrWhiteSpace(kv.Value)) textes[n] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Textes d'email personnalisés illisibles, textes d'origine utilisés : " + ex.Message);
            }
            return textes;
        }

        /// <summary>
        /// Enregistre les textes personnalisés. Un texte identique à celui d'origine n'est pas
        /// retenu : il suivra ainsi les améliorations des versions suivantes.
        /// </summary>
        public static bool Enregistrer(Dictionary<NatureTexte, string> textes, out string message)
        {
            message = "";
            try
            {
                Dictionary<string, string> aEcrire = new Dictionary<string, string>();
                foreach (KeyValuePair<NatureTexte, string> kv in textes)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                    if (Normaliser(kv.Value) == Normaliser(Origine(kv.Key))) continue;
                    aEcrire[kv.Key.ToString()] = kv.Value;
                }

                string chemin = Chemin();
                Directory.CreateDirectory(Path.GetDirectoryName(chemin));
                if (aEcrire.Count == 0)
                {
                    if (File.Exists(chemin)) File.Delete(chemin);
                    return true;
                }

                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = true;
                options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                string temporaire = chemin + ".tmp";
                File.WriteAllText(temporaire, JsonSerializer.Serialize(aEcrire, options), new UTF8Encoding(false));
                File.Move(temporaire, chemin, true);
                return true;
            }
            catch (Exception ex)
            {
                message = "Textes non enregistrés : " + ex.Message;
                LogService.Write(message);
                return false;
            }
        }

        private static string Normaliser(string texte)
        {
            return (texte ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }

        /// <summary>
        /// Le texte qui servira : celui de l'utilisateur s'il est utilisable, sinon celui
        /// d'origine.
        /// </summary>
        public static string Courant(NatureTexte n)
        {
            string perso;
            if (Personnalises().TryGetValue(n, out perso))
            {
                string manque = Probleme(perso);
                if (manque == "") return perso;
                LogService.Write("Texte personnalisé « " + Libelle(n) + " » inutilisable (" + manque
                               + ") : texte d'origine utilisé.");
            }
            return Origine(n);
        }

        /// <summary>
        /// Ce qui rend un texte inutilisable, ou "" s'il convient. Sans le tableau, le
        /// fournisseur ne saurait pas ce qu'on lui demande.
        /// </summary>
        public static string Probleme(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return "le texte est vide";
            if (texte.IndexOf("{{TABLEAU}}", StringComparison.Ordinal) < 0) return "il manque {{TABLEAU}}";
            return "";
        }

        /// <summary>Les jetons de bloc absents du texte : ce qu'ils auraient inséré n'apparaîtra pas.</summary>
        public static List<string> BlocsAbsents(string texte, NatureTexte n)
        {
            List<string> absents = new List<string>();
            foreach (string j in JetonsBlocs)
            {
                if (j == "{{TABLEAU}}") continue;
                // Un achat catalogue n'annonce aucun fichier.
                if (j == "{{FICHIERS}}" && (n == NatureTexte.OffreCatalogue || n == NatureTexte.CommandeCatalogue)) continue;
                if ((texte ?? "").IndexOf(j, StringComparison.Ordinal) < 0) absents.Add(j);
            }
            return absents;
        }

        // ------------------------------------------------------------------ conversion

        private static readonly Regex Gras = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);

        /// <summary>
        /// Le texte en HTML : paragraphes, retours à la ligne, gras ; les jetons restent en
        /// place, pour être remplacés ensuite.
        /// </summary>
        public static string EnHtml(string texte)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<html><body><div style=\"font-family:Aptos, 'Segoe UI', Calibri, Arial, sans-serif; "
                    + "font-size:12pt; color:#222222;\">");

            List<string> paragraphe = new List<string>();
            foreach (string brute in (texte ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
            {
                string ligne = brute.TrimEnd();
                if (ligne.Trim() == "")
                {
                    Fermer(sb, paragraphe);
                    continue;
                }
                if (EstBloc(ligne.Trim()))
                {
                    Fermer(sb, paragraphe);
                    sb.Append(ligne.Trim());
                    continue;
                }
                paragraphe.Add(ligne);
            }
            Fermer(sb, paragraphe);

            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        private static bool EstBloc(string ligne)
        {
            foreach (string j in JetonsBlocs)
                if (ligne == j) return true;
            return false;
        }

        private static void Fermer(StringBuilder sb, List<string> paragraphe)
        {
            if (paragraphe.Count == 0) return;
            List<string> lignes = new List<string>();
            foreach (string l in paragraphe)
                lignes.Add(Gras.Replace(WebUtility.HtmlEncode(l), "<b>$1</b>"));
            sb.Append("<p>").Append(string.Join("<br/>", lignes)).Append("</p>");
            paragraphe.Clear();
        }
    }
}
