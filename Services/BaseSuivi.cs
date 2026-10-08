using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using AskThem.Models;
using ClosedXML.Excel;

namespace AskThem.Services
{
    /// <summary>
    /// La base des demandes : un classeur Excel sur le partage, que tout le monde peut ouvrir
    /// et qu'AskThem seul écrit.
    ///
    /// Chaque type de demande a deux onglets. L'onglet « BDD … », masqué, porte les données,
    /// une ligne par demande : c'est la base qu'AskThem lit et écrit. L'onglet visible est
    /// reconstruit à partir d'elle à chaque écriture — état, demandeur, envoi, réponse, et
    /// un Gantt des jours ouvrés d'attente. Tous sont protégés : on lit, on filtre, on ne modifie
    /// pas.
    ///
    /// Plusieurs postes écrivent dans le même fichier, et quelqu'un peut l'avoir ouvert dans
    /// Excel. Une écriture n'est donc jamais immédiate : elle entre dans une file sur le poste,
    /// que l'on vide quand le fichier est libre, sous un verrou partagé. Rien n'est perdu
    /// quand le partage est injoignable ou le classeur occupé : l'écriture attend.
    /// </summary>
    public static class BaseSuivi
    {
        public const string NomFichier = "Suivi des demandes AskThem.xlsx";

        /// <summary>
        /// Protection des onglets. Ce n'est pas un secret — le code est public — mais un garde
        /// contre la modification par mégarde : il faut le vouloir pour lever la protection.
        /// </summary>
        private const string Protection = "AskThem";

        /// <summary>Onglet masqué où chaque demandeur garde sa couleur.</summary>
        private const string NomUtilisateurs = "BDD Utilisateurs";

        /// <summary>Les trois onglets, dans l'ordre où on les lit.</summary>
        public static readonly string[] Types =
        {
            RequestTypes.SousDossier(RequestType.Offre),
            RequestTypes.SousDossier(RequestType.Fabrication),
            RequestTypes.SousDossier(RequestType.CommandeCatalogue)
        };

        /// <summary>Colonnes de l'onglet de données. L'ordre n'est pas imposé à la lecture.</summary>
        private static readonly string[] Champs =
        {
            "Id", "Type", "Statut", "CreeeLe", "Auteur", "AuteurNom", "Poste", "Fournisseur",
            "Destinataires", "Reference", "NbArticles", "Articles", "NbMessages", "MessagesEnvoyes",
            "EnvoyeeLe", "SujetEnvoye", "DestinatairesEnvoyes", "ProchainRappel", "NbRappels",
            "ReponseLe", "ClotureLe", "ClotureePar", "DossierArchive", "MisAJourLe",
            "AuteurEmail", "DerniereAction", "DerniereActionPar", "DerniereActionLe",
            "Masquee", "MasqueeLe", "MasqueePar"
        };

        private static readonly object Verrou = new object();

        // ------------------------------------------------------------------ chemins

        /// <summary>Le classeur, à la racine de l'archive des demandes.</summary>
        public static string Chemin(AppConfig config)
        {
            string racine = config != null ? config.ArchiveRoot : "";
            if (string.IsNullOrWhiteSpace(racine)) return "";
            return Path.Combine(racine, NomFichier);
        }

        private static string DossierLocal()
        {
            string d = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "AskThem");
            try { Directory.CreateDirectory(d); }
            catch (Exception) { }
            return d;
        }

        private static string CheminFile() { return Path.Combine(DossierLocal(), "suivi-a-ecrire.json"); }
        private static string CheminCopie() { return Path.Combine(DossierLocal(), "suivi-mes-demandes.json"); }

        // ------------------------------------------------------------------ écriture

        /// <summary>
        /// Retient l'état d'une demande, pour l'écrire dans le classeur dès qu'il est libre.
        /// La dernière version d'une demande remplace les précédentes dans la file.
        /// </summary>
        public static void Enregistrer(DemandeSuivie d)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Id)) return;
            lock (Verrou)
            {
                d.MisAJourLe = DateTime.Now;
                Dictionary<string, DemandeSuivie> file = LireJson(CheminFile());
                file[d.Id] = d.Copie();
                EcrireJson(CheminFile(), file);

                // La copie locale suit aussi : les rappels doivent fonctionner hors réseau.
                if (string.Equals(d.Auteur, System.Environment.UserName, StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, DemandeSuivie> miennes = LireJson(CheminCopie());
                    miennes[d.Id] = d.Copie();
                    EcrireJson(CheminCopie(), miennes);
                }
            }
        }

        /// <summary>Nombre d'états en attente d'écriture sur ce poste.</summary>
        public static int EnAttente()
        {
            lock (Verrou) return LireJson(CheminFile()).Count;
        }

        /// <summary>
        /// Écrit dans le classeur ce que la file retient. Faux, avec un motif, si le classeur
        /// n'a pas pu être écrit : la file est conservée pour la prochaine tentative.
        /// </summary>
        public static bool Vider(AppConfig config, out string message)
        {
            message = "";
            lock (Verrou)
            {
                Dictionary<string, DemandeSuivie> file = LireJson(CheminFile());
                if (file.Count == 0 && !VueARafraichir(config)) return true;

                // Un simple rafraîchissement de la vue qui échoue — partage injoignable, classeur
                // ouvert dans Excel — se retente plus tard, sans rien journaliser : il n'y a rien
                // à perdre, et toutes les deux minutes, le journal ne dirait plus que cela.
                bool seulementVue = file.Count == 0;

                string chemin = Chemin(config);
                if (chemin == "" || !Directory.Exists(Path.GetDirectoryName(chemin)))
                {
                    if (seulementVue) { Reporter(); return true; }
                    message = "Base des demandes injoignable : " + (chemin == "" ? "aucun chemin configuré" : Path.GetDirectoryName(chemin)) + ".";
                    return false;
                }

                // Rien à écrire et pas encore de classeur : on ne crée pas un classeur vide.
                if (file.Count == 0 && !File.Exists(chemin))
                {
                    RetenirVueAJour(config);
                    return true;
                }

                using (VerrouFichier v = VerrouFichier.Prendre(chemin + ".verrou", TimeSpan.FromSeconds(20)))
                {
                    if (v == null)
                    {
                        if (seulementVue) { Reporter(); return true; }
                        message = "Base des demandes occupée par un autre poste : écriture reportée.";
                        return false;
                    }

                    string temporaire = chemin + ".ecriture-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".xlsx";
                    try
                    {
                        bool dejaAJour = false;
                        using (XLWorkbook wb = Ouvrir(chemin))
                        {
                            // Rien à écrire, et un autre poste a déjà construit la vue du jour.
                            if (file.Count == 0 && MarqueVue(wb) == MarqueDuJour())
                            {
                                dejaAJour = true;
                            }
                            else
                            {
                                foreach (string type in Types) Preparer(wb, type);
                                foreach (DemandeSuivie d in file.Values) Ecrire(wb, d);

                                List<DemandeSuivie> toutes = LireToutes(wb);
                                Dictionary<string, XLColor> couleurs = Couleurs(wb, toutes);
                                foreach (string type in Types) Construire(wb, type, toutes, couleurs);
                                ConstruireToutes(wb, toutes, couleurs);
                                ConstruireSupprimees(wb, toutes);
                                PoserMarqueVue(wb);
                                ProtegerClasseur(wb);
                                wb.SaveAs(temporaire);
                                MemoriserMiennes(toutes);
                            }
                        }
                        if (dejaAJour)
                        {
                            RetenirVueAJour(config);
                            return true;
                        }
                        LectureSeuleConseillee(temporaire);
                        PositionnerGantt(temporaire);

                        // Remplacement d'un seul geste. Un classeur ouvert dans Excel refuse
                        // d'être remplacé : on n'écrase rien, on réessaiera.
                        if (File.Exists(chemin)) File.Replace(temporaire, chemin, null);
                        else File.Move(temporaire, chemin);
                    }
                    catch (Exception ex)
                    {
                        try { if (File.Exists(temporaire)) File.Delete(temporaire); }
                        catch (Exception) { }
                        if (seulementVue) { Reporter(); return true; }
                        message = "Base des demandes non écrite (" + ex.Message + ") : écriture reportée.";
                        LogService.Write(message);
                        return false;
                    }
                }

                EcrireJson(CheminFile(), new Dictionary<string, DemandeSuivie>());
                RetenirVueAJour(config);
                return true;
            }
        }

        // ------------------------------------------------------------------ lecture

        /// <summary>
        /// Toutes les demandes connues : le classeur, complété par ce que la file n'y a pas
        /// encore écrit. Hors réseau, les demandes de l'utilisateur viennent de sa copie locale.
        /// </summary>
        public static List<DemandeSuivie> Lire(AppConfig config)
        {
            Dictionary<string, DemandeSuivie> parId = new Dictionary<string, DemandeSuivie>(StringComparer.OrdinalIgnoreCase);
            bool lu = false;
            string chemin = Chemin(config);
            try
            {
                if (chemin != "" && File.Exists(chemin))
                {
                    using (FileStream fs = Quantite.OuvrirPartage(chemin))
                    using (XLWorkbook wb = new XLWorkbook(fs))
                    {
                        foreach (DemandeSuivie d in LireToutes(wb)) parId[d.Id] = d;
                    }
                    lu = true;
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Base des demandes illisible : " + ex.Message);
            }

            lock (Verrou)
            {
                if (lu)
                {
                    MemoriserMiennes(new List<DemandeSuivie>(parId.Values));
                }
                else
                {
                    foreach (DemandeSuivie d in LireJson(CheminCopie()).Values) parId[d.Id] = d;
                }
                foreach (DemandeSuivie d in LireJson(CheminFile()).Values)
                {
                    DemandeSuivie enBase;
                    if (parId.TryGetValue(d.Id, out enBase)) d.ReprendreMasque(enBase);
                    parId[d.Id] = d;
                }
            }
            return new List<DemandeSuivie>(parId.Values);
        }

        /// <summary>Les demandes de cet utilisateur.</summary>
        public static List<DemandeSuivie> Miennes(AppConfig config)
        {
            List<DemandeSuivie> miennes = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in Lire(config))
                if (string.Equals(d.Auteur, System.Environment.UserName, StringComparison.OrdinalIgnoreCase))
                    miennes.Add(d);
            return miennes;
        }

        /// <summary>Une demande par son identifiant, ou null.</summary>
        public static DemandeSuivie Trouver(AppConfig config, string id)
        {
            foreach (DemandeSuivie d in Lire(config))
                if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        private static void MemoriserMiennes(List<DemandeSuivie> toutes)
        {
            Dictionary<string, DemandeSuivie> miennes = new Dictionary<string, DemandeSuivie>(StringComparer.OrdinalIgnoreCase);
            foreach (DemandeSuivie d in toutes)
                if (string.Equals(d.Auteur, System.Environment.UserName, StringComparison.OrdinalIgnoreCase))
                    miennes[d.Id] = d;
            EcrireJson(CheminCopie(), miennes);
        }

        // ------------------------------------------------------------------ classeur

        private static XLWorkbook Ouvrir(string chemin)
        {
            if (!File.Exists(chemin)) return new XLWorkbook();

            // Le classeur est lu en mémoire, le fichier aussitôt relâché. Le flux mémoire n'est
            // pas fermé : ClosedXML y revient à l'enregistrement.
            MemoryStream ms = new MemoryStream();
            using (FileStream fs = Quantite.OuvrirPartage(chemin)) fs.CopyTo(ms);
            ms.Position = 0;
            return new XLWorkbook(ms);
        }

        private static string NomBdd(string type) { return "BDD " + type; }

        /// <summary>Crée les deux onglets d'un type s'ils manquent, dans l'ordre : les vues d'abord.</summary>
        private static void Preparer(XLWorkbook wb, string type)
        {
            int position = 1;
            foreach (string t in Types)
            {
                if (t == type) break;
                position++;
            }

            IXLWorksheet vue;
            if (!wb.TryGetWorksheet(type, out vue)) wb.Worksheets.Add(type, Math.Min(position, wb.Worksheets.Count + 1));

            IXLWorksheet bdd;
            if (!wb.TryGetWorksheet(NomBdd(type), out bdd))
            {
                bdd = wb.Worksheets.Add(NomBdd(type));
                for (int c = 0; c < Champs.Length; c++)
                {
                    bdd.Cell(1, c + 1).Value = Champs[c];
                    bdd.Cell(1, c + 1).Style.Font.Bold = true;
                }
            }
            else
            {
                // Une colonne ajoutée par une version plus récente est ajoutée en fin de ligne.
                Dictionary<string, int> cols = Colonnes(bdd);
                int derniere = bdd.LastColumnUsed() == null ? 0 : bdd.LastColumnUsed().ColumnNumber();
                foreach (string champ in Champs)
                {
                    if (cols.ContainsKey(champ)) continue;
                    derniere++;
                    bdd.Cell(1, derniere).Value = champ;
                    bdd.Cell(1, derniere).Style.Font.Bold = true;
                }
            }
            bdd.Visibility = XLWorksheetVisibility.Hidden;
        }

        private static Dictionary<string, int> Colonnes(IXLWorksheet bdd)
        {
            Dictionary<string, int> cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IXLCell derniere = bdd.Row(1).LastCellUsed();
            int n = derniere == null ? 0 : derniere.Address.ColumnNumber;
            for (int c = 1; c <= n; c++)
            {
                string nom = bdd.Cell(1, c).GetString().Trim();
                if (nom != "" && !cols.ContainsKey(nom)) cols[nom] = c;
            }
            return cols;
        }

        /// <summary>Écrit une demande à sa ligne, ou en fin d'onglet si elle est nouvelle.</summary>
        private static void Ecrire(XLWorkbook wb, DemandeSuivie d)
        {
            string type = string.IsNullOrWhiteSpace(d.Type) ? Types[0] : d.Type;
            IXLWorksheet bdd;
            if (!wb.TryGetWorksheet(NomBdd(type), out bdd)) { Preparer(wb, type); bdd = wb.Worksheet(NomBdd(type)); }

            Dictionary<string, int> cols = Colonnes(bdd);
            int colId = cols["Id"];
            int derniere = bdd.LastRowUsed() == null ? 1 : bdd.LastRowUsed().RowNumber();
            int ligne = -1;
            for (int r = 2; r <= derniere; r++)
                if (string.Equals(bdd.Cell(r, colId).GetString(), d.Id, StringComparison.OrdinalIgnoreCase)) { ligne = r; break; }
            if (ligne < 0) ligne = derniere + 1;
            else
            {
                // Plusieurs postes écrivent la même demande — son auteur, et désormais un
                // collègue qui la clôt. Une version plus récente déjà écrite n'est pas
                // écrasée par un état plus ancien resté dans une file.
                DateTime? deja = Date(bdd, ligne, cols, "MisAJourLe");
                if (deja.HasValue && deja.Value > d.MisAJourLe.AddSeconds(1)) return;

                // La suppression a sa propre date : une réécriture pour une autre raison, partie
                // d'une copie plus ancienne, ne fait pas réapparaître une demande supprimée.
                DemandeSuivie enPlace = new DemandeSuivie();
                enPlace.Masquee = Texte(bdd, ligne, cols, "Masquee", "") == "1";
                enPlace.MasqueeLe = Date(bdd, ligne, cols, "MasqueeLe");
                enPlace.MasqueePar = Texte(bdd, ligne, cols, "MasqueePar", "");
                d = d.Copie();
                d.ReprendreMasque(enPlace);
            }

            Poser(bdd, ligne, cols, "Id", d.Id);
            Poser(bdd, ligne, cols, "Type", type);
            Poser(bdd, ligne, cols, "Statut", d.Statut);
            Poser(bdd, ligne, cols, "CreeeLe", d.CreeeLe);
            Poser(bdd, ligne, cols, "Auteur", d.Auteur);
            Poser(bdd, ligne, cols, "AuteurNom", d.AuteurNom);
            Poser(bdd, ligne, cols, "Poste", d.Poste);
            Poser(bdd, ligne, cols, "Fournisseur", d.Fournisseur);
            Poser(bdd, ligne, cols, "Destinataires", d.Destinataires);
            Poser(bdd, ligne, cols, "Reference", d.Reference);
            Poser(bdd, ligne, cols, "NbArticles", d.NbArticles);
            Poser(bdd, ligne, cols, "Articles", d.Articles);
            Poser(bdd, ligne, cols, "NbMessages", d.NbMessages);
            Poser(bdd, ligne, cols, "MessagesEnvoyes", d.MessagesEnvoyes);
            Poser(bdd, ligne, cols, "EnvoyeeLe", d.EnvoyeeLe);
            Poser(bdd, ligne, cols, "SujetEnvoye", d.SujetEnvoye);
            Poser(bdd, ligne, cols, "DestinatairesEnvoyes", d.DestinatairesEnvoyes);
            Poser(bdd, ligne, cols, "ProchainRappel", d.ProchainRappel);
            Poser(bdd, ligne, cols, "NbRappels", d.NbRappels);
            Poser(bdd, ligne, cols, "ReponseLe", d.ReponseLe);
            Poser(bdd, ligne, cols, "ClotureLe", d.ClotureLe);
            Poser(bdd, ligne, cols, "ClotureePar", d.ClotureePar);
            Poser(bdd, ligne, cols, "DossierArchive", d.DossierArchive);
            Poser(bdd, ligne, cols, "MisAJourLe", d.MisAJourLe);
            Poser(bdd, ligne, cols, "AuteurEmail", d.AuteurEmail);
            Poser(bdd, ligne, cols, "DerniereAction", d.DerniereAction);
            Poser(bdd, ligne, cols, "DerniereActionPar", d.DerniereActionPar);
            Poser(bdd, ligne, cols, "DerniereActionLe", d.DerniereActionLe);
            Poser(bdd, ligne, cols, "Masquee", d.Masquee ? "1" : "");
            Poser(bdd, ligne, cols, "MasqueeLe", d.MasqueeLe);
            Poser(bdd, ligne, cols, "MasqueePar", d.MasqueePar);
        }

        private static void Poser(IXLWorksheet ws, int ligne, Dictionary<string, int> cols, string champ, object valeur)
        {
            int c;
            if (!cols.TryGetValue(champ, out c)) return;
            IXLCell cell = ws.Cell(ligne, c);
            if (valeur == null) { cell.Value = Blank.Value; return; }
            if (valeur is DateTime) { cell.Value = (DateTime)valeur; cell.Style.DateFormat.Format = "dd.mm.yyyy hh:mm"; return; }
            if (valeur is DateTime?)
            {
                DateTime? dt = (DateTime?)valeur;
                if (dt.HasValue) { cell.Value = dt.Value; cell.Style.DateFormat.Format = "dd.mm.yyyy hh:mm"; }
                else cell.Value = Blank.Value;
                return;
            }
            if (valeur is int) { cell.Value = (int)valeur; return; }
            cell.Value = valeur.ToString();
        }

        private static List<DemandeSuivie> LireToutes(XLWorkbook wb)
        {
            List<DemandeSuivie> toutes = new List<DemandeSuivie>();
            foreach (string type in Types)
            {
                IXLWorksheet bdd;
                if (!wb.TryGetWorksheet(NomBdd(type), out bdd)) continue;
                Dictionary<string, int> cols = Colonnes(bdd);
                if (!cols.ContainsKey("Id")) continue;
                IXLRow derniere = bdd.LastRowUsed();
                int n = derniere == null ? 1 : derniere.RowNumber();
                for (int r = 2; r <= n; r++)
                {
                    string id = bdd.Cell(r, cols["Id"]).GetString().Trim();
                    if (id == "") continue;
                    DemandeSuivie d = new DemandeSuivie();
                    d.Id = id;
                    d.Type = Texte(bdd, r, cols, "Type", type);
                    d.Statut = Texte(bdd, r, cols, "Statut", DemandeSuivie.Preparee);
                    d.CreeeLe = Date(bdd, r, cols, "CreeeLe") ?? DateTime.MinValue;
                    d.Auteur = Texte(bdd, r, cols, "Auteur", "");
                    d.AuteurNom = Texte(bdd, r, cols, "AuteurNom", "");
                    d.Poste = Texte(bdd, r, cols, "Poste", "");
                    d.Fournisseur = Texte(bdd, r, cols, "Fournisseur", "");
                    d.Destinataires = Texte(bdd, r, cols, "Destinataires", "");
                    d.Reference = Texte(bdd, r, cols, "Reference", "");
                    d.NbArticles = Entier(bdd, r, cols, "NbArticles");
                    d.Articles = Texte(bdd, r, cols, "Articles", "");
                    d.NbMessages = Entier(bdd, r, cols, "NbMessages");
                    d.MessagesEnvoyes = Entier(bdd, r, cols, "MessagesEnvoyes");
                    d.EnvoyeeLe = Date(bdd, r, cols, "EnvoyeeLe");
                    d.SujetEnvoye = Texte(bdd, r, cols, "SujetEnvoye", "");
                    d.DestinatairesEnvoyes = Texte(bdd, r, cols, "DestinatairesEnvoyes", "");
                    d.ProchainRappel = Date(bdd, r, cols, "ProchainRappel");
                    d.NbRappels = Entier(bdd, r, cols, "NbRappels");
                    d.ReponseLe = Date(bdd, r, cols, "ReponseLe");
                    d.ClotureLe = Date(bdd, r, cols, "ClotureLe");
                    d.ClotureePar = Texte(bdd, r, cols, "ClotureePar", "");
                    d.DossierArchive = Texte(bdd, r, cols, "DossierArchive", "");
                    d.MisAJourLe = Date(bdd, r, cols, "MisAJourLe") ?? DateTime.MinValue;
                    d.AuteurEmail = Texte(bdd, r, cols, "AuteurEmail", "");
                    d.DerniereAction = Texte(bdd, r, cols, "DerniereAction", "");
                    d.DerniereActionPar = Texte(bdd, r, cols, "DerniereActionPar", "");
                    d.DerniereActionLe = Date(bdd, r, cols, "DerniereActionLe");
                    d.Masquee = Texte(bdd, r, cols, "Masquee", "") == "1";
                    d.MasqueeLe = Date(bdd, r, cols, "MasqueeLe");
                    d.MasqueePar = Texte(bdd, r, cols, "MasqueePar", "");
                    toutes.Add(d);
                }
            }
            return toutes;
        }

        private static string Texte(IXLWorksheet ws, int r, Dictionary<string, int> cols, string champ, string defaut)
        {
            int c;
            if (!cols.TryGetValue(champ, out c)) return defaut;
            string v = ws.Cell(r, c).GetString();
            return string.IsNullOrWhiteSpace(v) ? defaut : v.Trim();
        }

        private static int Entier(IXLWorksheet ws, int r, Dictionary<string, int> cols, string champ)
        {
            int c;
            if (!cols.TryGetValue(champ, out c)) return 0;
            IXLCell cell = ws.Cell(r, c);
            if (cell.Value.IsNumber) return (int)cell.Value.GetNumber();
            int n;
            return int.TryParse(cell.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        private static DateTime? Date(IXLWorksheet ws, int r, Dictionary<string, int> cols, string champ)
        {
            int c;
            if (!cols.TryGetValue(champ, out c)) return null;
            IXLCell cell = ws.Cell(r, c);
            if (cell.Value.IsDateTime) return cell.Value.GetDateTime();
            if (cell.Value.IsNumber) { try { return DateTime.FromOADate(cell.Value.GetNumber()); } catch (Exception) { return null; } }
            DateTime d;
            if (DateTime.TryParse(cell.GetString(), CultureInfo.GetCultureInfo("fr-CH"), DateTimeStyles.None, out d)) return d;
            return null;
        }

        // ------------------------------------------------------------------ vue lisible

        /// <summary>
        /// Version de la mise en page des onglets visibles. Un classeur construit autrement —
        /// ou un autre jour : le Gantt suit la date — est reconstruit au passage suivant,
        /// même sans demande nouvelle à y écrire.
        /// </summary>
        private const int VersionVue = 3;

        /// <summary>Onglet de toutes les demandes, avec tout leur détail.</summary>
        private const string NomToutes = "Toutes les demandes";

        /// <summary>Onglet des demandes retirées du suivi, qu'on peut rétablir depuis AskThem.</summary>
        public const string NomSupprimees = "Supprimées";

        private const string Police = "Segoe UI";

        private static readonly XLColor Encre = XLColor.FromHtml("#1F2328");
        private static readonly XLColor Encre2 = XLColor.FromHtml("#5B6470");
        private static readonly XLColor Discret = XLColor.FromHtml("#8A939E");
        private static readonly XLColor Filet = XLColor.FromHtml("#E3E7EC");
        private static readonly XLColor FiletSemaine = XLColor.FromHtml("#C5CCD5");
        private static readonly XLColor Nuit = XLColor.FromHtml("#1F3A5F");
        private static readonly XLColor FondEntete = XLColor.FromHtml("#F2F4F7");
        private static readonly XLColor Accent = XLColor.FromHtml("#005A9E");
        private static readonly XLColor Gris = XLColor.FromHtml("#B4BAC1");
        private static readonly XLColor Orange = XLColor.FromHtml("#E8710A");
        private static readonly XLColor FondAujourdhui = XLColor.FromHtml("#FDF0E3");

        /// <summary>
        /// Couleurs des demandeurs : nettement distinctes, et aucune ne se confond avec le gris
        /// d'une demande sans suite.
        /// </summary>
        private static readonly string[] Palette =
        {
            "#2F6FB3", "#E8833A", "#3E9651", "#C83E4D", "#7A5195", "#1B9AAA",
            "#B5892B", "#D45087", "#5B6E1E", "#8C564B", "#3366CC", "#A05195"
        };

        // La partie gauche, figée, tient en quatre colonnes étroites : sur un petit écran, les
        // treize colonnes d'avant occupaient toute la largeur et le Gantt restait hors de vue.
        // Le détail complet d'une demande s'affiche au survol, et dans l'onglet « Toutes les
        // demandes ».
        private const int ColBande = 1;      // couleur du demandeur
        private const int ColDemande = 2;    // fournisseur, puis référence, articles, demandeur
        private const int ColStatut = 3;
        private const int ColAttente = 4;    // jours ouvrés d'attente
        private const int ColDossier = 5;    // lien vers le dossier archivé
        private const int ColGantt = 6;

        private const int LigneSemaines = 4;
        private const int LigneEntete = 5;

        /// <summary>Jours ouvrés montrés avant aujourd'hui, et après.</summary>
        private const int JoursAvant = 60;
        private const int JoursApres = 15;

        /// <summary>À l'ouverture, le Gantt montre d'abord les jours ouvrés depuis celui-ci.</summary>
        private const int JoursVisiblesAvant = 15;

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CH");

        /// <summary>
        /// La couleur de chaque demandeur, attribuée une fois pour toutes dans l'onglet masqué
        /// « BDD Utilisateurs » : un nouveau venu reçoit la suivante, et personne ne change de
        /// couleur quand la liste s'allonge.
        /// </summary>
        private static Dictionary<string, XLColor> Couleurs(XLWorkbook wb, List<DemandeSuivie> toutes)
        {
            IXLWorksheet u = FeuilleUtilisateurs(wb);
            u.Unprotect(Protection);

            Dictionary<string, XLColor> couleurs = new Dictionary<string, XLColor>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> lignes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int derniere = 1;
            for (int r = 2; r <= (u.LastRowUsed() == null ? 1 : u.LastRowUsed().RowNumber()); r++)
            {
                string auteur = u.Cell(r, 1).GetString().Trim();
                if (auteur == "") continue;
                derniere = r;
                lignes[auteur] = r;
                try { couleurs[auteur] = XLColor.FromHtml(u.Cell(r, 3).GetString().Trim()); }
                catch (Exception) { couleurs[auteur] = XLColor.FromHtml(Palette[(r - 2) % Palette.Length]); }
            }

            foreach (DemandeSuivie d in toutes)
            {
                if (string.IsNullOrWhiteSpace(d.Auteur)) continue;
                int ligne;
                if (!lignes.TryGetValue(d.Auteur, out ligne))
                {
                    derniere++;
                    ligne = derniere;
                    lignes[d.Auteur] = ligne;
                    string html = Palette[(ligne - 2) % Palette.Length];
                    u.Cell(ligne, 1).Value = d.Auteur;
                    u.Cell(ligne, 3).Value = html;
                    couleurs[d.Auteur] = XLColor.FromHtml(html);
                }
                if (!string.IsNullOrWhiteSpace(d.AuteurNom)) u.Cell(ligne, 2).Value = d.AuteurNom;
            }

            u.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512, XLSheetProtectionElements.SelectEverything);
            u.Visibility = XLWorksheetVisibility.Hidden;
            return couleurs;
        }

        private static IXLWorksheet FeuilleUtilisateurs(XLWorkbook wb)
        {
            IXLWorksheet u;
            if (!wb.TryGetWorksheet(NomUtilisateurs, out u))
            {
                u = wb.Worksheets.Add(NomUtilisateurs);
                u.Cell(1, 1).Value = "Auteur";
                u.Cell(1, 2).Value = "Nom";
                u.Cell(1, 3).Value = "Couleur";
                u.Row(1).Style.Font.Bold = true;
                u.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512, XLSheetProtectionElements.SelectEverything);
                u.Visibility = XLWorksheetVisibility.Hidden;
            }
            return u;
        }

        /// <summary>Ce qui identifie la vue du jour : sa mise en page, et la date du Gantt.</summary>
        private static string MarqueDuJour()
        {
            return VersionVue + "|" + DateTime.Today.ToString("yyyy-MM-dd");
        }

        private static string MarqueVue(XLWorkbook wb)
        {
            IXLWorksheet u;
            if (!wb.TryGetWorksheet(NomUtilisateurs, out u)) return "";
            return u.Cell(2, 6).GetString().Trim();
        }

        private static void PoserMarqueVue(XLWorkbook wb)
        {
            IXLWorksheet u = FeuilleUtilisateurs(wb);
            u.Unprotect(Protection);
            u.Cell(1, 6).Value = "Vue";
            u.Cell(2, 6).Value = MarqueDuJour();
            u.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512, XLSheetProtectionElements.SelectEverything);
        }

        private static string CheminMarqueLocale() { return Path.Combine(DossierLocal(), "suivi-vue.txt"); }

        private static string MarqueLocale(AppConfig config)
        {
            return Chemin(config) + "|" + MarqueDuJour();
        }

        /// <summary>
        /// Vrai si la vue du classeur n'a pas encore été vue à jour aujourd'hui depuis ce poste :
        /// mise en page d'une version antérieure, ou Gantt arrêté à un autre jour.
        /// </summary>
        public static bool VueARafraichir(AppConfig config)
        {
            if (Chemin(config) == "" || DateTime.Now < _pasAvant) return false;
            try
            {
                string f = CheminMarqueLocale();
                return !File.Exists(f) || File.ReadAllText(f).Trim() != MarqueLocale(config);
            }
            catch (Exception) { return true; }
        }

        /// <summary>Prochaine tentative de rafraîchissement, après un échec.</summary>
        private static DateTime _pasAvant = DateTime.MinValue;

        private static void Reporter()
        {
            _pasAvant = DateTime.Now.AddMinutes(30);
        }

        private static void RetenirVueAJour(AppConfig config)
        {
            try { File.WriteAllText(CheminMarqueLocale(), MarqueLocale(config)); }
            catch (Exception) { }
        }

        /// <summary>La même couleur, éclaircie : fond de cellule, ou barre d'une demande close.</summary>
        private static XLColor Pale(XLColor c, double part)
        {
            System.Drawing.Color b = c.Color;
            return XLColor.FromArgb((int)(b.R + (255 - b.R) * part), (int)(b.G + (255 - b.G) * part), (int)(b.B + (255 - b.B) * part));
        }

        /// <summary>Les jours ouvrés montrés dans le Gantt, du plus ancien au plus récent.</summary>
        private static List<DateTime> JoursOuvres()
        {
            List<DateTime> jours = new List<DateTime>();
            DateTime j = DateTime.Today;
            while (!Ouvre(j)) j = j.AddDays(-1);
            for (int n = 0; n < JoursAvant; n++)
            {
                j = j.AddDays(-1);
                while (!Ouvre(j)) j = j.AddDays(-1);
            }
            int apres = 0;
            while (true)
            {
                if (Ouvre(j))
                {
                    jours.Add(j);
                    if (j > DateTime.Today) apres++;
                    if (apres >= JoursApres) break;
                }
                j = j.AddDays(1);
            }
            return jours;
        }

        private static bool Ouvre(DateTime j)
        {
            return j.DayOfWeek != DayOfWeek.Saturday && j.DayOfWeek != DayOfWeek.Sunday;
        }

        /// <summary>Jours ouvrés écoulés entre deux dates.</summary>
        private static int Ouvres(DateTime debut, DateTime fin)
        {
            int n = 0;
            for (DateTime j = debut.Date.AddDays(1); j <= fin.Date; j = j.AddDays(1))
                if (Ouvre(j)) n++;
            return n;
        }

        private static readonly string[] InitialesJours = { "D", "L", "M", "M", "J", "V", "S" };

        /// <summary>Statut tel qu'on le lit d'un coup d'œil : libellé court, texte, fond.</summary>
        private static void Pastille(DemandeSuivie d, out string libelle, out XLColor texte, out XLColor fond)
        {
            if (d.EnAttente && d.ProchainRappel.HasValue && d.ProchainRappel.Value.Date <= DateTime.Today)
            {
                libelle = "À relancer"; texte = XLColor.FromHtml("#B3261E"); fond = XLColor.FromHtml("#FDECEC");
                return;
            }
            switch (d.Statut)
            {
                case DemandeSuivie.Envoyee:
                    libelle = "En attente"; texte = XLColor.FromHtml("#0B4F8A"); fond = XLColor.FromHtml("#E1EEFA"); break;
                case DemandeSuivie.Repondue:
                    libelle = "Répondu"; texte = XLColor.FromHtml("#1E6B3C"); fond = XLColor.FromHtml("#E3F2E8"); break;
                case DemandeSuivie.SansSuite:
                    libelle = "Sans suite"; texte = XLColor.FromHtml("#5F6670"); fond = XLColor.FromHtml("#EEF0F2"); break;
                case DemandeSuivie.NonEnvoyee:
                    libelle = "Jamais partie"; texte = XLColor.FromHtml("#8A939E"); fond = XLColor.FromHtml("#F6F7F8"); break;
                default:
                    libelle = "À envoyer"; texte = XLColor.FromHtml("#8A5A00"); fond = XLColor.FromHtml("#FFF4D6"); break;
            }
        }

        /// <summary>Le détail d'une demande, pour la note qui s'affiche au survol.</summary>
        private static string Detail(DemandeSuivie d)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(d.Fournisseur + (string.IsNullOrWhiteSpace(d.Reference) ? "" : " — " + d.Reference));
            sb.AppendLine("Demandé par " + d.Demandeur);
            if (d.CreeeLe != DateTime.MinValue) sb.AppendLine("Préparée le " + d.CreeeLe.ToString("dd.MM.yyyy"));
            if (d.EnvoyeeLe.HasValue) sb.AppendLine("Envoyée le " + d.EnvoyeeLe.Value.ToString("dd.MM.yyyy"));
            if (d.ReponseLe.HasValue) sb.AppendLine("Réponse le " + d.ReponseLe.Value.ToString("dd.MM.yyyy"));
            if (d.EnAttente && d.ProchainRappel.HasValue) sb.AppendLine("Prochain rappel le " + d.ProchainRappel.Value.ToString("dd.MM.yyyy"));
            if (!string.IsNullOrWhiteSpace(d.DerniereAction))
                sb.AppendLine("Dernière action : " + d.DerniereAction
                    + (string.IsNullOrWhiteSpace(d.DerniereActionPar) ? "" : " — " + d.DerniereActionPar)
                    + (d.DerniereActionLe.HasValue ? ", " + d.DerniereActionLe.Value.ToString("dd.MM.yyyy") : ""));
            sb.AppendLine();
            sb.Append(d.NbArticles + " article(s) : " + DemandeSuivie.ListeCourte(new List<string>(
                (d.Articles ?? "").Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries)), 20));
            return sb.ToString();
        }

        /// <summary>
        /// Reconstruit l'onglet visible d'un type à partir de son onglet de données : rien n'y
        /// est saisi, tout y est déduit, et il est protégé.
        /// </summary>
        private static void Construire(XLWorkbook wb, string type, List<DemandeSuivie> toutes,
                                       Dictionary<string, XLColor> couleurs)
        {
            IXLWorksheet ws = wb.Worksheet(type);
            Vider(ws);

            List<DemandeSuivie> lignes = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in toutes)
                if (!d.Masquee && string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase)) lignes.Add(d);
            lignes.Sort(Ordre);

            int attente = 0, relancer = 0, repondues = 0, sansSuite = 0, preparees = 0;
            List<string> auteurs = new List<string>();
            foreach (DemandeSuivie d in lignes)
            {
                if (d.EnAttente)
                {
                    attente++;
                    if (d.ProchainRappel.HasValue && d.ProchainRappel.Value.Date <= DateTime.Today) relancer++;
                }
                else if (d.Statut == DemandeSuivie.Repondue) repondues++;
                else if (d.Statut == DemandeSuivie.SansSuite) sansSuite++;
                else if (d.Statut == DemandeSuivie.Preparee) preparees++;
                if (!string.IsNullOrWhiteSpace(d.Auteur) && !auteurs.Contains(d.Auteur)) auteurs.Add(d.Auteur);
            }

            List<DateTime> jours = JoursOuvres();
            int derniereColonne = ColGantt + jours.Count - 1;
            int aujourdhui = jours.IndexOf(DateTime.Today);

            // --- largeurs : la partie figée reste étroite ---
            ws.Column(ColBande).Width = 1.0;
            ws.Column(ColDemande).Width = 30;
            ws.Column(ColStatut).Width = 13;
            ws.Column(ColAttente).Width = 7;
            ws.Column(ColDossier).Width = 3.2;
            for (int i = 0; i < jours.Count; i++) ws.Column(ColGantt + i).Width = 3.0;

            // --- bandeau de titre ---
            IXLRange bandeau = ws.Range(1, 1, 1, derniereColonne);
            bandeau.Style.Fill.BackgroundColor = Nuit;
            ws.Row(1).Height = 30;
            IXLCell titre = ws.Cell(1, ColDemande);
            titre.Value = Titre(type);
            titre.Style.Font.FontSize = 15;
            titre.Style.Font.Bold = true;
            titre.Style.Font.FontColor = XLColor.White;
            titre.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            IXLCell maj = ws.Cell(1, ColGantt);
            maj.Value = "Mis à jour le " + DateTime.Now.ToString("dd.MM.yyyy à HH:mm") + " par AskThem · lecture seule";
            maj.Style.Font.FontSize = 8.5;
            maj.Style.Font.FontColor = XLColor.FromHtml("#C9D6E5");
            maj.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // --- les chiffres du moment ---
            IXLRange resume = ws.Range(2, ColDemande, 3, ColDossier);
            resume.Merge();
            resume.Style.Alignment.WrapText = true;
            IXLRichText chiffres = resume.FirstCell().GetRichText();
            Chiffre(chiffres, attente, "en attente", "#0B4F8A", true);
            if (relancer > 0) Chiffre(chiffres, relancer, "à relancer", "#B3261E", false);
            Chiffre(chiffres, repondues, "répondues", "#1E6B3C", false);
            if (sansSuite > 0) Chiffre(chiffres, sansSuite, "sans suite", "#5F6670", false);
            if (preparees > 0) Chiffre(chiffres, preparees, "à envoyer", "#8A5A00", false);
            ws.Row(2).Height = 22;
            ws.Row(3).Height = 20;
            resume.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // La légende des demandeurs et des barres, au-dessus du Gantt : elle déborde sur les
            // cellules vides, un nom long n'y est jamais tronqué.
            IXLRichText legende = ws.Cell(2, ColGantt).GetRichText();
            foreach (string a in auteurs)
            {
                DemandeSuivie exemple = lignes.Find(delegate (DemandeSuivie x) { return string.Equals(x.Auteur, a, StringComparison.OrdinalIgnoreCase); });
                XLColor c = couleurs.ContainsKey(a) ? couleurs[a] : Gris;
                legende.AddText("■ ").SetFontColor(c).SetFontSize(12).SetFontName(Police);
                legende.AddText((exemple != null ? exemple.Demandeur : a) + "     ").SetFontColor(Encre2).SetFontSize(9).SetFontName(Police);
            }
            ws.Cell(2, ColGantt).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            IXLRichText lecture = ws.Cell(3, ColGantt).GetRichText();
            lecture.AddText("Barre pleine : en attente   ·   claire : réponse reçue   ·   grise : sans suite   ·   ").SetFontColor(Discret).SetFontSize(8.5).SetFontName(Police);
            lecture.AddText("◆").SetFontColor(Orange).SetFontSize(8.5).SetFontName(Police);
            lecture.AddText(" prochain rappel   ·   colonne orange : aujourd'hui   ·   survolez une demande pour son détail").SetFontColor(Discret).SetFontSize(8.5).SetFontName(Police);

            // --- en-têtes de la partie figée ---
            string[] entetes = { "", "Demande", "Statut", "Attente", "" };
            for (int c = 0; c < entetes.Length; c++)
            {
                IXLRange h = ws.Range(LigneSemaines, c + 1, LigneEntete, c + 1);
                h.Merge();
                h.FirstCell().Value = entetes[c];
            }
            IXLRange gauche = ws.Range(LigneSemaines, 1, LigneEntete, ColDossier);
            gauche.Style.Fill.BackgroundColor = FondEntete;
            gauche.Style.Font.Bold = true;
            gauche.Style.Font.FontSize = 9;
            gauche.Style.Font.FontColor = Encre2;
            gauche.Style.Alignment.Vertical = XLAlignmentVerticalValues.Bottom;
            ws.Cell(LigneSemaines, ColAttente).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(LigneSemaines, ColStatut).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // --- calendrier : un jour ouvré par colonne, la semaine au-dessus ---
            int debutSemaine = -1;
            for (int i = 0; i < jours.Count; i++)
            {
                DateTime j = jours[i];
                IXLCell jour = ws.Cell(LigneEntete, ColGantt + i);
                jour.Value = InitialesJours[(int)j.DayOfWeek] + "\n" + j.Day;
                bool nouvelleSemaine = i == 0 || j.DayOfWeek == DayOfWeek.Monday || (j - jours[i - 1]).TotalDays > 3;
                if (nouvelleSemaine)
                {
                    if (debutSemaine >= 0) Semaine(ws, debutSemaine, i - 1, jours);
                    debutSemaine = i;
                }
            }
            if (debutSemaine >= 0) Semaine(ws, debutSemaine, jours.Count - 1, jours);

            IXLRange calendrier = ws.Range(LigneEntete, ColGantt, LigneEntete, derniereColonne);
            calendrier.Style.Fill.BackgroundColor = FondEntete;
            calendrier.Style.Font.FontSize = 8;
            calendrier.Style.Font.FontColor = Encre2;
            calendrier.Style.Alignment.WrapText = true;
            calendrier.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            calendrier.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            if (aujourdhui >= 0)
            {
                IXLCell c = ws.Cell(LigneEntete, ColGantt + aujourdhui);
                c.Style.Fill.BackgroundColor = Orange;
                c.Style.Font.FontColor = XLColor.White;
                c.Style.Font.Bold = true;
            }
            ws.Row(LigneSemaines).Height = 16;
            ws.Row(LigneEntete).Height = 26;
            IXLRange basEntete = ws.Range(LigneEntete, 1, LigneEntete, derniereColonne);
            basEntete.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            basEntete.Style.Border.BottomBorderColor = FiletSemaine;

            // --- une ligne par demande ---
            int r = LigneEntete + 1;
            foreach (DemandeSuivie d in lignes)
            {
                XLColor couleur = couleurs.ContainsKey(d.Auteur ?? "") ? couleurs[d.Auteur] : Gris;
                bool eteinte = d.Statut == DemandeSuivie.NonEnvoyee || d.Statut == DemandeSuivie.SansSuite;

                ws.Cell(r, ColBande).Style.Fill.BackgroundColor = eteinte ? Pale(couleur, 0.6) : couleur;

                // La demande sur deux lignes : à qui, puis quoi et par qui.
                IXLCell dem = ws.Cell(r, ColDemande);
                IXLRichText t = dem.GetRichText();
                t.AddText(string.IsNullOrWhiteSpace(d.Fournisseur) ? "(fournisseur ?)" : Tronquer(d.Fournisseur, 26))
                 .SetBold(true).SetFontSize(10).SetFontName(Police).SetFontColor(eteinte ? Discret : Encre);
                List<string> sous = new List<string>();
                if (!string.IsNullOrWhiteSpace(d.Reference)) sous.Add(Tronquer(d.Reference.Trim(), 18));
                sous.Add(d.NbArticles + (d.NbArticles > 1 ? " articles" : " article"));
                sous.Add(Court(d.Demandeur));
                t.AddNewLine();
                t.AddText(Tronquer(string.Join("  ·  ", sous), 40)).SetFontSize(8).SetFontName(Police).SetFontColor(Discret);
                dem.Style.Alignment.WrapText = true;
                dem.Style.Alignment.Indent = 1;
                IXLComment note = dem.CreateComment();
                note.AddText(Detail(d));
                note.Style.Size.SetAutomaticSize();

                string libelle; XLColor encre, fond;
                Pastille(d, out libelle, out encre, out fond);
                IXLCell st = ws.Cell(r, ColStatut);
                st.Value = libelle;
                st.Style.Fill.BackgroundColor = fond;
                st.Style.Font.FontColor = encre;
                st.Style.Font.Bold = true;
                st.Style.Font.FontSize = 8.5;
                st.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (d.EnvoyeeLe.HasValue)
                {
                    int n = Ouvres(d.EnvoyeeLe.Value, d.ReponseLe ?? d.ClotureLe ?? DateTime.Today);
                    IXLCell at = ws.Cell(r, ColAttente);
                    at.Value = n;
                    at.Style.NumberFormat.Format = "0\" j\"";
                    at.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    at.Style.Font.FontSize = 9;
                    at.Style.Font.Bold = d.EnAttente;
                    at.Style.Font.FontColor = !d.EnAttente ? Discret
                        : n >= 15 ? XLColor.FromHtml("#B3261E") : n >= 8 ? XLColor.FromHtml("#B06000") : Encre;
                }

                if (!string.IsNullOrWhiteSpace(d.DossierArchive))
                {
                    IXLCell lien = ws.Cell(r, ColDossier);
                    try
                    {
                        lien.Value = "↗";
                        lien.SetHyperlink(new XLHyperlink(new Uri(d.DossierArchive)));
                        lien.GetHyperlink().Tooltip = "Ouvrir le dossier de la demande";
                    }
                    catch (Exception) { lien.Value = ""; }
                    lien.Style.Font.FontColor = Accent;
                    lien.Style.Font.Underline = XLFontUnderlineValues.None;
                    lien.Style.Font.Bold = true;
                    lien.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // --- Gantt : de l'envoi à la réponse, ou jusqu'à aujourd'hui, jour ouvré par jour ouvré ---
                if (aujourdhui >= 0)
                    ws.Cell(r, ColGantt + aujourdhui).Style.Fill.BackgroundColor = FondAujourdhui;
                if (d.EnvoyeeLe.HasValue)
                {
                    DateTime debut = d.EnvoyeeLe.Value.Date;
                    DateTime fin = (d.ReponseLe ?? d.ClotureLe ?? DateTime.Today).Date;
                    XLColor barre = d.Statut == DemandeSuivie.SansSuite ? Gris
                                  : d.Statut == DemandeSuivie.Repondue ? Pale(couleur, 0.55) : couleur;
                    int premier = -1, dernier = -1;
                    for (int i = 0; i < jours.Count; i++)
                    {
                        if (jours[i] < debut || jours[i] > fin) continue;
                        ws.Cell(r, ColGantt + i).Style.Fill.BackgroundColor = barre;
                        if (premier < 0) premier = i;
                        dernier = i;
                    }
                    // Partie plus tôt que le calendrier : une flèche le dit.
                    if (premier == 0 && debut < jours[0])
                    {
                        IXLCell c = ws.Cell(r, ColGantt);
                        c.Value = "◀";
                        c.Style.Font.FontColor = XLColor.White;
                        c.Style.Font.FontSize = 7;
                        c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                    // La durée au bout de la barre, dans la première case libre.
                    if (dernier >= 0 && dernier + 1 < jours.Count)
                    {
                        IXLCell c = ws.Cell(r, ColGantt + dernier + 1);
                        int n = Ouvres(debut, fin);
                        c.Value = (d.Statut == DemandeSuivie.Repondue ? "✓ " : d.Statut == DemandeSuivie.SansSuite ? "✕ " : "")
                                + n + " j";
                        c.Style.Font.FontSize = 8;
                        c.Style.Font.FontColor = d.EnAttente ? Encre2 : Discret;
                        c.Style.Alignment.Indent = 0;
                    }
                }
                // Le prochain rappel, s'il est à venir ; tombé un week-end, il se lit le lundi.
                // Échu, c'est le statut « À relancer » qui le dit.
                if (d.EnAttente && d.ProchainRappel.HasValue && d.ProchainRappel.Value.Date > DateTime.Today)
                {
                    int i = jours.FindIndex(delegate (DateTime x) { return x >= d.ProchainRappel.Value.Date; });
                    if (i >= 0 && ws.Cell(r, ColGantt + i).IsEmpty())
                    {
                        IXLCell c = ws.Cell(r, ColGantt + i);
                        c.Value = "◆";
                        c.Style.Font.FontColor = Orange;
                        c.Style.Font.FontSize = 9;
                        c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                }

                ws.Row(r).Height = 31;
                r++;
            }

            if (lignes.Count == 0)
            {
                ws.Cell(r, ColDemande).Value = "Aucune demande pour l'instant.";
                ws.Cell(r, ColDemande).Style.Font.Italic = true;
                ws.Cell(r, ColDemande).Style.Font.FontColor = Discret;
                r++;
            }

            // --- filets : un trait léger entre les demandes, un trait par semaine ---
            int derniere = Math.Max(r - 1, LigneEntete + 1);
            IXLRange corps = ws.Range(LigneEntete + 1, 1, derniere, derniereColonne);
            corps.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            corps.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            corps.Style.Border.BottomBorderColor = Filet;
            for (int i = 0; i < jours.Count; i++)
            {
                if (i > 0 && jours[i].DayOfWeek != DayOfWeek.Monday && (jours[i] - jours[i - 1]).TotalDays <= 3) continue;
                IXLRange col = ws.Range(LigneEntete, ColGantt + i, derniere, ColGantt + i);
                col.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
                col.Style.Border.LeftBorderColor = FiletSemaine;
            }
            IXLRange separation = ws.Range(LigneSemaines, ColDossier, derniere, ColDossier);
            separation.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            separation.Style.Border.RightBorderColor = FiletSemaine;

            // Filtres sur la demande et le statut : un bouton de filtre de plus rognait l'en-tête
            // de la colonne étroite des jours.
            if (lignes.Count > 0) ws.Range(LigneEntete, ColDemande, derniere, ColStatut).SetAutoFilter();

            // La partie gauche et les en-têtes restent en place ; seuls les jours défilent.
            ws.SheetView.Freeze(LigneEntete, ColDossier);
            ws.ShowGridLines = false;

            // À l'impression : tout le tableau, Gantt compris, sur la largeur d'une page.
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A3Paper;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.SetRowsToRepeatAtTop(LigneSemaines, LigneEntete);
            ws.PageSetup.Margins.Left = 0.4;
            ws.PageSetup.Margins.Right = 0.4;

            ws.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512,
                       XLSheetProtectionElements.SelectEverything | XLSheetProtectionElements.AutoFilter);
        }

        /// <summary>Remet un onglet visible à blanc, police comprise.</summary>
        private static void Vider(IXLWorksheet ws)
        {
            ws.Unprotect(Protection);
            if (ws.AutoFilter != null && ws.AutoFilter.IsEnabled) ws.AutoFilter.Clear();
            ws.SheetView.Freeze(0, 0);
            foreach (IXLRange fusion in new List<IXLRange>(ws.MergedRanges)) fusion.Unmerge();
            ws.ConditionalFormats.RemoveAll();
            ws.Clear();
            ws.Columns().Width = 9.14;
            ws.Rows().Height = 15;
            ws.Style.Font.FontName = Police;
            ws.Style.Font.FontSize = 9.5;
            ws.Style.Font.FontColor = Encre;
        }

        /// <summary>Un chiffre et ce qu'il compte, dans le résumé en tête d'onglet.</summary>
        private static void Chiffre(IXLRichText t, int n, string quoi, string couleur, bool premier)
        {
            if (!premier) t.AddText("    ").SetFontSize(9).SetFontName(Police);
            t.AddText(n.ToString()).SetBold(true).SetFontSize(12).SetFontName(Police).SetFontColor(XLColor.FromHtml(couleur));
            t.AddText(" " + quoi).SetFontSize(9).SetFontName(Police).SetFontColor(Encre2);
        }

        /// <summary>Un texte raccourci à la place qu'il a, terminé par « … ».</summary>
        private static string Tronquer(string texte, int max)
        {
            if (string.IsNullOrEmpty(texte) || texte.Length <= max) return texte ?? "";
            return texte.Substring(0, max - 1).TrimEnd() + "…";
        }

        /// <summary>Type court, pour la colonne étroite de l'onglet de toutes les demandes.</summary>
        private static string TypeCourt(string type)
        {
            if (type == RequestTypes.SousDossier(RequestType.Fabrication)) return "Fabrication";
            if (type == RequestTypes.SousDossier(RequestType.CommandeCatalogue)) return "Catalogue";
            return "Offre";
        }

        /// <summary>« Antonin Trottet » devient « A. Trottet » : la place est comptée à gauche.</summary>
        private static string Court(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return "";
            string[] mots = nom.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (mots.Length < 2) return nom.Trim();
            return mots[0].Substring(0, 1) + ". " + string.Join(" ", mots, 1, mots.Length - 1);
        }

        /// <summary>Le libellé d'une semaine, au-dessus de ses jours : « S41 · 6 oct. ».</summary>
        private static void Semaine(IXLWorksheet ws, int de, int a, List<DateTime> jours)
        {
            IXLRange plage = ws.Range(LigneSemaines, ColGantt + de, LigneSemaines, ColGantt + a);
            plage.Merge();
            plage.FirstCell().Value = a - de >= 2
                ? "S" + ISOWeek.GetWeekOfYear(jours[de]) + " · " + jours[de].ToString("d MMM", Fr)
                : "S" + ISOWeek.GetWeekOfYear(jours[de]);
            plage.Style.Font.FontSize = 8;
            plage.Style.Font.Bold = true;
            plage.Style.Font.FontColor = Encre2;
            plage.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            plage.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            plage.Style.Fill.BackgroundColor = FondEntete;
        }

        /// <summary>
        /// Toutes les demandes, tous types confondus, avec tout leur détail : l'onglet où l'on
        /// filtre et trie. Les onglets par type, eux, restent étroits pour laisser voir le Gantt.
        /// </summary>
        private static void ConstruireToutes(XLWorkbook wb, List<DemandeSuivie> toutes, Dictionary<string, XLColor> couleurs)
        {
            IXLWorksheet ws;
            if (!wb.TryGetWorksheet(NomToutes, out ws)) ws = wb.Worksheets.Add(NomToutes, Types.Length + 1);
            Vider(ws);

            List<DemandeSuivie> lignes = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in toutes) if (!d.Masquee) lignes.Add(d);
            lignes.Sort(Ordre);

            string[] entetes = { "Type", "Statut", "Fournisseur", "Réf. commande", "Articles", "Liste des articles",
                                 "Demandé par", "Préparée le", "Envoyée le", "Réponse le", "Jours ouvrés",
                                 "Prochain rappel", "Relances", "Dernière action", "Dossier" };
            double[] largeurs = { 12, 16, 26, 16, 10, 40, 18, 13, 13, 13, 13.5, 16, 11, 34, 10 };
            int nb = entetes.Length;
            const int ligneEntete = 3;

            ws.Range(1, 1, 1, nb).Style.Fill.BackgroundColor = Nuit;
            ws.Row(1).Height = 30;
            ws.Cell(1, 1).Value = "Toutes les demandes";
            ws.Cell(1, 1).Style.Font.FontSize = 15;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(1, 4).Value = "Mis à jour le " + DateTime.Now.ToString("dd.MM.yyyy à HH:mm") + " par AskThem · lecture seule · filtres dans les en-têtes";
            ws.Cell(1, 4).Style.Font.FontSize = 8.5;
            ws.Cell(1, 4).Style.Font.FontColor = XLColor.FromHtml("#C9D6E5");
            ws.Cell(1, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(2).Height = 8;

            for (int c = 0; c < nb; c++)
            {
                ws.Cell(ligneEntete, c + 1).Value = entetes[c];
                ws.Column(c + 1).Width = largeurs[c];
            }
            IXLRange h = ws.Range(ligneEntete, 1, ligneEntete, nb);
            h.Style.Fill.BackgroundColor = FondEntete;
            h.Style.Font.Bold = true;
            h.Style.Font.FontSize = 9;
            h.Style.Font.FontColor = Encre2;
            h.Style.Alignment.WrapText = true;
            h.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            h.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            h.Style.Border.BottomBorderColor = FiletSemaine;
            ws.Row(ligneEntete).Height = 28;

            int r = ligneEntete + 1;
            foreach (DemandeSuivie d in lignes)
            {
                XLColor couleur = couleurs.ContainsKey(d.Auteur ?? "") ? couleurs[d.Auteur] : Gris;
                string libelle; XLColor encre, fond;
                Pastille(d, out libelle, out encre, out fond);

                ws.Cell(r, 1).Value = TypeCourt(d.Type);
                ws.Cell(r, 2).Value = libelle;
                ws.Cell(r, 2).Style.Fill.BackgroundColor = fond;
                ws.Cell(r, 2).Style.Font.FontColor = encre;
                ws.Cell(r, 2).Style.Font.Bold = true;
                ws.Cell(r, 3).Value = d.Fournisseur;
                ws.Cell(r, 3).Style.Font.Bold = true;
                ws.Cell(r, 4).Value = d.Reference;
                ws.Cell(r, 5).Value = d.NbArticles;
                ws.Cell(r, 6).Value = DemandeSuivie.ListeCourte(new List<string>(
                    (d.Articles ?? "").Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries)), 4);
                ws.Cell(r, 7).Value = d.Demandeur;
                ws.Cell(r, 7).Style.Border.LeftBorder = XLBorderStyleValues.Thick;
                ws.Cell(r, 7).Style.Border.LeftBorderColor = couleur;
                DateCellule(ws.Cell(r, 8), d.CreeeLe == DateTime.MinValue ? (DateTime?)null : d.CreeeLe);
                DateCellule(ws.Cell(r, 9), d.EnvoyeeLe);
                DateCellule(ws.Cell(r, 10), d.ReponseLe);
                if (d.EnvoyeeLe.HasValue)
                    ws.Cell(r, 11).Value = Ouvres(d.EnvoyeeLe.Value, d.ReponseLe ?? d.ClotureLe ?? DateTime.Today);
                if (d.EnAttente) DateCellule(ws.Cell(r, 12), d.ProchainRappel);
                if (d.NbRappels > 0) ws.Cell(r, 13).Value = d.NbRappels;
                if (!string.IsNullOrWhiteSpace(d.DerniereAction))
                    ws.Cell(r, 14).Value = d.DerniereAction
                        + (string.IsNullOrWhiteSpace(d.DerniereActionPar) ? "" : " — " + d.DerniereActionPar)
                        + (d.DerniereActionLe.HasValue ? ", " + d.DerniereActionLe.Value.ToString("dd.MM") : "");
                if (!string.IsNullOrWhiteSpace(d.DossierArchive))
                {
                    try
                    {
                        ws.Cell(r, 15).Value = "Ouvrir";
                        ws.Cell(r, 15).SetHyperlink(new XLHyperlink(new Uri(d.DossierArchive)));
                        ws.Cell(r, 15).Style.Font.FontColor = Accent;
                    }
                    catch (Exception) { ws.Cell(r, 15).Value = d.DossierArchive; }
                }
                if (d.Statut == DemandeSuivie.NonEnvoyee || d.Statut == DemandeSuivie.SansSuite)
                    ws.Range(r, 3, r, 14).Style.Font.FontColor = Discret;
                if ((r - ligneEntete) % 2 == 0)
                {
                    ws.Range(r, 1, r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FAFBFC");
                    ws.Range(r, 3, r, nb).Style.Fill.BackgroundColor = XLColor.FromHtml("#FAFBFC");
                }
                ws.Row(r).Height = 19;
                r++;
            }
            if (lignes.Count == 0)
            {
                ws.Cell(r, 1).Value = "Aucune demande pour l'instant.";
                ws.Cell(r, 1).Style.Font.Italic = true;
                ws.Cell(r, 1).Style.Font.FontColor = Discret;
                r++;
            }

            int derniere = Math.Max(r - 1, ligneEntete + 1);
            IXLRange corps = ws.Range(ligneEntete + 1, 1, derniere, nb);
            corps.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            corps.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            corps.Style.Border.BottomBorderColor = Filet;
            if (lignes.Count > 0) ws.Range(ligneEntete, 1, derniere, nb).SetAutoFilter();

            ws.SheetView.Freeze(ligneEntete, 3);
            ws.ShowGridLines = false;
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A3Paper;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.SetRowsToRepeatAtTop(ligneEntete, ligneEntete);

            ws.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512,
                       XLSheetProtectionElements.SelectEverything | XLSheetProtectionElements.AutoFilter
                       | XLSheetProtectionElements.Sort);
        }

        /// <summary>
        /// Les demandes retirées du suivi. Rien n'est effacé : elles attendent ici, et se
        /// rétablissent depuis AskThem.
        /// </summary>
        private static void ConstruireSupprimees(XLWorkbook wb, List<DemandeSuivie> toutes)
        {
            IXLWorksheet ws;
            if (!wb.TryGetWorksheet(NomSupprimees, out ws)) ws = wb.Worksheets.Add(NomSupprimees, Types.Length + 2);
            Vider(ws);

            List<DemandeSuivie> lignes = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in toutes) if (d.Masquee) lignes.Add(d);
            lignes.Sort(delegate (DemandeSuivie a, DemandeSuivie b)
            {
                return Nullable.Compare(b.MasqueeLe, a.MasqueeLe);           // la plus récente d'abord
            });

            string[] entetes = { "Type", "Fournisseur", "Réf. commande", "Articles", "Demandé par",
                                 "Statut", "Envoyée le", "Supprimée le", "Supprimée par" };
            double[] largeurs = { 12, 26, 16, 10, 18, 16, 13, 16, 20 };
            int nb = entetes.Length;
            const int ligneEntete = 4;

            ws.Range(1, 1, 1, nb).Style.Fill.BackgroundColor = Nuit;
            ws.Row(1).Height = 30;
            ws.Cell(1, 1).Value = "Demandes supprimées";
            ws.Cell(1, 1).Style.Font.FontSize = 15;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(2, 1).Value = "Retirées du suivi et du Gantt, jamais effacées. Pour en rétablir une : AskThem › "
                                + "Suivi des demandes › Supprimées › Rétablir.";
            ws.Cell(2, 1).Style.Font.FontColor = Encre2;
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Row(2).Height = 22;
            ws.Cell(2, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            for (int c = 0; c < nb; c++)
            {
                ws.Cell(ligneEntete, c + 1).Value = entetes[c];
                ws.Column(c + 1).Width = largeurs[c];
            }
            IXLRange h = ws.Range(ligneEntete, 1, ligneEntete, nb);
            h.Style.Fill.BackgroundColor = FondEntete;
            h.Style.Font.Bold = true;
            h.Style.Font.FontSize = 9;
            h.Style.Font.FontColor = Encre2;
            h.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            h.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            h.Style.Border.BottomBorderColor = FiletSemaine;
            ws.Row(ligneEntete).Height = 24;

            int r = ligneEntete + 1;
            foreach (DemandeSuivie d in lignes)
            {
                ws.Cell(r, 1).Value = TypeCourt(d.Type);
                ws.Cell(r, 2).Value = d.Fournisseur;
                ws.Cell(r, 2).Style.Font.Bold = true;
                ws.Cell(r, 3).Value = d.Reference;
                ws.Cell(r, 4).Value = d.NbArticles;
                ws.Cell(r, 5).Value = d.Demandeur;
                ws.Cell(r, 6).Value = d.Statut;
                DateCellule(ws.Cell(r, 7), d.EnvoyeeLe);
                DateCellule(ws.Cell(r, 8), d.MasqueeLe);
                ws.Cell(r, 9).Value = d.MasqueePar;
                ws.Range(r, 1, r, nb).Style.Font.FontColor = Encre2;
                ws.Row(r).Height = 19;
                r++;
            }
            if (lignes.Count == 0)
            {
                ws.Cell(r, 1).Value = "Aucune demande supprimée.";
                ws.Cell(r, 1).Style.Font.Italic = true;
                ws.Cell(r, 1).Style.Font.FontColor = Discret;
                r++;
            }

            int derniere = Math.Max(r - 1, ligneEntete + 1);
            IXLRange corps = ws.Range(ligneEntete + 1, 1, derniere, nb);
            corps.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            corps.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            corps.Style.Border.BottomBorderColor = Filet;
            if (lignes.Count > 0) ws.Range(ligneEntete, 1, derniere, nb).SetAutoFilter();
            ws.SheetView.Freeze(ligneEntete, 0);
            ws.ShowGridLines = false;
            ws.SetTabColor(XLColor.FromHtml("#B4BAC1"));

            ws.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512,
                       XLSheetProtectionElements.SelectEverything | XLSheetProtectionElements.AutoFilter
                       | XLSheetProtectionElements.Sort);
        }

        /// <summary>
        /// À l'ouverture, le Gantt montre les dernières semaines, aujourd'hui compris : les
        /// jours défilent depuis la partie figée au lieu de commencer trois mois plus tôt.
        /// </summary>
        private static void PositionnerGantt(string chemin)
        {
            List<DateTime> jours = JoursOuvres();
            int aujourdhui = jours.IndexOf(DateTime.Today);
            if (aujourdhui < 0)
            {
                for (int i = 0; i < jours.Count; i++) if (jours[i] > DateTime.Today) { aujourdhui = i; break; }
            }
            if (aujourdhui < 0) return;
            int premiere = ColGantt + Math.Max(0, aujourdhui - JoursVisiblesAvant);
            string cellule = XLHelper.GetColumnLetterFromNumber(premiere) + (LigneEntete + 1);

            try
            {
                using (DocumentFormat.OpenXml.Packaging.SpreadsheetDocument doc =
                           DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(chemin, true))
                {
                    DocumentFormat.OpenXml.Packaging.WorkbookPart wbp = doc.WorkbookPart;
                    foreach (DocumentFormat.OpenXml.Spreadsheet.Sheet s in wbp.Workbook.Sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
                    {
                        if (Array.IndexOf(Types, s.Name.Value) < 0) continue;
                        DocumentFormat.OpenXml.Packaging.WorksheetPart wsp =
                            (DocumentFormat.OpenXml.Packaging.WorksheetPart)wbp.GetPartById(s.Id.Value);
                        foreach (DocumentFormat.OpenXml.Spreadsheet.Pane pane in
                                 wsp.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Pane>())
                        {
                            pane.TopLeftCell = cellule;
                        }
                        wsp.Worksheet.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Position initiale du Gantt non réglée : " + ex.Message);
            }
        }

        private static void ProtegerClasseur(XLWorkbook wb)
        {
            foreach (string type in Types)
            {
                IXLWorksheet bdd;
                if (!wb.TryGetWorksheet(NomBdd(type), out bdd)) continue;
                bdd.Unprotect(Protection);
                bdd.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512, XLSheetProtectionElements.SelectEverything);
                bdd.Visibility = XLWorksheetVisibility.Hidden;
            }
            IXLWorksheet premiere;
            if (wb.TryGetWorksheet(Types[0], out premiere)) premiere.SetTabActive();
            try { wb.Unprotect(Protection); }
            catch (Exception) { }
            wb.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512, XLWorkbookProtectionElements.Windows);
        }

        private static int Ordre(DemandeSuivie a, DemandeSuivie b)
        {
            int ra = Rang(a.Statut), rb = Rang(b.Statut);
            if (ra != rb) return ra.CompareTo(rb);
            if (a.Statut == DemandeSuivie.Envoyee)
                return Nullable.Compare(a.EnvoyeeLe, b.EnvoyeeLe);          // la plus ancienne attente d'abord
            DateTime da = a.EnvoyeeLe ?? a.CreeeLe, db = b.EnvoyeeLe ?? b.CreeeLe;
            return db.CompareTo(da);                                         // la plus récente d'abord
        }

        private static int Rang(string statut)
        {
            switch (statut)
            {
                case DemandeSuivie.Envoyee: return 0;
                case DemandeSuivie.Preparee: return 1;
                case DemandeSuivie.Repondue: return 2;
                case DemandeSuivie.SansSuite: return 3;
                default: return 4;
            }
        }

        private static string Titre(string type)
        {
            if (type == RequestTypes.SousDossier(RequestType.Fabrication)) return "Demandes de fabrication";
            if (type == RequestTypes.SousDossier(RequestType.CommandeCatalogue)) return "Commandes catalogue";
            return "Demandes d'offre";
        }

        private static void DateCellule(IXLCell cell, DateTime? date)
        {
            if (!date.HasValue) return;
            cell.Value = date.Value.Date;
            cell.Style.DateFormat.Format = "dd.mm.yyyy";
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        /// <summary>
        /// Marque le classeur « lecture seule conseillée » : Excel propose de l'ouvrir sans le
        /// bloquer. Ouvert en écriture, il empêcherait AskThem d'y consigner les demandes.
        /// </summary>
        private static void LectureSeuleConseillee(string chemin)
        {
            try
            {
                using (DocumentFormat.OpenXml.Packaging.SpreadsheetDocument doc =
                           DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(chemin, true))
                {
                    DocumentFormat.OpenXml.Spreadsheet.Workbook classeur = doc.WorkbookPart.Workbook;
                    classeur.FileSharing = new DocumentFormat.OpenXml.Spreadsheet.FileSharing() { ReadOnlyRecommended = true };
                    classeur.Save();
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Lecture seule conseillée non posée : " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ fichiers locaux

        private static Dictionary<string, DemandeSuivie> LireJson(string chemin)
        {
            try
            {
                if (File.Exists(chemin))
                {
                    Dictionary<string, DemandeSuivie> d = JsonSerializer.Deserialize<Dictionary<string, DemandeSuivie>>(
                        File.ReadAllText(chemin, Encoding.UTF8));
                    if (d != null) return new Dictionary<string, DemandeSuivie>(d, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Suivi local illisible (" + Path.GetFileName(chemin) + ") : " + ex.Message);
            }
            return new Dictionary<string, DemandeSuivie>(StringComparer.OrdinalIgnoreCase);
        }

        private static void EcrireJson(string chemin, Dictionary<string, DemandeSuivie> contenu)
        {
            try
            {
                JsonSerializerOptions o = new JsonSerializerOptions();
                o.WriteIndented = true;
                string temporaire = chemin + ".tmp";
                File.WriteAllText(temporaire, JsonSerializer.Serialize(contenu, o), new UTF8Encoding(false));
                File.Move(temporaire, chemin, true);
            }
            catch (Exception ex)
            {
                LogService.Write("Suivi local non écrit (" + Path.GetFileName(chemin) + ") : " + ex.Message);
            }
        }

        /// <summary>
        /// Verrou partagé entre les postes : un fichier créé en exclusivité à côté du classeur.
        /// Un verrou abandonné par un poste éteint en pleine écriture est repris après
        /// quelques minutes.
        /// </summary>
        private sealed class VerrouFichier : IDisposable
        {
            private FileStream _flux;
            private readonly string _chemin;

            private VerrouFichier(FileStream flux, string chemin)
            {
                _flux = flux;
                _chemin = chemin;
            }

            public static VerrouFichier Prendre(string chemin, TimeSpan attente)
            {
                DateTime limite = DateTime.Now + attente;
                while (true)
                {
                    try
                    {
                        FileStream f = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                        byte[] qui = Encoding.UTF8.GetBytes(System.Environment.MachineName + " " + System.Environment.UserName + " " + DateTime.Now.ToString("s"));
                        f.Write(qui, 0, qui.Length);
                        f.Flush();
                        return new VerrouFichier(f, chemin);
                    }
                    catch (IOException)
                    {
                        try
                        {
                            if (File.Exists(chemin) && DateTime.Now - File.GetLastWriteTime(chemin) > TimeSpan.FromMinutes(5))
                                File.Delete(chemin);
                        }
                        catch (Exception) { }
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                    if (DateTime.Now > limite) return null;
                    Thread.Sleep(500);
                }
            }

            public void Dispose()
            {
                try { if (_flux != null) _flux.Dispose(); }
                catch (Exception) { }
                _flux = null;
            }
        }
    }
}
