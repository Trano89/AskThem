using System;
using System.Collections.Generic;

namespace AskThem.Models
{
    /// <summary>
    /// Une demande telle que la suit la base des demandes : qui l'a faite, quand elle est
    /// réellement partie, et si le fournisseur a répondu.
    /// </summary>
    public class DemandeSuivie
    {
        // ---- États ----

        /// <summary>Générée, le message n'est pas encore constaté dans les éléments envoyés.</summary>
        public const string Preparee = "Préparée";

        /// <summary>Partie : elle entre dans le Gantt et attend une réponse.</summary>
        public const string Envoyee = "Envoyée";

        /// <summary>Le demandeur a confirmé avoir reçu la réponse : le Gantt se clôt.</summary>
        public const string Repondue = "Réponse reçue";

        /// <summary>Le demandeur a arrêté le suivi sans réponse.</summary>
        public const string SansSuite = "Sans suite";

        /// <summary>Générée mais jamais envoyée : elle n'est ni archivée ni suivie.</summary>
        public const string NonEnvoyee = "Non envoyée";

        // ---- Identité ----

        public string Id { get; set; }

        /// <summary>« Offres », « Fabrications » ou « Commandes » : c'est aussi l'onglet.</summary>
        public string Type { get; set; }

        public string Statut { get; set; }
        public DateTime CreeeLe { get; set; }

        /// <summary>Identifiant Windows du demandeur : c'est à lui que vont les rappels.</summary>
        public string Auteur { get; set; }

        /// <summary>Nom affiché du demandeur, pour la lecture.</summary>
        public string AuteurNom { get; set; }

        /// <summary>Adresse du demandeur : c'est là qu'on le prévient quand un collègue agit sur sa demande.</summary>
        public string AuteurEmail { get; set; }

        public string Poste { get; set; }

        // ---- La demande ----

        public string Fournisseur { get; set; }
        public string Destinataires { get; set; }
        public string Reference { get; set; }
        public int NbArticles { get; set; }
        public string Articles { get; set; }
        public int NbMessages { get; set; }

        // ---- L'envoi ----

        public int MessagesEnvoyes { get; set; }

        /// <summary>Heure d'envoi lue sur le message dans les éléments envoyés. Nulle tant qu'il n'est pas parti.</summary>
        public DateTime? EnvoyeeLe { get; set; }

        /// <summary>Objet et destinataires tels qu'ils sont partis : l'utilisateur a pu les retoucher.</summary>
        public string SujetEnvoye { get; set; }
        public string DestinatairesEnvoyes { get; set; }

        // ---- Le suivi ----

        public DateTime? ProchainRappel { get; set; }
        public int NbRappels { get; set; }
        public DateTime? ReponseLe { get; set; }
        public DateTime? ClotureLe { get; set; }
        public string ClotureePar { get; set; }

        /// <summary>Où la demande est archivée sur le réseau, une fois partie.</summary>
        public string DossierArchive { get; set; }

        /// <summary>Dernière action de suivi — « Réponse reçue », « Rappel repoussé »… —, par qui, et quand.</summary>
        public string DerniereAction { get; set; }
        public string DerniereActionPar { get; set; }
        public DateTime? DerniereActionLe { get; set; }

        public DateTime MisAJourLe { get; set; }

        // ---- Suppression ----
        // Une demande n'est jamais effacée : « supprimée », elle quitte le suivi et le Gantt
        // pour l'onglet « Supprimées », d'où on peut la rétablir.

        /// <summary>Vrai si la demande a été retirée du suivi.</summary>
        public bool Masquee { get; set; }

        /// <summary>
        /// Quand elle a été supprimée ou rétablie pour la dernière fois. C'est cette date qui
        /// départage deux états : un poste qui réécrit la demande pour une autre raison — envoi
        /// constaté, archivage — ne la fait pas réapparaître.
        /// </summary>
        public DateTime? MasqueeLe { get; set; }
        public string MasqueePar { get; set; }

        public DemandeSuivie()
        {
            Id = "";
            Type = "";
            Statut = Preparee;
            Auteur = "";
            AuteurNom = "";
            AuteurEmail = "";
            Poste = "";
            Fournisseur = "";
            Destinataires = "";
            Reference = "";
            Articles = "";
            SujetEnvoye = "";
            DestinatairesEnvoyes = "";
            ClotureePar = "";
            DossierArchive = "";
            DerniereAction = "";
            DerniereActionPar = "";
            MasqueePar = "";
        }

        /// <summary>
        /// Reprend de l'autre version l'état « supprimée » s'il est plus récent que le sien.
        /// </summary>
        public void ReprendreMasque(DemandeSuivie autre)
        {
            if (autre == null || !autre.MasqueeLe.HasValue) return;
            if (MasqueeLe.HasValue && MasqueeLe.Value >= autre.MasqueeLe.Value) return;
            Masquee = autre.Masquee;
            MasqueeLe = autre.MasqueeLe;
            MasqueePar = autre.MasqueePar;
        }

        /// <summary>Vrai si cette demande appartient à cet utilisateur.</summary>
        public bool EstA(string utilisateur)
        {
            return string.Equals(Auteur, utilisateur, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Le nom à afficher pour le demandeur.</summary>
        public string Demandeur
        {
            get { return string.IsNullOrWhiteSpace(AuteurNom) ? Auteur : AuteurNom; }
        }

        /// <summary>Vrai tant qu'une réponse est attendue : la barre du Gantt court encore.</summary>
        public bool EnAttente { get { return Statut == Envoyee; } }

        /// <summary>Vrai si ce rappel concerne cet utilisateur et qu'il est échu.</summary>
        public bool RappelEchu(string utilisateur, DateTime aujourdHui)
        {
            return EnAttente && !Masquee
                && ProchainRappel.HasValue && ProchainRappel.Value.Date <= aujourdHui.Date
                && string.Equals(Auteur, utilisateur, StringComparison.OrdinalIgnoreCase);
        }

        public DemandeSuivie Copie()
        {
            return (DemandeSuivie)MemberwiseClone();
        }

        /// <summary>Identifiant d'une nouvelle demande : lisible, et unique d'un poste à l'autre.</summary>
        public static string NouvelId()
        {
            return "D" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>Liste courte des articles : les premiers, puis le nombre restant.</summary>
        public static string ListeCourte(List<string> articles, int max)
        {
            if (articles == null || articles.Count == 0) return "";
            if (articles.Count <= max) return string.Join(", ", articles);
            return string.Join(", ", articles.GetRange(0, max)) + " … (+" + (articles.Count - max) + ")";
        }
    }
}
