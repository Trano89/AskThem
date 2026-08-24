using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace AskThem.Services
{
    /// <summary>
    /// Lecture d'un classeur .xlsx sans bibliothèque tierce : un classeur Excel est
    /// une archive ZIP contenant du XML, que la bibliothèque standard sait déjà ouvrir.
    /// Seule la première feuille est lue, en valeurs affichées.
    /// </summary>
    public static class XlsxService
    {
        private static readonly XNamespace Main =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRel =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>
        /// Retourne les lignes de la première feuille. Chaque ligne est la liste de ses
        /// cellules, les cellules vides étant conservées pour que les colonnes
        /// restent alignées.
        /// </summary>
        public static List<List<string>> ReadFirstSheet(string path)
        {
            List<List<string>> lignes = new List<List<string>>();
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                List<string> partagees = ReadSharedStrings(zip);
                ZipArchiveEntry feuille = FindFirstSheet(zip);
                if (feuille == null)
                    throw new Exception("Ce classeur ne contient aucune feuille lisible.");

                XDocument doc;
                using (Stream flux = feuille.Open()) doc = XDocument.Load(flux);

                XElement data = doc.Root.Element(Main + "sheetData");
                if (data == null) return lignes;

                foreach (XElement row in data.Elements(Main + "row"))
                {
                    List<string> cellules = new List<string>();
                    foreach (XElement c in row.Elements(Main + "c"))
                    {
                        int index = ColumnIndex((string)c.Attribute("r"));
                        while (cellules.Count < index) cellules.Add("");
                        cellules.Add(CellValue(c, partagees));
                    }
                    lignes.Add(cellules);
                }
            }
            return lignes;
        }

        /// <summary>Table des chaînes partagées, où Excel factorise les textes répétés.</summary>
        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            List<string> textes = new List<string>();
            ZipArchiveEntry e = zip.GetEntry("xl/sharedStrings.xml");
            if (e == null) return textes;

            XDocument doc;
            using (Stream flux = e.Open()) doc = XDocument.Load(flux);

            foreach (XElement si in doc.Root.Elements(Main + "si"))
            {
                // Un texte enrichi est découpé en fragments : on les recolle.
                textes.Add(string.Concat(si.Descendants(Main + "t").Select(t => t.Value)));
            }
            return textes;
        }

        /// <summary>Première feuille du classeur, résolue par le classeur puis ses relations.</summary>
        private static ZipArchiveEntry FindFirstSheet(ZipArchive zip)
        {
            try
            {
                ZipArchiveEntry classeur = zip.GetEntry("xl/workbook.xml");
                ZipArchiveEntry relations = zip.GetEntry("xl/_rels/workbook.xml.rels");
                if (classeur != null && relations != null)
                {
                    XDocument dc, dr;
                    using (Stream f = classeur.Open()) dc = XDocument.Load(f);
                    using (Stream f = relations.Open()) dr = XDocument.Load(f);

                    XElement premiere = dc.Root.Element(Main + "sheets").Elements(Main + "sheet").FirstOrDefault();
                    if (premiere != null)
                    {
                        string id = (string)premiere.Attribute(Rel + "id");
                        XElement lien = dr.Root.Elements(PkgRel + "Relationship")
                            .FirstOrDefault(x => (string)x.Attribute("Id") == id);
                        if (lien != null)
                        {
                            string cible = ((string)lien.Attribute("Target")).TrimStart('/');
                            if (!cible.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) cible = "xl/" + cible;
                            ZipArchiveEntry trouvee = zip.GetEntry(cible);
                            if (trouvee != null) return trouvee;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Classeur inhabituel : on retombe sur la première feuille rencontrée.
            }

            return zip.Entries
                .Where(x => x.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                         && x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        /// <summary>
        /// Importe la première feuille d'un classeur et ajoute les lignes.
        /// La correspondance des colonnes est celle du CSV, pour que les deux formats
        /// se comportent exactement pareil.
        /// </summary>
        public static int Import(System.ComponentModel.BindingList<Models.PartLine> lines, string path)
        {
            return CsvService.AddRows(lines, ReadFirstSheet(path));
        }

        /// <summary>Valeur affichée d'une cellule, quel que soit son mode de stockage.</summary>
        private static string CellValue(XElement c, List<string> partagees)
        {
            string type = (string)c.Attribute("t");

            if (type == "s")
            {
                // Renvoi vers la table des chaînes partagées.
                XElement v = c.Element(Main + "v");
                int index;
                if (v != null && int.TryParse(v.Value, out index)
                    && index >= 0 && index < partagees.Count)
                {
                    return partagees[index].Trim();
                }
                return "";
            }

            if (type == "inlineStr")
            {
                XElement isEl = c.Element(Main + "is");
                if (isEl == null) return "";
                return string.Concat(isEl.Descendants(Main + "t").Select(t => t.Value)).Trim();
            }

            if (type == "str")
            {
                // Résultat textuel d'une formule.
                XElement v = c.Element(Main + "v");
                return v == null ? "" : v.Value.Trim();
            }

            if (type == "b")
            {
                XElement v = c.Element(Main + "v");
                if (v == null) return "";
                return v.Value == "1" ? "VRAI" : "FAUX";
            }

            XElement val = c.Element(Main + "v");
            return val == null ? "" : val.Value.Trim();
        }

        /// <summary>
        /// Indice de colonne déduit de la référence de cellule : A vaut 0, B vaut 1,
        /// AA vaut 26. Permet de conserver la place des cellules vides.
        /// </summary>
        private static int ColumnIndex(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return 0;
            int index = 0;
            foreach (char c in reference)
            {
                char maj = char.ToUpperInvariant(c);
                if (maj < 'A' || maj > 'Z') break;
                index = index * 26 + (maj - 'A' + 1);
            }
            return index > 0 ? index - 1 : 0;
        }
    }
}
