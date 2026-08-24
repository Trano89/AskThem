# Journal des versions

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et les numéros de version [SemVer](https://semver.org/lang/fr/).

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
