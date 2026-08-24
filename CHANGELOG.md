# Journal des versions

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et les numéros de version [SemVer](https://semver.org/lang/fr/).

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
