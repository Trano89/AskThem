using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AskThem.Services
{
    /// <summary>
    /// Lecture des quantités et des fichiers tabulaires tels qu'ils arrivent sur un poste
    /// suisse romand.
    ///
    /// L'apostrophe y sépare les milliers (1'000), la virgule ou le point les décimales
    /// (2,00 ou 2.00). Un analyseur « invariant » lisait 2,00 comme deux cents, et un
    /// analyseur strict renvoyait 1 pour 1'000 : dans les deux cas la commande partait
    /// avec une quantité fausse, sans un mot.
    /// </summary>
    public static class Quantite
    {
        /// <summary>La quantité lue, arrondie à l'entier, ou la valeur par défaut si illisible.</summary>
        public static int Lire(string brut, int defaut)
        {
            if (string.IsNullOrWhiteSpace(brut)) return defaut;

            StringBuilder sb = new StringBuilder();
            foreach (char c in brut.Trim())
            {
                // Séparateurs de milliers : apostrophes et espaces, insécables compris.
                if (c == '\'' || c == '’' || c == ' ' || c == ' ' || c == ' ') continue;
                sb.Append(c);
            }
            string t = sb.ToString();
            if (t == "") return defaut;

            int entier;
            if (int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out entier))
                return entier;

            // Le dernier séparateur est la virgule décimale ; un autre, avant lui, sépare les
            // milliers (1.000,5 ou 1,000.5).
            int dernier = Math.Max(t.LastIndexOf(','), t.LastIndexOf('.'));
            if (dernier < 0) return defaut;
            string partieEntiere = t.Substring(0, dernier).Replace(",", "").Replace(".", "");
            string normalise = partieEntiere + "." + t.Substring(dernier + 1);

            double d;
            if (double.TryParse(normalise, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out d))
                return (int)Math.Round(d, MidpointRounding.AwayFromZero);
            return defaut;
        }

        /// <summary>
        /// Ouvre un fichier en lecture en laissant les autres programmes l'utiliser.
        ///
        /// Excel garde ouverts les classeurs qu'il affiche : une lecture exclusive échouait
        /// sur un fichier que l'utilisateur avait encore sous les yeux, ou sur l'export de
        /// l'inventaire ouvert par un collègue.
        /// </summary>
        public static FileStream OuvrirPartage(string chemin)
        {
            return new FileStream(chemin, FileMode.Open, FileAccess.Read,
                                  FileShare.ReadWrite | FileShare.Delete);
        }

        /// <summary>
        /// Les lignes d'un fichier texte, dans son encodage réel.
        ///
        /// Excel enregistre ses CSV en Windows-1252 : lus comme UTF-8, « Quantité » devenait
        /// illisible, la colonne n'était pas reconnue, et toutes les quantités valaient 1.
        /// </summary>
        public static string[] LireLignes(string chemin)
        {
            byte[] octets;
            using (FileStream fs = OuvrirPartage(chemin))
            using (MemoryStream ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                octets = ms.ToArray();
            }
            string texte = Decoder(octets);
            return texte.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private static string Decoder(byte[] o)
        {
            if (o.Length >= 3 && o[0] == 0xEF && o[1] == 0xBB && o[2] == 0xBF)
                return new UTF8Encoding(false).GetString(o, 3, o.Length - 3);
            if (o.Length >= 2 && o[0] == 0xFF && o[1] == 0xFE)
                return Encoding.Unicode.GetString(o, 2, o.Length - 2);
            if (o.Length >= 2 && o[0] == 0xFE && o[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(o, 2, o.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(o);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    return Encoding.GetEncoding(1252).GetString(o);
                }
                catch (Exception)
                {
                    return Encoding.Latin1.GetString(o);
                }
            }
        }
    }
}
