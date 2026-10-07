using System;
using System.Net;
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
            // Les lectures restent sur le serveur de l'inventaire : ce canal porte le cookie
            // de session, et n'a rien à demander ailleurs.
            if (methode == HttpMethod.Get || methode == HttpMethod.Head)
                return adresse == null || !adresse.IsAbsoluteUri || MemeServeur(adresse);
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

        /// <summary>Même schéma, même hôte, même port que l'adresse de connexion.</summary>
        private bool MemeServeur(Uri adresse)
        {
            Uri connexion;
            if (!Uri.TryCreate(_urlConnexion, UriKind.Absolute, out connexion)) return false;
            return string.Equals(adresse.Scheme, connexion.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(adresse.Host, connexion.Host, StringComparison.OrdinalIgnoreCase)
                && adresse.Port == connexion.Port;
        }

        private static bool EstLecture(HttpMethod methode)
        {
            return methode == HttpMethod.Get || methode == HttpMethod.Head;
        }

        private static bool EstRedirection(HttpResponseMessage reponse)
        {
            if (reponse == null || reponse.Headers.Location == null) return false;
            int code = (int)reponse.StatusCode;
            return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
        }

        /// <summary>
        /// La requête suivante d'une redirection, vérifiée comme la première.
        ///
        /// Les redirections ne sont plus suivies par le transport : il les suivait SOUS le
        /// garde-fou, si bien qu'une réponse 307 renvoyait un dépôt, ou le mot de passe de la
        /// connexion, vers une adresse que personne n'avait contrôlée. Seules les lectures
        /// sont suivies, ici, et chaque étape repasse par Verifier.
        /// </summary>
        private HttpRequestMessage Suivante(HttpRequestMessage precedente, HttpResponseMessage reponse)
        {
            Uri cible = reponse.Headers.Location;
            if (!cible.IsAbsoluteUri) cible = new Uri(precedente.RequestUri, cible);

            HttpRequestMessage suivante = new HttpRequestMessage(precedente.Method, cible);
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.IEnumerable<string>> h
                     in precedente.Headers)
                suivante.Headers.TryAddWithoutValidation(h.Key, h.Value);
            Verifier(suivante);
            return suivante;
        }

        private const int SautsMax = 5;

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
            HttpResponseMessage reponse = base.Send(request, cancellationToken);
            for (int saut = 0; saut < SautsMax && EstLecture(request.Method) && EstRedirection(reponse); saut++)
            {
                HttpRequestMessage suivante = Suivante(request, reponse);
                reponse.Dispose();
                request = suivante;
                reponse = base.Send(request, cancellationToken);
            }
            return reponse;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Verifier(request);
            HttpResponseMessage reponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            for (int saut = 0; saut < SautsMax && EstLecture(request.Method) && EstRedirection(reponse); saut++)
            {
                HttpRequestMessage suivante = Suivante(request, reponse);
                reponse.Dispose();
                request = suivante;
                reponse = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            return reponse;
        }
    }
}
