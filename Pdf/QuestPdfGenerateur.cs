using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AskThem.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AskThem.Pdf
{
    /// <summary>
    /// Met en page le rapport de contrôle avec QuestPDF, d'après la maquette validée
    /// docs\rapport-controle-exemple.html. En cas de divergence, la maquette fait foi.
    /// </summary>
    public sealed class QuestPdfGenerateur : IGenerateurPdf
    {
        // ---- Palette, reprise telle quelle de la maquette ----
        private const string Ink = "#15181C";
        private const string InkSoft = "#5A6169";
        private const string Rule = "#AAB0B7";
        private const string Auto = "#ECEFF2";
        private const string Fill = "#FFFBE6";
        private const string Key = "#FFE14D";
        private const string Ok = "#1B7F4B";
        private const string Ko = "#B3261E";
        private const string Blanc = "#FFFFFF";
        private const string EnTeteTexte = "#C9CED4";

        private const float TailleCorps = 9f;
        private const float TailleEn = 7.5f;
        private const float TaillePied = 7f;
        private const float HauteurLigne = 7.2f;

        static QuestPdfGenerateur()
        {
            // Licence Community : la division est sous le seuil de 1 000 000 USD de chiffre
            // d'affaires annuel et n'est pas cotée en bourse.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public string Generer(RapportControle rapport, string dossier)
        {
            if (rapport == null) throw new ArgumentNullException("rapport");
            Directory.CreateDirectory(dossier);
            string chemin = CheminUnique(dossier, rapport);

            Document.Create(delegate (IDocumentContainer conteneur)
            {
                conteneur.Page(delegate (PageDescriptor page)
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(8, Unit.Millimetre);
                    page.DefaultTextStyle(delegate (TextStyle s)
                    {
                        return s.FontFamily("Arial", "Liberation Sans").FontSize(TailleCorps).FontColor(Ink).LineHeight(1.25f);
                    });

                    page.Header().Column(delegate (ColumnDescriptor col)
                    {
                        col.Item().ShowOnce().Element(delegate (IContainer c) { Bandeau(c, rapport, false); });
                        col.Item().SkipOnce().Element(delegate (IContainer c) { Bandeau(c, rapport, true); });
                    });

                    page.Content().Element(delegate (IContainer c) { Corps(c, rapport); });

                    page.Footer().Element(delegate (IContainer c) { Pied(c, rapport); });
                });
            }).GeneratePdf(chemin);

            return chemin;
        }

        // ------------------------------------------------------------------
        // Bandeau titre
        // ------------------------------------------------------------------

        private static void Bandeau(IContainer c, RapportControle r, bool reduit)
        {
            c.PaddingBottom(1.5f, Unit.Millimetre)
             .BorderBottom(1.6f, Unit.Point).BorderColor(Ink)
             .Row(delegate (RowDescriptor row)
             {
                 row.RelativeItem().AlignBottom().Text(delegate (TextDescriptor t)
                 {
                     float taille = reduit ? 10 : 13;
                     t.Span("Park").FontSize(taille).Bold();
                     t.Span("Systems").FontSize(taille).FontColor(InkSoft);
                     t.Span("  /  Lyncée Tec Division").FontSize(taille - 3.5f).FontColor(InkSoft);
                 });

                 row.AutoItem().AlignBottom().AlignRight().Column(delegate (ColumnDescriptor col)
                 {
                     col.Item().AlignRight().Text("Rapport de contrôle de fabrication")
                        .FontSize(reduit ? 10 : 12).Bold();

                     if (!reduit)
                     {
                         col.Item().AlignRight().Text("Manufacturing inspection report")
                            .FontSize(TailleEn).Italic().FontColor(InkSoft);
                         col.Item().AlignRight()
                            .Text("Généré par AskThem le " + r.DateGeneration.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                                  + " — source : " + Source(r) + " — modèle RC-01 rev.A (bêta)")
                            .FontSize(TaillePied).FontColor(InkSoft);
                     }
                     else
                     {
                         col.Item().AlignRight().Text(r.NumeroPlan + " rev." + r.Revision + " — suite")
                            .FontSize(TaillePied).FontColor(InkSoft);
                     }
                 });
             });
        }

        private static string Source(RapportControle r)
        {
            return string.IsNullOrWhiteSpace(r.CheminSourcePlan) ? "coffre PDM" : r.CheminSourcePlan;
        }

        // ------------------------------------------------------------------
        // Corps
        // ------------------------------------------------------------------

        private static void Corps(IContainer c, RapportControle r)
        {
            c.PaddingTop(2, Unit.Millimetre).Column(delegate (ColumnDescriptor col)
            {
                if (r.ExtractionPartielle)
                {
                    col.Item().PaddingBottom(2, Unit.Millimetre)
                       .Text("Extraction partielle — vérifier le plan avant envoi.")
                       .FontSize(8).Bold().FontColor(Ko);
                }

                col.Item().Element(Legende);
                col.Item().PaddingTop(1.5f, Unit.Millimetre).Element(delegate (IContainer x) { Identification(x, r); });
                col.Item().PaddingTop(1.5f, Unit.Millimetre).Element(delegate (IContainer x) { BlocMatiere(x, r); });
                col.Item().PaddingTop(1, Unit.Millimetre).Element(delegate (IContainer x) { Tableau(x, r); });
                col.Item().PaddingTop(1.5f, Unit.Millimetre).Element(delegate (IContainer x) { BasDePage(x, r); });
            });
        }

        private static void Legende(IContainer c)
        {
            c.Row(delegate (RowDescriptor row)
            {
                Pastille(row, Auto, "Rempli automatiquement — ne pas modifier");
                Pastille(row, Fill, "À compléter par le fournisseur");
                Pastille(row, Key, "Identification de la pièce");
                row.RelativeItem();
            });
        }

        private static void Pastille(RowDescriptor row, string couleur, string texte)
        {
            row.AutoItem().PaddingRight(5, Unit.Millimetre).Row(delegate (RowDescriptor interne)
            {
                interne.AutoItem().AlignMiddle().PaddingRight(1.2f, Unit.Millimetre)
                       .Width(4, Unit.Millimetre).Height(3, Unit.Millimetre)
                       .Border(0.5f, Unit.Point).BorderColor(Rule).Background(couleur);
                interne.AutoItem().AlignMiddle().Text(texte).FontSize(TailleEn);
            });
        }

        // ------------------------------------------------------------------
        // Identification et matière
        // ------------------------------------------------------------------

        private static void Identification(IContainer c, RapportControle r)
        {
            c.Column(delegate (ColumnDescriptor col)
            {
                col.Item().Row(delegate (RowDescriptor row)
                {
                    Etiquette(row, "N° de plan", "Drawing number", 26);
                    Case(row.ConstantItem(44, Unit.Millimetre), r.NumeroPlan, Key, true, 11, true);
                    Etiquette(row, "Révision", "", 16);
                    Case(row.ConstantItem(12, Unit.Millimetre), r.Revision, Key, true, 11, true);
                    Etiquette(row, "Désignation", "Article designation", 30);
                    Case(row.RelativeItem(), r.Designation, Key, true, 11, true);
                    Etiquette(row, "Qté lot", "Batch qty", 18);
                    Case(row.ConstantItem(18, Unit.Millimetre), Quantite(r), Auto, true, TailleCorps, false);
                });

                col.Item().Row(delegate (RowDescriptor row)
                {
                    Etiquette(row, "Fournisseur", "Supplier", 22);
                    Case(row.RelativeItem(), r.Fournisseur, Auto, true, TailleCorps, false);
                    Etiquette(row, "N° cde", "Order", 16);
                    Case(row.RelativeItem(), r.NumeroCommande, Auto, true, TailleCorps, false);
                    Etiquette(row, "N° de lot", "Batch number", 24);
                    Case(row.ConstantItem(34, Unit.Millimetre), "", Fill, false, TailleCorps, false);
                    Etiquette(row, "Date", "", 12);
                    Case(row.ConstantItem(28, Unit.Millimetre), "", Fill, false, TailleCorps, false);
                });
            });
        }

        private static string Quantite(RapportControle r)
        {
            return r.QuantiteLot > 0 ? r.QuantiteLot.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static void BlocMatiere(IContainer c, RapportControle r)
        {
            c.Row(delegate (RowDescriptor row)
            {
                Etiquette(row, "Matière", "Raw material", 22);
                Case(row.ConstantItem(46, Unit.Millimetre), r.Matiere, Auto, true, TailleCorps, false);
                Etiquette(row, "Traitement", "Heat treatment", 24);
                Case(row.RelativeItem(2), r.Traitement, Auto, true, TailleCorps, false);
                Etiquette(row, "Peinture", "Paint", 17);
                Case(row.ConstantItem(26, Unit.Millimetre), r.Peinture, Auto, true, TailleCorps, false);
                Etiquette(row, "N° coulée / certificat", "Heat no. / material cert.", 38);
                Case(row.RelativeItem(), "", Fill, false, TailleCorps, false);
            });
        }

        /// <summary>
        /// Cellule d'intitulé : libellé français en gras, traduction en dessous. La largeur
        /// est imposée — laissée libre, un intitulé long se replie et fait grandir toute la
        /// rangée, ce qui coûte une page entière sur un rapport dense.
        /// </summary>
        private static void Etiquette(RowDescriptor row, string fr, string en, float largeurMm)
        {
            row.ConstantItem(largeurMm, Unit.Millimetre).Element(Bordure).Background(Blanc)
               .Padding(1, Unit.Millimetre)
               .Column(delegate (ColumnDescriptor col)
               {
                   col.Item().Text(fr).FontSize(8.5f).Bold().LineHeight(1f);
                   if (en != "") col.Item().Text(en).FontSize(TailleEn).Italic().FontColor(InkSoft).LineHeight(1f);
               });
        }

        /// <summary>Cellule de valeur, remplie par le programme ou laissée au fournisseur.</summary>
        private static void Case(IContainer c, string valeur, string fond, bool gras, float taille, bool centre)
        {
            IContainer boite = c.Element(Bordure).Background(fond)
                                .MinHeight(7, Unit.Millimetre)
                                .Padding(1, Unit.Millimetre).AlignMiddle();
            if (centre) boite = boite.AlignCenter();

            TextSpanDescriptor t = boite.Text(valeur == null ? "" : valeur).FontSize(taille);
            if (gras) t.Bold();
        }

        private static IContainer Bordure(IContainer c)
        {
            return c.Border(0.5f, Unit.Point).BorderColor(Rule);
        }

        // ------------------------------------------------------------------
        // Tableau des caractéristiques
        // ------------------------------------------------------------------

        private static void Tableau(IContainer c, RapportControle r)
        {
            c.Table(delegate (TableDescriptor table)
            {
                table.ColumnsDefinition(delegate (TableColumnsDefinitionDescriptor col)
                {
                    col.ConstantColumn(9, Unit.Millimetre);   // N°
                    col.ConstantColumn(13, Unit.Millimetre);  // Zone
                    col.ConstantColumn(52, Unit.Millimetre);  // Caractéristique
                    col.ConstantColumn(46, Unit.Millimetre);  // Spécification
                    col.ConstantColumn(24, Unit.Millimetre);  // Valeur mesurée
                    col.ConstantColumn(34, Unit.Millimetre);  // Instrument
                    col.ConstantColumn(16, Unit.Millimetre);  // OK / KO
                    col.RelativeColumn();                     // Commentaire
                });

                // L'en-tête se répète sur chaque page.
                table.Header(delegate (TableCellDescriptor entete)
                {
                    CelluleEnTete(entete.Cell(), "N°", "");
                    CelluleEnTete(entete.Cell(), "Zone", "Grid");
                    CelluleEnTete(entete.Cell(), "Caractéristique", "Characteristic");
                    CelluleEnTete(entete.Cell(), "Spécification", "Specification");
                    CelluleEnTete(entete.Cell(), "Valeur mesurée", "Measured");
                    CelluleEnTete(entete.Cell(), "Instrument utilisé", "Instrument used");
                    CelluleEnTete(entete.Cell(), "OK / KO", "");
                    CelluleEnTete(entete.Cell(), "Commentaire fournisseur", "Supplier comment");
                });

                foreach (Caracteristique ca in r.Caracteristiques)
                {
                    Caracteristique courante = ca;

                    CelluleTexte(table.Cell(), courante.Numero.ToString(CultureInfo.InvariantCulture), Blanc, true, true);
                    CelluleTexte(table.Cell(), courante.Zone, Blanc, false, true);
                    CelluleCaracteristique(table.Cell(), courante);
                    CelluleTexte(table.Cell(), courante.Specification, Blanc, true, false);
                    CelluleTexte(table.Cell(), "", Fill, false, false);
                    CelluleTexte(table.Cell(), "", Fill, false, false);
                    CelluleOkKo(table.Cell());
                    CelluleTexte(table.Cell(), "", Fill, false, false);
                }
            });
        }

        private static void CelluleEnTete(IContainer c, string fr, string en)
        {
            c.Background(Ink).Border(0.5f, Unit.Point).BorderColor(Ink)
             .Padding(1.2f, Unit.Millimetre)
             .Text(delegate (TextDescriptor t)
             {
                 t.DefaultTextStyle(delegate (TextStyle st) { return st.LineHeight(1.05f); });
                 t.Span(fr).FontSize(8).Bold().FontColor(Blanc);
                 if (en != "") t.Span("  " + en).FontSize(TailleEn).Italic().FontColor(EnTeteTexte);
             });
        }

        private static void CelluleTexte(IContainer c, string texte, string fond, bool gras, bool centre)
        {
            IContainer boite = c.Element(Bordure).Background(fond)
                                .MinHeight(HauteurLigne, Unit.Millimetre)
                                .Padding(1.2f, Unit.Millimetre).AlignMiddle();
            if (centre) boite = boite.AlignCenter();

            TextSpanDescriptor t = boite.Text(texte == null ? "" : texte);
            if (gras) t.Bold();
        }

        /// <summary>Le symbole GD&amp;T précède le libellé, en 11 pt, la traduction suit.</summary>
        private static void CelluleCaracteristique(IContainer c, Caracteristique ca)
        {
            c.Element(Bordure).Background(Blanc)
             .MinHeight(HauteurLigne, Unit.Millimetre)
             .Padding(1.2f, Unit.Millimetre).AlignMiddle()
             .Text(delegate (TextDescriptor t)
             {
                 if (!string.IsNullOrEmpty(ca.Symbole)) t.Span(ca.Symbole + " ").FontSize(11);
                 t.Span(ca.LibelleFr);
                 if (!string.IsNullOrEmpty(ca.LibelleEn))
                     t.Span("  " + ca.LibelleEn).FontSize(TailleEn).Italic().FontColor(InkSoft);
             });
        }

        /// <summary>Le fournisseur entoure la bonne mention.</summary>
        private static void CelluleOkKo(IContainer c)
        {
            c.Element(Bordure).Background(Fill)
             .MinHeight(HauteurLigne, Unit.Millimetre)
             .Padding(1.2f, Unit.Millimetre).AlignMiddle().AlignCenter()
             .Text(delegate (TextDescriptor t)
             {
                 t.Span("OK").FontSize(8).Bold().FontColor(Ok);
                 t.Span("  /  ").FontSize(8).FontColor(InkSoft);
                 t.Span("KO").FontSize(8).FontColor(Ko);
             });
        }

        // ------------------------------------------------------------------
        // Bas de page : table des symboles et bloc signature
        // ------------------------------------------------------------------

        private static void BasDePage(IContainer c, RapportControle r)
        {
            List<Caracteristique> avecSymbole = SymbolesUtilises(r);

            c.Row(delegate (RowDescriptor row)
            {
                if (avecSymbole.Count > 0)
                {
                    row.ConstantItem(96, Unit.Millimetre).PaddingRight(4, Unit.Millimetre)
                       .Element(delegate (IContainer x) { TableSymboles(x, avecSymbole); });
                }
                row.RelativeItem().Element(Signature);
            });
        }

        /// <summary>Seuls les symboles réellement présents dans ce rapport sont repris.</summary>
        private static List<Caracteristique> SymbolesUtilises(RapportControle r)
        {
            List<Caracteristique> retenus = new List<Caracteristique>();
            List<int> vus = new List<int>();
            foreach (Caracteristique ca in r.Caracteristiques)
            {
                if (ca.NumeroTfp <= 0 || string.IsNullOrEmpty(ca.Symbole)) continue;
                if (vus.Contains(ca.NumeroTfp)) continue;
                vus.Add(ca.NumeroTfp);
                retenus.Add(ca);
            }
            retenus.Sort(delegate (Caracteristique a, Caracteristique b) { return a.NumeroTfp.CompareTo(b.NumeroTfp); });
            return retenus;
        }

        private static void TableSymboles(IContainer c, List<Caracteristique> symboles)
        {
            c.Column(delegate (ColumnDescriptor col)
            {
                foreach (Caracteristique ca in symboles)
                {
                    Caracteristique courante = ca;
                    col.Item().Row(delegate (RowDescriptor row)
                    {
                        row.ConstantItem(8, Unit.Millimetre).Element(Bordure).Padding(0.8f, Unit.Millimetre)
                           .AlignCenter().Text(courante.NumeroTfp.ToString(CultureInfo.InvariantCulture))
                           .FontSize(TailleEn).Bold();

                        row.ConstantItem(8, Unit.Millimetre).Element(Bordure).Padding(0.8f, Unit.Millimetre)
                           .AlignCenter().Text(courante.Symbole).FontSize(11);

                        row.RelativeItem().Element(Bordure).Padding(0.8f, Unit.Millimetre)
                           .Text(delegate (TextDescriptor t)
                           {
                               t.Span(NomSeul(courante.LibelleFr)).FontSize(TailleEn);
                               if (!string.IsNullOrEmpty(courante.LibelleEn))
                                   t.Span("  " + courante.LibelleEn).FontSize(TailleEn).Italic().FontColor(InkSoft);
                           });
                    });
                }

                col.Item().PaddingTop(1, Unit.Millimetre)
                   .Text("Seuls les symboles présents sur le plan sont repris. Numérotation TF&P Lyncée Tec.")
                   .FontSize(TaillePied).FontColor(InkSoft);
            });
        }

        /// <summary>« Symétrie / A » redevient « Symétrie » dans la table des symboles.</summary>
        private static string NomSeul(string libelle)
        {
            if (string.IsNullOrEmpty(libelle)) return "";
            int coupe = libelle.IndexOf(" / ", StringComparison.Ordinal);
            if (coupe > 0) libelle = libelle.Substring(0, coupe);
            coupe = libelle.IndexOf(" (cadre ", StringComparison.Ordinal);
            if (coupe > 0) libelle = libelle.Substring(0, coupe);
            return libelle.Trim();
        }

        private static void Signature(IContainer c)
        {
            c.Column(delegate (ColumnDescriptor col)
            {
                col.Item().Row(delegate (RowDescriptor row)
                {
                    EtiquetteSignature(row, "Contrôlé par", "Inspected by", 24);
                    CaseSignature(row.RelativeItem(2), Fill, 11);
                    EtiquetteSignature(row, "Date", "", 16);
                    CaseSignature(row.ConstantItem(24, Unit.Millimetre), Fill, 11);
                    EtiquetteSignature(row, "Signature", "", 22);
                    CaseSignature(row.RelativeItem(), Fill, 11);
                });

                col.Item().Row(delegate (RowDescriptor row)
                {
                    EtiquetteSignature(row, "Pièce conforme", "Conformity", 24);
                    row.RelativeItem(2).Element(Bordure).Background(Fill)
                       .MinHeight(9, Unit.Millimetre).Padding(1.2f, Unit.Millimetre)
                       .AlignMiddle().AlignCenter()
                       .Text(delegate (TextDescriptor t)
                       {
                           t.Span("OUI").Bold().FontColor(Ok);
                           t.Span("   /   ").FontColor(InkSoft);
                           t.Span("NON — voir commentaires").FontColor(Ko);
                       });
                    EtiquetteSignature(row, "Dérogation", "Derogation", 20);
                    CaseSignature(row.RelativeItem(), Fill, 9);
                });

                // Réservé Lyncée Tec : le fournisseur n'y touche pas.
                col.Item().Row(delegate (RowDescriptor row)
                {
                    EtiquetteSignature(row, "Reçu / validé par", "Received by", 24);
                    CaseSignature(row.RelativeItem(2), Auto, 9);
                    EtiquetteSignature(row, "Date", "", 16);
                    CaseSignature(row.ConstantItem(24, Unit.Millimetre), Auto, 9);
                    EtiquetteSignature(row, "Visa", "", 22);
                    CaseSignature(row.RelativeItem(), Auto, 9);
                });
            });
        }

        private static void EtiquetteSignature(RowDescriptor row, string fr, string en, float largeurMm)
        {
            row.ConstantItem(largeurMm, Unit.Millimetre).Element(Bordure).Background(Blanc)
               .Padding(1.2f, Unit.Millimetre).AlignMiddle()
               .Column(delegate (ColumnDescriptor col)
               {
                   col.Item().Text(fr).FontSize(TailleEn).Bold().LineHeight(1f);
                   if (en != "") col.Item().Text(en).FontSize(TaillePied).Italic().FontColor(InkSoft).LineHeight(1f);
               });
        }

        /// <summary>
        /// 11 mm sur la rangee de signature, ou l'on ecrit vraiment ; 9 mm sur les deux
        /// suivantes, ce qui rend une ligne de tableau a la premiere page.
        /// </summary>
        private static void CaseSignature(IContainer c, string fond, float hauteurMm)
        {
            c.Element(Bordure).Background(fond).MinHeight(hauteurMm, Unit.Millimetre);
        }

        // ------------------------------------------------------------------
        // Pied de page
        // ------------------------------------------------------------------

        private static void Pied(IContainer c, RapportControle r)
        {
            c.Column(delegate (ColumnDescriptor col)
            {
                col.Item().PaddingTop(1.5f, Unit.Millimetre).LineHorizontal(0.5f, Unit.Point).LineColor(Rule);
                col.Item().PaddingTop(1, Unit.Millimetre).Row(delegate (RowDescriptor row)
                {
                    row.RelativeItem().Text(r.NumeroPlan + " rev." + r.Revision + " — "
                        + r.Caracteristiques.Count + " caractéristiques relevées automatiquement sur "
                        + r.NombreFeuilles + " feuille(s)")
                       .FontSize(TaillePied).FontColor(InkSoft);

                    row.RelativeItem().AlignRight().Text(delegate (TextDescriptor t)
                    {
                        t.DefaultTextStyle(delegate (TextStyle s) { return s.FontSize(TaillePied).FontColor(InkSoft); });
                        t.Span("Retourner ce document rempli et signé avec la livraison — Page ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        }

        // ------------------------------------------------------------------
        // Nommage
        // ------------------------------------------------------------------

        /// <summary>RC_{NumeroPlan}_rev{Revision}.pdf, suffixé _2, _3 en cas de collision.</summary>
        private static string CheminUnique(string dossier, RapportControle r)
        {
            string racine = "RC_" + Sain(r.NumeroPlan) + "_rev" + Sain(r.Revision);
            string candidat = Path.Combine(dossier, racine + ".pdf");
            int n = 2;
            while (File.Exists(candidat))
            {
                candidat = Path.Combine(dossier, racine + "_" + n + ".pdf");
                n++;
            }
            return candidat;
        }

        /// <summary>Remplace les caractères interdits par Windows.</summary>
        private static string Sain(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return "sans";
            char[] interdits = Path.GetInvalidFileNameChars();
            string s = nom.Trim();
            foreach (char c in interdits) s = s.Replace(c, '-');
            return s.Replace(' ', '-');
        }
    }
}
