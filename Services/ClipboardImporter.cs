using System;
using System.ComponentModel;
using System.Windows.Forms;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>Import de lignes depuis le presse-papiers (colonnes Excel séparées par des tabulations).</summary>
    public static class ClipboardImporter
    {
        /// <summary>Ajoute les lignes du presse-papiers. Retourne le nombre de lignes ajoutées.</summary>
        public static int ImportFromClipboard(BindingList<PartLine> lines)
        {
            if (!Clipboard.ContainsText()) return 0;
            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return 0;

            string[] rows = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int count = 0;
            foreach (string row in rows)
            {
                // Tabulation (Excel), sinon point-virgule, sinon virgule.
                string[] cells = row.Split('\t');
                if (cells.Length == 1) cells = row.Split(';');
                if (cells.Length == 1) cells = row.Split(',');

                string part = cells[0].Trim();
                if (part == "") continue;

                // Ligne d'en-tête éventuelle.
                if (IsHeader(part)) continue;

                PartLine line = new PartLine();
                line.PartNumber = part;
                line.Qty1 = ParseQty(cells, 1, 1);
                line.Qty2 = ParseQty(cells, 2, 0);
                line.Qty3 = ParseQty(cells, 3, 0);
                if (cells.Length > 4) line.Remark = cells[4].Trim();
                lines.Add(line);
                count++;
            }
            return count;
        }

        private static int ParseQty(string[] cells, int index, int defaultValue)
        {
            if (cells.Length <= index) return defaultValue;
            int value;
            if (int.TryParse(cells[index].Trim(), out value)) return value;
            return defaultValue;
        }

        private static bool IsHeader(string firstCell)
        {
            string v = firstCell.Trim().ToLowerInvariant();
            return v == "n° article" || v == "article" || v == "numéro" || v == "part";
        }
    }
}
