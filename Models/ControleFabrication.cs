using System;
using System.Collections.Generic;

namespace AskThem.Models
{
    /// <summary>Nature d'une caractéristique relevée sur le plan.</summary>
    public enum TypeCaracteristique
    {
        /// <summary>Cote portant une tolérance explicite ou un ajustement.</summary>
        Dimension,

        /// <summary>Tolérance géométrique : planéité, symétrie, battement...</summary>
        ToleranceGeometrique,

        /// <summary>État de surface : Ra, Rz.</summary>
        EtatSurface,

        /// <summary>Note du plan retenue comme exigence (tolérances générales).</summary>
        Note,

        /// <summary>Ligne ajoutée systématiquement, quel que soit le plan.</summary>
        Fixe
    }

    /// <summary>
    /// Une ligne du tableau de contrôle : ce que le fournisseur doit vérifier, et
    /// l'endroit du plan où il le trouve.
    /// </summary>
    public sealed class Caracteristique
    {
        /// <summary>Numéro d'ordre dans le contrôle, séquentiel sur toutes les feuilles.</summary>
        public int Numero { get; set; }

        /// <summary>Case du quadrillage du cadre, par exemple B3. « — » si indéterminable.</summary>
        public string Zone { get; set; }

        /// <summary>Origine de la caractéristique.</summary>
        public TypeCaracteristique Type { get; set; }

        /// <summary>Caractère Unicode du symbole GD&amp;T. Vide si non applicable.</summary>
        public string Symbole { get; set; }

        /// <summary>Libellé français affiché dans la colonne Caractéristique.</summary>
        public string LibelleFr { get; set; }

        /// <summary>Traduction anglaise, affichée en italique sous le libellé français.</summary>
        public string LibelleEn { get; set; }

        /// <summary>Exigence à respecter, déjà mise en forme : « 16 ±0.2 », « Ra 0.8 ».</summary>
        public string Specification { get; set; }

        /// <summary>Numéro de la nomenclature TF&amp;P LyncéeTec. 0 si non applicable.</summary>
        public int NumeroTfp { get; set; }

        public Caracteristique()
        {
            Numero = 0;
            Zone = "—";
            Type = TypeCaracteristique.Dimension;
            Symbole = "";
            LibelleFr = "";
            LibelleEn = "";
            Specification = "";
            NumeroTfp = 0;
        }
    }

    /// <summary>
    /// Le contrôle de fabrication d'un article : l'en-tête que le programme remplit, et
    /// la liste des caractéristiques que le fournisseur devra mesurer.
    /// </summary>
    public sealed class ControleFabrication
    {
        /// <summary>Numéro d'article, au format XYZ-AAAAA-BB.</summary>
        public string NumeroPlan { get; set; }

        /// <summary>Révision du plan, prioritairement celle lue sur le .SLDDRW.</summary>
        public string Revision { get; set; }

        /// <summary>Désignation de l'article.</summary>
        public string Designation { get; set; }

        /// <summary>Quantité commandée pour ce lot.</summary>
        public int QuantiteLot { get; set; }

        /// <summary>Fournisseur destinataire de la demande.</summary>
        public string Fournisseur { get; set; }

        /// <summary>Référence de la commande.</summary>
        public string NumeroCommande { get; set; }

        /// <summary>Matière première.</summary>
        public string Matiere { get; set; }

        /// <summary>Traitement thermique ou de surface.</summary>
        public string Traitement { get; set; }

        /// <summary>Peinture ou teinte RAL.</summary>
        public string Peinture { get; set; }

        /// <summary>Horodatage porté sur le document.</summary>
        public DateTime DateGeneration { get; set; }

        /// <summary>Chemin du .SLDDRW dont ce contrôle est issu.</summary>
        public string CheminSourcePlan { get; set; }

        /// <summary>Nombre de feuilles parcourues sur le plan.</summary>
        public int NombreFeuilles { get; set; }

        /// <summary>Les lignes du tableau, dans leur ordre de numérotation.</summary>
        public List<Caracteristique> Caracteristiques { get; private set; }

        /// <summary>
        /// Ce que l'extraction n'a pas su faire : symbole inconnu, propriété absente,
        /// zone indéterminable. Repris dans le journal.
        /// </summary>
        public List<string> Avertissements { get; private set; }

        /// <summary>
        /// Vrai quand le contrôle est trop maigre pour être envoyé tel quel : signale
        /// presque toujours un plan dont les cotes sont du texte libre.
        /// </summary>
        public bool ExtractionPartielle
        {
            get { return Caracteristiques.Count < 3; }
        }

        public ControleFabrication()
        {
            NumeroPlan = "";
            Revision = "";
            Designation = "";
            QuantiteLot = 0;
            Fournisseur = "";
            NumeroCommande = "";
            Matiere = "";
            Traitement = "";
            Peinture = "";
            DateGeneration = DateTime.Now;
            CheminSourcePlan = "";
            NombreFeuilles = 0;
            Caracteristiques = new List<Caracteristique>();
            Avertissements = new List<string>();
        }
    }
}
