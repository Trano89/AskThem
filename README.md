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

## config.json

| Clé | Type | Rôle |
|---|---|---|
| `PdmRoot` | texte | Racine de la vue locale du coffre PDM à explorer. |
| `OutputRoot` | texte | Dossier racine des exports. **Vide = dossier Téléchargements de l'utilisateur.** |
| `ZipThresholdMb` | entier | Volume, en Mo, au-delà duquel les archives par article sont regroupées en une seule. |
| `MaxAttachments` | entier | Nombre d'archives au-delà duquel elles sont regroupées en une seule. Évite les emails à 300 pièces jointes. |
| `ArchiveRoot` | texte | Racine où chaque demande est archivée, un dossier par demande nommé `date_destinataire_OFFRE|FAB`. Vide = pas d'archivage. Un réseau indisponible n'interrompt jamais le traitement. |
| `PartNumberPatterns` | liste | Format imposé, décrit par les longueurs de groupes. `["3-5-2"]` = `XYZ-AAAAA-BB` : tout numéro non conforme est refusé. Le premier motif sert aussi à insérer les tirets automatiquement. |
| `ReleasedStates` | liste | Valeurs d'état PDM considérées comme libérées. Tout autre état non vide déclenche l'avertissement groupé. |
| `Properties` | objet | Noms des propriétés SolidWorks à lire pour la désignation, la révision, la date, la matière et les finitions. Plusieurs noms par donnée : le premier trouvé non vide gagne. Adaptez cette liste si vos cartes de données évoluent. |
| `DefaultSender` | texte | Expéditeur par défaut (réservé, non utilisé actuellement). |
| `SupplierListPath` | texte | Dossier réseau de la liste des fournisseurs (`fournisseurs.json`). Relue à chaque démarrage, réenregistrée à chaque modification. |
| `Export3D` | booléen | État initial de la case « Exporter 3D (STEP AP203) ». |
| `Export2D` | booléen | État initial de la case « Exporter 2D (PDF + DXF) ». |

Le journal d'exécution est écrit dans `%LOCALAPPDATA%\AskThem\logs\askthem_AAAAMMJJ.log`.

---

## Mode d'emploi en 5 étapes

1. **Choisir le type de demande** avec l'interrupteur en haut de la fenêtre : *Demande d'offre*
   (jusqu'à 3 paliers de quantité) ou *Demande de fabrication* (une seule quantité,
   avec l'avertissement sur la révision des plans dans l'email).
2. **Saisir les articles** : bouton *Ajouter ligne*, *Coller Excel* (ou `Ctrl+V`
   directement dans la grille), ou *Importer liste* — qui accepte aussi bien un
   fichier **CSV** qu'un classeur **Excel `.xlsx`**, dont la première feuille est lue. Un simple clic suffit pour modifier
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
4. **Renseigner les paramètres** en bas : **fournisseur** choisi dans la liste, référence projet, délai souhaité,
   exports souhaités, et éventuellement des **conditions générales** — ce texte libre est
   ajouté en fin d'email, après le tableau des articles.

   En **demande de fabrication**, le **bon de commande au format PDF est obligatoire** :
   la génération est refusée tant qu'il n'est pas joint. Il est archivé avec la demande et
   joint à l'email à part, hors des archives par article.

   Le destinataire ne se tape pas : il se choisit. Le bouton **Fournisseurs…** ouvre la
   gestion de la liste — création, modification, suppression, avec **plusieurs adresses
   par fournisseur** et des **adresses en copie**. La liste vit sur le réseau, elle est
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
un fichier par article, contenant son STEP, son PDF et son DXF. Au-delà de
`MaxAttachments` archives ou de `ZipThresholdMb` mégaoctets, elles sont regroupées dans
une archive unique pour ne pas produire un email ingérable.

Les fichiers sont nommés `<Numéro d'article>_Rev<Révision>` lorsque la révision est
lisible dans les propriétés SolidWorks, sinon `<Numéro d'article>`.
