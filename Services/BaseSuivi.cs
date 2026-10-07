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
    /// un Gantt des semaines d'attente. Tous sont protégés : on lit, on filtre, on ne modifie
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
            "ReponseLe", "ClotureLe", "ClotureePar", "DossierArchive", "MisAJourLe"
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
                if (file.Count == 0) return true;

                string chemin = Chemin(config);
                if (chemin == "" || !Directory.Exists(Path.GetDirectoryName(chemin)))
                {
                    message = "Base des demandes injoignable : " + (chemin == "" ? "aucun chemin configuré" : Path.GetDirectoryName(chemin)) + ".";
                    return false;
                }

                using (VerrouFichier v = VerrouFichier.Prendre(chemin + ".verrou", TimeSpan.FromSeconds(20)))
                {
                    if (v == null)
                    {
                        message = "Base des demandes occupée par un autre poste : écriture reportée.";
                        return false;
                    }

                    string temporaire = chemin + ".ecriture-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".xlsx";
                    try
                    {
                        using (XLWorkbook wb = Ouvrir(chemin))
                        {
                            foreach (string type in Types) Preparer(wb, type);
                            foreach (DemandeSuivie d in file.Values) Ecrire(wb, d);

                            List<DemandeSuivie> toutes = LireToutes(wb);
                            foreach (string type in Types) Construire(wb, type, toutes);
                            ProtegerClasseur(wb);
                            wb.SaveAs(temporaire);
                            MemoriserMiennes(toutes);
                        }
                        LectureSeuleConseillee(temporaire);

                        // Remplacement d'un seul geste. Un classeur ouvert dans Excel refuse
                        // d'être remplacé : on n'écrase rien, on réessaiera.
                        if (File.Exists(chemin)) File.Replace(temporaire, chemin, null);
                        else File.Move(temporaire, chemin);
                    }
                    catch (Exception ex)
                    {
                        try { if (File.Exists(temporaire)) File.Delete(temporaire); }
                        catch (Exception) { }
                        message = "Base des demandes non écrite (" + ex.Message + ") : écriture reportée.";
                        LogService.Write(message);
                        return false;
                    }
                }

                EcrireJson(CheminFile(), new Dictionary<string, DemandeSuivie>());
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
                foreach (DemandeSuivie d in LireJson(CheminFile()).Values) parId[d.Id] = d;
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

        private static readonly XLColor Bleu = XLColor.FromHtml("#2F6FB3");
        private static readonly XLColor Vert = XLColor.FromHtml("#5B9B3C");
        private static readonly XLColor Gris = XLColor.FromHtml("#A6A6A6");

        private const int ColGantt = 13;
        private const int SemainesAvant = 20;
        private const int SemainesApres = 4;

        /// <summary>
        /// Reconstruit l'onglet visible d'un type à partir de son onglet de données : rien n'y
        /// est saisi, tout y est déduit, et il est protégé.
        /// </summary>
        private static void Construire(XLWorkbook wb, string type, List<DemandeSuivie> toutes)
        {
            IXLWorksheet ws = wb.Worksheet(type);
            ws.Unprotect(Protection);
            ws.Clear();
            ws.ConditionalFormats.RemoveAll();
            if (ws.AutoFilter != null && ws.AutoFilter.IsEnabled) ws.AutoFilter.Clear();

            List<DemandeSuivie> lignes = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in toutes)
                if (string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase)) lignes.Add(d);
            lignes.Sort(Ordre);

            int attente = 0, repondues = 0, sansSuite = 0, nonEnvoyees = 0, preparees = 0;
            foreach (DemandeSuivie d in lignes)
            {
                if (d.Statut == DemandeSuivie.Envoyee) attente++;
                else if (d.Statut == DemandeSuivie.Repondue) repondues++;
                else if (d.Statut == DemandeSuivie.SansSuite) sansSuite++;
                else if (d.Statut == DemandeSuivie.NonEnvoyee) nonEnvoyees++;
                else preparees++;
            }

            // --- titre et compteurs ---
            ws.Cell(1, 1).Value = Titre(type) + " — suivi";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 15;
            ws.Cell(1, 6).Value = "Mis à jour le " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + " par AskThem — onglet en lecture seule, construit à partir des données.";
            ws.Cell(1, 6).Style.Font.FontColor = XLColor.FromHtml("#7F7F7F");
            ws.Cell(1, 6).Style.Font.Italic = true;
            ws.Cell(2, 1).Value = "En attente de réponse : " + attente + "     Réponses reçues : " + repondues
                                + "     Sans suite : " + sansSuite + "     Préparées, pas encore parties : " + preparees
                                + "     Jamais envoyées : " + nonEnvoyees;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#404040");

            // --- en-têtes ---
            string[] entetes = { "Statut", "Fournisseur", "Réf. commande", "Articles", "Liste des articles",
                                 "Demandé par", "Préparée le", "Envoyée le", "Réponse le", "Jours", "Prochain rappel", "Dossier" };
            int ligneEntete = 4;
            for (int c = 0; c < entetes.Length; c++) ws.Cell(ligneEntete, c + 1).Value = entetes[c];

            DateTime lundi = Lundi(DateTime.Today);
            DateTime premiere = lundi.AddDays(-7 * SemainesAvant);
            int nbSemaines = SemainesAvant + SemainesApres + 1;
            for (int s = 0; s < nbSemaines; s++)
            {
                DateTime debut = premiere.AddDays(7 * s);
                IXLCell haut = ws.Cell(ligneEntete - 1, ColGantt + s);
                haut.Value = debut.ToString("dd.MM");
                haut.Style.Font.FontSize = 7;
                haut.Style.Alignment.TextRotation = 90;
                haut.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                IXLCell sem = ws.Cell(ligneEntete, ColGantt + s);
                sem.Value = "S" + ISOWeek.GetWeekOfYear(debut);
                sem.Style.Font.FontSize = 8;
                sem.Style.Alignment.TextRotation = 90;
                if (debut == lundi)
                {
                    sem.Style.Fill.BackgroundColor = XLColor.FromHtml("#F4B183");
                    haut.Style.Fill.BackgroundColor = XLColor.FromHtml("#F4B183");
                }
                ws.Column(ColGantt + s).Width = 2.6;
            }

            IXLRange entete = ws.Range(ligneEntete, 1, ligneEntete, ColGantt + nbSemaines - 1);
            entete.Style.Font.Bold = true;
            entete.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDE5EE");
            entete.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(ligneEntete, 1, ligneEntete, entetes.Length).Style.Alignment.WrapText = true;
            ws.Row(ligneEntete).Height = 30;
            ws.Row(ligneEntete - 1).Height = 30;

            // --- lignes ---
            int r = ligneEntete + 1;
            foreach (DemandeSuivie d in lignes)
            {
                ws.Cell(r, 1).Value = d.Statut;
                ws.Cell(r, 1).Style.Fill.BackgroundColor = CouleurStatut(d.Statut);
                ws.Cell(r, 2).Value = d.Fournisseur;
                ws.Cell(r, 3).Value = d.Reference;
                ws.Cell(r, 4).Value = d.NbArticles;
                ws.Cell(r, 5).Value = DemandeSuivie.ListeCourte(new List<string>(
                    (d.Articles ?? "").Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries)), 8);
                ws.Cell(r, 6).Value = string.IsNullOrWhiteSpace(d.AuteurNom) ? d.Auteur : d.AuteurNom;
                DateCellule(ws.Cell(r, 7), d.CreeeLe == DateTime.MinValue ? (DateTime?)null : d.CreeeLe);
                DateCellule(ws.Cell(r, 8), d.EnvoyeeLe);
                DateCellule(ws.Cell(r, 9), d.ReponseLe);
                if (d.EnvoyeeLe.HasValue)
                {
                    DateTime fin = d.ReponseLe ?? d.ClotureLe ?? DateTime.Today;
                    ws.Cell(r, 10).Value = Math.Max(0, (int)(fin.Date - d.EnvoyeeLe.Value.Date).TotalDays);
                }
                if (d.EnAttente) DateCellule(ws.Cell(r, 11), d.ProchainRappel);
                if (!string.IsNullOrWhiteSpace(d.DossierArchive))
                {
                    ws.Cell(r, 12).Value = "Ouvrir";
                    try { ws.Cell(r, 12).SetHyperlink(new XLHyperlink(new Uri(d.DossierArchive))); }
                    catch (Exception) { ws.Cell(r, 12).Value = d.DossierArchive; }
                }

                if (d.Statut == DemandeSuivie.NonEnvoyee)
                    ws.Range(r, 2, r, 12).Style.Font.FontColor = XLColor.FromHtml("#8C8C8C");

                // --- Gantt : de l'envoi à la réponse, ou jusqu'à aujourd'hui ---
                if (d.EnvoyeeLe.HasValue)
                {
                    DateTime debut = d.EnvoyeeLe.Value.Date;
                    DateTime fin = (d.ReponseLe ?? d.ClotureLe ?? DateTime.Today).Date;
                    XLColor couleur = d.Statut == DemandeSuivie.Repondue ? Vert
                                    : d.Statut == DemandeSuivie.SansSuite ? Gris : Bleu;
                    for (int s = 0; s < nbSemaines; s++)
                    {
                        DateTime semDebut = premiere.AddDays(7 * s);
                        DateTime semFin = semDebut.AddDays(6);
                        if (semFin >= debut && semDebut <= fin)
                            ws.Cell(r, ColGantt + s).Style.Fill.BackgroundColor = couleur;
                    }
                }
                ws.Cell(r, ColGantt + SemainesAvant).Style.Border.LeftBorder = XLBorderStyleValues.Thin;
                ws.Cell(r, ColGantt + SemainesAvant).Style.Border.LeftBorderColor = XLColor.FromHtml("#ED7D31");
                r++;
            }

            if (lignes.Count == 0)
            {
                ws.Cell(r, 1).Value = "Aucune demande pour l'instant.";
                ws.Cell(r, 1).Style.Font.Italic = true;
            }

            // --- mise en forme ---
            double[] largeurs = { 14, 26, 18, 8, 44, 20, 16, 16, 16, 7, 14, 9 };
            for (int c = 0; c < largeurs.Length; c++) ws.Column(c + 1).Width = largeurs[c];
            int derniere = Math.Max(r - 1, ligneEntete);
            ws.Range(ligneEntete + 1, 1, derniere, entetes.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Range(ligneEntete + 1, 5, derniere, 5).Style.Alignment.WrapText = true;
            if (lignes.Count > 0)
            {
                ws.Range(ligneEntete, 1, derniere, entetes.Length).SetAutoFilter();
                IXLRange tableau = ws.Range(ligneEntete, 1, derniere, ColGantt + nbSemaines - 1);
                tableau.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                tableau.Style.Border.InsideBorderColor = XLColor.FromHtml("#D9D9D9");
                tableau.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            ws.SheetView.FreezeRows(ligneEntete);
            ws.SheetView.FreezeColumns(2);

            // À l'impression : tout le tableau, Gantt compris, sur la largeur d'une page.
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.SetRowsToRepeatAtTop(ligneEntete - 1, ligneEntete);
            ws.PageSetup.Margins.Left = 0.4;
            ws.PageSetup.Margins.Right = 0.4;

            ws.Protect(Protection, XLProtectionAlgorithm.Algorithm.SHA512,
                       XLSheetProtectionElements.SelectEverything | XLSheetProtectionElements.AutoFilter);
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

        private static XLColor CouleurStatut(string statut)
        {
            switch (statut)
            {
                case DemandeSuivie.Envoyee: return XLColor.FromHtml("#DDEBF7");
                case DemandeSuivie.Repondue: return XLColor.FromHtml("#E2EFDA");
                case DemandeSuivie.SansSuite: return XLColor.FromHtml("#EDEDED");
                case DemandeSuivie.NonEnvoyee: return XLColor.FromHtml("#F7F7F7");
                default: return XLColor.FromHtml("#FFF2CC");
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

        private static DateTime Lundi(DateTime jour)
        {
            int ecart = ((int)jour.DayOfWeek + 6) % 7;
            return jour.Date.AddDays(-ecart);
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
