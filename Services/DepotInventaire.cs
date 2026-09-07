using System;
using System.Collections.Generic;
using System.IO;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>
    /// La base documentaire des articles, tenue dans l'inventaire.
    ///
    /// Chaque document y vit seul, distingué par sa nature — plan, DXF, modèle, contrôle —
    /// et non par son extension : le plan et le formulaire de contrôle sont tous deux des
    /// PDF et n'ont pas le même rôle. Il n'y a plus d'archive de stockage : les ZIP sont
    /// assemblés au moment d'un envoi, et ne subsistent que dans l'historique de la demande.
    ///
    /// Les droits sont ceux du serveur, pas ceux du programme : tout le monde lit, seuls les
    /// comptes portant « articles.document_upload » déposent. AskThem se contente de ne pas
    /// proposer ce qu'il sait refusé.
    /// </summary>
    public class DepotInventaire : IDisposable
    {
        private readonly AppConfig _config;
        private readonly List<string> _etatsLiberes;
        private readonly bool _seulementLiberes;

        private InventoryApiService _api;
        private Dictionary<string, DocumentsArticle> _resume;

        public DepotInventaire(AppConfig config)
        {
            _config = config;
            _etatsLiberes = config != null && config.ReleasedStates != null
                ? config.ReleasedStates : new List<string>();
            _seulementLiberes = config == null || config.PublierSeulementLiberes;
            _resume = new Dictionary<string, DocumentsArticle>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Vrai si la session est ouverte et l'inventaire lisible.</summary>
        public bool Lisible { get { return _api != null && _api.Connected; } }

        /// <summary>Vrai si ce compte peut déposer. C'est le serveur qui l'a dit.</summary>
        public bool PeutPublier { get { return _api != null && _api.PeutDeposerDocuments; } }

        /// <summary>Ouvre la session. Sans elle, rien ne se lit ni ne se dépose.</summary>
        public bool Connecter(out string message)
        {
            message = "";
            if (Lisible) return true;

            string mdp = SecretStore.Load(InventoryApiService.SecretName);
            if (_config == null || string.IsNullOrWhiteSpace(_config.InventoryApiUrl)
                || string.IsNullOrWhiteSpace(_config.InventoryUser) || string.IsNullOrWhiteSpace(mdp))
            {
                message = "Inventaire non configuré : aucun document ne peut être lu ni déposé.";
                return false;
            }

            _api = new InventoryApiService();
            if (!_api.Connect(_config.InventoryApiUrl, _config.InventoryUser, mdp, out message))
            {
                _api.Dispose();
                _api = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Charge en un seul appel ce que l'inventaire possède pour ces références.
        ///
        /// C'est ce qui rend le dispositif tenable : une requête pour toute une demande, au
        /// lieu d'une par article. Rien du contenu n'est transféré à ce stade.
        /// </summary>
        public void Charger(List<string> references, Action<string> journal)
        {
            if (!Lisible || references == null || references.Count == 0) return;

            string message;
            _resume = _api.Resume(references, out message);

            if (journal != null && !string.IsNullOrWhiteSpace(message))
            {
                try { journal(message); }
                catch (Exception) { }
            }
        }

        /// <summary>Ce que l'inventaire sait de cet article, ou null.</summary>
        public DocumentsArticle Pour(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            DocumentsArticle d;
            return _resume.TryGetValue(reference.Trim(), out d) ? d : null;
        }

        // ------------------------------------------------------------------ lecture

        /// <summary>
        /// Rapatrie les documents destinés au fournisseur — plan, DXF, modèle.
        ///
        /// Le formulaire de contrôle n'en fait pas partie : il se remplit, part séparément,
        /// et ne concerne que les demandes de fabrication.
        /// </summary>
        public List<string> TelechargerPour(string reference, string dossierCible, Action<string> journal)
        {
            List<string> fichiers = new List<string>();
            DocumentsArticle d = Pour(reference);
            if (d == null || !Lisible) return fichiers;

            foreach (DocumentArticle doc in d.Documents)
            {
                if (!doc.IsCurrent || !TypeDocument.PourFournisseur(doc.Kind)) continue;

                string message;
                string chemin = _api.Telecharger(d.ArticleId, doc, dossierCible, out message);
                if (chemin != null) fichiers.Add(chemin);
                else Dire(journal, reference + " : " + message);
            }
            return fichiers;
        }

        /// <summary>Rapatrie le formulaire de contrôle, ou null s'il n'y en a pas.</summary>
        public string TelechargerControle(string reference, string dossierCible, Action<string> journal)
        {
            DocumentsArticle d = Pour(reference);
            if (d == null || !Lisible) return null;

            DocumentArticle doc = d.De(TypeDocument.Controle);
            if (doc == null) return null;

            string message;
            string chemin = _api.Telecharger(d.ArticleId, doc, dossierCible, out message);
            if (chemin == null) Dire(journal, reference + " : " + message);
            return chemin;
        }

        // ------------------------------------------------------------------ dépôt

        /// <summary>
        /// Dépose ou remplace les documents d'un article.
        ///
        /// Rien n'est réécrit inutilement : le serveur reconnaît un contenu identique et
        /// répond sans rien créer. Un article dont l'état n'est pas libéré n'est pas publié —
        /// il deviendrait joignable par un acheteur qui n'a aucun moyen de savoir qu'il ne
        /// doit pas partir.
        /// </summary>
        public void Publier(string reference, string revision, string etat,
                            List<string> fichiers, Action<string> journal)
        {
            Publier(reference, revision, "", etat, fichiers, journal);
        }

        /// <param name="dateRevision">
        /// Date d'émission de la révision, au format AAAA-MM-JJ. Vide si le plan ne la porte
        /// pas de façon lisible : mieux vaut « date inconnue » qu'une date inventée, qui
        /// fausserait l'ordre des révisions.
        /// </param>
        public void Publier(string reference, string revision, string dateRevision, string etat,
                            List<string> fichiers, Action<string> journal)
        {
            if (!Lisible || fichiers == null || fichiers.Count == 0) return;

            if (!PeutPublier)
            {
                Dire(journal, "Documents non déposés pour " + reference
                            + " : votre compte n'a pas le droit de publier.");
                return;
            }

            if (!EstLibere(etat))
            {
                Dire(journal, "Documents non déposés pour " + reference
                            + " — état « " + etat + " » hors des états libérés.");
                return;
            }

            DocumentsArticle d = Pour(reference);
            if (d == null || !d.Trouve || d.ArticleId <= 0)
            {
                Dire(journal, "ARTICLE INCONNU DE L'INVENTAIRE : " + reference
                            + ". Aucun document déposé — la fiche article est à créer côté "
                            + "inventaire, AskThem ne la crée jamais.");
                return;
            }

            int deposes = 0, remplaces = 0, inchanges = 0, echecs = 0, epargnes = 0;
            foreach (string chemin in fichiers)
            {
                if (string.IsNullOrWhiteSpace(chemin) || !File.Exists(chemin)) continue;

                string kind = TypeDocument.DapresFichier(chemin);
                if (kind == TypeDocument.Autre) continue;

                // On décide avant d'envoyer : republier un contenu identique est sans effet
                // côté serveur, mais coûte un aller-retour par document et par article.
                if (DejaEnPlace(d, kind, chemin, revision, dateRevision)) { epargnes++; continue; }

                string message;
                ResultatDepot r = _api.Deposer(d.ArticleId, kind, revision, dateRevision,
                                               chemin, out message);

                switch (r)
                {
                    case ResultatDepot.Depose: deposes++; break;
                    case ResultatDepot.Remplace: remplaces++; break;
                    case ResultatDepot.MetadonneeCorrigee: remplaces++; break;
                    case ResultatDepot.Inchange: inchanges++; break;
                    default:
                        echecs++;
                        Dire(journal, reference + " — " + TypeDocument.Libelle(kind) + " : " + message);
                        break;
                }
            }

            if (deposes + remplaces + inchanges + echecs + epargnes == 0) return;

            Dire(journal, "Inventaire : " + reference + " — " + deposes + " déposé(s), "
                        + remplaces + " remplacé(s), " + (inchanges + epargnes) + " déjà à jour"
                        + (echecs > 0 ? ", " + echecs + " échec(s)" : "") + ".");
        }

        /// <summary>
        /// Vrai si l'inventaire porte déjà exactement ce document.
        ///
        /// Trois choses doivent coïncider : le contenu, la révision et sa date. Un contenu
        /// identique sous une révision annoncée différemment doit être republié — c'est ainsi
        /// que la métadonnée se corrige, sans créer de nouvelle version.
        /// </summary>
        private bool DejaEnPlace(DocumentsArticle d, string kind, string chemin,
                                 string revision, string dateRevision)
        {
            DocumentArticle distant = d.De(kind);
            if (distant == null || string.IsNullOrWhiteSpace(distant.Sha256)) return false;

            string local = InventoryApiService.Empreinte(chemin);
            if (local == "" || !string.Equals(local, distant.Sha256, StringComparison.OrdinalIgnoreCase))
                return false;

            string revLocale = revision == null ? "" : revision.Trim();
            string revDistante = distant.Revision == null ? "" : distant.Revision.Trim();
            if (!string.Equals(revLocale, revDistante, StringComparison.OrdinalIgnoreCase)) return false;

            string dateLocale = dateRevision == null ? "" : dateRevision.Trim();
            string dateDistante = distant.RevisionDate == null ? "" : distant.RevisionDate.Trim();

            // Une date locale absente ne justifie pas de republier : on n'a rien de mieux à
            // proposer que ce qui est déjà en place.
            if (dateLocale == "") return true;
            return string.Equals(dateLocale, dateDistante, StringComparison.Ordinal);
        }

        /// <summary>Dépose le seul formulaire de contrôle, sous sa nature propre.</summary>
        public void PublierControle(string reference, string revision, string chemin, Action<string> journal)
        {
            PublierControle(reference, revision, "", chemin, journal);
        }

        public void PublierControle(string reference, string revision, string dateRevision,
                                    string chemin, Action<string> journal)
        {
            if (!Lisible || !PeutPublier) return;
            if (string.IsNullOrWhiteSpace(chemin) || !File.Exists(chemin)) return;

            DocumentsArticle d = Pour(reference);
            if (d == null || !d.Trouve || d.ArticleId <= 0) return;
            if (DejaEnPlace(d, TypeDocument.Controle, chemin, revision, dateRevision)) return;

            string message;
            ResultatDepot r = _api.Deposer(d.ArticleId, TypeDocument.Controle, revision,
                                           dateRevision, chemin, out message);
            if (r == ResultatDepot.Depose || r == ResultatDepot.Remplace
                || r == ResultatDepot.MetadonneeCorrigee)
                Dire(journal, "Inventaire : contrôle de " + reference + " déposé.");
            else if (r != ResultatDepot.Inchange)
                Dire(journal, "Contrôle de " + reference + " non déposé : " + message);
        }

        // ------------------------------------------------------------------ utilitaires

        /// <summary>
        /// Vrai si cet état autorise la publication.
        ///
        /// Un état vide passe : beaucoup de cartes de données n'en portent pas, et refuser
        /// viderait la base de la moitié de son contenu. C'est un état explicitement « en
        /// développement » qu'on refuse.
        /// </summary>
        public bool EstLibere(string etat)
        {
            if (!_seulementLiberes) return true;
            if (string.IsNullOrWhiteSpace(etat)) return true;

            string valeur = etat.Trim();
            foreach (string libere in _etatsLiberes)
                if (string.Equals(valeur, libere, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void Dire(Action<string> journal, string message)
        {
            LogService.Write(message);
            if (journal != null)
            {
                try { journal(message); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            try { if (_api != null) _api.Dispose(); }
            catch (Exception) { }
            finally { _api = null; }
        }
    }
}
