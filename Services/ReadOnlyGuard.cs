using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AskThem.Services
{
    /// <summary>
    /// Garde-fou de transport : AskThem consulte l'inventaire, il ne le modifie jamais.
    ///
    /// Toute requête qui n'est pas une lecture est refusée ici, avant même de partir
    /// sur le réseau. L'unique exception est l'ouverture de session, nécessairement un
    /// POST, et limitée à l'adresse exacte de connexion.
    ///
    /// Le contrôle est placé au niveau du transport, et non dans le code appelant :
    /// ainsi une modification ultérieure du programme ne peut pas écrire dans
    /// l'inventaire par inadvertance, même si quelqu'un ajoutait un appel.
    /// </summary>
    public sealed class ReadOnlyGuard : DelegatingHandler
    {
        private readonly string _urlConnexion;

        public ReadOnlyGuard(HttpMessageHandler inner, string urlConnexion)
            : base(inner)
        {
            _urlConnexion = urlConnexion == null ? "" : urlConnexion;
        }

        /// <summary>Vrai si cette requête est une lecture, ou l'ouverture de session.</summary>
        public bool EstAutorisee(HttpMethod methode, Uri adresse)
        {
            if (methode == HttpMethod.Get || methode == HttpMethod.Head) return true;
            if (methode != HttpMethod.Post || adresse == null) return false;
            return string.Equals(adresse.AbsoluteUri, _urlConnexion, StringComparison.OrdinalIgnoreCase);
        }

        private void Verifier(HttpRequestMessage requete)
        {
            if (EstAutorisee(requete.Method, requete.RequestUri)) return;
            throw new InvalidOperationException(
                "AskThem ne modifie jamais l'inventaire : requête " + requete.Method
                + " vers " + (requete.RequestUri == null ? "(inconnue)" : requete.RequestUri.AbsoluteUri)
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
