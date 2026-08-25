using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AskThem.Services
{
    /// <summary>
    /// Interroge l'inventaire par son API. L'authentification se fait par session :
    /// une connexion dépose un cookie, réutilisé pour les requêtes suivantes.
    /// Le mot de passe n'est jamais conservé ici : il est fourni à la connexion
    /// et provient du magasin chiffré de Windows.
    /// </summary>
    public class InventoryApiService : IDisposable
    {
        /// <summary>Nom sous lequel le mot de passe est rangé dans le magasin chiffré.</summary>
        public const string SecretName = "inventaire";

        private HttpClient _client;
        private CookieContainer _cookies;
        private string _base;
        private string _urlConnexion;

        public bool Connected { get; private set; }

        /// <summary>Ouvre une session. Retourne false et un message explicite en cas d'échec.</summary>
        public bool Connect(string baseUrl, string user, string password, out string message)
        {
            message = "";
            Connected = false;

            if (string.IsNullOrWhiteSpace(baseUrl)) { message = "Aucune adresse d'inventaire configurée."; return false; }
            if (string.IsNullOrWhiteSpace(user)) { message = "Aucun utilisateur d'inventaire configuré."; return false; }
            if (string.IsNullOrWhiteSpace(password)) { message = "Aucun mot de passe enregistré pour l'inventaire."; return false; }

            _base = baseUrl.TrimEnd('/');
            try
            {
                _cookies = new CookieContainer();
                HttpClientHandler handler = new HttpClientHandler();
                handler.CookieContainer = _cookies;
                handler.UseCookies = true;
                handler.AllowAutoRedirect = true;

                // Toutes les requêtes passent par le garde-fou : rien ne peut écrire.
                _urlConnexion = _base + "/auth/login";
                _client = new HttpClient(new ReadOnlyGuard(handler, _urlConnexion));
                _client.Timeout = TimeSpan.FromSeconds(30);
                _client.DefaultRequestHeaders.Add("User-Agent", "AskThem");

                string corps = JsonSerializer.Serialize(new Dictionary<string, string> {
                    { "username", user }, { "password", password } });

                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, _urlConnexion);
                req.Content = new StringContent(corps, Encoding.UTF8, "application/json");
                HttpResponseMessage rep = _client.Send(req);

                if (!rep.IsSuccessStatusCode)
                {
                    message = "Connexion à l'inventaire refusée (" + (int)rep.StatusCode + "). "
                            + "Vérifiez l'utilisateur et le mot de passe enregistrés.";
                    return false;
                }
                Connected = true;
                message = "Connecté à l'inventaire en tant que " + user + ".";
                return true;
            }
            catch (Exception ex)
            {
                message = "Inventaire injoignable : " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Charge tous les articles en une requête et les indexe par référence interne.
        /// Une seule requête vaut mieux qu'un appel par article : une nomenclature en
        /// compte plusieurs centaines.
        /// </summary>
        public Dictionary<string, InventoryService.Entry> LoadAll(out string message)
        {
            Dictionary<string, InventoryService.Entry> table =
                new Dictionary<string, InventoryService.Entry>(StringComparer.OrdinalIgnoreCase);
            message = "";

            if (!Connected) { message = "Pas de session ouverte sur l'inventaire."; return table; }

            try
            {
                HttpResponseMessage rep = Get("/articles");
                if (!rep.IsSuccessStatusCode)
                {
                    message = "Lecture des articles refusée (" + (int)rep.StatusCode + ").";
                    return table;
                }

                string json = rep.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                int avecAncienne = 0;
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    foreach (JsonElement article in Articles(doc.RootElement))
                    {
                        InventoryService.Entry e = new InventoryService.Entry();
                        e.InternalRef = Texte(article, "internal_ref");
                        if (e.InternalRef == "") continue;
                        e.OldRef = Texte(article, "old_ref");
                        e.SupplierRef = Texte(article, "supplier_ref");
                        if (e.SupplierRef == "") e.SupplierRef = Texte(article, "manufacturer_ref");
                        e.Supplier = Texte(article, "supplier");
                        if (e.Supplier == "") e.Supplier = Texte(article, "supplier_name");
                        if (e.OldRef != "") avecAncienne++;
                        table[e.InternalRef] = e;
                    }
                }
                message = table.Count + " article(s) lus dans l'inventaire, dont "
                        + avecAncienne + " avec une ancienne référence.";
            }
            catch (Exception ex)
            {
                message = "Lecture de l'inventaire impossible : " + ex.Message;
                LogService.Write(message);
            }
            return table;
        }

        /// <summary>
        /// Unique primitive de lecture. Aucun autre verbe n'est disponible dans cette
        /// classe : l'application n'a pas les moyens d'écrire dans l'inventaire.
        /// </summary>
        private HttpResponseMessage Get(string chemin)
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, _base + chemin);
            return _client.Send(req);
        }

        /// <summary>Compte connecté, tel que l'inventaire le voit. Lecture seule.</summary>
        public string WhoAmI()
        {
            if (!Connected) return "";
            try
            {
                HttpResponseMessage rep = Get("/auth/me");
                if (!rep.IsSuccessStatusCode) return "";
                string json = rep.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement racine = doc.RootElement;
                    JsonElement u;
                    if (racine.TryGetProperty("user", out u)) racine = u;
                    string nom = Texte(racine, "username");
                    string role = Texte(racine, "role");
                    if (role == "") role = Texte(racine, "roles");
                    if (nom == "") return "";
                    return role == "" ? nom : nom + " (" + role + ")";
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>La réponse peut être un tableau, ou un objet enveloppant la liste.</summary>
        private static IEnumerable<JsonElement> Articles(JsonElement racine)
        {
            if (racine.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement e in racine.EnumerateArray()) yield return e;
                yield break;
            }
            if (racine.ValueKind != JsonValueKind.Object) yield break;

            foreach (string cle in new string[] { "items", "results", "data", "articles" })
            {
                JsonElement liste;
                if (racine.TryGetProperty(cle, out liste) && liste.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement e in liste.EnumerateArray()) yield return e;
                    yield break;
                }
            }
        }

        private static string Texte(JsonElement objet, string nom)
        {
            JsonElement v;
            if (!objet.TryGetProperty(nom, out v)) return "";
            if (v.ValueKind == JsonValueKind.String) return (v.GetString() ?? "").Trim();
            if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            if (v.ValueKind == JsonValueKind.Object)
            {
                // Un fournisseur peut être renvoyé sous forme d'objet imbriqué.
                JsonElement n;
                if (v.TryGetProperty("name", out n) && n.ValueKind == JsonValueKind.String)
                    return (n.GetString() ?? "").Trim();
            }
            return "";
        }

        public void Dispose()
        {
            try { if (_client != null) _client.Dispose(); }
            catch (Exception) { }
            finally { _client = null; Connected = false; }
        }
    }
}
