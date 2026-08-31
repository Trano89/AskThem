using AskThem.Models;

namespace AskThem.Pdf
{
    /// <summary>
    /// Produit le PDF d'un rapport de contrôle.
    ///
    /// L'interface isole la bibliothèque de mise en page du reste du programme : changer de
    /// moteur PDF ne coûte qu'une classe, sans toucher à l'extraction ni au pipeline.
    /// </summary>
    public interface IGenerateurPdf
    {
        /// <summary>
        /// Écrit le rapport dans le dossier indiqué et retourne le chemin du fichier créé.
        /// Le dossier est créé au besoin. En cas de collision, un suffixe _2, _3 est ajouté.
        /// Le PDF produit n'est ni protégé par mot de passe ni verrouillé : le fournisseur
        /// doit pouvoir l'imprimer librement.
        /// </summary>
        string Generer(RapportControle rapport, string dossier);
    }
}
