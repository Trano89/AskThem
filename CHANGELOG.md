# Journal des versions

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et les numéros de version [SemVer](https://semver.org/lang/fr/).

## [1.2.11] — 2026-08-25

### Modifié

- **L'ancienne référence suit immédiatement le numéro d'article** dans le tableau, au lieu
  d'être reléguée après la désignation.
- **Les notes en couleur sont regroupées en fin de message**, juste avant la signature :
  article recodifié puis rappel sur la révision des plans. Elles n'interrompent plus la
  lecture entre le tableau et le corps du message.
- **Le commentaire général devient un bloc titré placé sous le tableau**, dans la même
  taille que le reste du message. Il était jusqu'ici rendu en petits caractères gris,
  perdu en fin de message.
- **La référence demandée est celle de la commande**, et non plus celle du projet — dans
  le champ de saisie, dans l'objet de l'email et dans son corps.

## [1.2.10] — 2026-08-25

### Modifié

- **Une seule écriture dans toute l'application : Aptos, taille 12.** Aptos n'étant pas
  présente sur tous les postes, une chaîne de repli explicite est appliquée — Aptos, puis
  Segoe UI, puis Calibri. Sans elle, Windows substitue silencieusement une police très
  datée quand la police demandée manque.
- **Le bandeau de paramètres se replie tout seul.** Ses champs étaient posés à des
  positions fixes en pixels, qui ne survivaient pas au changement de taille : chaque
  groupe est désormais dimensionné d'après son texte et l'ensemble se réorganise selon
  la largeur de la fenêtre.
- Boutons, colonnes et intitulés tirent leur largeur du texte qu'ils portent, dans la
  police réellement retenue. Vérifié par capture du rendu des trois fenêtres, à la taille
  nominale comme à la taille minimale : plus aucun texte tronqué.

## [1.2.9] — 2026-08-25

### Corrigé

- **L'icône n'apparaissait pas dans la barre des tâches.** Elle était bien présente dans
  l'exécutable, mais la barre des tâches affiche l'icône de la **fenêtre**, pas celle du
  fichier. Elle était chargée par extraction depuis l'exécutable, ce qui ne fonctionnait
  pas dans un exécutable unique et ne fournissait qu'une seule taille.
- L'icône est désormais **embarquée dans l'assembly** et chargée directement, avec toutes
  ses tailles : Windows choisit la bonne selon le contexte. Vérifié sur l'application en
  fonctionnement, en interrogeant la fenêtre elle-même en 16 et 32 pixels.

## [1.2.8] — 2026-08-25

### Ajouté

- **Icône propre à l'application.** Une feuille de plan avec son coin replié et son
  cartouche, sur fond pétrole, et une pastille d'envoi ambre. Elle reprend le langage
  visuel des rapports du projet.
- L'icône est déclinée en neuf tailles, de 16 à 256 pixels, chacune contrôlée : la
  silhouette reste lisible dans la barre des tâches comme dans l'explorateur.
- Les fenêtres de l'application — principale, fournisseurs, inventaire — portent la même
  icône, extraite de l'exécutable pour qu'elle le suive où qu'il soit copié.

## [1.2.7] — 2026-08-25

### Sécurité

- **AskThem ne peut pas écrire dans l'inventaire.** Un garde-fou placé au niveau du
  transport refuse toute requête qui n'est pas une lecture, avant qu'elle ne parte sur
  le réseau. La seule exception est le `POST` d'ouverture de session, limité à l'adresse
  exacte de connexion — le serveur n'offrant aucun autre moyen de s'authentifier.
- Le contrôle est **indépendant des droits du compte** : un utilisateur disposant de
  droits de gestion sur l'inventaire ne peut rien y modifier en passant par AskThem.
- Le client d'inventaire n'expose **aucune méthode d'écriture**, et toutes ses lectures
  passent par une primitive `GET` unique.
- Vérifié par test : sur dix tentatives — création, modification, suppression d'articles,
  de commandes et de fournisseurs, sauvegarde d'administration — huit sont refusées et
  seules la lecture des articles et l'ouverture de session atteignent le réseau.
- Le compte connecté est journalisé, et la fenêtre de connexion recommande un compte en
  lecture seule : le refus vient alors du serveur et non du seul programme.

## [1.2.6] — 2026-08-25

### Ajouté

- **Connexion directe à l'inventaire.** Un bouton *Inventaire…* ouvre une fenêtre où
  l'adresse, l'utilisateur et le mot de passe sont saisis une fois. La connexion est
  éprouvée avant d'être enregistrée. Les demandes lisent ensuite les articles par l'API,
  avec **repli automatique sur l'export** si le service est indisponible.
- **Le mot de passe est chiffré par Windows** (DPAPI) dans `%LOCALAPPDATA%\AskThem`,
  lié à ce poste et à cette session. Il n'existe **ni dans le code, ni dans l'exécutable,
  ni dans `config.json`, ni sur le dépôt** : un identifiant placé dans un programme
  distribuable est lisible par quiconque en obtient une copie, et l'historique d'un dépôt
  est définitif. Le bouton *Oublier le mot de passe* l'efface du poste.

## [1.2.5] — 2026-08-25

### Ajouté

- **Anciennes références.** L'application consulte l'inventaire pour savoir si un article
  a déjà porté une autre référence. Quand c'est le cas, l'email affiche une colonne
  *Ancienne réf.* et un encadré avertit le fournisseur qu'il s'agit d'une **nouvelle
  référence de production**, que des **modifications ont pu être apportées**, et qu'il
  convient de **revoir la gamme** plutôt que de reconduire une préparation établie sur
  l'ancienne version. Rien ne s'affiche si aucun article n'est concerné.
- L'inventaire fournit également le **fournisseur** et la **référence fournisseur** des
  articles catalogue, données absentes du PDM.
- La source est un **export déposé sur le réseau**, dont le chemin est réglé par
  `InventoryExportPath`. Les colonnes sont reconnues par leur intitulé : `internal_ref`,
  `old_ref`, `supplier`, `supplier_ref`, ou leurs équivalents français.
  Un export absent ou illisible est signalé sans empêcher de travailler.

## [1.2.4] — 2026-08-24

### Modifié

- `24` catalogue modifié après livraison : le **plan et le 3D accompagnent désormais la
  demande**. La fabrication reste impossible sur ce type.
- **Un type non déclaré ne donne lieu à aucune demande.** Les codes dont les caractères
  `YZ` ne figurent pas dans `ArticleTypes` sont refusés à la saisie comme à l'import, au
  même titre que les assemblages, et le message nomme le type en cause. Rien ne part plus
  au hasard sur un type dont la règle n'a pas été écrite.
- Quand aucune référence fournisseur n'est lisible dans les propriétés des fichiers, le
  fait est signalé **une fois** dans le journal au lieu d'énumérer chaque article
  catalogue à chaque demande.

### Précision

- Le premier caractère du code (`X` dans `XYZ-AAAAA-BB`) n'indique que l'origine —
  mécanique, optique, électronique — et n'a jamais eu d'incidence sur les règles.
  `A21`, `B21`, `H21` et `#21` sont traités identiquement.

## [1.2.3] — 2026-08-24

### Ajouté

- **Le type d'article commande le contenu de la demande.** Il est lu dans les deux
  caractères `YZ` du code `XYZ-AAAAA-BB` — `X` n'indique que l'origine et n'entre pas
  en compte.
  - `21` pièce de détail : 3D et plan livrés, fabrication possible.
  - `20` article catalogue : ni 3D ni plan, seule la **référence fournisseur** est
    transmise ; fabrication impossible ; fournisseur figé par le PDM.
  - `22` catalogue modifié à l'achat : 3D et plan livrés, fournisseur figé.
  - `24` catalogue modifié après livraison : référence seule, fournisseur figé.
  - `13` assemblage : **aucune demande possible**, l'article est refusé à la saisie
    comme à l'import.
- Une **demande de fabrication** refuse de partir si elle contient des articles qui ne
  se fabriquent pas, en les listant.
- L'email porte une colonne **Votre référence** dès qu'un article catalogue en possède une.
- L'avertissement groupé signale les articles dont le **fournisseur est imposé** par le PDM
  et diffère du destinataire choisi, ainsi que ceux dépourvus de référence fournisseur.
- Les statuts ne réclament plus un plan pour un type qui n'est pas censé en avoir : les
  375 articles catalogue du coffre n'en ont aucun.
- Les règles sont modifiables dans `config.json`, section `ArticleTypes`.

## [1.2.2] — 2026-08-24

### Ajouté

- **Import d'une liste de fournisseurs** depuis un tableau CSV ou Excel, par le bouton
  *Importer une liste…* de la fenêtre Fournisseurs. Les colonnes sont reconnues par leur
  intitulé, dans n'importe quel ordre : *Nom*, *Nom 1*, *Fournisseur*, *Raison sociale*,
  *Entreprise* pour le nom ; *E-Mail*, *Courriel*, *Mail* pour les adresses ; *Cc* ou
  *Copie* pour les copies ; *Note*, *Remarque* ou *Libellé* pour la note.
  Plusieurs adresses dans une même cellule sont acceptées.
- Un fournisseur déjà présent est **complété sans être dupliqué** : réimporter une liste
  enrichie ajoute les adresses manquantes sans écraser l'existant.
- Un fichier sans colonne d'adresse est accepté — les noms sont créés, les adresses restent
  à compléter — et l'absence est signalée plutôt que passée sous silence.

## [1.2.1] — 2026-08-24

### Ajouté

- **Import des exports de nomenclature.** Les colonnes sont désormais reconnues par leur
  intitulé au lieu de leur position : le code article peut se trouver en colonne B, et la
  ligne d'en-tête après un titre. Intitulés reconnus, accents et casse indifférents :
  *Code article*, *N° article*, *Référence*, *Qté totale*, *Qté ligne*, *Quantité*,
  *Remarque*. À défaut d'en-tête reconnu, la lecture par position est conservée.
- **Regroupement des articles répétés.** Une nomenclature cite le même article à plusieurs
  niveaux de l'assemblage ; l'import n'en garde qu'une ligne et **additionne les quantités**.
  Sur un cas réel de 327 lignes : 212 articles, 115 lignes regroupées. Le nombre de
  regroupements est annoncé, rien ne se fait en silence.
- Les quantités écrites en décimal par Excel (`17.0`) sont lues correctement.

## [1.2.0] — 2026-08-24

### Ajouté

- **Import de listes Excel (`.xlsx`)**, en plus du CSV. Le bouton devient *Importer liste*
  et accepte les deux formats, avec la même correspondance de colonnes et le même contrôle
  du format de numéro d'article.
- La lecture des classeurs n'ajoute **aucune dépendance** : un `.xlsx` étant une archive
  ZIP de fichiers XML, il est lu avec la seule bibliothèque standard. Rien de tiers n'entre
  dans la chaîne de compilation.

## [1.1.0] — 2026-08-24

### Ajouté

- **Bon de commande obligatoire en demande de fabrication.** Un champ permet de joindre
  le PDF ; sans lui, la génération est refusée. Le fichier est archivé avec la demande,
  joint à l'email **séparément** des archives par article — le fournisseur doit le voir
  sans rien décompresser — et mentionné dans le corps du message.
  Le champ n'apparaît pas en demande d'offre, où il n'a pas lieu d'être.

## [1.0.2] — 2026-08-24

### Sécurité

- **Le dépôt de mise à jour ne peut plus être détourné.** Il était lu dans `config.json`,
  fichier posé à côté d'un exécutable volontairement portable : quiconque pouvait écrire
  dans ce dossier pouvait faire télécharger et exécuter un programme de son choix au
  démarrage suivant. Le dépôt est désormais figé dans le code.
- Le script de mise à jour porte un nom aléatoire, au lieu d'un nom fixe et prévisible
  dans le dossier temporaire.
- L'action GitHub de publication est épinglée sur un commit, et non plus sur une
  étiquette qui peut être redéplacée.

### Corrigé

- La liste des fournisseurs s'enregistre de façon atomique : une coupure réseau ne peut
  plus laisser le fichier partagé tronqué pour tout le monde.

### Modifié

- La demande est écrite **directement dans l'archive réseau**, sans passer par un dossier
  dans Téléchargements. Repli local automatique si le réseau est injoignable.
- Le dossier de sortie ne s'ouvre plus à la fin du traitement.
- Plus aucune fenêtre après la génération de l'email : le bilan va dans le journal.
  Seuls les problèmes restent signalés.
- L'email est réenregistré silencieusement tant qu'il reste ouvert dans Outlook : vos
  retouches sont archivées sans qu'on vous demande quoi que ce soit.

## [1.0.1] — 2026-08-24

### Corrigé

- La mise à jour remplace l'exécutable **exactement à l'emplacement d'où il tourne**,
  qui peut différer d'un poste à l'autre. Les chemins contenant des espaces ou des
  parenthèses sont pris en charge.
- Le script de remplacement ne boucle plus indéfiniment si le fichier reste verrouillé :
  40 tentatives, puis un message expliquant quoi faire.
- Le dossier est contrôlé en écriture **avant** le téléchargement, et le fichier reçu
  est vérifié : plus de remplacement par un téléchargement incomplet.

## [1.0.0] — 2026-08-24

Première version publiée.

### Fonctionnalités

- Demande d'**offre** (jusqu'à trois paliers de quantité) ou de **fabrication**,
  choisies par un interrupteur.
- Recherche des fichiers dans la vue locale du coffre SolidWorks PDM.
- Export **STEP AP203** pour le 3D, **PDF** (toutes les feuilles) et **DXF** pour les plans.
- Lecture dans le coffre de la désignation, des révisions modèle et plan, de la date
  de réalisé, de la matière et des finitions — l'utilisateur ne saisit que le numéro
  d'article, les quantités et une remarque.
- Une **archive ZIP par numéro d'article**, regroupée en une seule au-delà des seuils
  configurés.
- **Avertissement unique** listant les articles sans plan et ceux qui ne sont pas libérés.
- Gestion des **fournisseurs** partagée sur le réseau : plusieurs adresses et copies
  par fournisseur.
- Email Outlook pré-rempli, **jamais envoyé automatiquement**, avec la signature par
  défaut de l'expéditeur et un texte de conditions générales.
- Archivage de chaque demande sur le réseau, email `.msg` compris.
- Contrôle du format de numéro d'article `XYZ-AAAAA-BB`, tirets insérés automatiquement.
- Recherche de mise à jour au démarrage et remplacement de l'exécutable en un clic.
