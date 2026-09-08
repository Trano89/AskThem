using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AskThem.Config;
using AskThem.Inspection;
using AskThem.Models;
using SolidWorks.Interop.sldworks;

namespace AskThem.Services
{
    /// <summary>
    /// Met la base articles à jour en masse, depuis un poste équipé.
    ///
    /// Le principe qui rend l'affaire tenable : l'empreinte d'un article se calcule sans
    /// ouvrir SolidWorks, à partir de la seule taille et de la date des fichiers du coffre.
    /// On sait donc AVANT de démarrer SolidWorks lesquels ont bougé. La première campagne
    /// traite quelques centaines d'articles ; les suivantes n'en traitent que quelques
    /// dizaines et durent quelques minutes.
    ///
    /// Le contrôle de fabrication n'est pas produit ici : il porte le nom du fournisseur et
    /// la référence de l'affaire, qui n'existent qu'au moment d'une demande. Il reste donc
    /// l'apanage des postes équipés, au coup par coup.
    /// </summary>
    public class CampagneDepot
    {
        // ------------------------------------------------------------------ types

        public class Options
        {
            /// <summary>Catégories retenues. Les articles de catalogue n'ont rien à livrer.</summary>
            public List<string> Categories { get; set; }

            /// <summary>Les assemblages ouvrent tous leurs composants : hors campagne par défaut.</summary>
            public bool InclureAssemblages { get; set; }

            /// <summary>Nombre d'articles entre deux redémarrages de SolidWorks.</summary>
            public int TailleLot { get; set; }

            /// <summary>0 = tous. Sert à mesurer le coût réel sur un échantillon.</summary>
            public int MaxArticles { get; set; }

            /// <summary>Ne rien écrire : on regarde l'état de la base et on s'arrête là.</summary>
            public bool RecensementSeul { get; set; }

            public Options()
            {
                Categories = new List<string> { "21", "22", "24" };
                InclureAssemblages = false;
                TailleLot = 25;
                MaxArticles = 0;
                RecensementSeul = false;
            }
        }

        /// <summary>Ce qu'on sait d'un article avant d'avoir ouvert SolidWorks.</summary>
        public class Candidat
        {
            public string NoArticle;
            public string Modele;
            public string Plan;
            public string Empreinte;
            public string Verdict;      // "à jour", "à produire", "à remplacer", "sans source"
        }

        public class Bilan
        {
            public int Candidats, AJour, Produits, Remplaces, SansSource, NonLiberes, Echecs, Ignores;
            public TimeSpan Duree;
            public List<string> Orphelins = new List<string>();
            public string CheminRapport = "";
        }

        /// <summary>Reprise : ce qui reste à faire, écrit après chaque lot.</summary>
        public class Etat
        {
            public List<string> Restants { get; set; }
            public DateTime DebutLe { get; set; }
            public int DejaTraites { get; set; }
            public Etat() { Restants = new List<string>(); }

            private static string Chemin()
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "AskThem", "campagne.json");
            }

            public static Etat Lire()
            {
                try
                {
                    string p = Chemin();
                    if (!File.Exists(p)) return null;
                    JsonSerializerOptions o = new JsonSerializerOptions();
                    o.PropertyNameCaseInsensitive = true;
                    Etat e = JsonSerializer.Deserialize<Etat>(File.ReadAllText(p), o);
                    if (e == null || e.Restants == null || e.Restants.Count == 0) return null;
                    return e;
                }
                catch (Exception) { return null; }
            }

            public void Ecrire()
            {
                try
                {
                    string p = Chemin();
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    JsonSerializerOptions o = new JsonSerializerOptions();
                    o.WriteIndented = true;
                    File.WriteAllText(p, JsonSerializer.Serialize(this, o), Encoding.UTF8);
                }
                catch (Exception ex) { LogService.Write("État de campagne non écrit : " + ex.Message); }
            }

            public static void Effacer()
            {
                try { if (File.Exists(Chemin())) File.Delete(Chemin()); }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ état

        private readonly AppConfig _config;
        private readonly DepotArticles _depot;
        private readonly Action<string> _journal;
        private readonly Func<bool> _annule;
        private readonly ControleFabricationConfig _controleCfg;

        /// <summary>Base de l'inventaire, quand c'est elle qui porte les documents.</summary>
        private DepotInventaire _inventaire;

        public CampagneDepot(AppConfig config, DepotArticles depot,
                             Action<string> journal, Func<bool> annulation)
        {
            _config = config;
            _depot = depot;
            _journal = journal;
            _annule = annulation;

            // Le contrôle est extrait pendant la campagne, au même titre que le plan et le
            // modèle : sans lui, un poste sans SolidWorks n'aurait pas de formulaire à joindre
            // à une demande de fabrication.
            try { _controleCfg = ControleFabricationConfig.Load(); }
            catch (Exception ex) { LogService.Write("Réglages de contrôle illisibles : " + ex.Message); }
        }

        /// <summary>
        /// Fait publier la campagne dans l'inventaire plutôt que sur le partage.
        ///
        /// Le recensement continue de s'appuyer sur la base passée au constructeur pour
        /// savoir ce qui existe ; c'est le dépôt qui change de destination.
        /// </summary>
        public void PublierDansInventaire(DepotInventaire inventaire)
        {
            _inventaire = inventaire;
        }

        private void Dire(string message)
        {
            LogService.Write(message);
            if (_journal != null) { try { _journal(message); } catch (Exception) { } }
        }

        private bool Annule() { return _annule != null && _annule(); }

        // ------------------------------------------------------------------ recensement

        /// <summary>
        /// Établit la liste des articles du périmètre, et l'état de chacun, SANS ouvrir
        /// SolidWorks. C'est la partie qui coûte quelques secondes et répond à la question
        /// « où en est notre documentation ».
        /// </summary>
        public List<Candidat> Recenser(Dictionary<string, string> indexPdm, Options o)
        {
            List<Candidat> candidats = new List<Candidat>();
            if (indexPdm == null) return candidats;

            // Le reconditionnement écrit sur le partage : il n'a donc pas sa place dans un
            // recensement, annoncé comme n'écrivant rien. Il a lieu au début d'une production.
            if (_inventaire == null && !o.RecensementSeul) _depot.Reconditionner(_journal);

            List<string> articles = new List<string>();
            foreach (string cle in indexPdm.Keys)
            {
                string sansExt = Path.GetFileNameWithoutExtension(cle);
                if (string.IsNullOrWhiteSpace(sansExt)) continue;

                string numero = PartNumberFormat.Normalize(sansExt, _config.PartNumberPatterns);
                if (string.IsNullOrWhiteSpace(numero)) continue;
                if (!PartNumberFormat.IsValid(numero, _config.PartNumberPatterns)) continue;
                if (articles.Contains(numero)) continue;

                // Le perimetre porte sur ce que la reference declare : seuls les articles
                // qui attendent un plan interne ont des documents a publier. Un article de
                // catalogue non modifie se commande par sa reference fournisseur.
                if (!Codification.AttendUnPlan(numero)) continue;

                string categorie = PartNumberFormat.TypeCode(numero);
                if (o.Categories != null && o.Categories.Count > 0 && !o.Categories.Contains(categorie)) continue;

                ArticleTypeRule regle = ValidationArticle.RegleDe(_config, numero);
                if (!regle.Allowed) continue;
                if (!regle.Export2D && !regle.Export3D) continue;

                articles.Add(numero);
            }
            articles.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string numero in articles)
            {
                Candidat c = new Candidat();
                c.NoArticle = numero;

                // Les references de projet et les articles non geres restent disponibles pour
                // une demande ponctuelle, mais n'entrent pas dans la base de production.
                if (!Codification.EstDeProduction(numero))
                {
                    c.Verdict = "ignoré (" + Codification.LibelleOrigine(numero) + ")";
                    if (Codification.Categorie(numero) == Codification.Projet)
                        c.Verdict = "ignoré (projet)";
                    candidats.Add(c);
                    continue;
                }

                c.Modele = PdmSearchService.Find3DInIndex(indexPdm, numero);
                c.Plan = PdmSearchService.FindDrawingInIndex(indexPdm, numero);

                // La structure se lit sur le numero, pas sur l'extension du fichier : c'est
                // la reference qui declare ce qu'est l'article, tandis que l'extension ne dit
                // que la facon dont il a ete modelise. Un assemblage ouvre tous ses
                // composants, et c'est la que les campagnes s'enlisent.
                if (Codification.EstAssemblage(numero) && !o.InclureAssemblages)
                {
                    c.Verdict = "ignoré (" + Codification.LibelleStructure(numero) + ")";
                    candidats.Add(c);
                    continue;
                }

                if (c.Modele == null && c.Plan == null)
                {
                    c.Verdict = "sans source";
                    candidats.Add(c);
                    continue;
                }

                c.Empreinte = DepotArticles.Empreinte(c.Modele, c.Plan);

                // Quand l'inventaire porte les documents, c'est lui qui dit ce qui existe.
                if (_inventaire != null)
                {
                    DocumentsArticle d = _inventaire.Pour(numero);
                    if (d == null || !d.Trouve) c.Verdict = "hors inventaire";
                    else if (d.Documents.Count == 0) c.Verdict = "à produire";
                    else if (d.De(TypeDocument.Controle) == null && c.Plan != null) c.Verdict = "sans contrôle";
                    else c.Verdict = "à jour";
                    candidats.Add(c);
                    continue;
                }

                FicheArticle enPlace = _depot.Lire(numero);

                if (enPlace == null) c.Verdict = "à produire";
                else if (enPlace.Empreinte != c.Empreinte) c.Verdict = "à remplacer";
                else if (enPlace.Controle == null && c.Plan != null) c.Verdict = "sans contrôle";
                else c.Verdict = "à jour";

                candidats.Add(c);
            }
            return candidats;
        }

        // ------------------------------------------------------------------ exécution

        /// <summary>
        /// Produit et publie les articles à faire, par lots, en redémarrant SolidWorks entre
        /// chacun : c'est ce qui distingue une campagne qui va au bout d'une campagne qui
        /// s'enlise vers le centième document.
        /// </summary>
        public Bilan Executer(List<Candidat> candidats, Options o, Action<int, int, string> avancement)
        {
            DateTime debut = DateTime.Now;
            Bilan bilan = new Bilan();

            List<Candidat> aFaire = new List<Candidat>();
            foreach (Candidat c in candidats)
            {
                bilan.Candidats++;
                if (c.Verdict == "à jour") bilan.AJour++;
                else if (c.Verdict == "sans source") bilan.SansSource++;
                else if (c.Verdict != null && c.Verdict.StartsWith("ignoré")) bilan.Ignores++;
                else aFaire.Add(c);
            }

            if (o.MaxArticles > 0 && aFaire.Count > o.MaxArticles)
            {
                Dire("Campagne limitée à " + o.MaxArticles + " article(s) sur " + aFaire.Count + " à faire.");
                aFaire = aFaire.GetRange(0, o.MaxArticles);
            }

            bilan.Orphelins = Orphelins(candidats);

            if (o.RecensementSeul)
            {
                Dire("Recensement seul : " + bilan.Candidats + " article(s) examinés, "
                   + aFaire.Count + " à produire ou remplacer. Rien n'a été écrit.");
                bilan.Duree = DateTime.Now - debut;
                bilan.CheminRapport = EcrireRapport(candidats, bilan, o);
                return bilan;
            }

            if (aFaire.Count == 0)
            {
                Dire("Base articles déjà à jour : rien à produire.");
                bilan.Duree = DateTime.Now - debut;
                bilan.CheminRapport = EcrireRapport(candidats, bilan, o);
                Etat.Effacer();
                return bilan;
            }

            string travail = Path.Combine(Path.GetTempPath(), "AskThem-campagne");
            Directory.CreateDirectory(travail);

            Etat etat = new Etat();
            etat.DebutLe = DateTime.Now;
            foreach (Candidat c in aFaire) etat.Restants.Add(c.NoArticle);
            etat.Ecrire();

            int taille = o.TailleLot > 0 ? o.TailleLot : 25;
            int traites = 0;

            for (int depart = 0; depart < aFaire.Count && !Annule(); depart += taille)
            {
                int fin = Math.Min(depart + taille, aFaire.Count);
                SolidWorksExporter exporter = new SolidWorksExporter(_config.Properties);
                bool connecte = false;

                try
                {
                    exporter.Connect();
                    connecte = true;

                    for (int i = depart; i < fin && !Annule(); i++)
                    {
                        Candidat c = aFaire[i];
                        traites++;
                        if (avancement != null) avancement(traites, aFaire.Count, c.NoArticle);

                        try
                        {
                            TraiterUn(exporter, c, travail, bilan);
                        }
                        catch (Exception ex)
                        {
                            bilan.Echecs++;
                            Dire("ERREUR " + c.NoArticle + " : " + ex.Message);
                        }

                        etat.Restants.Remove(c.NoArticle);
                        etat.DejaTraites = traites;
                    }
                }
                catch (Exception ex)
                {
                    Dire("ERREUR SolidWorks : " + ex.Message);
                    bilan.Echecs += fin - depart;
                }
                finally
                {
                    exporter.Dispose();
                    if (connecte) Dire("Lot terminé, SolidWorks redémarré.");
                    etat.Ecrire();
                }
            }

            try { Directory.Delete(travail, true); } catch (Exception) { }

            if (Annule())
            {
                Dire("Campagne interrompue : " + etat.Restants.Count
                   + " article(s) restent à faire, la reprise sera proposée au prochain démarrage.");
            }
            else
            {
                Etat.Effacer();
            }

            bilan.Duree = DateTime.Now - debut;
            bilan.CheminRapport = EcrireRapport(candidats, bilan, o);
            return bilan;
        }

        // ------------------------------------------------------------------ un article

        /// <summary>
        /// Produit et publie un article.
        ///
        /// L'annulation est verifiee entre chaque etape couteuse — apres le plan, avant le
        /// modele, avant la publication. Un article prend une dizaine de secondes : ne
        /// verifier qu'entre deux articles ferait attendre l'utilisateur qui vient de
        /// demander l'arret, et il croirait le bouton sans effet.
        /// </summary>
        private void TraiterUn(SolidWorksExporter exporter, Candidat c, string travail, Bilan bilan)
        {
            string dossier = Path.Combine(travail, DepotArticlesNomSur(c.NoArticle));
            if (Directory.Exists(dossier)) Directory.Delete(dossier, true);
            Directory.CreateDirectory(dossier);

            ArticleTypeRule regle = ValidationArticle.RegleDe(_config, c.NoArticle);
            List<string> produits = new List<string>();

            FicheArticle fiche = new FicheArticle();
            fiche.NoArticle = c.NoArticle;
            fiche.Empreinte = c.Empreinte;

            // Date de realisation du plan : elle date toute la fournee de documents.
            string dateRevision = "";

            // --- le plan : une seule ouverture pour lire et exporter ---
            if (c.Plan != null && regle.Export2D)
            {
                ModelDoc2 doc = null;
                try
                {
                    doc = exporter.OpenDocument(c.Plan);
                    SolidWorksExporter.DocMetadata m = exporter.ReadMetadata(doc);
                    fiche.Revision = m.Revision;
                    fiche.Designation = m.Description;
                    fiche.Matiere = m.Material;
                    fiche.Traitement = m.Treatment;
                    fiche.Etat = m.State;
                    dateRevision = DateRevision.Normaliser(m.ReleaseDate, _journal);
                    produits.AddRange(exporter.ExportDrawing(doc, dossier, c.NoArticle));
                    fiche.Controle = ExtraireControle(doc, c.NoArticle, m);
                }
                finally { exporter.CloseDocument(doc); }
            }

            if (Annule()) { Nettoyer(dossier); return; }

            // --- le modèle : le plan reste prioritaire, le modèle comble les manques ---
            if (c.Modele != null && regle.Export3D)
            {
                ModelDoc2 doc = null;
                try
                {
                    doc = exporter.OpenDocument(c.Modele);
                    SolidWorksExporter.DocMetadata m = exporter.ReadMetadata(doc);
                    fiche.RevisionModele = m.Revision;
                    if (string.IsNullOrWhiteSpace(fiche.Revision)) fiche.Revision = m.Revision;
                    if (string.IsNullOrWhiteSpace(fiche.Designation)) fiche.Designation = m.Description;
                    if (string.IsNullOrWhiteSpace(fiche.Matiere)) fiche.Matiere = m.Material;
                    if (string.IsNullOrWhiteSpace(fiche.Traitement)) fiche.Traitement = m.Treatment;
                    if (string.IsNullOrWhiteSpace(fiche.Etat)) fiche.Etat = m.State;
                    produits.Add(exporter.ExportStep(doc, dossier, c.NoArticle));
                }
                finally { exporter.CloseDocument(doc); }
            }

            if (produits.Count == 0)
            {
                bilan.SansSource++;
                Dire(c.NoArticle + " : aucun fichier produit.");
                Nettoyer(dossier);
                return;
            }

            // Publier prend du temps — un depot par document sur le reseau. On ne le lance
            // pas si l'arret vient d'etre demande.
            if (Annule()) { Nettoyer(dossier); return; }

            if (_inventaire != null)
            {
                // Chaque document part nu, sous sa nature. Aucune archive n'est constituée :
                // les ZIP naissent au moment d'une demande, et n'y survivent pas.
                _inventaire.Publier(c.NoArticle, fiche.Revision, dateRevision, fiche.Etat,
                                    produits, _journal);

                if (fiche.Controle != null)
                {
                    string cf = ProduireControle(fiche, dossier);
                    if (cf != null)
                        _inventaire.PublierControle(c.NoArticle, fiche.Revision, dateRevision,
                                                    cf, _journal);
                }

                bilan.Produits++;
                Nettoyer(dossier);
                return;
            }

            CompressionLevel niveau = ZipService.Niveau(_config.ZipCompression);
            ResultatPublication r = _depot.Publier(fiche, produits, niveau);

            switch (r)
            {
                case ResultatPublication.Publie:
                    bilan.Produits++;
                    Dire(c.NoArticle + " rev " + fiche.RevisionAffichee + " : publié ("
                       + produits.Count + " fichier(s)).");
                    break;
                case ResultatPublication.Remplace:
                    bilan.Remplaces++;
                    Dire(c.NoArticle + " rev " + fiche.RevisionAffichee + " : remplacé, l'ancienne archive est dans "
                       + DepotArticles.DossierAnciennes + ".");
                    break;
                case ResultatPublication.Inchange:
                    bilan.AJour++;
                    break;
                case ResultatPublication.RefuseNonLibere:
                    bilan.NonLiberes++;
                    Dire(c.NoArticle + " : NON publié — état « " + fiche.Etat + " » hors des états libérés.");
                    break;
                default:
                    bilan.Echecs++;
                    Dire(c.NoArticle + " : échec de publication.");
                    break;
            }

            Nettoyer(dossier);
        }

        /// <summary>Efface le dossier de travail d'un article, sans jamais lever.</summary>
        private static void Nettoyer(string dossier)
        {
            try { if (Directory.Exists(dossier)) Directory.Delete(dossier, true); }
            catch (Exception) { }
        }

        /// <summary>
        /// Relève les caractéristiques du plan déjà ouvert, sans destinataire.
        ///
        /// Le formulaire prendra le nom d'un fournisseur au moment d'une demande : ici on ne
        /// conserve que ce que le plan dit. Un échec n'interrompt jamais la campagne — un
        /// article sans contrôle vaut mieux qu'une campagne arrêtée.
        /// </summary>
        private ControleFabrication ExtraireControle(ModelDoc2 plan, string noArticle,
                                                     SolidWorksExporter.DocMetadata m)
        {
            if (_controleCfg == null) return null;
            try
            {
                PartLine ligne = new PartLine();
                ligne.PartNumber = noArticle;
                ligne.Description = m.Description;
                ligne.DrawingRevision = m.Revision;
                ligne.Material = m.Material;
                ligne.Treatment = m.Treatment;
                ligne.State = m.State;

                ExtracteurCaracteristiques extracteur =
                    new ExtracteurCaracteristiques(_controleCfg, null);
                ControleFabrication controle = extracteur.Extraire(plan, ligne, "", "");

                if (controle != null && controle.Caracteristiques != null
                    && controle.Caracteristiques.Count > 0) return controle;
                return null;
            }
            catch (Exception ex)
            {
                Dire(noArticle + " : contrôle non extrait — " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Met en page le formulaire de contrôle, sans destinataire.
        ///
        /// Celui qui dort dans la base vaut pour n'importe quel sous-traitant : il prendra un
        /// nom au moment d'une demande. La mise en page n'exige pas SolidWorks.
        /// </summary>
        private string ProduireControle(FicheArticle fiche, string dossier)
        {
            try
            {
                fiche.Controle.Fournisseur = "";
                fiche.Controle.NumeroCommande = "";
                fiche.Controle.QuantiteLot = 0;
                fiche.Controle.CheminSourcePlan = "";
                return new AskThem.Pdf.QuestPdfGenerateur().Generer(fiche.Controle, dossier);
            }
            catch (Exception ex)
            {
                Dire(fiche.NoArticle + " : contrôle non mis en page — " + ex.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ rapport

        /// <summary>Archives publiées dont plus aucune source n'existe dans le coffre.</summary>
        private List<string> Orphelins(List<Candidat> candidats)
        {
            List<string> orphelins = new List<string>();
            try
            {
                List<string> connus = new List<string>();
                foreach (Candidat c in candidats) connus.Add(c.NoArticle);

                foreach (string publie in _depot.ArticlesPublies())
                    if (!connus.Contains(publie)) orphelins.Add(publie);
            }
            catch (Exception) { }
            return orphelins;
        }

        /// <summary>
        /// Écrit le rapport sur le poste, pas sur le partage : celui-ci n'accueille que les
        /// archives et les dossiers de demande.
        /// </summary>
        private string EcrireRapport(List<Candidat> candidats, Bilan bilan, Options o)
        {
            try
            {
                string dossier = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "AskThem");
                Directory.CreateDirectory(dossier);
                string chemin = Path.Combine(dossier,
                    "campagne_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".txt");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CAMPAGNE DE MISE A JOUR DE LA BASE ARTICLES");
                sb.AppendLine(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "   par " + System.Environment.UserName
                            + " sur " + System.Environment.MachineName);
                sb.AppendLine("Base : " + _depot.Racine);
                sb.AppendLine("Categories : " + string.Join(", ", o.Categories)
                            + (o.InclureAssemblages ? "   assemblages inclus" : "   assemblages exclus"));
                sb.AppendLine(o.RecensementSeul ? "MODE RECENSEMENT — rien n'a ete ecrit" : "MODE COMPLET");
                sb.AppendLine();
                sb.AppendLine("Examines .......... " + bilan.Candidats);
                sb.AppendLine("Deja a jour ....... " + bilan.AJour);
                sb.AppendLine("Publies ........... " + bilan.Produits);
                sb.AppendLine("Remplaces ......... " + bilan.Remplaces);
                sb.AppendLine("Sans source CAO ... " + bilan.SansSource);
                sb.AppendLine("Non liberes ....... " + bilan.NonLiberes);
                sb.AppendLine("Ignores ........... " + bilan.Ignores);
                sb.AppendLine("Echecs ............ " + bilan.Echecs);
                sb.AppendLine("Duree ............. " + bilan.Duree.ToString(@"hh\:mm\:ss"));
                sb.AppendLine();

                sb.AppendLine("DETAIL PAR ARTICLE");
                foreach (Candidat c in candidats)
                    sb.AppendLine("  " + c.NoArticle.PadRight(18) + (c.Verdict == null ? "" : c.Verdict));

                if (bilan.Orphelins.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("ARCHIVES SANS SOURCE DANS LE COFFRE — a examiner, rien n'a ete supprime");
                    foreach (string x in bilan.Orphelins) sb.AppendLine("  " + x);
                }

                File.WriteAllText(chemin, sb.ToString(), Encoding.UTF8);
                return chemin;
            }
            catch (Exception ex)
            {
                LogService.Write("Rapport de campagne non ecrit : " + ex.Message);
                return "";
            }
        }

        private static string DepotArticlesNomSur(string valeur)
        {
            StringBuilder sb = new StringBuilder();
            char[] interdits = Path.GetInvalidFileNameChars();
            foreach (char c in valeur) sb.Append(Array.IndexOf(interdits, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
