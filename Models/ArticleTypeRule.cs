namespace AskThem.Models
{
    /// <summary>
    /// Règle attachée au type d'article, lu dans les deux caractères YZ du code
    /// XYZ-AAAAA-BB. X n'indique que l'origine (mécanique, optique, électronique) et
    /// n'a aucune incidence : seul YZ détermine ce qu'on peut demander et ce qu'on livre.
    /// </summary>
    public class ArticleTypeRule
    {
        /// <summary>Intitulé affiché dans les messages.</summary>
        public string Label { get; set; }

        /// <summary>Faux pour un type qui ne peut faire l'objet d'aucune demande (assemblages).</summary>
        public bool Allowed { get; set; }

        /// <summary>Vrai si une demande de fabrication est possible sur ce type.</summary>
        public bool AllowFabrication { get; set; }

        /// <summary>Vrai si le modèle 3D doit accompagner la demande.</summary>
        public bool Export3D { get; set; }

        /// <summary>Vrai si le plan doit accompagner la demande.</summary>
        public bool Export2D { get; set; }

        /// <summary>
        /// Vrai si le fournisseur est figé par le PDM : on ne peut alors pas adresser
        /// la demande à quelqu'un d'autre, et c'est sa référence qui fait foi.
        /// </summary>
        public bool SupplierImposed { get; set; }

        public ArticleTypeRule()
        {
            Label = "";
            Allowed = true;
            AllowFabrication = true;
            Export3D = true;
            Export2D = true;
            SupplierImposed = false;
        }

        public static ArticleTypeRule Create(string label, bool allowed, bool fabrication,
                                             bool export3D, bool export2D, bool supplierImposed)
        {
            ArticleTypeRule r = new ArticleTypeRule();
            r.Label = label;
            r.Allowed = allowed;
            r.AllowFabrication = fabrication;
            r.Export3D = export3D;
            r.Export2D = export2D;
            r.SupplierImposed = supplierImposed;
            return r;
        }
    }
}
