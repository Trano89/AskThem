using System;
using System.Collections.Generic;
using System.IO;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>Un email : les articles qu'il annonce et les archives qu'il porte.</summary>
    public sealed class LotEnvoi
    {
        /// <summary>Articles décrits dans le tableau de ce message.</summary>
        public List<PartLine> Lignes { get; private set; }

        /// <summary>Archives jointes à ce message.</summary>
        public List<string> PiecesJointes { get; private set; }

        /// <summary>Poids des pièces jointes, en méga-octets.</summary>
        public double TailleMb { get; set; }

        public LotEnvoi()
        {
            Lignes = new List<PartLine>();
            PiecesJointes = new List<string>();
            TailleMb = 0;
        }
    }

    /// <summary>
    /// Répartit les articles d'une demande sur un ou plusieurs emails, quand leurs archives
    /// ne tiennent pas dans un seul message.
    ///
    /// L'ordre de la grille est conservé : le fournisseur reçoit ses articles dans l'ordre
    /// où ils ont été saisis, ce qui vaut mieux qu'un remplissage optimal mais mélangé.
    /// </summary>
    public static class RepartitionEnvois
    {
        /// <summary>
        /// Constitue les lots.
        /// </summary>
        /// <param name="lignes">Articles de la demande, dans l'ordre de la grille.</param>
        /// <param name="limiteMb">Poids maximal des pièces jointes d'un message.</param>
        /// <param name="maxPieces">Nombre maximal de pièces jointes d'un message.</param>
        /// <param name="journal">Passerelle de journalisation, facultative.</param>
        public static List<LotEnvoi> Repartir(List<PartLine> lignes, double limiteMb,
                                              int maxPieces, Action<string> journal)
        {
            List<LotEnvoi> lots = new List<LotEnvoi>();
            if (lignes == null || lignes.Count == 0) return lots;

            if (limiteMb <= 0) limiteMb = double.MaxValue;
            if (maxPieces <= 0) maxPieces = int.MaxValue;

            LotEnvoi courant = new LotEnvoi();
            lots.Add(courant);

            foreach (PartLine ligne in lignes)
            {
                string archive = ligne.ZipPath;
                bool aUneArchive = !string.IsNullOrEmpty(archive) && File.Exists(archive);

                // Un article sans archive ne pèse rien : il suit le lot en cours et reste
                // ainsi à sa place dans la suite des messages.
                if (!aUneArchive)
                {
                    courant.Lignes.Add(ligne);
                    continue;
                }

                double poids = TailleMb(archive);

                // Une archive plus lourde que la limite ne peut pas être coupée : elle part
                // seule, et l'on prévient plutôt que de fabriquer un message impossible.
                if (poids > limiteMb)
                {
                    if (journal != null)
                        journal("ATTENTION : l'archive de " + ligne.PartNumber + " pèse "
                              + poids.ToString("0.0") + " Mo à elle seule, au-delà de la limite de "
                              + limiteMb.ToString("0.#") + " Mo. Elle part dans un message à part.");

                    if (courant.PiecesJointes.Count > 0)
                    {
                        courant = new LotEnvoi();
                        lots.Add(courant);
                    }
                    courant.Lignes.Add(ligne);
                    courant.PiecesJointes.Add(archive);
                    courant.TailleMb = poids;

                    courant = new LotEnvoi();
                    lots.Add(courant);
                    continue;
                }

                bool tropLourd = courant.TailleMb + poids > limiteMb;
                bool tropNombreux = courant.PiecesJointes.Count + 1 > maxPieces;
                if (courant.PiecesJointes.Count > 0 && (tropLourd || tropNombreux))
                {
                    courant = new LotEnvoi();
                    lots.Add(courant);
                }

                courant.Lignes.Add(ligne);
                courant.PiecesJointes.Add(archive);
                courant.TailleMb += poids;
            }

            // Le dernier lot peut être resté vide, et un lot sans aucun article n'a pas
            // de message à porter.
            List<LotEnvoi> retenus = new List<LotEnvoi>();
            foreach (LotEnvoi lot in lots)
                if (lot.Lignes.Count > 0) retenus.Add(lot);

            return retenus;
        }

        /// <summary>Poids d'un fichier, en méga-octets. Zéro s'il est absent.</summary>
        public static double TailleMb(string chemin)
        {
            try
            {
                if (string.IsNullOrEmpty(chemin) || !File.Exists(chemin)) return 0;
                return new FileInfo(chemin).Length / 1024.0 / 1024.0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
