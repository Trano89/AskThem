using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AskThem.Models;
using AskThem.Pdf;

namespace AskThem.Services
{
    /// <summary>Ce qu'il est advenu d'une tentative de publication.</summary>
    public enum ResultatPublication
    {
        Inchange,
        Publie,
        Remplace,
        RefuseNonLibere,
        RefuseHorsProduction,
        DepotIndisponible,
        Echec
    }

    /// <summary>Ce qu'une archive d'article conserve d'elle-même, dans son manifeste.</summary>
    public class FicheArticle
    {
        public string NoArticle { get; set; }
        public string Designation { get; set; }
        public string Revision { get; set; }
        public string RevisionModele { get; set; }
        public string Etat { get; set; }
        public string Matiere { get; set; }
        public string Traitement { get; set; }
        public string Empreinte { get; set; }
        public List<string> Fichiers { get; set; }
        public string PubliePar { get; set; }
        public string Poste { get; set; }
        public string VersionAskThem { get; set; }
        public DateTime PublieLeUtc { get; set; }

        /// <summary>
        /// Le contrôle de fabrication de cet article, tel qu'extrait du plan.
        ///
        /// Conservé en données et non en PDF : le formulaire porte le nom du fournisseur et la
        /// référence de commande, qui changent à chaque demande. On le regénère donc à chaque
        /// fois, ce qu'un poste sans SolidWorks sait faire — seule l'extraction exige le
        /// coffre, pas la mise en page.
        /// </summary>
        public ControleFabrication Controle { get; set; }

        public FicheArticle()
        {
            NoArticle = "";
            Designation = "";
            Revision = "";
            RevisionModele = "";
            Etat = "";
            Matiere = "";
            Traitement = "";
            Empreinte = "";
            Fichiers = new List<string>();
            PubliePar = "";
            Poste = "";
            VersionAskThem = "";
        }

        /// <summary>Révision telle qu'elle apparaît sur le nom de l'archive.</summary>
        public string RevisionAffichee
        {
            get { return string.IsNullOrWhiteSpace(Revision) ? "inconnue" : Revision.Trim(); }
        }

        /// <summary>Âge de la publication, pour dire à l'acheteur depuis quand elle dort.</summary>
        public int JoursDepuisPublication
        {
            get
            {
                if (PublieLeUtc == default(DateTime)) return -1;
                return (int)Math.Floor((DateTime.UtcNow - PublieLeUtc).TotalDays);
            }
        }
    }

    /// <summary>
    /// La base documentaire des articles, sur le partage de production.
    ///
    /// Une archive ZIP par article, posée à côté des dossiers de demande. Elle contient les
    /// fichiers nus — plan, DXF, STEP — nommés au seul numéro d'article, plus un manifeste.
    /// La révision est portée par le NOM de l'archive, où un humain la lit sans rien ouvrir ;
    /// l'empreinte des fichiers sources est dans le manifeste, où le programme la lit pour
    /// décider s'il faut régénérer. Chacun sa fonction : on ne fait jamais dépendre l'identité
    /// d'un champ de révision saisi à la main.
    ///
    /// Sans cette base, un acheteur sans SolidWorks ne peut joindre aucun plan à une demande
    /// de fabrication. C'est tout l'objet du dispositif.
    ///
    /// L'unité étant un fichier unique, sa publication est presque atomique : on écrit à côté
    /// puis on renomme. Deux postes publiant le même article au même instant produisent chacun
    /// une archive complète et cohérente — le dernier gagne, sans jamais mélanger le plan d'une
    /// révision avec le modèle d'une autre. C'est ce qui permet de se passer d'un verrou.
    /// </summary>
    public class DepotArticles
    {
        /// <summary>
        /// Nom du manifeste dans les archives d'avant le reconditionnement.
        ///
        /// Le manifeste vit désormais dans le commentaire de l'archive : un fichier étranger
        /// au milieu d'un plan et d'un STEP finit tôt ou tard chez un fournisseur, le jour où
        /// quelqu'un joint l'archive telle quelle. Ce nom ne sert plus qu'à relire les
        /// archives déjà publiées et à les reconditionner.
        /// </summary>
        public const string NomManifeste = "article.json";

        /// <summary>Où atterrissent les archives remplacées.</summary>
        public const string DossierAnciennes = "Old_Versions";

        /// <summary>
        /// Où vivent les formulaires de contrôle, à part des archives.
        ///
        /// Ils ne sont pas de même nature : un plan et un STEP se transmettent, un contrôle se
        /// remplit. Le sous-traitant doit pouvoir l'ouvrir sans décompresser quoi que ce soit,
        /// et il ne concerne que la fabrication.
        /// </summary>
        public const string DossierControles = "Controles fabrication";

        /// <summary>Ce qui sépare le numéro d'article de sa révision, dans le nom de l'archive.</summary>
        private const string Separateur = " rev ";

        /// <summary>
        /// Taille maximale du manifeste. Le format ZIP plafonne son commentaire à 65535
        /// octets ; on garde une marge pour ne jamais frôler la troncature.
        /// </summary>
        private const int LimiteCommentaire = 60000;

        private readonly string _racine;
        private readonly List<string> _etatsLiberes;
        private readonly bool _seulementLiberes;

        public DepotArticles(AppConfig config)
            : this(RacineParDefaut(config),
                   config != null ? config.ReleasedStates : null,
                   config == null || config.PublierSeulementLiberes)
        {
        }

        public DepotArticles(string racine, List<string> etatsLiberes, bool seulementLiberes)
        {
            _racine = racine != null ? racine.Trim() : "";
            _etatsLiberes = etatsLiberes != null ? etatsLiberes : new List<string>();
            _seulementLiberes = seulementLiberes;
        }

        /// <summary>
        /// Les archives se rangent à la racine du dossier des demandes fournisseur.
        ///
        /// L'Explorateur liste les dossiers avant les fichiers : les archives se regroupent
        /// donc d'elles-mêmes sous les dossiers de demande, sans qu'on ait à les isoler.
        /// </summary>
        public static string RacineParDefaut(AppConfig config)
        {
            if (config == null) return "";
            if (!string.IsNullOrWhiteSpace(config.DepotArticlesRoot)) return config.DepotArticlesRoot.Trim();
            return config.ArchiveRoot != null ? config.ArchiveRoot.Trim() : "";
        }

        public string Racine { get { return _racine; } }

        // ------------------------------------------------------------------ disponibilité

        /// <summary>
        /// Vérifie que ce poste peut réellement publier, et le dit une seule fois.
        ///
        /// Tester l'existence du dossier ne suffit pas : un compte en lecture seule le voit
        /// parfaitement et échoue ensuite à chaque article. On écrit donc un témoin, qu'on
        /// efface aussitôt.
        /// </summary>
        public bool Amorcer(out string raison)
        {
            raison = "";
            if (string.IsNullOrWhiteSpace(_racine))
            {
                raison = "aucun emplacement de base articles n'est configuré";
                return false;
            }

            string temoin = null;
            try
            {
                Directory.CreateDirectory(_racine);
                temoin = Path.Combine(_racine, ".ecriture-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                File.WriteAllText(temoin, "");
                return true;
            }
            catch (Exception ex)
            {
                raison = _racine + " — " + ex.Message;
                return false;
            }
            finally
            {
                try { if (temoin != null && File.Exists(temoin)) File.Delete(temoin); }
                catch (Exception) { }
            }
        }

        /// <summary>Vrai si la base est simplement lisible, ce qui suffit à un poste consommateur.</summary>
        public bool Lisible()
        {
            if (string.IsNullOrWhiteSpace(_racine)) return false;
            try { return Directory.Exists(_racine); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ empreinte

        /// <summary>
        /// Empreinte des fichiers sources : nom, taille et date de dernière écriture.
        ///
        /// Ce n'est pas la révision du coffre, et c'est délibéré : la lire exigerait d'ouvrir
        /// le document, donc SolidWorks, donc précisément ce dont le poste consommateur ne
        /// dispose pas. Un fichier rouvert et resauvegardé sans modification change d'empreinte
        /// et provoquera une régénération inutile — compromis assumé, largement préférable au
        /// hachage de plusieurs gigaoctets à travers le réseau.
        /// </summary>
        public static string Empreinte(string cheminModele, string cheminPlan)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string chemin in new string[] { cheminModele, cheminPlan })
            {
                if (string.IsNullOrWhiteSpace(chemin)) continue;
                try
                {
                    FileInfo fi = new FileInfo(chemin);
                    if (!fi.Exists) continue;
                    sb.Append(fi.Name.ToLowerInvariant()).Append('|')
                      .Append(fi.Length).Append('|')
                      .Append(fi.LastWriteTimeUtc.Ticks).Append(';');
                }
                catch (Exception)
                {
                    // Un fichier illisible ne doit pas empêcher de tenir compte des autres.
                }
            }
            if (sb.Length == 0) return "";

            using (SHA256 sha = SHA256.Create())
            {
                byte[] somme = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                StringBuilder hex = new StringBuilder(16);
                for (int i = 0; i < 8; i++) hex.Append(somme[i].ToString("x2"));
                return hex.ToString();
            }
        }

        // ------------------------------------------------------------------ lecture

        /// <summary>Nom d'archive d'un article pour une révision donnée.</summary>
        public string NomArchive(string noArticle, string revision)
        {
            string rev = string.IsNullOrWhiteSpace(revision) ? "inconnue" : revision.Trim();
            return NomSur(noArticle) + Separateur + NomSur(rev) + ".zip";
        }

        /// <summary>
        /// L'archive publiée pour cet article, quelle que soit sa révision, ou null.
        ///
        /// S'il en existait plusieurs — ce qui ne devrait pas arriver — on retient la plus
        /// récente, et on le journalise : deux archives pour un même article signifient qu'un
        /// remplacement s'est mal terminé.
        /// </summary>
        public string TrouverArchive(string noArticle)
        {
            if (!Lisible() || string.IsNullOrWhiteSpace(noArticle)) return null;
            try
            {
                string[] candidats = Directory.GetFiles(_racine, NomSur(noArticle) + Separateur + "*.zip");
                if (candidats.Length == 0) return null;
                if (candidats.Length == 1) return candidats[0];

                string retenu = candidats[0];
                foreach (string c in candidats)
                    if (File.GetLastWriteTimeUtc(c) > File.GetLastWriteTimeUtc(retenu)) retenu = c;

                LogService.Write("Plusieurs archives pour " + noArticle + " ("
                               + candidats.Length + ") : la plus récente est retenue.");
                return retenu;
            }
            catch (Exception ex)
            {
                LogService.Write("Base articles illisible pour " + noArticle + " : " + ex.Message);
                return null;
            }
        }

        /// <summary>Manifeste de l'archive publiée pour cet article, ou null.</summary>
        public FicheArticle Lire(string noArticle)
        {
            string archive = TrouverArchive(noArticle);
            return archive == null ? null : LireArchive(archive);
        }

        /// <summary>
        /// Manifeste d'une archive, ou null.
        ///
        /// On lit d'abord le commentaire, puis à défaut l'ancienne entrée : les archives
        /// publiées avant le reconditionnement restent parfaitement lisibles, et ne sont donc
        /// pas régénérées pour rien.
        /// </summary>
        public static FicheArticle LireArchive(string cheminArchive)
        {
            string json = ZipService.LireCommentaire(cheminArchive);
            if (json == null) json = ZipService.LireEntree(cheminArchive, NomManifeste);
            if (json == null) return null;
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.PropertyNameCaseInsensitive = true;
                options.AllowTrailingCommas = true;

                FicheArticle f = JsonSerializer.Deserialize<FicheArticle>(json, options);
                if (f != null && f.Fichiers == null) f.Fichiers = new List<string>();
                return f;
            }
            catch (Exception ex)
            {
                LogService.Write("Manifeste illisible dans " + cheminArchive + " : " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Extrait dans un dossier de travail les fichiers destinés au fournisseur.
        ///
        /// Le manifeste est écarté : il décrit l'article pour nous — état, empreinte, auteur —
        /// et n'a rien à faire dans ce qui part chez un sous-traitant.
        /// </summary>
        public List<string> ExtraireVers(string noArticle, string dossierCible)
        {
            string archive = TrouverArchive(noArticle);
            if (archive == null) return new List<string>();

            List<string> extraits = ZipService.Extraire(archive, dossierCible, NomManifeste);

            // Les archives d'avant la séparation contiennent un formulaire de contrôle figé,
            // au nom du fournisseur de la demande qui l'a produit. Le renvoyer tel quel à un
            // autre sous-traitant serait une faute : il est écarté, et celui qui part est
            // regénéré au nom du destinataire du jour.
            List<string> retenus = new List<string>();
            foreach (string f in extraits)
            {
                if (Path.GetFileName(f).StartsWith("CF_", StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(f); }
                    catch (Exception) { }
                    continue;
                }
                retenus.Add(f);
            }
            return retenus;
        }

        /// <summary>Numéros d'article ayant une archive publiée.</summary>
        public List<string> ArticlesPublies()
        {
            List<string> articles = new List<string>();
            if (!Lisible()) return articles;
            try
            {
                foreach (string f in Directory.GetFiles(_racine, "*" + Separateur + "*.zip"))
                {
                    string nom = Path.GetFileNameWithoutExtension(f);
                    int coupe = nom.LastIndexOf(Separateur, StringComparison.Ordinal);
                    if (coupe <= 0) continue;
                    string article = nom.Substring(0, coupe).Trim();
                    if (!articles.Contains(article)) articles.Add(article);
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Base articles illisible : " + ex.Message);
            }
            return articles;
        }

        // ------------------------------------------------------------------ écriture

        /// <summary>
        /// Publie ou remplace l'archive d'un article.
        ///
        /// Ne fait rien si l'empreinte publiée est déjà la bonne : une demande qui repasse sur
        /// un article inchangé ne réécrit pas le partage. Si l'empreinte diffère, l'archive en
        /// place rejoint Old_Versions avant d'être remplacée — rien n'est jamais perdu sans
        /// avoir été rangé.
        ///
        /// Un article dont l'état n'est pas libéré est refusé : publié, il deviendrait joignable
        /// des semaines plus tard par un acheteur qui n'a aucun moyen de savoir qu'il ne doit
        /// pas partir.
        /// </summary>
        public ResultatPublication Publier(FicheArticle fiche, List<string> fichiersSource, CompressionLevel niveau)
        {
            if (fiche == null || string.IsNullOrWhiteSpace(fiche.NoArticle)) return ResultatPublication.Echec;
            if (fichiersSource == null || fichiersSource.Count == 0) return ResultatPublication.Echec;
            if (string.IsNullOrWhiteSpace(fiche.Empreinte)) return ResultatPublication.Echec;
            if (!Lisible()) return ResultatPublication.DepotIndisponible;
            if (!EstDeProduction(fiche.NoArticle)) return ResultatPublication.RefuseHorsProduction;
            if (!EstLibere(fiche.Etat)) return ResultatPublication.RefuseNonLibere;

            string existante = TrouverArchive(fiche.NoArticle);
            if (existante != null)
            {
                FicheArticle enPlace = LireArchive(existante);

                // Même empreinte ne veut pas dire même contenu utile : une archive publiée
                // avant que les contrôles n'existent n'en porte aucun. Sans cette exception,
                // elle resterait « à jour » pour toujours et aucun poste sans SolidWorks
                // n'obtiendrait jamais son formulaire.
                bool controleAAjouter = enPlace != null && enPlace.Controle == null && fiche.Controle != null;

                if (enPlace != null && enPlace.Empreinte == fiche.Empreinte && !controleAAjouter)
                    return ResultatPublication.Inchange;
            }

            string destination = Path.Combine(_racine, NomArchive(fiche.NoArticle, fiche.Revision));
            string temporaire = destination + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                // 1. L'archive est construite à côté, complète, avant d'être rendue visible.
                fiche.Fichiers = new List<string>();
                foreach (string source in fichiersSource)
                {
                    if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;
                    fiche.Fichiers.Add(Path.GetFileName(source));
                }
                if (fiche.Fichiers.Count == 0) return ResultatPublication.Echec;

                // Le formulaire conservé dans la base est neutre : il vaut pour n'importe quel
                // sous-traitant. Le nom du destinataire et la référence de commande sont
                // effacés AVANT la sérialisation, sinon le manifeste garderait le fournisseur
                // de la demande qui a servi à le produire.
                if (fiche.Controle != null)
                {
                    fiche.Controle.Fournisseur = "";
                    fiche.Controle.NumeroCommande = "";

                    // La quantite d'une demande passee n'a aucun sens dans la base, et le
                    // chemin du coffre n'a rien a faire dans un document qui part dehors.
                    fiche.Controle.QuantiteLot = 0;
                    fiche.Controle.CheminSourcePlan = "";
                }

                fiche.PublieLeUtc = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(fiche.PubliePar)) fiche.PubliePar = Environment.UserName;
                if (string.IsNullOrWhiteSpace(fiche.Poste)) fiche.Poste = Environment.MachineName;
                if (string.IsNullOrWhiteSpace(fiche.VersionAskThem)) fiche.VersionAskThem = UpdateService.CurrentVersion();

                // Le manifeste loge dans le commentaire de l'archive, plafonné à 65535 octets
                // par le format ZIP. Compact et sans échappement inutile des accents, il tient
                // largement ; au-delà, on préfère publier sans le contrôle plutôt que de le
                // laisser tronquer en silence, ce qui rendrait le manifeste illisible et
                // l'article invisible pour les postes sans SolidWorks.
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = false;
                options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

                string manifeste = JsonSerializer.Serialize(fiche, options);
                if (Encoding.UTF8.GetByteCount(manifeste) > LimiteCommentaire)
                {
                    int caracteristiques = fiche.Controle != null && fiche.Controle.Caracteristiques != null
                        ? fiche.Controle.Caracteristiques.Count : 0;
                    LogService.Write("Manifeste trop volumineux pour " + fiche.NoArticle
                                   + " (" + caracteristiques + " caractéristiques) : publié sans le contrôle.");
                    fiche.Controle = null;
                    manifeste = JsonSerializer.Serialize(fiche, options);
                }

                using (ZipArchive zip = ZipFile.Open(temporaire, ZipArchiveMode.Create))
                {
                    foreach (string source in fichiersSource)
                    {
                        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;
                        zip.CreateEntryFromFile(source, Path.GetFileName(source), niveau);
                    }
                    zip.Comment = manifeste;
                }

                // 2. L'archive en place est rangée avant d'être remplacée.
                bool remplacement = false;
                if (existante != null)
                {
                    remplacement = true;
                    Archiver(existante);
                }
                if (File.Exists(destination)) File.Delete(destination);

                // 3. Le renommage rend l'archive visible, d'un seul geste.
                File.Move(temporaire, destination);

                // 4. Le contrôle vit à part, en clair, pour être lu sans décompresser.
                PublierControle(fiche);

                return remplacement ? ResultatPublication.Remplace : ResultatPublication.Publie;
            }
            catch (Exception ex)
            {
                LogService.Write("Publication impossible pour " + fiche.NoArticle + " : " + ex.Message);
                try { if (File.Exists(temporaire)) File.Delete(temporaire); }
                catch (Exception) { }
                return ResultatPublication.Echec;
            }
        }

        /// <summary>Dossier des contrôles, créé au besoin.</summary>
        public string DossierDesControles()
        {
            if (string.IsNullOrWhiteSpace(_racine)) return "";
            return Path.Combine(_racine, DossierControles);
        }

        /// <summary>
        /// Écrit le formulaire de contrôle de cet article, sans fournisseur ni commande.
        ///
        /// Celui qui dort dans la base est neutre : il vaut pour n'importe quel sous-traitant.
        /// C'est au moment d'une demande qu'il prend le nom de son destinataire.
        /// </summary>
        private void PublierControle(FicheArticle fiche)
        {
            if (fiche.Controle == null || fiche.Controle.Caracteristiques == null
                || fiche.Controle.Caracteristiques.Count == 0) return;

            try
            {
                string dossier = DossierDesControles();
                Directory.CreateDirectory(dossier);

                // L'ancien formulaire est rangé avant d'être remplacé, comme une archive.
                foreach (string ancien in Directory.GetFiles(dossier, "CF_" + NomSur(fiche.NoArticle) + "_rev*.pdf"))
                {
                    try { Archiver(ancien); }
                    catch (Exception) { }
                }

                // Le contrôle a déjà été neutralisé avant la sérialisation du manifeste :
                // ce qu'on met en page ici est donc exactement ce que la base conserve.
                new QuestPdfGenerateur().Generer(fiche.Controle, dossier);
            }
            catch (Exception ex)
            {
                LogService.Write("Contrôle non publié pour " + fiche.NoArticle + " : " + ex.Message);
            }
        }

        /// <summary>
        /// Regénère le formulaire de contrôle d'un article pour une demande précise.
        ///
        /// La mise en page ne demande pas SolidWorks : un acheteur sans coffre obtient donc un
        /// formulaire complet, au nom de son fournisseur, à partir des caractéristiques
        /// relevées sur le plan par un poste équipé. Renvoie le chemin du PDF, ou null.
        /// </summary>
        public string GenererControlePour(FicheArticle fiche, string fournisseur,
                                          string commande, int quantiteLot, string dossierCible)
        {
            if (fiche == null || fiche.Controle == null) return null;
            if (fiche.Controle.Caracteristiques == null || fiche.Controle.Caracteristiques.Count == 0) return null;

            try
            {
                Directory.CreateDirectory(dossierCible);
                fiche.Controle.Fournisseur = fournisseur != null ? fournisseur : "";
                fiche.Controle.NumeroCommande = commande != null ? commande : "";

                // La quantité conservée dans la base est celle du jour de la publication :
                // sans cela le sous-traitant recevrait un formulaire annonçant un lot qui
                // n'est pas le sien.
                fiche.Controle.QuantiteLot = quantiteLot;

                return new QuestPdfGenerateur().Generer(fiche.Controle, dossierCible);
            }
            catch (Exception ex)
            {
                LogService.Write("Contrôle non regénéré pour " + fiche.NoArticle + " : " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Sort le manifeste des archives où il est encore un fichier.
        ///
        /// Rien n'est régénéré : on recopie les entrées utiles dans une nouvelle archive et on
        /// place le manifeste dans son commentaire. Aucune ouverture de SolidWorks, quelques
        /// secondes pour tout le dépôt. Renvoie le nombre d'archives reconditionnées.
        /// </summary>
        public int Reconditionner(Action<string> journal)
        {
            if (!Lisible()) return 0;
            int faites = 0;

            string[] archives;
            try { archives = Directory.GetFiles(_racine, "*" + Separateur + "*.zip"); }
            catch (Exception) { return 0; }

            int sorties = 0;
            foreach (string archive in archives)
            {
                try
                {
                    // Une référence de projet publiée par erreur est sortie de la base. On la
                    // range plutôt que de la supprimer : rien ne se perd sans recours.
                    string nom = Path.GetFileNameWithoutExtension(archive);
                    int coupe = nom.LastIndexOf(Separateur, StringComparison.Ordinal);
                    string numero = coupe > 0 ? nom.Substring(0, coupe).Trim() : nom;
                    if (!EstDeProduction(numero))
                    {
                        Archiver(archive);
                        SortirControle(numero);
                        sorties++;
                        continue;
                    }

                    if (ZipService.LireCommentaire(archive) != null) continue;   // déjà fait

                    string manifeste = ZipService.LireEntree(archive, NomManifeste);
                    if (manifeste == null) continue;                             // rien à sortir

                    string temporaire = archive + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);

                    using (ZipArchive source = ZipFile.OpenRead(archive))
                    using (ZipArchive cible = ZipFile.Open(temporaire, ZipArchiveMode.Create))
                    {
                        foreach (ZipArchiveEntry entree in source.Entries)
                        {
                            if (string.IsNullOrEmpty(entree.Name)) continue;
                            if (string.Equals(entree.Name, NomManifeste, StringComparison.OrdinalIgnoreCase)) continue;

                            ZipArchiveEntry copie = cible.CreateEntry(entree.Name, CompressionLevel.Optimal);
                            using (Stream lu = entree.Open())
                            using (Stream ecrit = copie.Open())
                                lu.CopyTo(ecrit);
                        }
                        cible.Comment = manifeste;
                    }

                    // L'ancienne est rangée, pas supprimée : une coupure réseau entre les
                    // deux gestes ne doit jamais laisser l'article sans archive du tout.
                    Archiver(archive);
                    File.Move(temporaire, archive);
                    faites++;
                }
                catch (Exception ex)
                {
                    LogService.Write("Reconditionnement impossible pour "
                                   + Path.GetFileName(archive) + " : " + ex.Message);
                }
            }

            if (journal != null)
            {
                try
                {
                    if (faites > 0)
                        journal(faites + " archive(s) reconditionnée(s) : le manifeste est passé dans le commentaire.");
                    if (sorties > 0)
                        journal(sorties + " référence(s) de projet sortie(s) de la base vers "
                              + DossierAnciennes + " : elles n'ont pas leur place en production.");
                }
                catch (Exception) { }
            }
            return faites;
        }

        /// <summary>Sort de la base le formulaire de contrôle d'une référence hors production.</summary>
        private void SortirControle(string noArticle)
        {
            try
            {
                string dossier = DossierDesControles();
                if (!Directory.Exists(dossier)) return;
                foreach (string f in Directory.GetFiles(dossier, "CF_" + NomSur(noArticle) + "_rev*.pdf"))
                    Archiver(f);
            }
            catch (Exception ex)
            {
                LogService.Write("Contrôle non sorti pour " + noArticle + " : " + ex.Message);
            }
        }

        /// <summary>
        /// Range une archive remplacée dans Old_Versions.
        ///
        /// Son nom porte déjà l'article et sa révision : elle reste identifiable sans son
        /// contexte d'origine. En cas d'homonymie — même article, même révision remplacée deux
        /// fois — la date départage.
        /// </summary>
        private void Archiver(string fichier)
        {
            string anciennes = Path.Combine(_racine, DossierAnciennes);
            Directory.CreateDirectory(anciennes);

            string extension = Path.GetExtension(fichier);
            string cible = Path.Combine(anciennes, Path.GetFileName(fichier));
            if (File.Exists(cible))
            {
                string sansExt = Path.GetFileNameWithoutExtension(fichier);
                string jour = DateTime.Now.ToString("yyyy-MM-dd");
                cible = Path.Combine(anciennes, sansExt + " - remplacé le " + jour + extension);

                int suffixe = 2;
                while (File.Exists(cible))
                {
                    cible = Path.Combine(anciennes,
                        sansExt + " - remplacé le " + jour + " (" + suffixe + ")" + extension);
                    suffixe++;
                }
            }
            File.Move(fichier, cible);
        }

        /// <summary>
        /// Vrai si ce numéro désigne un article de production.
        ///
        /// La codification commence par une lettre d'origine. Le coffre contient aussi des
        /// références de projet, préfixées d'un dièse : elles restent utilisables pour une
        /// demande ponctuelle — le format les accepte délibérément — mais elles n'ont rien à
        /// faire dans la base documentaire de production, qui doit ne contenir que des
        /// articles établis.
        /// </summary>
        public static bool EstDeProduction(string noArticle)
        {
            return Codification.EstDeProduction(noArticle);
        }

        /// <summary>
        /// Vrai si cet état autorise la publication.
        ///
        /// Un état vide passe : beaucoup de cartes de données n'en portent pas, et refuser
        /// viderait la base de la moitié de son contenu. C'est un état explicitement « en
        /// développement » qu'on refuse.
        /// </summary>
        public bool EstLibere(string etat)
        {
            if (!_seulementLiberes) return true;
            if (string.IsNullOrWhiteSpace(etat)) return true;

            string valeur = etat.Trim();
            foreach (string libere in _etatsLiberes)
                if (string.Equals(valeur, libere, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Rend un fragment de nom sûr, sans jamais renvoyer une chaîne vide.</summary>
        private static string NomSur(string valeur)
        {
            if (string.IsNullOrWhiteSpace(valeur)) return "_";
            StringBuilder sb = new StringBuilder(valeur.Length);
            char[] interdits = Path.GetInvalidFileNameChars();
            foreach (char c in valeur.Trim())
                sb.Append(Array.IndexOf(interdits, c) >= 0 ? '_' : c);
            string net = sb.ToString().TrimEnd('.', ' ');
            return net.Length == 0 ? "_" : net;
        }
    }
}
