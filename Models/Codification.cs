using System;

namespace AskThem.Models
{
    /// <summary>
    /// Lecture d'une référence Lyncée Tec : XYZ-AAAAA-BB.
    ///
    /// Trois caractères indépendants, et non deux champs comme on pourrait le croire :
    ///
    ///   X — catégorie, alphabétique  (A mécanique, B optique, C électrique, D et E
    ///       fournitures au catalogue, F informatique, G maquette, H électronique,
    ///       I packaging, X et Z équipements internes, # projet)
    ///   Y — structure, numérique     (0 assemblage complet, 1 sous-ensemble et
    ///       prémontage, 2 pièce)
    ///   Z — origine, numérique       (0 acheté au catalogue, 1 fabriqué sur plan Lyncée Tec,
    ///       2 article acheté puis modifié, 3 ensemble d'articles, 4 pièce fabriquée puis
    ///       modifiée, 9 non géré — usage interne ou projet)
    ///
    /// C'est <b>Y</b> qui dit si l'on a affaire à une pièce ou à un assemblage, et <b>Z</b>
    /// s'il faut un plan. Le programme le déduisait auparavant de l'extension du fichier
    /// SolidWorks, ce qui revenait à faire confiance à la façon dont le document a été
    /// modélisé plutôt qu'à ce que la référence déclare.
    /// </summary>
    public static class Codification
    {
        // ---- Y : structure ----
        public const char AssemblageComplet = '0';
        public const char SousEnsemble = '1';
        public const char Piece = '2';

        // ---- Z : origine ----
        public const char AcheteCatalogue = '0';
        public const char Fabrique = '1';
        public const char AcheteModifie = '2';
        public const char EnsembleArticles = '3';
        public const char FabriqueModifie = '4';
        public const char NonGere = '9';

        // ---- X : catégorie ----
        public const char Projet = '#';

        /// <summary>Le groupe XYZ d'une référence, ou une chaîne vide.</summary>
        private static string Tete(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return "";
            string premier = reference.Trim().Split('-')[0];
            return premier.Length < 3 ? "" : premier.Substring(0, 3).ToUpperInvariant();
        }

        /// <summary>Catégorie (X), ou '\0' si la référence est trop courte.</summary>
        public static char Categorie(string reference)
        {
            string t = Tete(reference);
            return t.Length == 3 ? t[0] : '\0';
        }

        /// <summary>Structure (Y), ou '\0'.</summary>
        public static char Structure(string reference)
        {
            string t = Tete(reference);
            return t.Length == 3 ? t[1] : '\0';
        }

        /// <summary>Origine (Z), ou '\0'.</summary>
        public static char Origine(string reference)
        {
            string t = Tete(reference);
            return t.Length == 3 ? t[2] : '\0';
        }

        // ------------------------------------------------------------------ structure

        /// <summary>
        /// Vrai si la référence désigne un assemblage ou un sous-ensemble.
        ///
        /// Ce sont eux qui font échouer une campagne : les ouvrir entraîne l'ouverture de
        /// tous leurs composants. On le sait maintenant du numéro, avant d'avoir touché au
        /// moindre fichier.
        /// </summary>
        public static bool EstAssemblage(string reference)
        {
            char y = Structure(reference);
            return y == AssemblageComplet || y == SousEnsemble;
        }

        /// <summary>Vrai si la référence désigne une pièce.</summary>
        public static bool EstPiece(string reference)
        {
            return Structure(reference) == Piece;
        }

        // ------------------------------------------------------------------ origine

        /// <summary>Vrai si l'article est fabriqué sur un plan Lyncée Tec, modifié ou non.</summary>
        public static bool EstFabrique(string reference)
        {
            char z = Origine(reference);
            return z == Fabrique || z == FabriqueModifie;
        }

        /// <summary>Vrai si l'article s'achète au catalogue, sans modification.</summary>
        public static bool EstAchete(string reference)
        {
            return Origine(reference) == AcheteCatalogue;
        }

        /// <summary>Vrai si l'article est acheté puis modifié : il a un fournisseur ET un plan.</summary>
        public static bool EstAcheteModifie(string reference)
        {
            return Origine(reference) == AcheteModifie;
        }

        /// <summary>Vrai si la référence désigne un regroupement d'articles.</summary>
        public static bool EstEnsemble(string reference)
        {
            return Origine(reference) == EnsembleArticles;
        }

        // ------------------------------------------------------------------ périmètre

        /// <summary>
        /// Vrai si la référence relève de la production courante.
        ///
        /// Les références de projet (catégorie #) et les articles non gérés (origine 9)
        /// existent pour un usage interne : ils restent utilisables pour une demande
        /// ponctuelle, mais n'ont rien à faire dans la base documentaire partagée.
        /// </summary>
        public static bool EstDeProduction(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return false;
            if (Categorie(reference) == Projet) return false;
            if (!char.IsLetter(Categorie(reference))) return false;
            return Origine(reference) != NonGere;
        }

        /// <summary>
        /// Vrai si un plan interne accompagne normalement cet article.
        ///
        /// Un article fabriqué en a un ; un article acheté puis modifié aussi, puisque la
        /// modification est décrite quelque part. Un article de catalogue non modifié n'en
        /// a pas, et le réclamer n'aurait pas de sens.
        /// </summary>
        public static bool AttendUnPlan(string reference)
        {
            return EstFabrique(reference) || EstAcheteModifie(reference);
        }

        /// <summary>Intitulé lisible de la structure, pour les messages.</summary>
        public static string LibelleStructure(string reference)
        {
            switch (Structure(reference))
            {
                case AssemblageComplet: return "assemblage complet";
                case SousEnsemble: return "sous-ensemble ou prémontage";
                case Piece: return "pièce";
                default: return "structure inconnue";
            }
        }

        /// <summary>Intitulé lisible de l'origine, pour les messages.</summary>
        public static string LibelleOrigine(string reference)
        {
            switch (Origine(reference))
            {
                case AcheteCatalogue: return "acheté au catalogue";
                case Fabrique: return "fabriqué sur plan interne";
                case AcheteModifie: return "acheté puis modifié";
                case EnsembleArticles: return "ensemble d'articles";
                case FabriqueModifie: return "fabriqué puis modifié";
                case NonGere: return "non géré (usage interne ou projet)";
                default: return "origine inconnue";
            }
        }

        /// <summary>Description complète d'une référence, pour le journal et les refus.</summary>
        public static string Decrire(string reference)
        {
            if (Tete(reference).Length != 3) return "référence illisible";
            return LibelleStructure(reference) + ", " + LibelleOrigine(reference);
        }
    }
}
