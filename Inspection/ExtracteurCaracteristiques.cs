using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AskThem.Config;
using AskThem.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using ModelDoc2 = SolidWorks.Interop.sldworks.ModelDoc2;

namespace AskThem.Inspection
{
    /// <summary>
    /// Parcourt un dessin déjà ouvert et en tire les caractéristiques que le fournisseur
    /// devra contrôler. Le document n'est jamais ouvert ni refermé ici : l'appelant le
    /// fournit dans la passe d'export existante.
    /// </summary>
    public sealed class ExtracteurCaracteristiques
    {
        /// <summary>
        /// Mettre à true pour reprendre TOUTES les cotes du plan, tolérancées ou non.
        /// Le filtre normal ne retient que ce qui engage le fournisseur ; ce drapeau existe
        /// pour le cas où il s'avérerait trop strict à l'usage.
        /// </summary>
        public const bool INCLURE_TOUTES_LES_COTES = false;

        private const double M2MM = 1000.0;

        private readonly RapportControleConfig _cfg;
        private readonly Action<string> _journal;

        public ExtracteurCaracteristiques(RapportControleConfig cfg, Action<string> journal)
        {
            _cfg = cfg != null ? cfg : new RapportControleConfig();
            _journal = journal;
        }

        // ------------------------------------------------------------------
        // Point d'entrée
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit le rapport d'un article. Ne lève jamais : ce qui n'a pas pu être lu
        /// devient un avertissement.
        /// </summary>
        public RapportControle Extraire(ModelDoc2 plan, PartLine ligne,
                                        string fournisseur, string numeroCommande)
        {
            RapportControle r = new RapportControle();
            r.DateGeneration = DateTime.Now;
            r.Fournisseur = fournisseur == null ? "" : fournisseur;
            r.NumeroCommande = numeroCommande == null ? "" : numeroCommande;

            if (ligne != null)
            {
                r.NumeroPlan = ligne.PartNumber;
                r.Revision = ligne.EffectiveRevision;
                r.Designation = ligne.Description;
                r.QuantiteLot = ligne.Qty1;
            }
            if (plan == null)
            {
                r.Avertissements.Add("Aucun plan fourni : rapport vide.");
                return r;
            }

            try { r.CheminSourcePlan = plan.GetPathName(); }
            catch (Exception) { }

            RemplirEnTete(r, plan, ligne);

            List<Releve> releves = new List<Releve>();
            try
            {
                ParcourirFeuilles(plan, r, releves);
            }
            catch (Exception ex)
            {
                r.Avertissements.Add("Parcours du plan interrompu : " + ex.Message);
            }

            Numeroter(r, releves);
            AjouterLignesFixes(r, releves);
            return r;
        }

        // ------------------------------------------------------------------
        // En-tête : matière, traitement, peinture
        // ------------------------------------------------------------------

        private void RemplirEnTete(RapportControle r, ModelDoc2 plan, PartLine ligne)
        {
            // Priorité : le modèle référencé par la vue principale, puis le plan lui-même,
            // puis ce que la carte de données PDM avait déjà donné à AskThem.
            ModelDoc2 modele = PremierModeleReference(plan);

            r.Matiere = Champ(r, "matiere", modele, plan, ligne == null ? "" : ligne.Material);
            r.Traitement = Champ(r, "traitement", modele, plan, ligne == null ? "" : ligne.Treatment);
            r.Peinture = Champ(r, "peinture", modele, plan, "");

            if (string.IsNullOrWhiteSpace(r.Designation))
                r.Designation = Champ(r, "designation", modele, plan, "");
            if (string.IsNullOrWhiteSpace(r.Revision))
                r.Revision = Champ(r, "revision", modele, plan, "");
        }

        /// <summary>Première valeur non vide parmi le modèle, le plan, puis le repli.</summary>
        private string Champ(RapportControle r, string champ, ModelDoc2 modele, ModelDoc2 plan, string repli)
        {
            List<string> noms = _cfg.NomsDe(champ);
            string v = LirePropriete(modele, noms);
            if (v == "") v = LirePropriete(plan, noms);
            if (v == "") v = repli == null ? "" : repli.Trim();
            if (v == "")
            {
                Avertir(r, "Aucune propriété trouvée pour « " + champ + " » : « " + _cfg.ValeurSiVide + " » écrit.");
                return _cfg.ValeurSiVide;
            }
            return v;
        }

        /// <summary>
        /// Lit la première propriété non vide parmi les noms proposés, au niveau du document
        /// puis de chaque configuration. Les valeurs évaluées priment sur les expressions.
        /// </summary>
        private static string LirePropriete(ModelDoc2 doc, List<string> noms)
        {
            if (doc == null || noms == null || noms.Count == 0) return "";

            List<CustomPropertyManager> sources = new List<CustomPropertyManager>();
            try { sources.Add(doc.Extension.get_CustomPropertyManager("")); }
            catch (Exception) { }
            try
            {
                string[] configs = doc.GetConfigurationNames() as string[];
                if (configs != null)
                    foreach (string c in configs)
                    {
                        try { sources.Add(doc.Extension.get_CustomPropertyManager(c)); }
                        catch (Exception) { }
                    }
            }
            catch (Exception) { }

            foreach (CustomPropertyManager cpm in sources)
            {
                if (cpm == null) continue;
                foreach (string nom in noms)
                {
                    string val = "", resolu = "";
                    bool ok = false;
                    try { cpm.Get5(nom, false, out val, out resolu, out ok); }
                    catch (Exception) { continue; }
                    if (!string.IsNullOrWhiteSpace(resolu)) return resolu.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
            return "";
        }

        private static ModelDoc2 PremierModeleReference(ModelDoc2 plan)
        {
            try
            {
                object[] feuilles = ((DrawingDoc)plan).GetViews() as object[];
                if (feuilles == null) return null;
                foreach (object of in feuilles)
                {
                    object[] vues = of as object[];
                    if (vues == null) continue;
                    for (int i = 1; i < vues.Length; i++)
                    {
                        View v = vues[i] as View;
                        if (v == null) continue;
                        ModelDoc2 m = v.ReferencedDocument;
                        if (m != null) return m;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        // ------------------------------------------------------------------
        // Parcours des feuilles
        // ------------------------------------------------------------------

        private void ParcourirFeuilles(ModelDoc2 plan, RapportControle r, List<Releve> releves)
        {
            DrawingDoc drw = (DrawingDoc)plan;
            object[] feuilles = drw.GetViews() as object[];
            string[] noms = drw.GetSheetNames() as string[];
            if (feuilles == null) { Avertir(r, "Aucune feuille lisible sur ce plan."); return; }

            r.NombreFeuilles = feuilles.Length;

            for (int f = 0; f < feuilles.Length; f++)
            {
                object[] vues = feuilles[f] as object[];
                if (vues == null || vues.Length == 0) continue;

                string nomFeuille = (noms != null && f < noms.Length) ? noms[f] : "";
                GrilleZones grille = ConstruireGrille(drw, nomFeuille, vues[0] as View, r);

                // Ordre imposé par la spécification : les cotes de la feuille, puis ses
                // tolérances géométriques, puis ses états de surface.
                List<Releve> cotes = new List<Releve>();
                List<Releve> gtols = new List<Releve>();
                List<Releve> surfaces = new List<Releve>();

                int lues = 0;
                for (int i = 0; i < vues.Length; i++)
                {
                    View vue = vues[i] as View;
                    if (vue == null) continue;
                    lues += LireCotes(vue, grille, r, cotes);
                    LireGtols(vue, grille, r, gtols);
                    LireEtatsSurface(vue, grille, r, surfaces);
                }

                int avant = cotes.Count + gtols.Count + surfaces.Count;
                Dedupliquer(cotes);
                Dedupliquer(gtols);
                Dedupliquer(surfaces);
                int fusionnes = avant - (cotes.Count + gtols.Count + surfaces.Count);

                cotes.Sort(Releve.ParZone);
                gtols.Sort(Releve.ParZone);
                surfaces.Sort(Releve.ParZone);

                releves.AddRange(cotes);
                releves.AddRange(gtols);
                releves.AddRange(surfaces);

                Journal("Feuille " + (f + 1) + " : " + lues + " cote(s) lue(s), " + cotes.Count
                    + " retenue(s), " + gtols.Count + " tolérance(s) géométrique(s), "
                    + surfaces.Count + " état(s) de surface"
                    + (fusionnes > 0 ? ", " + fusionnes + " doublon(s) fusionné(s)." : "."));
            }
        }

        // ------------------------------------------------------------------
        // Cotes
        // ------------------------------------------------------------------

        private int LireCotes(View vue, GrilleZones grille, RapportControle r, List<Releve> sortie)
        {
            int lues = 0;
            DisplayDimension dd = null;
            try { dd = vue.GetFirstDisplayDimension5(); }
            catch (Exception ex) { Avertir(r, "Cotes illisibles sur une vue : " + ex.Message); return 0; }

            while (dd != null)
            {
                lues++;
                try { UneCote(dd, grille, r, sortie); }
                catch (Exception ex) { Avertir(r, "Cote ignorée : " + ex.Message); }

                DisplayDimension suivante = null;
                try { suivante = dd.GetNext5(); }
                catch (Exception) { suivante = null; }
                dd = suivante;
            }
            return lues;
        }

        private void UneCote(DisplayDimension dd, GrilleZones grille, RapportControle r, List<Releve> sortie)
        {
            // Une cote entre parenthèses est une cote de référence : elle informe, elle
            // n'engage pas. Attention, IsReferenceDim est vrai pour presque toute cote de
            // mise en plan et ne convient pas comme test.
            bool entreParentheses = false;
            try { entreParentheses = dd.ShowParenthesis; }
            catch (Exception) { }
            if (entreParentheses) return;

            Dimension d = dd.GetDimension2(0) as Dimension;
            if (d == null) return;

            double valeur = 0;
            try
            {
                double[] v = d.GetValue3((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                if (v != null && v.Length > 0) valeur = v[0];
            }
            catch (Exception) { }

            int typeTol = (int)swTolType_e.swTolNONE;
            try { typeTol = d.GetToleranceType(); }
            catch (Exception) { }

            double min = 0, max = 0;
            try
            {
                double[] t = d.GetToleranceValues() as double[];
                if (t != null && t.Length >= 2) { min = t[0]; max = t[1]; }
            }
            catch (Exception) { }

            string ajustement = "";
            try
            {
                string brut = d.GetToleranceFitValues();
                if (!string.IsNullOrWhiteSpace(brut)) ajustement = brut.Split(',')[0].Trim();
            }
            catch (Exception) { }

            if (!INCLURE_TOUTES_LES_COTES && !FormatteurTolerance.EstTolerancee(typeTol, min, max))
                return;

            string prefixe = TableSymbolesGtol.RemplacerModificateurs(Texte(dd, swDimensionTextParts_e.swDimensionTextPrefix));
            string suffixe = TableSymbolesGtol.RemplacerModificateurs(Texte(dd, swDimensionTextParts_e.swDimensionTextSuffix));

            int type = 0;
            try { type = dd.Type2; }
            catch (Exception) { }
            bool angulaire = type == (int)swDimensionType_e.swAngularDimension
                          || type == (int)swDimensionType_e.swAngularOrdinateDimension;

            Releve rel = new Releve();
            rel.Type = TypeCaracteristique.Dimension;
            rel.Specification = FormatteurTolerance.Composer(valeur, typeTol, min, max,
                                                             prefixe, suffixe, ajustement, angulaire);
            Libelle(type, prefixe, out rel.LibelleFr, out rel.LibelleEn);
            PoserZone(rel, dd.GetAnnotation(), grille, r);
            sortie.Add(rel);
        }

        /// <summary>Nature de la cote, déduite de son type et de son préfixe.</summary>
        private static void Libelle(int type, string prefixe, out string fr, out string en)
        {
            switch ((swDimensionType_e)type)
            {
                case swDimensionType_e.swDiameterDimension:
                case swDimensionType_e.swDiametricLinearDimension:
                    fr = "Diamètre"; en = "Diameter"; return;
                case swDimensionType_e.swRadialDimension:
                case swDimensionType_e.swRadialLinearDimension:
                    fr = "Rayon"; en = "Radius"; return;
                case swDimensionType_e.swAngularDimension:
                case swDimensionType_e.swAngularOrdinateDimension:
                    fr = "Angle"; en = "Angle"; return;
                case swDimensionType_e.swChamferDimension:
                    fr = "Chanfrein"; en = "Chamfer"; return;
                case swDimensionType_e.swArcLengthDimension:
                    fr = "Longueur d'arc"; en = "Arc length"; return;
            }

            // Le type ne dit rien d'utile : le préfixe, lui, parle.
            string p = prefixe == null ? "" : prefixe.Trim();
            if (p.StartsWith("Ø")) { fr = "Diamètre"; en = "Diameter"; return; }
            if (p.StartsWith("R")) { fr = "Rayon"; en = "Radius"; return; }
            if (p.StartsWith("M")) { fr = "Filetage"; en = "Thread"; return; }
            fr = "Cote"; en = "Dimension";
        }

        private static string Texte(DisplayDimension dd, swDimensionTextParts_e partie)
        {
            try { string s = dd.GetText((int)partie); return s == null ? "" : s; }
            catch (Exception) { return ""; }
        }

        // ------------------------------------------------------------------
        // Tolérances géométriques
        // ------------------------------------------------------------------

        private void LireGtols(View vue, GrilleZones grille, RapportControle r, List<Releve> sortie)
        {
            object[] tab = null;
            try { tab = vue.GetGTols() as object[]; }
            catch (Exception ex) { Avertir(r, "Tolérances géométriques illisibles : " + ex.Message); return; }
            if (tab == null) return;

            foreach (object o in tab)
            {
                Gtol g = o as Gtol;
                if (g == null) continue;
                try { UnGtol(g, grille, r, sortie); }
                catch (Exception ex) { Avertir(r, "Tolérance géométrique ignorée : " + ex.Message); }
            }
        }

        /// <summary>
        /// Un cadre de tolérance géométrique. Les accesseurs par cadre (GetFrameSymbols2,
        /// GetFrameValues) reviennent vides sur les plans du coffre ; le contenu réel est
        /// dans la liste de textes, où chaque code de symbole ouvre un nouveau cadre.
        /// </summary>
        private void UnGtol(Gtol g, GrilleZones grille, RapportControle r, List<Releve> sortie)
        {
            int nb = 0;
            try { nb = g.GetTextCount(); }
            catch (Exception) { }

            List<List<string>> cadres = new List<List<string>>();
            List<string> courant = null;
            for (int i = 0; i < nb; i++)
            {
                string t = null;
                try { t = g.GetTextAtIndex(i); }
                catch (Exception) { }
                if (string.IsNullOrWhiteSpace(t)) continue;
                t = t.Trim();

                if (TableSymbolesGtol.EstUnCode(t) || courant == null)
                {
                    courant = new List<string>();
                    cadres.Add(courant);
                }
                courant.Add(t);
            }
            if (cadres.Count == 0) return;

            object annotation = null;
            try { annotation = g.GetAnnotation(); }
            catch (Exception) { }

            for (int c = 0; c < cadres.Count; c++)
            {
                List<string> textes = cadres[c];
                string code = textes[0];
                SymboleGtol sym = TableSymbolesGtol.Traduire(code);

                Releve rel = new Releve();
                rel.Type = TypeCaracteristique.ToleranceGeometrique;

                List<string> reste = new List<string>();
                for (int i = 1; i < textes.Count; i++)
                    reste.Add(FormatteurTolerance.NormaliserDecimales(
                        TableSymbolesGtol.RemplacerModificateurs(textes[i])));

                if (sym == null)
                {
                    // Code inconnu : on écrit le code brut, on ne devine jamais le symbole.
                    rel.Symbole = "";
                    rel.LibelleFr = code;
                    rel.LibelleEn = "";
                    rel.NumeroTfp = 0;
                    Avertir(r, "Symbole de tolérance géométrique inconnu : " + code);
                }
                else
                {
                    rel.Symbole = sym.Unicode;
                    rel.LibelleFr = sym.LibelleFr;
                    rel.LibelleEn = sym.LibelleEn;
                    rel.NumeroTfp = sym.NumeroTfp;
                }

                // La valeur ouvre la spécification, les références suivent après un trait.
                string valeur = reste.Count > 0 ? reste[0] : "";
                List<string> references = reste.Count > 1 ? reste.GetRange(1, reste.Count - 1) : new List<string>();

                rel.Specification = valeur;
                if (references.Count > 0)
                {
                    rel.Specification = valeur + "  |  " + string.Join(" ", references);
                    rel.LibelleFr = rel.LibelleFr + " / " + string.Join(" ", references);
                }
                if (cadres.Count > 1)
                    rel.LibelleFr = rel.LibelleFr + " (cadre " + (c + 1) + "/" + cadres.Count + ")";

                PoserZone(rel, annotation, grille, r);
                sortie.Add(rel);
            }
        }

        // ------------------------------------------------------------------
        // États de surface et notes
        // ------------------------------------------------------------------

        private void LireEtatsSurface(View vue, GrilleZones grille, RapportControle r, List<Releve> sortie)
        {
            object[] tab = null;
            try { tab = vue.GetSFSymbols() as object[]; }
            catch (Exception) { return; }
            if (tab == null) return;

            foreach (object o in tab)
            {
                SFSymbol sf = o as SFSymbol;
                if (sf == null) continue;
                try
                {
                    string texte = "";
                    int nb = 0;
                    try { nb = sf.GetTextCount(); }
                    catch (Exception) { }
                    for (int i = 0; i < Math.Max(nb, 4); i++)
                    {
                        string t = null;
                        try { t = sf.GetTextAtIndex(i); }
                        catch (Exception) { }
                        if (!string.IsNullOrWhiteSpace(t)) texte += (texte == "" ? "" : " ") + t.Trim();
                    }
                    if (texte == "") continue;

                    Releve rel = new Releve();
                    rel.Type = TypeCaracteristique.EtatSurface;
                    rel.LibelleFr = "État de surface";
                    rel.LibelleEn = "Surface finish";
                    rel.Specification = EspacerRugosite(FormatteurTolerance.NormaliserDecimales(texte));
                    PoserZone(rel, sf.GetAnnotation(), grille, r);
                    sortie.Add(rel);
                }
                catch (Exception ex) { Avertir(r, "État de surface ignoré : " + ex.Message); }
            }
        }

        /// <summary>« Ra1.6 » devient « Ra 1.6 » : plus lisible pour qui remplit à la main.</summary>
        private static string EspacerRugosite(string texte)
        {
            if (string.IsNullOrEmpty(texte)) return "";
            foreach (string prefixe in new string[] { "Ra", "Rz", "Rt", "Rmax" })
            {
                if (texte.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase)
                    && texte.Length > prefixe.Length
                    && texte[prefixe.Length] != ' ')
                {
                    return prefixe + " " + texte.Substring(prefixe.Length).Trim();
                }
            }
            return texte;
        }

        // ------------------------------------------------------------------
        // Zones, numérotation, lignes fixes
        // ------------------------------------------------------------------

        private GrilleZones ConstruireGrille(DrawingDoc drw, string nomFeuille, View fondDePlan, RapportControle r)
        {
            double largeur = 0, hauteur = 0;
            Sheet sh = null;
            try
            {
                if (!string.IsNullOrEmpty(nomFeuille)) sh = drw.get_Sheet(nomFeuille);
                if (sh != null) sh.GetSize(ref largeur, ref hauteur);
            }
            catch (Exception) { }
            finally
            {
                if (sh != null) { try { Marshal.ReleaseComObject(sh); } catch (Exception) { } }
            }

            GrilleZones grille = GrilleZones.Construire(fondDePlan, largeur * M2MM, hauteur * M2MM, _cfg.MargeBordRepere);
            if (!grille.Utilisable)
                Avertir(r, "Repères de cadre introuvables sur la feuille « " + nomFeuille + " » : zones non renseignées.");
            return grille;
        }

        private void PoserZone(Releve rel, object annotation, GrilleZones grille, RapportControle r)
        {
            double[] p = null;
            try
            {
                Annotation a = annotation as Annotation;
                if (a != null) p = a.GetPosition() as double[];
            }
            catch (Exception) { }

            if (p != null && p.Length >= 2)
            {
                rel.X = p[0] * M2MM;
                rel.Y = p[1] * M2MM;
                rel.PositionConnue = true;
            }

            if (p == null || p.Length < 2 || grille == null || !grille.Utilisable)
            {
                rel.Zone = "—";
                return;
            }
            rel.Zone = grille.Zone(rel.X, rel.Y);
            if (rel.Zone == "—") Avertir(r, "Zone indéterminable pour « " + rel.LibelleFr + " ».");
        }

        /// <summary>
        /// Une même exigence peut être cotée deux fois au même endroit — la vue et sa vue
        /// de détail, par exemple. Deux relevés de même libellé, même spécification et
        /// distants de moins d'un millimètre n'en font qu'un. Deux callouts éloignés sur la
        /// pièce restent deux lignes : ce sont deux surfaces à contrôler.
        /// </summary>
        private const double ToleranceDoublonMm = 1.0;

        private static void Dedupliquer(List<Releve> liste)
        {
            for (int i = 0; i < liste.Count; i++)
            {
                for (int j = liste.Count - 1; j > i; j--)
                {
                    Releve a = liste[i];
                    Releve b = liste[j];
                    if (a.LibelleFr != b.LibelleFr || a.Specification != b.Specification) continue;
                    if (!a.PositionConnue || !b.PositionConnue) continue;
                    if (Math.Abs(a.X - b.X) > ToleranceDoublonMm) continue;
                    if (Math.Abs(a.Y - b.Y) > ToleranceDoublonMm) continue;
                    liste.RemoveAt(j);
                }
            }
        }

        private static void Numeroter(RapportControle r, List<Releve> releves)
        {
            foreach (Releve rel in releves)
            {
                Caracteristique c = new Caracteristique();
                c.Numero = r.Caracteristiques.Count + 1;
                c.Zone = rel.Zone;
                c.Type = rel.Type;
                c.Symbole = rel.Symbole;
                c.LibelleFr = rel.LibelleFr;
                c.LibelleEn = rel.LibelleEn;
                c.Specification = rel.Specification;
                c.NumeroTfp = rel.NumeroTfp;
                r.Caracteristiques.Add(c);
            }
        }

        /// <summary>
        /// La ligne présente sur tous les rapports, quoi qu'il arrive.
        ///
        /// Les tolérances générales n'y figurent pas : elles sont portées sur le plan et ne
        /// font pas l'objet d'un contrôle à la réception.
        /// </summary>
        private void AjouterLignesFixes(RapportControle r, List<Releve> releves)
        {
            Caracteristique aspect = new Caracteristique();
            aspect.Numero = r.Caracteristiques.Count + 1;
            aspect.Zone = "—";
            aspect.Type = TypeCaracteristique.Fixe;
            aspect.LibelleFr = "Aspect, bavures, arêtes";
            aspect.LibelleEn = "Visual, burrs, edges";
            aspect.Specification = _cfg.AspectParDefaut;
            r.Caracteristiques.Add(aspect);
        }

        private void Avertir(RapportControle r, string message)
        {
            if (!r.Avertissements.Contains(message)) r.Avertissements.Add(message);
            Journal(message);
        }

        private void Journal(string message)
        {
            if (_journal != null) _journal(message);
        }

        // ------------------------------------------------------------------
        // Structures internes
        // ------------------------------------------------------------------

        /// <summary>Une caractéristique avant numérotation.</summary>
        private sealed class Releve
        {
            public string Zone = "—";
            public TypeCaracteristique Type;
            public string Symbole = "";
            public string LibelleFr = "";
            public string LibelleEn = "";
            public string Specification = "";
            public int NumeroTfp;

            /// <summary>Position sur la feuille, en millimètres. Sert à repérer les doublons.</summary>
            public double X;
            public double Y;
            public bool PositionConnue;

            /// <summary>Tri par zone : colonne d'abord, rangée ensuite, indéterminées à la fin.</summary>
            public static int ParZone(Releve a, Releve b)
            {
                bool sansA = a.Zone == "—";
                bool sansB = b.Zone == "—";
                if (sansA != sansB) return sansA ? 1 : -1;
                return string.CompareOrdinal(a.Zone, b.Zone);
            }
        }

        /// <summary>
        /// Le quadrillage du cadre, reconstruit d'après les repères réellement présents sur
        /// le fond de plan. Rien n'est codé en dur : un A4 comme un A0 sont traités par la
        /// même mesure, et le sens des lettres est celui du cadre, quel qu'il soit.
        /// </summary>
        private sealed class GrilleZones
        {
            private sealed class Repere
            {
                public string Libelle;
                public double Position;
            }

            private readonly List<Repere> _colonnes = new List<Repere>();
            private readonly List<Repere> _rangees = new List<Repere>();

            /// <summary>Vrai si le cadre a livré au moins une colonne et une rangée.</summary>
            public bool Utilisable
            {
                get { return _colonnes.Count > 0 && _rangees.Count > 0; }
            }

            public static GrilleZones Construire(View fondDePlan, double largeurMm, double hauteurMm, double marge)
            {
                GrilleZones g = new GrilleZones();
                if (fondDePlan == null || largeurMm <= 0 || hauteurMm <= 0) return g;

                object[] notes = null;
                try { notes = fondDePlan.GetNotes() as object[]; }
                catch (Exception) { return g; }
                if (notes == null) return g;

                foreach (object o in notes)
                {
                    Note n = o as Note;
                    if (n == null) continue;

                    string t = null;
                    try { t = n.GetText(); }
                    catch (Exception) { }
                    if (t == null) continue;
                    t = t.Trim();
                    if (t.Length == 0 || t.Length > 2) continue;

                    double[] p = null;
                    try
                    {
                        Annotation a = n.GetAnnotation() as Annotation;
                        if (a != null) p = a.GetPosition() as double[];
                    }
                    catch (Exception) { }
                    if (p == null || p.Length < 2) continue;

                    double x = p[0] * M2MM;
                    double y = p[1] * M2MM;

                    // Un repère de cadre touche un bord de feuille. La lettre de révision du
                    // cartouche est elle aussi une note d'un caractère, mais elle est à
                    // l'intérieur : c'est ce qui les distingue.
                    bool bordVertical = x <= marge || x >= largeurMm - marge;
                    bool bordHorizontal = y <= marge || y >= hauteurMm - marge;

                    if (bordVertical && !bordHorizontal) Ajouter(g._rangees, t, y);
                    else if (bordHorizontal && !bordVertical) Ajouter(g._colonnes, t, x);
                }

                g._colonnes.Sort(ParPosition);
                g._rangees.Sort(ParPosition);
                return g;
            }

            private static int ParPosition(Repere a, Repere b)
            {
                return a.Position.CompareTo(b.Position);
            }

            /// <summary>Un même libellé figure des deux côtés du cadre : on ne le garde qu'une fois.</summary>
            private static void Ajouter(List<Repere> liste, string libelle, double position)
            {
                foreach (Repere r in liste)
                    if (string.Equals(r.Libelle, libelle, StringComparison.OrdinalIgnoreCase)) return;

                Repere nouveau = new Repere();
                nouveau.Libelle = libelle;
                nouveau.Position = position;
                liste.Add(nouveau);
            }

            /// <summary>Case du quadrillage contenant ce point. « — » si le cadre est muet.</summary>
            public string Zone(double xMm, double yMm)
            {
                if (!Utilisable) return "—";
                return PlusProche(_colonnes, xMm) + PlusProche(_rangees, yMm);
            }

            private static string PlusProche(List<Repere> reperes, double position)
            {
                string meilleur = "";
                double ecart = double.MaxValue;
                foreach (Repere r in reperes)
                {
                    double d = Math.Abs(r.Position - position);
                    if (d < ecart) { ecart = d; meilleur = r.Libelle; }
                }
                return meilleur;
            }
        }
    }
}
