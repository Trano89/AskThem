using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Dépôt consulté pour les mises à jour. Volontairement figé dans le code :
        /// s'il venait de config.json, quiconque peut écrire à côté de l'exécutable
        /// pourrait le rediriger vers un dépôt hostile et faire exécuter n'importe
        /// quel programme au prochain démarrage.
        /// </summary>
        public const string Repository = "Trano89/AskThem";

        /// <summary>Résultat d'une recherche de mise à jour.</summary>
        public class UpdateInfo
        {
            public bool Available;
            public string CurrentVersion = "";
            public string LatestVersion = "";
            public string DownloadUrl = "";
            public string PageUrl = "";
            public string Message = "";

            /// <summary>Modèles d'email publiés avec la version : nom du fichier, adresse.</summary>
            public Dictionary<string, string> Modeles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        public static UpdateInfo Check()
        {
            string repository = Repository;
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
                                if (n == null) continue;
                                if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (r.DownloadUrl == "") r.DownloadUrl = url.GetString();
                                }
                                else if (n.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                                {
                                    r.Modeles[n] = url.GetString();
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

            // Les modèles d'email suivent l'exécutable. Ceux du dossier templates priment sur
            // les modèles intégrés : restés anciens, ils privaient la nouvelle version de ses
            // propres textes — la case « délai et frais de livraison » n'y avait aucun effet.
            // Ils ne remplacent les anciens qu'une fois l'exécutable remplacé, pour qu'un
            // échec ne laisse pas des modèles nouveaux à une version qui ne les comprend pas.
            string dossierModeles = Path.Combine(dossier, "templates");
            bool avecModeles = false;
            if (info.Modeles.Count > 0 && Directory.Exists(dossierModeles))
            {
                foreach (KeyValuePair<string, string> m in info.Modeles)
                {
                    string nom = Path.GetFileName(m.Key);
                    if (string.IsNullOrWhiteSpace(nom) || nom.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                    string cible = Path.Combine(dossierModeles, nom + ".nouveau");
                    try
                    {
                        TelechargerVers(m.Value, info.CurrentVersion, cible);
                        avecModeles = true;
                    }
                    catch (Exception ex)
                    {
                        Supprimer(cible);
                        LogService.Write("Modèle " + nom + " non téléchargé : " + ex.Message);
                    }
                }
            }

            // Nom imprévisible : un fichier au nom fixe dans %TEMP% pourrait être
            // remplacé entre son écriture et son exécution.
            string script = Path.Combine(Path.GetTempPath(),
                "askthem_maj_" + Guid.NewGuid().ToString("N") + ".cmd");

            // Le script ne contient aucun chemin : ils lui sont passés en arguments. Écrits
            // dans le fichier, ils étaient lus par cmd dans la page de code OEM, et un chemin
            // accentué (C:\Users\Hélène\…) faisait échouer le remplacement sans un mot.
            File.WriteAllText(script, ScriptRemplacement(), Encoding.ASCII);

            ProcessStartInfo psi = new ProcessStartInfo(script);
            psi.Arguments = Q + nouveau + Q + " " + Q + exeActuel + Q + " " + Q + (avecModeles ? dossierModeles : "") + Q;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetTempPath();
            Process.Start(psi);
        }

        /// <summary>
        /// Message si la dernière mise à jour n'a pas pu remplacer l'exécutable, sinon "".
        ///
        /// Le script relance alors l'ancienne version, en silence : sans ce rappel,
        /// l'utilisateur croirait sa version à jour.
        /// </summary>
        public static string EchecPrecedent()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe)) return "";
                string reste = exe + ".nouveau";
                if (!File.Exists(reste)) return "";
                Supprimer(reste);
                return "La dernière mise à jour n'a pas pu remplacer " + exe
                     + " (fichier verrouillé ?) : la version " + CurrentVersion() + " est toujours en service.";
            }
            catch (Exception)
            {
                return "";
            }
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
            TelechargerVers(info.DownloadUrl, info.CurrentVersion, cible);
        }

        private static void TelechargerVers(string adresse, string version, string cible)
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(15);
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, adresse);
                req.Headers.Add("User-Agent", "AskThem/" + version);
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
        /// Script de remplacement, sans aucun chemin écrit dedans :
        ///   %1 le nouvel exécutable, %2 l'exécutable à remplacer, %3 le dossier des modèles
        ///   (vide s'il n'y en a pas à remplacer).
        /// Les arguments arrivent en Unicode : accents, espaces et parenthèses passent. Le
        /// nombre de tentatives est borné. En cas d'échec, l'ancienne version est relancée —
        /// la fenêtre du script est invisible, un message n'y serait lu par personne — et
        /// AskThem le signale au démarrage suivant.
        /// </summary>
        private static string ScriptRemplacement()
        {
            string[] lignes = new string[] {
                "@echo off",
                "setlocal",
                "set /a N=0",
                ":attente",
                "set /a N+=1",
                "move /y " + Q + "%~1" + Q + " " + Q + "%~2" + Q + " >nul 2>&1",
                "if not errorlevel 1 goto ok",
                "if %N% GEQ 40 goto echec",
                "ping 127.0.0.1 -n 2 >nul",
                "goto attente",
                ":ok",
                "if " + Q + "%~3" + Q + "==" + Q + Q + " goto relance",
                "if not exist " + Q + "%~3\\ancien" + Q + " mkdir " + Q + "%~3\\ancien" + Q + " >nul 2>&1",
                "for %%F in (" + Q + "%~3\\*.html.nouveau" + Q + ") do (",
                "  if exist " + Q + "%~3\\%%~nF" + Q + " copy /y " + Q + "%~3\\%%~nF" + Q + " " + Q + "%~3\\ancien\\%%~nF" + Q + " >nul 2>&1",
                "  move /y " + Q + "%%F" + Q + " " + Q + "%~3\\%%~nF" + Q + " >nul 2>&1",
                ")",
                "goto relance",
                ":echec",
                "if not " + Q + "%~3" + Q + "==" + Q + Q + " del /q " + Q + "%~3\\*.html.nouveau" + Q + " >nul 2>&1",
                ":relance",
                "start " + Q + Q + " " + Q + "%~2" + Q,
                "del " + Q + "%~f0" + Q
            };
            return string.Join("\r\n", lignes) + "\r\n";
        }
    }
}
