# Rapport de contrôle

> **Fonction en bêta.** Elle est utilisable, mais relisez le rapport avant de l'envoyer :
> l'extraction dépend de la façon dont chaque plan est coté, et un plan dont les cotes sont
> du texte libre ne livrera presque rien.

Formulaire PDF pré-rempli automatiquement à partir du dessin SolidWorks, joint à la demande
de fabrication. Le fournisseur le remplit à la main pendant la fabrication, le signe, et nous
le retourne avec la livraison pour certifier la conformité.

## Ce que fait le module

À chaque article dont un plan a été trouvé, AskThem lit le `.SLDDRW` **dans la passe d'export
existante** — le document n'est jamais rouvert une seconde fois — et en tire :

- les cotes portant une tolérance explicite ou un ajustement ;
- toutes les tolérances géométriques ;
- tous les états de surface ;
- une ligne fixe ajoutée systématiquement : aspect, bavures et arêtes.

Les **tolérances générales ne figurent pas** dans le rapport : elles sont portées sur le plan
et ne font pas l'objet d'un contrôle à la réception.

Le PDF est écrit dans `RapportsControle\` du dossier de la demande, sous le nom
`RC_{NumeroPlan}_rev{Revision}.pdf`, et **rejoint l'archive ZIP de l'article** — il part donc
au fournisseur avec les STEP, PDF et DXF.

Un échec de génération n'interrompt jamais l'export ni les articles suivants : il est
journalisé et le traitement continue.

### Deux façons de le déclencher

| Où | Quoi |
|---|---|
| Case **Générer le rapport de contrôle (PDF) — bêta** dans le bandeau d'options | Un rapport par article ayant un plan. Cochée d'office en mode Fabrication, décochée en mode Offre. |
| Clic droit sur une ligne de la grille → **Rapport de contrôle… (bêta)** | Le rapport de ce seul article, écrit dans `<dossier de sortie>\RapportsControle\` et ouvert à la fin. Ne rejoint aucun ZIP. |

## Ajuster le filtre de sélection

Le rapport ne liste que ce qui engage le fournisseur. Sur les plans du coffre, cela donne des
rapports courts : `A21-00066-01` compte 52 cotes lues pour 1 seule tolérancée. C'est normal —
tout le reste est couvert par la tolérance générale — mais si le filtre s'avère trop strict :

```csharp
// Inspection\ExtracteurCaracteristiques.cs
public const bool INCLURE_TOUTES_LES_COTES = false;   // -> true
```

À `true`, toutes les cotes du plan sont reprises, tolérancées ou non. Compter environ trois
pages pour un plan de cinquante cotes. C'est la seule constante à changer.

Les cotes **entre parenthèses** restent toujours écartées : ce sont des cotes de référence.
Le test est `IDisplayDimension.ShowParenthesis`, et **pas** `IsReferenceDim` — sur une mise en
plan, `IsReferenceDim` est vrai pour presque toute cote (48 sur 52 sur `A21-00066-01`) et
l'utiliser viderait le rapport.

Deux relevés de même libellé, même spécification et distants de moins d'un millimètre sont
fusionnés (`ToleranceDoublonMm`). Deux callouts éloignés restent deux lignes : ce sont deux
surfaces à contrôler.

## Ajustements ISO

Une cote à ajustement (`H7`, `g6`, `G8`…) porte sa classe **et** ses écarts :
`Ø10 G8 (+0.027 / +0.005)`. La classe est lue sur l'objet tolérance de la cote, par
`GetHoleFitValue` pour un alésage et `GetShaftFitValue` pour un arbre ; une cote
d'accouplement qui porte les deux est rendue `H7/g6`.

Ne pas utiliser `IDimension.GetToleranceFitValues` : sa chaîne vaut « arbre,alésage », donc
un alésage `G8` s'y lit `,G8` et lire le premier champ ne rend que du vide. Elle ne sert plus
que de repli quand l'objet tolérance ne répond pas.

Une cote répétée voit son préfixe précédé d'une quantité, `(2x)Ø4`. Elle est conservée telle
quelle dans la spécification, et écartée avant de déduire la nature de la cote — sans quoi un
perçage répété serait étiqueté « Cote » au lieu de « Diamètre ».

## Corriger la table des symboles

Les codes de tolérance géométrique viennent du fichier `gtol.sym` de l'installation
SolidWorks, cherché dans `C:\ProgramData\SolidWorks\SOLIDWORKS <version>\lang\french\`
puis `\english\`. Deux bibliothèques y coexistent, `GTOL` (ANSI) et `IGTOL` (ISO) ; elles
partagent les mêmes noms courts, la table est donc indexée sur le nom **sans son préfixe**.

Au démarrage, `TableSymbolesGtol.Charger` relit `gtol.sym` et journalise tout nom présent dans
le fichier et absent de la table. Si cette ligne apparaît dans le journal, ajouter l'entrée
manquante dans `Inspection\TableSymbolesGtol.cs` :

```csharp
Ajouter(8, "PERP", "⊥", "Perpendicularité", "Perpendicularity");
//       ^n° TF&P  ^nom court réel du fichier gtol.sym
```

Les numéros TF&P sont la numérotation interne LyncéeTec : **ne pas les modifier**. Les noms
courts, eux, doivent correspondre exactement au fichier — relever les vrais avec :

```bash
grep -a "^[*#]" "/c/ProgramData/SolidWorks/SOLIDWORKS 2025/lang/french/gtol.sym"
```

Un code inconnu n'est jamais deviné : il est écrit tel quel entre chevrons dans la colonne
Caractéristique, et un avertissement part au journal.

## Réglages

`config\rapport-controle.json`, à côté de l'exécutable, recréé avec ses valeurs par défaut
s'il manque. Aucun nom de propriété SolidWorks n'est écrit dans le code.

| Clé | Rôle |
|---|---|
| `proprietes` | Noms de propriétés à essayer par champ ; la première non vide gagne. |
| `valeurSiVide` | Écrit quand aucune propriété n'est trouvée. Par défaut `Sans`. |
| `aspectParDefaut` | La ligne fixe du tableau. |
| `margeBordRepere` | Distance au bord, en mm, pour qu'une note d'un caractère soit tenue pour un repère de cadre. |

Aucune clé ne décrit le quadrillage : il est **mesuré sur la feuille**. Les repères du cadre
sont des notes d'un seul caractère posées sur les bords ; le module les relève avec leurs
coordonnées et reconstruit la grille. Un A4 comme un A0 sont traités par la même mesure, et le
sens des lettres est celui du cadre — sur les fonds de plan LyncéeTec, les lettres vont de
droite à gauche. La lettre de révision du cartouche est elle aussi une note d'un caractère :
c'est `margeBordRepere` qui l'écarte, puisqu'elle se trouve à l'intérieur du cadre.

## Journal

`RapportsControle\extraction.log`, une entrée par article : cotes lues, cotes retenues,
tolérances géométriques, états de surface, doublons fusionnés, et chaque avertissement.
Les avertissements figurent aussi dans `RapportControle.Avertissements` et dans le journal
de la fenêtre.

Un rapport de moins de trois caractéristiques porte en tête, en rouge :
**« Extraction partielle — vérifier le plan avant envoi. »** Ce cas signale presque toujours un
plan dont les cotes sont du texte libre.

## Changer de moteur PDF

La mise en page passe par `Pdf\IGenerateurPdf.cs`. `QuestPdfGenerateur` en est la seule
implémentation ; en écrire une autre (PDFsharp/MigraDoc, licence MIT) ne touche ni
l'extraction ni le pipeline.

QuestPDF est sous licence **Community**, gratuite pour une organisation dont le chiffre
d'affaires annuel brut est inférieur à 1 000 000 USD et qui n'est ni cotée en bourse ni du
secteur public. Le PDF produit n'est ni chiffré ni protégé : le fournisseur peut l'imprimer.
