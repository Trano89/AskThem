# AskThem

Utilitaire Windows (C# / .NET 8 / WinForms) qui prépare une **demande d'offre** ou une
**demande de fabrication** à partir d'une liste de numéros d'article :
recherche des fichiers SolidWorks dans le coffre PDM, export **STEP AP203** (3D) et
**PDF + DXF** (2D), puis ouverture d'un **email Outlook pré-rempli**.

L'email n'est **jamais envoyé automatiquement** : il s'ouvre pour relecture.

---

## Installation

Téléchargez `AskThem.exe` depuis la [dernière version publiée](https://github.com/trano89/AskThem/releases/latest)
et placez-le où vous voulez : clé USB, partage réseau, bureau. Aucun installateur,
aucun droit administrateur, aucun .NET à installer.

## Mises à jour

Au démarrage, l'application interroge les publications du dépôt. Si une version plus
récente existe, un bouton **Mettre à jour** apparaît en bas de la fenêtre : un clic
télécharge la nouvelle version, remplace l'exécutable et redémarre l'application.
Le comportement se désactive avec `CheckUpdatesOnStartup` dans `config.json`.

### Publier une nouvelle version

Le numéro de version vient de l'étiquette Git. Pousser une étiquette déclenche la
compilation et la publication automatiques :

```bat
git tag v1.1.0
git push origin v1.1.0
```

Le workflow `.github/workflows/release.yml` compile l'exécutable portable et le joint
à une publication GitHub. Pensez à décrire le changement dans `CHANGELOG.md`.

---

## Prérequis

| Élément | Détail |
|---|---|
| Windows | Windows 11 x64 |
| SDK .NET | .NET 8 SDK (pour compiler) |
| SolidWorks | Installé sur le poste, avec les DLL d'interopérabilité dans `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\` |
| Coffre PDM | Vue locale montée sur `C:\00_LynceeTec\` |
| Outlook | **Outlook Classic** (l'application de bureau, pas la nouvelle version Store) |

Les deux DLL référencées par le projet sont :

- `SolidWorks.Interop.sldworks.dll`
- `SolidWorks.Interop.swconst.dll`

Si SolidWorks est installé ailleurs, corrigez les balises `HintPath` dans `AskThem.csproj`.

---

## Création du projet et compilation

```bat
dotnet new winforms -n AskThem -f net8.0
cd AskThem
```

Remplacez ensuite les fichiers générés par ceux de ce dépôt
(supprimez `Form1.cs` et `Form1.Designer.cs` créés par le modèle).

Publication de l'exécutable portable :

```bat
dotnet publish -c Release
```

Le résultat est **un fichier unique** : `dist\AskThem.exe` (environ 72 Mo).
Le RID, le mode auto-contenu, le mono-fichier et la compression sont déjà dans le `.csproj`.

### Portabilité

`AskThem.exe` se suffit à lui-même : copiez-le sur une clé USB, un partage réseau ou un
autre poste, il s'exécute depuis n'importe quel dossier. .NET **n'a pas besoin** d'être
installé sur la machine cible : le runtime est embarqué dans l'exe.

- `config.json` est **créé automatiquement** à côté de l'exe au premier lancement.
  Ajustez-y ensuite le chemin du coffre PDM propre au poste.
- Les modèles d'email sont intégrés à l'exe. Le dossier `templates\` reste facultatif :
  s'il est présent à côté de `AskThem.exe`, ses fichiers ont la priorité sur les modèles
  intégrés, ce qui permet d'ajuster les textes sans recompiler.
- **Seuls prérequis sur le poste cible** : SolidWorks et Outlook Classic installés,
  puisque l'application les pilote en COM.

Pour publier ailleurs que dans `dist\` :

```bat
dotnet publish -c Release -o "D:\chemin\de\votre\choix"
```

---

## Demande d'offre sur des articles de catalogue (X20)

Un article dont le type est `X20` s'achète au catalogue : il n'a ni plan, ni modèle 3D, ni
contrôle de fabrication. Quand **toutes** les lignes sont des X20, AskThem bascule seul en
mode catalogue et **n'ouvre ni SolidWorks ni le coffre PDM** — tout ce qu'il faut est dans
l'inventaire. Le traitement est immédiat.

Une demande ne mélange jamais catalogue et sur mesure : les deux n'appellent ni les mêmes
fichiers ni la même façon de choisir le fournisseur. Un panier mixte est refusé, en nommant
les articles des deux camps.

### Un article est refusé s'il n'est pas vendu par le destinataire

L'inventaire déclare **plusieurs fournisseurs par article**, chacun avec sa propre référence.
Dès qu'un article de catalogue est saisi dans la grille, AskThem vérifie que le destinataire
choisi le vend. Sinon, **la ligne est refusée sur-le-champ**, avec le nom des fournisseurs
qui le vendent réellement — plutôt que de laisser constituer une demande impossible.

Le contrôle ne s'applique qu'aux achats catalogue, et seulement quand un destinataire est
choisi et l'inventaire joignable. L'inventaire est chargé au démarrage pour que ce soit
instantané.

### Rechercher un article, à fabriquer ou au catalogue

Le bouton **Rechercher…** cherche dans les deux sources à la fois, qui ne se recouvrent pas :
le **coffre** connaît les pièces dessinées mais pas leur désignation, l'**inventaire** connaît
désignations, fournisseurs et références, y compris pour des articles sans aucun fichier.
Les réunir évite d'avoir à savoir d'avance dans lequel des deux chercher.

Quatre filtres, combinables :

| Filtre | Ce qu'il fait |
|---|---|
| Texte libre | numéro, désignation, ancienne référence, référence fournisseur ou fabricant, nom du fournisseur |
| Type d'article | les types déclarés dans `config.json` — pièce de détail, article catalogue, assemblage… |
| Vendus par le destinataire | ne garde que ce que le fournisseur choisi vend réellement |
| Avec un plan dans le coffre | ne garde que les articles dessinés |

Chaque ligne montre le type, la désignation, votre référence chez le destinataire, la
référence fabricant, l'ancienne référence, les fournisseurs déclarés, ce que le coffre
contient (3D, 2D ou les deux), le prix unitaire et le stock. On coche — ou on double-clique
la ligne — et on ajoute à la demande. Les articles déjà présents dans la grille sont ignorés,
et la première ligne vide est réutilisée.

### Comment le fournisseur est reconnu

Par son nom, et de façon indulgente : casse, accents, ponctuation et formes juridiques
finales sont ignorés, et un nom abrégé reconnaît son nom complet. `Thorlabs` vaut
`Thorlabs GmbH`, `Oritage` vaut `ORITAGE Sàrl`, `Mitutoyo` vaut `Mitutoyo (Schweiz) AG`.

Pour lever toute ambiguïté, un fournisseur peut être **lié à sa fiche d'inventaire** :
bouton **Fournisseurs…**, ligne *Inventaire*, bouton **Lier…**. La fenêtre propose les
fiches dont le nom correspond, et les présente toutes quand elles se confondent — l'inventaire
contient de vrais doublons, comme `Idex Health & Science` et `Idex Health & Science, LLC`.
Une fois le lien fait, c'est **l'identifiant** qui sert et le nom n'a plus d'importance.

Les fiches d'inventaire ne portent pas d'adresse email : les destinataires restent saisis
dans AskThem.

### Ce qui est signalé avant l'envoi

Rien n'est bloqué, tout est nommé :

| Statut | Ce qu'il veut dire |
|---|---|
| `OK` | l'article est vendu par ce fournisseur, sa référence est reprise |
| `Autre fournisseur` | il est vendu, mais pas par le destinataire choisi — le journal dit par qui |
| `Sans fournisseur` | aucun fournisseur déclaré dans l'inventaire |
| `Sans référence` | le fournisseur le vend, mais sans référence enregistrée |
| `Hors inventaire` | l'article n'existe pas dans l'inventaire |

Le tableau de l'email est réduit à ce qui a un sens : n° d'article, ancienne référence,
désignation, votre référence, référence fabricant, quantités, remarque. Ni révision, ni date
de réalisé, ni matière, ni finitions. La colonne *Réf. fabricant* n'apparaît que si au moins
un article en porte une — elle n'est renseignée que sur une minorité d'entre eux.

---

## config.json

| Clé | Type | Rôle |
|---|---|---|
| `PdmRoot` | texte | Racine de la vue locale du coffre PDM à explorer. |
| `OutputRoot` | texte | Dossier racine des exports. **Vide = dossier Téléchargements de l'utilisateur.** |
| `ZipThresholdMb` | entier | Poids maximal, en Mo, des pièces jointes d'un message. Au-delà, la demande est répartie sur plusieurs emails. |
| `MaxAttachments` | entier | Nombre maximal de pièces jointes d'un message. Au-delà, la demande est répartie sur plusieurs emails. |
| `ArchiveRoot` | texte | Racine où chaque demande est archivée, un dossier par demande nommé `date_destinataire_OFFRE|FAB`. Vide = pas d'archivage. Un réseau indisponible n'interrompt jamais le traitement. |
| `PartNumberPatterns` | liste | Format imposé, décrit par les longueurs de groupes. `["3-5-2"]` = `XYZ-AAAAA-BB` : tout numéro non conforme est refusé. Le premier motif sert aussi à insérer les tirets automatiquement. |
| `ReleasedStates` | liste | Valeurs d'état PDM considérées comme libérées. Tout autre état non vide déclenche l'avertissement groupé. |
| `Properties` | objet | Noms des propriétés SolidWorks à lire pour la désignation, la révision, la date, la matière et les finitions. Plusieurs noms par donnée : le premier trouvé non vide gagne. Adaptez cette liste si vos cartes de données évoluent. |
| `DefaultSender` | texte | Expéditeur par défaut (réservé, non utilisé actuellement). |
| `SupplierListPath` | texte | Dossier réseau de la liste des fournisseurs (`fournisseurs.json`). Relue à chaque démarrage, réenregistrée à chaque modification. |
| `Export3D` | booléen | État initial de la case « Exporter 3D (STEP AP203) ». |
| `Export2D` | booléen | État initial de la case « Exporter 2D (PDF + DXF) ». |
| `ZipCompression` | texte | Niveau de compression des archives : `Aucune`, `Rapide`, `Optimal` ou `Maximale`. Réglable dans le bandeau d'options, le choix s'y enregistre. |

### Contrôle de fabrication (bêta)

Un second fichier, `config\controle-fabrication.json`, règle le module de contrôle de fabrication :
noms de propriétés à lire pour la matière, le traitement et la peinture, valeur écrite quand
aucune n'est trouvée, exigence d'aspect et marge de détection des repères de cadre.
Il est recréé avec ses valeurs d'origine s'il manque. Voir [docs/ControleFabrication.md](docs/ControleFabrication.md).

Le journal d'exécution est écrit dans `%LOCALAPPDATA%\AskThem\logs\askthem_AAAAMMJJ.log`.

---

## Mode d'emploi en 5 étapes

1. **Choisir le type de demande** avec l'interrupteur en haut de la fenêtre : *Demande d'offre*
   (jusqu'à 3 paliers de quantité) ou *Demande de fabrication* (une seule quantité,
   avec l'avertissement sur la révision des plans dans l'email).
2. **Saisir les articles** : bouton *Ajouter ligne*, *Coller Excel* (ou `Ctrl+V`
   directement dans la grille), ou *Importer liste* — qui accepte aussi bien un
   fichier **CSV** qu'un classeur **Excel `.xlsx`**, dont la première feuille est lue.

   Les colonnes sont reconnues par leur **intitulé**, pas par leur position : un export de
   nomenclature dont le code article est en colonne B, sous un titre, est lu correctement.
   Les articles cités plusieurs fois sont **regroupés en une ligne, quantités additionnées**. Un simple clic suffit pour modifier
   une cellule. La grille ne contient que ce que vous remplissez vous-même : le
   **numéro d'article**, les **quantités** et une **remarque**. Désignation, révision
   du plan, date de réalisé, matière et finitions sont lues dans le coffre et
   s'affichent dans le **volet de droite** pour la ligne sélectionnée.

   Les **tirets du numéro d'article sont ajoutés automatiquement** : tapez `A210000001`,
   vous obtenez `A21-00000-01`. Un numéro qui ne respecte aucun format de
   `PartNumberPatterns` est refusé — à la saisie pour une cellule, par un message unique
   récapitulant les refus pour un collage ou un import.
3. **Cliquer sur *Vérifier*** pour contrôler la présence des fichiers dans le coffre.
   Vert = 3D et 2D trouvés, orange = un des deux manque, rouge = introuvable.
4. **Renseigner les paramètres** en bas : **fournisseur** choisi dans la liste, référence de commande, délai souhaité,
   exports souhaités, et éventuellement des **conditions générales** — ce texte libre est
   ajouté en fin d'email, après le tableau des articles.

   Un **document PDF** peut accompagner la demande : le **bon de commande** en fabrication,
   où il est **obligatoire** — la génération est refusée tant qu'il n'est pas joint — ou une
   **demande de PO** en offre, où il est facultatif. Il est archivé avec la demande et joint
   à l'email à part, hors des archives par article.

   Le destinataire ne se tape pas : il se choisit. Le bouton **Fournisseurs…** ouvre la
   gestion de la liste — création, modification, suppression, avec **plusieurs adresses
   par fournisseur** et des **adresses en copie**. Le bouton *Importer une liste…* reprend
   un tableau CSV ou Excel, les colonnes étant reconnues par leur intitulé. La liste vit sur le réseau, elle est
   donc partagée par tous les postes et relue à chaque démarrage.
5. **Cliquer sur *Générer la demande***. SolidWorks s'ouvre en arrière-plan, les fichiers
   sont exportés dans un dossier horodaté, puis Outlook affiche l'email pré-rempli.
   **Relisez-le, puis envoyez-le vous-même.**

> **Un seul avertissement, jamais un par pièce.** Avant de préparer l'email, un message
> unique récapitule les articles **sans plan 2D** et ceux qui ne sont **pas libérés** dans
> le PDM (état différent de `ReleasedStates`). Le détail complet va dans le journal et dans
> le journal, en bas de la fenêtre. Le contrôle d'état ne s'applique que si la variable d'état figure
> dans les propriétés des fichiers ; sinon il est signalé comme inapplicable dans le journal.

> **Si SolidWorks est déjà ouvert**, un avertissement demande confirmation avant de
> démarrer : le traitement se déroulera dans votre session, où des documents seront
> ouverts et refermés. Enregistrez votre travail avant de confirmer. Votre session
> n'est jamais masquée ni fermée par AskThem.

Le bouton *Annuler* interrompt le traitement en cours ; les fichiers déjà exportés
sont conservés et SolidWorks est refermé proprement.

---

## Dossier de sortie

```
<Téléchargements>\AskThem_OFFRE_20260821_1432\
├── 3D_STEP\             (fichiers .STEP AP203)
├── 2D_PLANS\            (fichiers .pdf et .dxf)
└── ZIP_par_article\     (une archive par numéro d'article)
```

Ce sont les **archives par article** qui sont jointes à l'email : le destinataire reçoit
un fichier par article, contenant son STEP, son PDF et son DXF.

### Quand la demande ne tient pas dans un seul email

Un message ne porte jamais plus de `ZipThresholdMb` mégaoctets ni plus de `MaxAttachments`
pièces jointes. Au-delà, **la demande part en plusieurs emails** : le sujet est suffixé
`(1/3)`, `(2/3)`…, et chaque message ne décrit dans son tableau que les articles qu'il
transporte. L'ordre de la grille est conservé. Le bon de commande n'est joint qu'au premier.

Une archive à elle seule plus lourde que la limite ne peut pas être coupée : elle part dans
un message à part et un avertissement le signale dans le journal.

Le **niveau de compression** se règle dans le bandeau d'options et se conserve d'une session
à l'autre. Mesuré sur les exports du coffre, `Maximale` ne gagne qu'environ trois pour cent
de plus qu'`Optimal` pour quatre fois le temps : les DXF sont déjà bien compressés, c'est le
découpage en plusieurs messages qui règle vraiment le problème de volume.

Les fichiers portent le **numéro d'article** seul. La révision n'est pas ajoutée au nom :
elle figure dans le tableau de l'email, colonne *Rév. plan*.
