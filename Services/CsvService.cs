using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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

        /// <summary>Importe un fichier CSV et ajoute les lignes. Retourne le nombre de lignes ajoutées.</summary>
        public static int Import(BindingList<PartLine> lines, string path)
        {
            string[] rows = File.ReadAllLines(path, Encoding.UTF8);
            int count = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(rows[i])) continue;
                List<string> f = ParseLine(rows[i]);
                string first = f.Count > 0 ? f[0].Trim() : "";

                // La première ligne est ignorée si elle correspond à l'en-tête.
                if (i == 0 && IsHeader(first)) continue;
                if (first == "") continue;

                PartLine l = new PartLine();
                l.PartNumber = first;
                // Désignation, révision, matière et finitions ne sont jamais reprises d'un fichier :
                // elles sont toujours relues dans le PDM au moment de l'export.
                l.Qty1 = ParseQty(f, 1, 1);
                l.Qty2 = ParseQty(f, 2, 0);
                l.Qty3 = ParseQty(f, 3, 0);
                if (f.Count > 4) l.Remark = f[4].Trim();
                lines.Add(l);
                count++;
            }
            return count;
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
            if (fields.Count <= index) return defaultValue;
            int value;
            if (int.TryParse(fields[index].Trim(), out value)) return value;
            return defaultValue;
        }

        private static bool IsHeader(string firstCell)
        {
            string v = firstCell.Trim().ToLowerInvariant();
            return v == "n° article" || v == "article" || v == "numéro" || v == "part";
        }
    }
}
