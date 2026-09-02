using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AskThem.Services
{
    /// <summary>
    /// Rapproche un nom de fournisseur saisi dans AskThem d'une fiche de l'inventaire.
    ///
    /// Ce rapprochement ne sert qu'une fois, au moment de lier les deux : ensuite, c'est
    /// l'identifiant numérique de l'inventaire qui fait foi. Comparer des noms à chaque
    /// demande serait fragile ; les comparer une fois, sous le contrôle de l'utilisateur,
    /// ne l'est pas.
    /// </summary>
    public static class NomFournisseur
    {
        /// <summary>
        /// Formes juridiques et mentions géographiques retirées en fin de nom. « Thorlabs
        /// GmbH » et « Thorlabs » désignent la même maison ; « Thorlabs Optique » non.
        /// </summary>
        private static readonly string[] Suffixes = {
            "ag", "sa", "gmbh", "sarl", "srl", "ltd", "ltda", "limited", "inc", "llc",
            "bv", "nv", "spa", "oy", "ab", "as", "kg", "co", "corp", "corporation",
            "company", "group", "europe", "international", "sas", "plc", "gbr", "ohg",
            "aps", "holding", "kgaa", "cie", "sagl", "gmbh&co", "eu"
        };

        /// <summary>
        /// Clé de comparaison : sans accent, sans casse, sans ponctuation, et débarrassée
        /// des formes juridiques finales.
        /// </summary>
        public static string Cle(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return "";

            string sansAccent = nom.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char c in sansAccent)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }

            List<string> mots = new List<string>(
                sb.ToString().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            while (mots.Count > 1 && EstUnSuffixe(mots[mots.Count - 1]))
                mots.RemoveAt(mots.Count - 1);

            return string.Join(" ", mots);
        }

        private static bool EstUnSuffixe(string mot)
        {
            foreach (string s in Suffixes)
                if (s == mot) return true;
            return false;
        }

        /// <summary>
        /// Fiches d'inventaire dont le nom correspond, la plus proche en tête.
        ///
        /// Retourne la liste entière et non un choix : quand deux fiches se confondent —
        /// l'inventaire en contient, « Idex Health &amp; Science » et « Idex Health &amp;
        /// Science, LLC » — c'est à l'utilisateur de trancher, jamais au programme.
        /// </summary>
        public static List<KeyValuePair<int, string>> Candidats(
            string nomCherche, IEnumerable<KeyValuePair<int, string>> fiches)
        {
            List<KeyValuePair<int, string>> exacts = new List<KeyValuePair<int, string>>();
            List<KeyValuePair<int, string>> normalises = new List<KeyValuePair<int, string>>();
            List<KeyValuePair<int, string>> partiels = new List<KeyValuePair<int, string>>();
            if (fiches == null) return exacts;

            string cle = Cle(nomCherche);
            string brut = (nomCherche == null ? "" : nomCherche.Trim());

            foreach (KeyValuePair<int, string> fiche in fiches)
            {
                if (string.Equals(fiche.Value, brut, StringComparison.OrdinalIgnoreCase))
                {
                    exacts.Add(fiche);
                    continue;
                }
                string cleFiche = Cle(fiche.Value);
                if (cleFiche == "" || cle == "") continue;

                if (cleFiche == cle) normalises.Add(fiche);
                else if (cleFiche.StartsWith(cle + " ", StringComparison.Ordinal)
                      || cle.StartsWith(cleFiche + " ", StringComparison.Ordinal)) partiels.Add(fiche);
            }

            exacts.AddRange(normalises);
            exacts.AddRange(partiels);
            return exacts;
        }
    }
}
