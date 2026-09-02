using System.Collections.Generic;

namespace AskThem.Models
{
    /// <summary>Un fournisseur : un nom, ses destinataires et ses copies.</summary>
    public class Supplier
    {
        /// <summary>Nom affiché et utilisé pour nommer le dossier d'archive.</summary>
        public string Name { get; set; }

        /// <summary>Destinataires principaux. Plusieurs adresses possibles.</summary>
        public List<string> Emails { get; set; }

        /// <summary>Adresses en copie.</summary>
        public List<string> CcEmails { get; set; }

        /// <summary>Note libre (contact, spécialité, délai habituel...).</summary>
        public string Note { get; set; }

        /// <summary>
        /// Identifiant de la fiche correspondante dans l'inventaire, ou zéro si le lien
        /// n'a pas été fait.
        ///
        /// C'est lui, et non le nom, qui décide si un fournisseur vend un article donné :
        /// les noms varient d'une base à l'autre — « Thorlabs » ici, « Thorlabs GmbH » là —
        /// alors que l'identifiant ne bouge pas. Le rapprochement des noms ne sert qu'à
        /// établir ce lien une fois, sous le contrôle de l'utilisateur.
        /// </summary>
        public int InventoryId { get; set; }

        public Supplier()
        {
            Name = "";
            Emails = new List<string>();
            CcEmails = new List<string>();
            Note = "";
            InventoryId = 0;
        }

        /// <summary>Adresses principales séparées par des points-virgules, pour Outlook.</summary>
        public string ToLine
        {
            get { return Join(Emails); }
        }

        /// <summary>Adresses en copie séparées par des points-virgules, pour Outlook.</summary>
        public string CcLine
        {
            get { return Join(CcEmails); }
        }

        private static string Join(List<string> adresses)
        {
            if (adresses == null) return "";
            List<string> propres = new List<string>();
            foreach (string a in adresses)
            {
                if (!string.IsNullOrWhiteSpace(a)) propres.Add(a.Trim());
            }
            return string.Join("; ", propres);
        }

        /// <summary>Libellé affiché dans la liste déroulante.</summary>
        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(Name)) return ToLine;
            int n = Emails == null ? 0 : Emails.Count;
            if (n <= 1) return Name;
            return Name + "  (" + n + " adresses)";
        }
    }
}
