using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AskThem.Services
{
    /// <summary>
    /// Recherche d'une nouvelle version publiée sur GitHub, puis remplacement
    /// de l'exécutable en place. Rien n'est envoyé : seule la dernière
    /// publication du dépôt est lue.
    /// </summary>
    public static class UpdateService
    {
        /// <summary>Résultat d'une recherche de mise à jour.</summary>
        public class UpdateInfo
        {
            public bool Available;
            public string CurrentVersion = "";
            public string LatestVersion = "";
            public string DownloadUrl = "";
            public string PageUrl = "";
            public string Message = "";
        }

        /// <summary>Version de l'exécutable en cours, sans métadonnée de compilation.</summary>
        public static string CurrentVersion()
        {
            try
            {
                Assembly a = Assembly.GetExecutingAssembly();
                AssemblyInformationalVersionAttribute info =
                    (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                        a, typeof(AssemblyInformationalVersionAttribute));
                string v = info != null ? info.InformationalVersion : a.GetName().Version.ToString();
                int plus = v.IndexOf('+');
                if (plus > 0) v = v.Substring(0, plus);
                return v;
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }

        /// <summary>Compare deux versions du type 1.2.3. Positif si a est plus récente que b.</summary>
        public static int Compare(string a, string b)
        {
            Version va, vb;
            if (!Version.TryParse(Clean(a), out va)) va = new Version(0, 0, 0);
            if (!Version.TryParse(Clean(b), out vb)) vb = new Version(0, 0, 0);
            return va.CompareTo(vb);
        }

        private static string Clean(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "0.0.0";
            v = v.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            return v;
        }

        /// <summary>
        /// Interroge la dernière publication du dépôt. Appel bloquant : à lancer
        /// depuis un thread d'arrière-plan. N'échoue jamais bruyamment.
        /// </summary>
        public static UpdateInfo Check(string repository)
        {
            UpdateInfo r = new UpdateInfo();
            r.CurrentVersion = CurrentVersion();

            if (string.IsNullOrWhiteSpace(repository))
            {
                r.Message = "Aucun dépôt de mise à jour configuré.";
                return r;
            }

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    HttpRequestMessage req = new HttpRequestMessage(
                        HttpMethod.Get, "https://api.github.com/repos/" + repository + "/releases/latest");
                    req.Headers.Add("User-Agent", "AskThem/" + r.CurrentVersion);
                    req.Headers.Add("Accept", "application/vnd.github+json");

                    HttpResponseMessage rep = client.Send(req);
                    if (!rep.IsSuccessStatusCode)
                    {
                        r.Message = "Recherche de mise à jour : réponse " + (int)rep.StatusCode + ".";
                        return r;
                    }

                    string json = rep.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement racine = doc.RootElement;
                        JsonElement tag;
                        if (racine.TryGetProperty("tag_name", out tag)) r.LatestVersion = tag.GetString();
                        JsonElement page;
                        if (racine.TryGetProperty("html_url", out page)) r.PageUrl = page.GetString();

                        JsonElement assets;
                        if (racine.TryGetProperty("assets", out assets))
                        {
                            foreach (JsonElement asset in assets.EnumerateArray())
                            {
                                JsonElement nom, url;
                                if (!asset.TryGetProperty("name", out nom)) continue;
                                if (!asset.TryGetProperty("browser_download_url", out url)) continue;
                                string n = nom.GetString();
                                if (n != null && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    r.DownloadUrl = url.GetString();
                                    break;
                                }
                            }
                        }
                    }
                }

                r.Available = Compare(r.LatestVersion, r.CurrentVersion) > 0;
                r.Message = r.Available
                    ? "Version " + Clean(r.LatestVersion) + " disponible (vous utilisez la " + r.CurrentVersion + ")."
                    : "AskThem est à jour (version " + r.CurrentVersion + ").";
            }
            catch (Exception ex)
            {
                r.Message = "Recherche de mise à jour impossible : " + ex.Message;
            }
            return r;
        }
        /// <summary>
        /// Télécharge la nouvelle version puis relance l'application dessus.
        /// Un exécutable ne pouvant pas s'écraser lui-même, un script attend sa
        /// fermeture, remplace le fichier, puis le redémarre.
        /// </summary>
        public static void DownloadAndRestart(UpdateInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
                throw new Exception("Aucun exécutable joint à cette publication.");

            string exeActuel = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exeActuel))
                throw new Exception("Chemin de l'exécutable introuvable.");

            string nouveau = exeActuel + ".nouveau";
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
                req.Headers.Add("User-Agent", "AskThem/" + info.CurrentVersion);
                HttpResponseMessage rep = client.Send(req);
                rep.EnsureSuccessStatusCode();
                using (Stream source = rep.Content.ReadAsStream())
                using (FileStream cible = File.Create(nouveau))
                {
                    source.CopyTo(cible);
                }
            }

            string script = Path.Combine(Path.GetTempPath(), "askthem_maj.cmd");
            string q = "\"";
            string[] lignes = new string[] {
                "@echo off",
                "ping 127.0.0.1 -n 3 >nul",
                ":attente",
                "move /y " + q + nouveau + q + " " + q + exeActuel + q + " >nul 2>&1",
                "if errorlevel 1 (",
                "  ping 127.0.0.1 -n 2 >nul",
                "  goto attente",
                ")",
                "start " + q + q + " " + q + exeActuel + q,
                "del " + q + "%~f0" + q
            };
            File.WriteAllText(script, string.Join(Environment.NewLine, lignes) + Environment.NewLine);

            ProcessStartInfo psi = new ProcessStartInfo(script);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
    }
}
