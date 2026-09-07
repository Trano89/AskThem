using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AskThem.Models;

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

        /// <summary>Permission exigée par le serveur pour déposer un document d'article.</summary>
        public const string PermissionDepot = "articles.document_upload";

        private HttpClient _client;
        private CookieContainer _cookies;
        private string _base;
        private string _urlConnexion;

        public bool Connected { get; private set; }

        /// <summary>Droits que le serveur reconnaît à ce compte. Vide tant qu'on n'est pas connecté.</summary>
        public List<string> Permissions { get; private set; }

        /// <summary>
        /// Vrai si ce compte peut déposer des documents d'article.
        ///
        /// C'est le serveur qui tranche, jamais un réglage local : le programme se contente
        /// de ne pas proposer ce qu'il sait refusé, et le serveur refuserait de toute façon.
        /// </summary>
        public bool PeutDeposerDocuments
        {
            get { return Permissions != null && Permissions.Contains(PermissionDepot); }
        }

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
                Permissions = LirePermissions();

                // Le canal d'ecriture ne s'ouvre qu'apres que le serveur a confirme le droit.
                // Tant qu'il ne l'a pas fait, le garde-fou reste en lecture seule stricte.
                if (PeutDeposerDocuments)
                {
                    HttpClientHandler ouvert = new HttpClientHandler();
                    ouvert.CookieContainer = _cookies;
                    ouvert.UseCookies = true;
                    ouvert.AllowAutoRedirect = true;

                    HttpClient ancien = _client;
                    _client = new HttpClient(new ReadOnlyGuard(ouvert, _urlConnexion, _base, true));
                    _client.Timeout = TimeSpan.FromMinutes(5);   // un depot peut peser plusieurs Mo
                    _client.DefaultRequestHeaders.Add("User-Agent", "AskThem");
                    try { ancien.Dispose(); }
                    catch (Exception) { }
                }

                message = "Connecté à l'inventaire en tant que " + user
                        + (PeutDeposerDocuments ? " (dépôt de documents autorisé)." : ".");
                return true;
            }
            catch (Exception ex)
            {
                message = "Inventaire injoignable : " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// <summary>
        /// Toutes les fiches fournisseur de l'inventaire, par identifiant. Sert à lier une
        /// fois pour toutes un fournisseur d'AskThem à sa fiche. Les adresses email n'y
        /// figurent pas : elles restent saisies dans AskThem.
        /// </summary>
        public Dictionary<int, string> LoadSuppliers(out string message)
        {
            Dictionary<int, string> fiches = new Dictionary<int, string>();
            message = "";
            if (!Connected) { message = "Pas de session ouverte sur l'inventaire."; return fiches; }

            try
            {
                HttpResponseMessage rep = Get("/suppliers");
                if (!rep.IsSuccessStatusCode)
                {
                    message = "Lecture des fournisseurs refusée (" + (int)rep.StatusCode + ").";
                    return fiches;
                }
                string json = rep.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    foreach (JsonElement f in Articles(doc.RootElement))
                    {
                        if (f.ValueKind != JsonValueKind.Object) continue;
                        int id = Entier(f, "id");
                        string nom = Texte(f, "name");
                        if (id != 0 && nom != "") fiches[id] = nom;
                    }
                }
                message = fiches.Count + " fournisseur(s) lus dans l'inventaire.";
            }
            catch (Exception ex)
            {
                message = "Lecture des fournisseurs impossible : " + ex.Message;
            }
            return fiches;
        }

        /// <summary>
        /// Reprend les fournisseurs déclarés sur l'article.
        ///
        /// Ils ne sont pas au niveau de l'article mais dans un tableau « suppliers », chaque
        /// entrée portant l'identifiant de la fiche, la référence de l'article chez ce
        /// fournisseur, et parfois la référence du fabricant.
        /// </summary>
        private static void LireFournisseurs(JsonElement article, InventoryService.Entry e)
        {
            JsonElement tableau;
            if (!article.TryGetProperty("suppliers", out tableau)) return;
            if (tableau.ValueKind != JsonValueKind.Array) return;

            foreach (JsonElement lien in tableau.EnumerateArray())
            {
                if (lien.ValueKind != JsonValueKind.Object) continue;

                InventoryService.Fournisseur f = new InventoryService.Fournisseur();
                f.Id = Entier(lien, "supplier_id");
                f.Reference = Texte(lien, "supplier_ref");
                f.ReferenceFabricant = Texte(lien, "manufacturer_ref");

                JsonElement fiche;
                if (lien.TryGetProperty("supplier", out fiche) && fiche.ValueKind == JsonValueKind.Object)
                {
                    f.Nom = Texte(fiche, "name");
                    if (f.Id == 0) f.Id = Entier(fiche, "id");
                }
                if (f.Id != 0 || f.Nom != "") e.Fournisseurs.Add(f);
            }
        }

        /// <summary>Nombre d'une propriété, ou zéro si elle manque ou n'en est pas un.</summary>
        private static double Nombre(JsonElement objet, string nom)
        {
            JsonElement v;
            if (!objet.TryGetProperty(nom, out v)) return 0;
            if (v.ValueKind != JsonValueKind.Number) return 0;
            double d;
            return v.TryGetDouble(out d) ? d : 0;
        }

        /// <summary>Entier d'une propriété, ou zéro si elle manque ou n'en est pas un.</summary>
        private static int Entier(JsonElement objet, string nom)
        {
            JsonElement v;
            if (!objet.TryGetProperty(nom, out v)) return 0;
            if (v.ValueKind != JsonValueKind.Number) return 0;
            int i;
            return v.TryGetInt32(out i) ? i : 0;
        }

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
                        e.Designation = Texte(article, "name");
                        e.PrixUnitaire = Nombre(article, "unit_price");
                        e.Monnaie = Texte(article, "currency");
                        e.Stock = Nombre(article, "current_qty");
                        LireFournisseurs(article, e);
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

        // ==================================================================
        // Documents d'article
        // ==================================================================

        /// <summary>Droits du compte connecté, tels que le serveur les déclare.</summary>
        private List<string> LirePermissions()
        {
            List<string> droits = new List<string>();
            try
            {
                HttpResponseMessage rep = Get("/auth/me");
                if (!rep.IsSuccessStatusCode) return droits;

                using (JsonDocument doc = JsonDocument.Parse(
                           rep.Content.ReadAsStringAsync().GetAwaiter().GetResult()))
                {
                    JsonElement racine = doc.RootElement;
                    JsonElement u;
                    if (racine.TryGetProperty("user", out u)) racine = u;

                    JsonElement perms;
                    if (!racine.TryGetProperty("permissions", out perms)) return droits;
                    if (perms.ValueKind != JsonValueKind.Array) return droits;

                    foreach (JsonElement e in perms.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String) droits.Add(e.GetString());
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Droits de l'inventaire illisibles : " + ex.Message);
            }
            return droits;
        }

        /// <summary>
        /// Ce que l'inventaire possède, pour un lot de références, en un seul appel.
        ///
        /// C'est le point d'entrée qui rend le dispositif tenable : savoir en une requête ce
        /// qui existe pour deux cents articles, sans transférer le moindre octet de contenu.
        /// </summary>
        public Dictionary<string, DocumentsArticle> Resume(List<string> references, out string message)
        {
            message = "";
            Dictionary<string, DocumentsArticle> table =
                new Dictionary<string, DocumentsArticle>(StringComparer.OrdinalIgnoreCase);
            if (!Connected || references == null || references.Count == 0) return table;

            // Le serveur accepte 500 références par appel. Toute une demande, et souvent
            // toute une campagne, tiennent donc en une ou deux requêtes.
            const int parLot = 500;
            for (int depart = 0; depart < references.Count; depart += parLot)
            {
                List<string> lot = references.GetRange(depart, Math.Min(parLot, references.Count - depart));
                try
                {
                    HttpResponseMessage rep = Get("/articles/documents/summary?refs="
                                                + Uri.EscapeDataString(string.Join(",", lot)));
                    if (!rep.IsSuccessStatusCode)
                    {
                        message = "Documents illisibles (" + (int)rep.StatusCode + ").";
                        continue;
                    }

                    using (JsonDocument doc = JsonDocument.Parse(
                               rep.Content.ReadAsStringAsync().GetAwaiter().GetResult()))
                    {
                        JsonElement res;
                        if (!doc.RootElement.TryGetProperty("results", out res)) continue;
                        foreach (JsonElement x in res.EnumerateArray())
                        {
                            DocumentsArticle d = LireDocumentsArticle(x);
                            if (d != null && d.Reference != "") table[d.Reference] = d;
                        }
                    }
                }
                catch (Exception ex)
                {
                    message = "Documents illisibles : " + ex.Message;
                }
            }
            return table;
        }

        private DocumentsArticle LireDocumentsArticle(JsonElement x)
        {
            DocumentsArticle d = new DocumentsArticle();
            d.Reference = Texte(x, "ref");
            if (d.Reference == "") d.Reference = Texte(x, "internal_ref");
            d.ArticleId = Entier(x, "article_id");

            JsonElement t;
            d.Trouve = x.TryGetProperty("found", out t)
                       && (t.ValueKind == JsonValueKind.True
                           || (t.ValueKind == JsonValueKind.String && t.GetString() == "true"));

            JsonElement docs;
            if (x.TryGetProperty("documents", out docs) && docs.ValueKind == JsonValueKind.Array)
                foreach (JsonElement e in docs.EnumerateArray())
                    d.Documents.Add(LireDocument(e));

            return d;
        }

        private DocumentArticle LireDocument(JsonElement e)
        {
            DocumentArticle d = new DocumentArticle();
            d.Id = Entier(e, "id");
            d.Kind = Texte(e, "kind");
            d.Revision = Texte(e, "revision");
            d.RevisionDate = Texte(e, "revision_date");
            d.Filename = Texte(e, "filename");
            d.ContentType = Texte(e, "content_type");
            d.Sha256 = Texte(e, "sha256");
            d.UploadedByUsername = Texte(e, "uploaded_by_username");
            if (d.UploadedByUsername == "") d.UploadedByUsername = Texte(e, "uploaded_by");

            JsonElement v;
            if (e.TryGetProperty("size_bytes", out v) && v.ValueKind == JsonValueKind.Number)
                d.SizeBytes = v.GetInt64();

            DateTime quand;
            if (DateTime.TryParse(Texte(e, "uploaded_at"), out quand)) d.UploadedAt = quand;

            d.IsCurrent = true;
            if (e.TryGetProperty("is_current", out v) && v.ValueKind == JsonValueKind.False)
                d.IsCurrent = false;

            if (d.Kind == "") d.Kind = TypeDocument.Autre;
            return d;
        }

        /// <summary>Documents courants d'un article, ou une liste vide.</summary>
        public List<DocumentArticle> Documents(int articleId, out string message)
        {
            message = "";
            List<DocumentArticle> liste = new List<DocumentArticle>();
            if (!Connected || articleId <= 0) return liste;
            try
            {
                HttpResponseMessage rep = Get("/articles/" + articleId + "/documents");
                if (!rep.IsSuccessStatusCode)
                {
                    message = "Documents illisibles (" + (int)rep.StatusCode + ").";
                    return liste;
                }
                using (JsonDocument doc = JsonDocument.Parse(
                           rep.Content.ReadAsStringAsync().GetAwaiter().GetResult()))
                {
                    JsonElement racine = doc.RootElement;
                    JsonElement items;
                    if (racine.ValueKind != JsonValueKind.Array
                        && racine.TryGetProperty("items", out items)) racine = items;
                    if (racine.ValueKind != JsonValueKind.Array) return liste;

                    foreach (JsonElement e in racine.EnumerateArray()) liste.Add(LireDocument(e));
                }
            }
            catch (Exception ex)
            {
                message = "Documents illisibles : " + ex.Message;
            }
            return liste;
        }

        /// <summary>
        /// Rapatrie un document dans un dossier de travail. Renvoie le chemin écrit, ou null.
        ///
        /// Le nom d'origine est conservé : c'est lui que le fournisseur verra en pièce jointe.
        /// </summary>
        public string Telecharger(int articleId, DocumentArticle document, string dossierCible,
                                  out string message)
        {
            message = "";
            if (!Connected || articleId <= 0 || document == null) return null;
            try
            {
                Directory.CreateDirectory(dossierCible);
                HttpResponseMessage rep = Get("/articles/" + articleId + "/documents/"
                                            + document.Id + "/download");
                if (!rep.IsSuccessStatusCode)
                {
                    message = "Téléchargement refusé (" + (int)rep.StatusCode + ") pour "
                            + document.Filename + ".";
                    return null;
                }

                string nom = string.IsNullOrWhiteSpace(document.Filename)
                    ? "document_" + document.Id : document.Filename;
                string cible = Path.Combine(dossierCible, NomSur(nom));

                using (Stream flux = rep.Content.ReadAsStream())
                using (FileStream sortie = File.Create(cible))
                    flux.CopyTo(sortie);

                return cible;
            }
            catch (Exception ex)
            {
                message = "Téléchargement impossible : " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Dépose un document sur un article.
        ///
        /// Si le serveur possède déjà le même contenu pour cette nature, il répond sans rien
        /// créer : on évite ainsi de réécrire à chaque demande ce qui n'a pas bougé. Le
        /// remplacement ne détruit rien — l'ancien document reste consultable.
        /// </summary>
        public ResultatDepot Deposer(int articleId, string kind, string revision, string chemin,
                                     out string message)
        {
            return Deposer(articleId, kind, revision, "", chemin, out message);
        }

        /// <param name="revisionDate">
        /// Date d'émission de la révision, au format AAAA-MM-JJ. Sans elle, le document
        /// s'affiche « date inconnue » et ne peut plus être situé dans l'ordre des révisions.
        /// </param>
        public ResultatDepot Deposer(int articleId, string kind, string revision,
                                     string revisionDate, string chemin, out string message)
        {
            message = "";
            if (!Connected) { message = "Inventaire non connecté."; return ResultatDepot.Echec; }
            if (!PeutDeposerDocuments)
            {
                message = "Votre compte n'a pas le droit de déposer des documents ("
                        + PermissionDepot + ").";
                return ResultatDepot.SansDroit;
            }
            if (articleId <= 0) { message = "Article inconnu de l'inventaire."; return ResultatDepot.Refuse; }
            if (string.IsNullOrWhiteSpace(chemin) || !File.Exists(chemin))
            {
                message = "Fichier introuvable : " + chemin;
                return ResultatDepot.Echec;
            }

            try
            {
                using (MultipartFormDataContent forme = new MultipartFormDataContent())
                using (FileStream flux = File.OpenRead(chemin))
                {
                    StreamContent fichier = new StreamContent(flux);
                    fichier.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    forme.Add(fichier, "file", Path.GetFileName(chemin));
                    forme.Add(new StringContent(kind, Encoding.UTF8), "kind");
                    if (!string.IsNullOrWhiteSpace(revision))
                        forme.Add(new StringContent(revision.Trim(), Encoding.UTF8), "revision");
                    if (!string.IsNullOrWhiteSpace(revisionDate))
                        forme.Add(new StringContent(revisionDate.Trim(), Encoding.UTF8), "revision_date");

                    HttpRequestMessage req = new HttpRequestMessage(
                        HttpMethod.Post, _base + "/articles/" + articleId + "/documents");
                    req.Content = forme;

                    HttpResponseMessage rep = _client.Send(req);
                    string corps = rep.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (!rep.IsSuccessStatusCode)
                    {
                        // Le message du serveur est repris tel quel : c'est lui qui sait
                        // pourquoi il refuse, et le deviner ferait perdre du temps.
                        message = "Dépôt refusé (" + (int)rep.StatusCode + ") : " + Court(corps);
                        if ((int)rep.StatusCode == 403) return ResultatDepot.SansDroit;
                        return ResultatDepot.Refuse;
                    }

                    return Verdict(corps);
                }
            }
            catch (Exception ex)
            {
                message = "Dépôt impossible : " + ex.Message;
                return ResultatDepot.Echec;
            }
        }

        /// <summary>Ce que le serveur dit avoir fait du document déposé.</summary>
        private static ResultatDepot Verdict(string corps)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(corps))
                {
                    JsonElement racine = doc.RootElement;
                    JsonElement v;

                    if (racine.TryGetProperty("unchanged", out v) && v.ValueKind == JsonValueKind.True)
                    {
                        // Le contenu était déjà là, mais la révision ou sa date ont pu être
                        // corrigées : ce n'est pas la même chose que « rien à faire ».
                        JsonElement maj;
                        if (racine.TryGetProperty("metadata_updated", out maj)
                            && maj.ValueKind == JsonValueKind.True)
                            return ResultatDepot.MetadonneeCorrigee;
                        return ResultatDepot.Inchange;
                    }
                    if (racine.TryGetProperty("superseded_id", out v)
                        && v.ValueKind == JsonValueKind.Number)
                        return ResultatDepot.Remplace;
                }
            }
            catch (Exception) { }
            return ResultatDepot.Depose;
        }

        /// <summary>Empreinte du contenu d'un fichier, au format rendu par le serveur.</summary>
        public static string Empreinte(string chemin)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream flux = File.OpenRead(chemin))
                {
                    byte[] somme = sha.ComputeHash(flux);
                    StringBuilder hex = new StringBuilder(64);
                    foreach (byte b in somme) hex.Append(b.ToString("x2"));
                    return hex.ToString();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string Court(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 220 ? s.Substring(0, 220) + "…" : s;
        }

        private static string NomSur(string nom)
        {
            StringBuilder sb = new StringBuilder(nom.Length);
            char[] interdits = Path.GetInvalidFileNameChars();
            foreach (char c in nom) sb.Append(Array.IndexOf(interdits, c) >= 0 ? '_' : c);
            string net = sb.ToString().Trim();
            return net.Length == 0 ? "document" : net;
        }

        public void Dispose()
        {
            try { if (_client != null) _client.Dispose(); }
            catch (Exception) { }
            finally { _client = null; Connected = false; }
        }
    }
}
