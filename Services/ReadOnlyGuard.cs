using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AskThem.Services
{
    /// <summary>
    /// Garde-fou de transport : AskThem lit l'inventaire, et n'y écrit que les documents
    /// d'article, quand cette autorisation a été explicitement accordée.
    ///
    /// Le contrôle est placé au niveau du transport, et non dans le code appelant : une
    /// évolution ultérieure du programme ne peut donc pas écrire par inadvertance, même si
    /// quelqu'un ajoutait un appel. Trois choses seulement passent :
    ///
    ///   — toute lecture, GET et HEAD ;
    ///   — l'ouverture de session, un POST vers l'adresse exacte de connexion ;
    ///   — le dépôt et la suppression d'un document d'article, sur des adresses dont la
    ///     forme est vérifiée caractère par caractère, et seulement si l'appelant l'a
    ///     demandé et que le serveur reconnaît la permission correspondante.
    ///
    /// Tout le reste est refusé avant même de partir sur le réseau. En particulier, aucune
    /// écriture n'est possible sur les articles eux-mêmes, les fournisseurs, le stock ou les
    /// commandes : ce ne sont pas des adresses de documents.
    /// </summary>
    public sealed class ReadOnlyGuard : DelegatingHandler
    {
        /// <summary>
        /// Adresses de documents d'article, et rien d'autre.
        ///
        /// « /articles/&lt;nombre&gt;/documents » pour un dépôt, éventuellement suivi de
        /// « /&lt;nombre&gt; » pour une suppression. Les identifiants doivent être purement
        /// numériques : aucune remontée de chemin, aucun paramètre, aucune autre ressource.
        /// </summary>
        private static readonly Regex FormeDocument =
            new Regex(@"^/articles/\d+/documents(/\d+)?$", RegexOptions.Compiled);

        private readonly string _urlConnexion;
        private readonly string _base;
        private readonly bool _depotAutorise;

        public ReadOnlyGuard(HttpMessageHandler inner, string urlConnexion)
            : this(inner, urlConnexion, "", false)
        {
        }

        /// <param name="baseApi">Racine de l'API, par exemple http://serveur/api/v1</param>
        /// <param name="depotAutorise">
        /// Vrai seulement si l'utilisateur possède la permission de dépôt côté serveur.
        /// À faux, le garde se comporte comme avant : lecture seule stricte.
        /// </param>
        public ReadOnlyGuard(HttpMessageHandler inner, string urlConnexion,
                             string baseApi, bool depotAutorise)
            : base(inner)
        {
            _urlConnexion = urlConnexion == null ? "" : urlConnexion;
            _base = baseApi == null ? "" : baseApi.TrimEnd('/');
            _depotAutorise = depotAutorise;
        }

        /// <summary>Vrai si le dépôt de documents est ouvert sur ce canal.</summary>
        public bool DepotAutorise { get { return _depotAutorise; } }

        /// <summary>Vrai si cette requête est permise.</summary>
        public bool EstAutorisee(HttpMethod methode, Uri adresse)
        {
            if (methode == HttpMethod.Get || methode == HttpMethod.Head) return true;
            if (adresse == null) return false;

            if (methode == HttpMethod.Post
                && string.Equals(adresse.AbsoluteUri, _urlConnexion, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!_depotAutorise) return false;
            if (methode != HttpMethod.Post && methode != HttpMethod.Delete) return false;

            return EstAdresseDeDocument(adresse);
        }

        /// <summary>
        /// Vrai si l'adresse désigne exactement un document d'article sous notre API.
        ///
        /// On compare l'adresse absolue reconstruite, et non le chemin brut : une requête
        /// vers un autre hôte, ou vers une autre racine que celle de notre inventaire, ne
        /// doit jamais passer, quelle que soit la forme de son chemin.
        /// </summary>
        private bool EstAdresseDeDocument(Uri adresse)
        {
            if (_base.Length == 0) return false;

            Uri racine;
            if (!Uri.TryCreate(_base, UriKind.Absolute, out racine)) return false;
            if (!string.Equals(adresse.Scheme, racine.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(adresse.Host, racine.Host, StringComparison.OrdinalIgnoreCase)) return false;
            if (adresse.Port != racine.Port) return false;

            // Ni requête, ni fragment : une adresse de document n'en porte pas.
            if (!string.IsNullOrEmpty(adresse.Query)) return false;
            if (!string.IsNullOrEmpty(adresse.Fragment)) return false;

            string chemin = adresse.AbsolutePath;
            string prefixe = racine.AbsolutePath.TrimEnd('/');
            if (!chemin.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase)) return false;

            return FormeDocument.IsMatch(chemin.Substring(prefixe.Length));
        }

        private void Verifier(HttpRequestMessage requete)
        {
            if (EstAutorisee(requete.Method, requete.RequestUri)) return;
            throw new InvalidOperationException(
                "AskThem n'écrit dans l'inventaire que les documents d'article : requête "
                + requete.Method + " vers "
                + (requete.RequestUri == null ? "(inconnue)" : requete.RequestUri.AbsoluteUri)
                + " refusée.");
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Verifier(request);
            return base.Send(request, cancellationToken);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Verifier(request);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
