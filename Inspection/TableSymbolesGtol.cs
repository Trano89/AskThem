using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AskThem.Services;

namespace AskThem.Inspection
{
    /// <summary>Un symbole de tolérance géométrique, tel qu'il apparaîtra dans le contrôle.</summary>
    public sealed class SymboleGtol
    {
        /// <summary>Numérotation interne LyncéeTec, reprise dans la table de pied de page.</summary>
        public int NumeroTfp { get; set; }

        /// <summary>Caractère Unicode du symbole.</summary>
        public string Unicode { get; set; }

        public string LibelleFr { get; set; }
        public string LibelleEn { get; set; }

        public SymboleGtol(int tfp, string unicode, string fr, string en)
        {
            NumeroTfp = tfp;
            Unicode = unicode;
            LibelleFr = fr;
            LibelleEn = en;
        }
    }

    /// <summary>
    /// Traduit les codes que SolidWorks rend dans les tolérances géométriques, du type
    /// &lt;IGTOL-PERP&gt;, en symbole et libellé bilingue.
    ///
    /// Les codes ne sont pas devinés : ils viennent du fichier gtol.sym de l'installation,
    /// lu au démarrage. Les bibliothèques GTOL (ANSI) et IGTOL (ISO) portent les mêmes noms
    /// de symboles, la table est donc indexée sur le nom seul, sans son préfixe.
    /// </summary>
    public static class TableSymbolesGtol
    {
        /// <summary>Symboles reconnus, indexés par nom court : PERP, FLAT, SYMMETRY...</summary>
        private static readonly Dictionary<string, SymboleGtol> Symboles =
            new Dictionary<string, SymboleGtol>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Modificateurs : diamètre, maxi de matière, etc.</summary>
        private static readonly Dictionary<string, string> Modificateurs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Noms relevés dans gtol.sym, pour vérifier que la table les couvre tous.</summary>
        private static readonly List<string> NomsDuFichier = new List<string>();

        private static bool _charge;
        private static string _fichierLu = "";

        /// <summary>Chemin du gtol.sym effectivement utilisé, vide si aucun n'a été trouvé.</summary>
        public static string FichierLu { get { return _fichierLu; } }

        static TableSymbolesGtol()
        {
            // Numérotation TF&P LyncéeTec : elle est fixe, seuls les codes ont été vérifiés
            // contre gtol.sym. Les noms courts ci-dessous sont ceux du fichier réel.
            Ajouter(1, "STRAIGHT", "⏤", "Rectitude", "Straightness");
            Ajouter(2, "FLAT", "⏥", "Planéité", "Flatness");
            Ajouter(3, "CIRC", "○", "Circularité", "Circularity");
            Ajouter(4, "CYL", "⌭", "Cylindricité", "Cylindricity");
            Ajouter(5, "LPROF", "⌒", "Profil d'une droite", "Profile of a line");
            Ajouter(6, "SPROF", "⌓", "Profil d'une surface", "Profile of a surface");
            Ajouter(7, "PARA", "∥", "Parallélisme", "Parallelism");
            Ajouter(8, "PERP", "⊥", "Perpendicularité", "Perpendicularity");
            Ajouter(9, "ANGULAR", "∠", "Inclinaison", "Angularity");
            Ajouter(10, "POSI", "⌖", "Position", "Position");
            Ajouter(11, "CONC", "◎", "Concentricité et coaxialité", "Concentricity");
            Ajouter(12, "SYMMETRY", "≡", "Symétrie", "Symmetry");
            Ajouter(13, "SRUN", "↗", "Oscillation circulaire", "Circular run-out");
            Ajouter(14, "TRUN", "⌰", "Oscillation totale", "Total run-out");

            // Variantes « ouvertes » de la bibliothèque ANSI, mêmes exigences.
            Ajouter(13, "SORUN", "↗", "Oscillation circulaire", "Circular run-out");
            Ajouter(14, "TORUN", "⌰", "Oscillation totale", "Total run-out");

            Modificateurs["DIAM"] = "Ø";
            Modificateurs["SPHDIA"] = "SØ";
            Modificateurs["MMC"] = "Ⓜ";
            Modificateurs["LMC"] = "Ⓛ";
            Modificateurs["FMC"] = "Ⓢ";
            Modificateurs["PTZ"] = "Ⓟ";
            Modificateurs["FREES"] = "Ⓕ";
            Modificateurs["TANP"] = "Ⓣ";
            Modificateurs["DEG"] = "°";
            Modificateurs["PM"] = "±";
            Modificateurs["BOX"] = "□";
            Modificateurs["CF"] = "CF";

            // Qualificatifs des bibliothèques GTOL/IGTOL : ils accompagnent un symbole
            // au lieu d'en être un, et n'ont donc pas de numéro TF&P.
            Modificateurs["MAX"] = "max";
            Modificateurs["BETW"] = "↔";
            Modificateurs["BETW2"] = "↔";
            Modificateurs["FROMTO"] = "↔";
            Modificateurs["DPT"] = "dyn";
        }

        private static void Ajouter(int tfp, string nom, string unicode, string fr, string en)
        {
            Symboles[nom] = new SymboleGtol(tfp, unicode, fr, en);
        }

        /// <summary>
        /// Relit gtol.sym pour contrôler que la table couvre bien les symboles de ce poste.
        /// Ne remplace aucune traduction : le fichier ne contient que des tracés, pas de
        /// libellé exploitable. Il sert uniquement à détecter un nom que nous ignorons.
        /// </summary>
        public static void Charger(Action<string> journal)
        {
            if (_charge) return;
            _charge = true;

            string fichier = TrouverGtolSym();
            if (fichier == "")
            {
                if (journal != null) journal("gtol.sym introuvable : table des symboles interne utilisée telle quelle.");
                return;
            }
            _fichierLu = fichier;

            try
            {
                string bibliotheque = "";
                foreach (string ligne in File.ReadAllLines(fichier, Encoding.GetEncoding("iso-8859-1")))
                {
                    if (ligne.StartsWith("#"))
                    {
                        bibliotheque = Avant(ligne.Substring(1), ',');
                    }
                    else if (ligne.StartsWith("*") &&
                             (bibliotheque == "IGTOL" || bibliotheque == "GTOL"))
                    {
                        string nom = Avant(ligne.Substring(1), ',').Trim();
                        if (nom != "" && !NomsDuFichier.Contains(nom)) NomsDuFichier.Add(nom);
                    }
                }
            }
            catch (Exception ex)
            {
                if (journal != null) journal("gtol.sym illisible : " + ex.Message);
                return;
            }

            List<string> inconnus = new List<string>();
            foreach (string nom in NomsDuFichier)
                if (!Symboles.ContainsKey(nom) && !Modificateurs.ContainsKey(nom)) inconnus.Add(nom);

            if (journal != null)
            {
                journal("gtol.sym lu : " + NomsDuFichier.Count + " symbole(s) GTOL/IGTOL dans " + fichier);
                if (inconnus.Count > 0)
                    journal("Symboles présents dans gtol.sym et absents de la table : " + string.Join(", ", inconnus));
            }
        }

        /// <summary>
        /// Traduit un code brut. Retourne null si le code n'est pas reconnu : l'appelant
        /// doit alors écrire le code tel quel et journaliser un avertissement, jamais deviner.
        /// </summary>
        public static SymboleGtol Traduire(string code)
        {
            string nom = NomCourt(code);
            if (nom == "") return null;
            SymboleGtol s;
            return Symboles.TryGetValue(nom, out s) ? s : null;
        }

        /// <summary>
        /// Remplace les modificateurs d'un texte : « &lt;MOD-DIAM&gt;6 » devient « Ø6 ».
        /// Un modificateur inconnu est laissé tel quel, entre chevrons.
        /// </summary>
        public static string RemplacerModificateurs(string texte)
        {
            if (string.IsNullOrEmpty(texte) || texte.IndexOf('<') < 0) return texte == null ? "" : texte;

            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < texte.Length)
            {
                int debut = texte.IndexOf('<', i);
                if (debut < 0) { sb.Append(texte, i, texte.Length - i); break; }
                int fin = texte.IndexOf('>', debut);
                if (fin < 0) { sb.Append(texte, i, texte.Length - i); break; }

                sb.Append(texte, i, debut - i);
                string code = texte.Substring(debut + 1, fin - debut - 1);
                string nom = NomCourt("<" + code + ">");
                string remplacement;
                if (nom != "" && Modificateurs.TryGetValue(nom, out remplacement)) sb.Append(remplacement);
                else sb.Append(texte, debut, fin - debut + 1);
                i = fin + 1;
            }
            return sb.ToString();
        }

        /// <summary>Vrai si le texte est un code de symbole, du type &lt;IGTOL-PERP&gt;.</summary>
        public static bool EstUnCode(string texte)
        {
            if (string.IsNullOrEmpty(texte)) return false;
            string t = texte.Trim();
            return t.Length > 2 && t[0] == '<' && t[t.Length - 1] == '>';
        }

        /// <summary>« &lt;IGTOL-PERP&gt; » donne « PERP ». Chaîne vide si le format ne s'y prête pas.</summary>
        private static string NomCourt(string code)
        {
            if (!EstUnCode(code)) return "";
            string interieur = code.Trim().Trim('<', '>');
            int tiret = interieur.IndexOf('-');
            return tiret >= 0 ? interieur.Substring(tiret + 1).Trim() : interieur.Trim();
        }

        private static string Avant(string s, char c)
        {
            int i = s.IndexOf(c);
            return i < 0 ? s.Trim() : s.Substring(0, i).Trim();
        }

        /// <summary>
        /// Cherche gtol.sym dans l'installation, en préférant la langue française puis
        /// l'anglaise, et la version la plus récente.
        /// </summary>
        private static string TrouverGtolSym()
        {
            try
            {
                string racine = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SolidWorks");
                if (!Directory.Exists(racine)) return "";

                List<string> versions = new List<string>(Directory.GetDirectories(racine, "SOLIDWORKS *"));
                versions.Sort();
                versions.Reverse();

                foreach (string v in versions)
                {
                    foreach (string langue in new string[] { "french", "english" })
                    {
                        string candidat = Path.Combine(v, "lang", langue, "gtol.sym");
                        if (File.Exists(candidat)) return candidat;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Recherche de gtol.sym : " + ex.Message);
            }
            return "";
        }
    }
}
