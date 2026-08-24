using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
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
        private const string Q = "\"";

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
        /// Télécharge la nouvelle version et remplace l'exécutable EXACTEMENT à
        /// l'emplacement d'où il tourne, quel qu'il soit sur ce poste.
        /// Un exécutable ne pouvant pas s'écraser lui-même, un script attend sa
        /// fermeture, remplace le fichier, puis le redémarre au même endroit.
        /// </summary>
        public static void DownloadAndRestart(UpdateInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
                throw new Exception("Aucun exécutable joint à cette publication.");

            // Chemin réel du processus : suit l'exe où qu'il soit, y compris sur
            // une clé USB ou un partage réseau, et diffère donc d'un poste à l'autre.
            string exeActuel = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exeActuel) || !File.Exists(exeActuel))
                throw new Exception("Emplacement de l'exécutable introuvable.");

            string dossier = Path.GetDirectoryName(exeActuel);
            VerifierEcriture(dossier);

            string nouveau = exeActuel + ".nouveau";
            try
            {
                Telecharger(info, nouveau);
            }
            catch (Exception)
            {
                Supprimer(nouveau);
                throw;
            }

            if (new FileInfo(nouveau).Length < 1024 * 1024)
            {
                Supprimer(nouveau);
                throw new Exception("Le fichier téléchargé est incomplet.");
            }

            string script = Path.Combine(Path.GetTempPath(), "askthem_maj.cmd");
            File.WriteAllText(script, ScriptRemplacement(nouveau, exeActuel), Encoding.Default);

            ProcessStartInfo psi = new ProcessStartInfo(script);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetTempPath();
            Process.Start(psi);
        }

        /// <summary>Échoue tôt et clairement si le dossier n'est pas inscriptible.</summary>
        private static void VerifierEcriture(string dossier)
        {
            string test = Path.Combine(dossier, "askthem_ecriture.tmp");
            try
            {
                File.WriteAllText(test, "x");
                File.Delete(test);
            }
            catch (Exception)
            {
                throw new Exception("Le dossier " + dossier + " n'autorise pas l'écriture. "
                    + "Déplacez AskThem.exe dans un dossier où vous avez les droits, "
                    + "ou téléchargez la nouvelle version manuellement.");
            }
        }

        private static void Telecharger(UpdateInfo info, string cible)
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(15);
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
                req.Headers.Add("User-Agent", "AskThem/" + info.CurrentVersion);
                HttpResponseMessage rep = client.Send(req);
                rep.EnsureSuccessStatusCode();
                using (Stream source = rep.Content.ReadAsStream())
                using (FileStream f = File.Create(cible))
                {
                    source.CopyTo(f);
                }
            }
        }

        private static void Supprimer(string chemin)
        {
            try { if (File.Exists(chemin)) File.Delete(chemin); }
            catch (Exception) { }
        }

        /// <summary>
        /// Script de remplacement. Les chemins passent par des variables entre
        /// guillemets : ils supportent espaces et parenthèses. Le nombre de tentatives
        /// est borné, pour ne jamais boucler indéfiniment si le fichier reste verrouillé.
        /// </summary>
        private static string ScriptRemplacement(string source, string destination)
        {
            string[] lignes = new string[] {
                "@echo off",
                "setlocal",
                "set " + Q + "SRC=" + source + Q,
                "set " + Q + "DST=" + destination + Q,
                "set /a N=0",
                ":attente",
                "set /a N+=1",
                "move /y " + Q + "%SRC%" + Q + " " + Q + "%DST%" + Q + " >nul 2>&1",
                "if not errorlevel 1 goto ok",
                "if %N% GEQ 40 goto echec",
                "ping 127.0.0.1 -n 2 >nul",
                "goto attente",
                ":ok",
                "start " + Q + Q + " " + Q + "%DST%" + Q,
                "goto fin",
                ":echec",
                "echo La mise a jour n'a pas pu remplacer :",
                "echo   %DST%",
                "echo Le fichier telecharge est conserve ici :",
                "echo   %SRC%",
                "echo Fermez AskThem, puis renommez ce fichier a la main.",
                "pause",
                ":fin",
                "del " + Q + "%~f0" + Q
            };
            return string.Join(Environment.NewLine, lignes) + Environment.NewLine;
        }
    }
}
