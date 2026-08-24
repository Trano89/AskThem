using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Text;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>Import et export CSV (séparateur point-virgule, UTF-8 avec BOM pour Excel).</summary>
    public static class CsvService
    {
        private const char Separator = ';';

        /// <summary>Colonnes que l'utilisateur saisit lui-même : ce sont les seules importées ou exportées.</summary>
        private const string Header = "N° article;Qté 1;Qté 2;Qté 3;Remarque";

        /// <summary>Exporte la liste de saisie dans un fichier CSV.</summary>
        public static void Export(BindingList<PartLine> lines, string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (PartLine l in lines)
            {
                sb.AppendLine(string.Join(Separator.ToString(), new string[] {
                    Escape(l.PartNumber), l.Qty1.ToString(), l.Qty2.ToString(),
                    l.Qty3.ToString(), Escape(l.Remark) }));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>Importe un fichier CSV et ajoute les lignes. Retourne le nombre ajouté.</summary>
        public static int Import(BindingList<PartLine> lines, string path, out int regroupees)
        {
            string[] rows = File.ReadAllLines(path, Encoding.UTF8);
            List<List<string>> cellules = new List<List<string>>();
            foreach (string row in rows)
            {
                if (string.IsNullOrWhiteSpace(row)) continue;
                cellules.Add(ParseLine(row));
            }
            return AddRows(lines, cellules, out regroupees);
        }

        /// <summary>Colonnes retenues, déduites d'une ligne d'en-tête ou, à défaut, de leur position.</summary>
        private class ColumnMap
        {
            public int Part = 0;
            public int Qty1 = 1;
            public int Qty2 = 2;
            public int Qty3 = 3;
            public int Remark = 4;
            public int HeaderRow = -1;   // -1 : aucun en-tête trouvé
        }

        // Intitulés reconnus, accents et casse indifférents.
        private static readonly string[] EntetesArticle = {
            "code article", "n article", "no article", "numero article", "numero",
            "article", "reference", "ref", "part number", "part" };
        private static readonly string[][] EntetesQte1 = {
            new string[] { "qte 1", "quantite 1" },
            new string[] { "qte totale", "quantite totale" },
            new string[] { "quantite", "qte", "qty", "quantity" },
            new string[] { "qte ligne", "quantite ligne" } };
        private static readonly string[] EntetesQte2 = { "qte 2", "quantite 2" };
        private static readonly string[] EntetesQte3 = { "qte 3", "quantite 3" };
        private static readonly string[] EntetesRemarque = { "remarque", "note", "commentaire", "observation" };

        /// <summary>Minuscules, sans accent, sans ponctuation de fin : pour comparer des intitulés.</summary>
        private static string Normalise(string valeur)
        {
            if (valeur == null) return "";
            string v = valeur.Trim().ToLowerInvariant().TrimEnd(':', '.', '?', ' ');
            StringBuilder sb = new StringBuilder();
            foreach (char c in v.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c == '°' ? ' ' : c);
            }
            while (sb.ToString().Contains("  ")) sb.Replace("  ", " ");
            return sb.ToString().Trim();
        }

        private static int IndexOfHeader(List<string> ligne, string[] intitules)
        {
            for (int j = 0; j < ligne.Count; j++)
            {
                string v = Normalise(ligne[j]);
                foreach (string i in intitules)
                {
                    if (v == i) return j;
                }
            }
            return -1;
        }

        /// <summary>
        /// Cherche une ligne d'en-tête dans les premières lignes du fichier. Les exports
        /// de nomenclature placent souvent un titre avant les intitulés, et le code
        /// article n'est pas forcément en première colonne.
        /// </summary>
        private static ColumnMap DetectColumns(List<List<string>> rows)
        {
            ColumnMap map = new ColumnMap();
            int limite = Math.Min(rows.Count, 20);
            for (int i = 0; i < limite; i++)
            {
                int article = IndexOfHeader(rows[i], EntetesArticle);
                if (article < 0) continue;

                map.HeaderRow = i;
                map.Part = article;
                map.Qty1 = -1;
                foreach (string[] groupe in EntetesQte1)
                {
                    int j = IndexOfHeader(rows[i], groupe);
                    if (j >= 0) { map.Qty1 = j; break; }
                }
                map.Qty2 = IndexOfHeader(rows[i], EntetesQte2);
                map.Qty3 = IndexOfHeader(rows[i], EntetesQte3);
                map.Remark = IndexOfHeader(rows[i], EntetesRemarque);
                return map;
            }
            return map;
        }

        /// <summary>
        /// Ajoute les lignes en appliquant la correspondance des colonnes, puis regroupe
        /// les articles répétés en additionnant leurs quantités : une nomenclature cite le
        /// même article à plusieurs endroits de l'assemblage, et une demande fournisseur
        /// doit porter une seule ligne avec le total.
        /// Retourne le nombre de lignes ajoutées ; regroupees indique combien ont été fusionnées.
        /// </summary>
        public static int AddRows(BindingList<PartLine> lines, List<List<string>> rows, out int regroupees)
        {
            regroupees = 0;
            ColumnMap map = DetectColumns(rows);
            int depart = map.HeaderRow + 1;

            // Index des articles déjà présents, pour additionner au lieu de dupliquer.
            Dictionary<string, PartLine> connus = new Dictionary<string, PartLine>(StringComparer.OrdinalIgnoreCase);
            foreach (PartLine existante in lines)
            {
                if (!string.IsNullOrWhiteSpace(existante.PartNumber) && !connus.ContainsKey(existante.PartNumber))
                    connus[existante.PartNumber] = existante;
            }

            int count = 0;
            for (int i = depart; i < rows.Count; i++)
            {
                List<string> f = rows[i];
                string article = Cell(f, map.Part);
                if (article == "") continue;

                // Sans en-tête, la première ligne peut être un intitulé.
                if (map.HeaderRow < 0 && i == 0 && IsHeader(article)) continue;

                int q1 = ParseQty(f, map.Qty1, 1);
                int q2 = ParseQty(f, map.Qty2, 0);
                int q3 = ParseQty(f, map.Qty3, 0);
                string remarque = Cell(f, map.Remark);

                PartLine deja;
                if (connus.TryGetValue(article, out deja))
                {
                    deja.Qty1 += q1;
                    deja.Qty2 += q2;
                    deja.Qty3 += q3;
                    if (remarque != "" && deja.Remark.IndexOf(remarque, StringComparison.OrdinalIgnoreCase) < 0)
                        deja.Remark = deja.Remark == "" ? remarque : deja.Remark + " ; " + remarque;
                    regroupees++;
                    continue;
                }

                PartLine l = new PartLine();
                l.PartNumber = article;
                l.Qty1 = q1;
                l.Qty2 = q2;
                l.Qty3 = q3;
                l.Remark = remarque;
                lines.Add(l);
                connus[article] = l;
                count++;
            }
            return count;
        }

        /// <summary>Valeur d'une colonne, ou vide si la colonne n'existe pas dans ce fichier.</summary>
        private static string Cell(List<string> ligne, int index)
        {
            if (index < 0 || index >= ligne.Count) return "";
            return ligne[index] == null ? "" : ligne[index].Trim();
        }

        /// <summary>Entoure de guillemets et double les guillemets internes si nécessaire.</summary>
        private static string Escape(string value)
        {
            if (value == null) return "";
            if (value.IndexOf('"') >= 0 || value.IndexOf(Separator) >= 0 ||
                value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        /// <summary>Découpe une ligne CSV en tenant compte des champs entre guillemets.</summary>
        private static List<string> ParseLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else current.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == Separator) { fields.Add(current.ToString()); current.Length = 0; }
                    else current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields;
        }

        private static int ParseQty(List<string> fields, int index, int defaultValue)
        {
            if (index < 0 || index >= fields.Count) return defaultValue;
            string brut = fields[index] == null ? "" : fields[index].Trim();
            if (brut == "") return defaultValue;

            int value;
            if (int.TryParse(brut, out value)) return value;

            // Excel écrit volontiers les entiers sous forme décimale : 17 devient 17.0.
            double d;
            if (double.TryParse(brut, NumberStyles.Any, CultureInfo.InvariantCulture, out d)
                || double.TryParse(brut, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
            {
                return (int)Math.Round(d);
            }
            return defaultValue;
        }

        private static bool IsHeader(string firstCell)
        {
            string v = firstCell.Trim().ToLowerInvariant();
            return v == "n° article" || v == "article" || v == "numéro" || v == "part";
        }
    }
}
