using System;
using System.Collections.Generic;
using System.Text;

namespace AskThem.Services
{
    /// <summary>
    /// Contrôle et mise en forme des numéros d'article.
    /// Un format est décrit par les longueurs de ses groupes, séparées par des tirets :
    /// "3-5-2" décrit XYZ-AAAAA-BB. Le premier format de la liste sert à insérer
    /// automatiquement les tirets quand l'utilisateur ne les tape pas.
    /// </summary>
    public static class PartNumberFormat
    {
        /// <summary>Découpe "3-5-2" en { 3, 5, 2 }. Retourne null si le motif est illisible.</summary>
        private static int[] ParsePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return null;
            string[] parts = pattern.Split('-');
            int[] longueurs = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i].Trim(), out n) || n <= 0) return null;
                longueurs[i] = n;
            }
            return longueurs;
        }

        private static bool GroupIsAllowed(string groupe, bool premier)
        {
            foreach (char c in groupe)
            {
                if (char.IsLetterOrDigit(c)) continue;
                // Le coffre contient des références commençant par # (ex. #21-00000-01).
                if (premier && c == '#') continue;
                return false;
            }
            return true;
        }

        /// <summary>Vrai si la valeur respecte l'un des formats acceptés.</summary>
        public static bool IsValid(string value, List<string> patterns)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (patterns == null || patterns.Count == 0) return true; // aucun format imposé
            string[] groupes = value.Trim().Split('-');
            foreach (string pattern in patterns)
            {
                int[] longueurs = ParsePattern(pattern);
                if (longueurs == null || longueurs.Length != groupes.Length) continue;
                bool ok = true;
                for (int i = 0; i < groupes.Length; i++)
                {
                    if (groupes[i].Length != longueurs[i] || !GroupIsAllowed(groupes[i], i == 0))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }
            return false;
        }

        /// <summary>
        /// Met la saisie en forme : espaces retirés, majuscules, et tirets insérés
        /// selon le format principal si l'utilisateur ne les a pas tapés.
        /// </summary>
        public static string Normalize(string input, List<string> patterns)
        {
            if (input == null) return "";
            string v = input.Trim().Replace(" ", "").ToUpperInvariant();
            if (v == "") return "";
            if (v.IndexOf('-') >= 0) return v;       // l'utilisateur a saisi les séparateurs
            if (patterns == null || patterns.Count == 0) return v;

            int[] longueurs = ParsePattern(patterns[0]);
            if (longueurs == null) return v;

            int total = 0;
            foreach (int n in longueurs) total += n;
            if (v.Length != total) return v;         // longueur inattendue : on ne devine pas

            StringBuilder sb = new StringBuilder();
            int pos = 0;
            for (int i = 0; i < longueurs.Length; i++)
            {
                if (i > 0) sb.Append('-');
                sb.Append(v.Substring(pos, longueurs[i]));
                pos += longueurs[i];
            }
            return sb.ToString();
        }

        /// <summary>Description lisible des formats acceptés, pour les messages d'erreur.</summary>
        public static string Describe(List<string> patterns)
        {
            if (patterns == null || patterns.Count == 0) return "(aucun format imposé)";
            List<string> exemples = new List<string>();
            foreach (string pattern in patterns)
            {
                int[] longueurs = ParsePattern(pattern);
                if (longueurs == null) continue;
                StringBuilder sb = new StringBuilder();
                char[] lettres = new char[] { 'X', 'A', 'B', 'C' };
                for (int i = 0; i < longueurs.Length; i++)
                {
                    if (i > 0) sb.Append('-');
                    char lettre = lettres[Math.Min(i, lettres.Length - 1)];
                    sb.Append(new string(lettre, longueurs[i]));
                }
                exemples.Add(sb.ToString());
            }
            return string.Join(", ", exemples);
        }
    }
}
