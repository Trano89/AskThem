# Journal des versions

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et les numéros de version [SemVer](https://semver.org/lang/fr/).

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
