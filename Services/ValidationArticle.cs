using System;
using System.Collections.Generic;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>
    /// Ce qu'un numéro d'article vaut : son format, son type, et le droit d'en faire une
    /// demande au destinataire choisi.
    ///
    /// Une seule implémentation, partagée par la vue complète et le mode guidé. Les avoir
    /// écrites deux fois avait laissé le mode guidé accepter des assemblages et des types
    /// non déclarés, que la vue complète refusait.
    /// </summary>
    public static class ValidationArticle
    {
        /// <summary>
        /// Règle attachée à ce numéro. Un type non déclaré dans config.json ne donne droit
        /// à rien : mieux vaut refuser que livrer au hasard.
        /// </summary>
        public static ArticleTypeRule RegleDe(AppConfig config, string numero)
        {
            string type = PartNumberFormat.TypeCode(numero);
            ArticleTypeRule regle;
            if (type != "" && config != null && config.ArticleTypes != null
                && config.ArticleTypes.TryGetValue(type, out regle))
                return regle;

            return ArticleTypeRule.Create(
                type == "" ? "type indéterminé" : "type " + type + " non déclaré",
                false, false, false, false, false);
        }

        /// <summary>Vrai si cet article s'achète au catalogue, sans plan ni modèle.</summary>
        public static bool EstCatalogue(AppConfig config, string numero)
        {
            return RegleDe(config, numero).Catalogue;
        }

        /// <summary>
        /// Ce qui empêche de retenir ce numéro, ou null s'il est recevable.
        ///
        /// Le contrôle du fournisseur ne s'applique qu'aux achats catalogue, et seulement
        /// quand un destinataire est choisi et l'inventaire chargé : sans quoi il n'y a rien
        /// à comparer et la ligne passe, l'avertissement d'avant envoi restant en filet.
        /// </summary>
        public static string Verifier(AppConfig config, string numeroNormalise,
                                      Supplier destinataire,
                                      Dictionary<string, InventoryService.Entry> inventaire)
        {
            if (string.IsNullOrWhiteSpace(numeroNormalise)) return null;

            if (!PartNumberFormat.IsValid(numeroNormalise, config.PartNumberPatterns))
            {
                return "Numéro d'article refusé : " + numeroNormalise + Environment.NewLine + Environment.NewLine
                     + "Formats acceptés : " + PartNumberFormat.Describe(config.PartNumberPatterns)
                     + Environment.NewLine
                     + "Les tirets sont ajoutés automatiquement : vous pouvez taper le numéro sans séparateur.";
            }

            ArticleTypeRule regle = RegleDe(config, numeroNormalise);
            if (!regle.Allowed)
            {
                return numeroNormalise + " est un " + regle.Label.ToLowerInvariant() + "."
                     + Environment.NewLine
                     + "Aucune demande n'est possible sur ce type d'article." + Environment.NewLine
                     + "Saisissez les pièces qui le composent.";
            }

            if (!regle.Catalogue) return null;
            if (inventaire == null || inventaire.Count == 0) return null;
            if (destinataire == null || string.IsNullOrWhiteSpace(destinataire.Name)) return null;

            InventoryService.Entry inv = InventoryService.Lookup(inventaire, numeroNormalise);
            if (inv == null || inv.Fournisseurs.Count == 0) return null;   // signalé avant l'envoi
            if (inv.Chez(destinataire.InventoryId, destinataire.Name) != null) return null;

            return numeroNormalise + " n'est pas vendu par « " + destinataire.Name + " »."
                 + Environment.NewLine + Environment.NewLine
                 + "Dans l'inventaire, cet article est déclaré chez : " + Fournisseurs(inv) + "."
                 + Environment.NewLine + Environment.NewLine
                 + "Choisissez ce destinataire, ou retirez cet article de la demande.";
        }

        /// <summary>Les fournisseurs déclarés pour cet article, en clair.</summary>
        public static string Fournisseurs(InventoryService.Entry inv)
        {
            if (inv == null) return "aucun";
            List<string> noms = new List<string>();
            foreach (InventoryService.Fournisseur f in inv.Fournisseurs)
                if (f.Nom != "") noms.Add(f.Nom);
            return noms.Count == 0 ? "aucun" : string.Join(", ", noms);
        }
    }
}
