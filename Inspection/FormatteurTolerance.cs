using System;
using System.Globalization;
using SolidWorks.Interop.swconst;

namespace AskThem.Inspection
{
    /// <summary>
    /// Met en forme la colonne Spécification du rapport : une cote, son préfixe et sa
    /// tolérance sur une seule ligne, telle que le fournisseur la lira sur le plan.
    ///
    /// Les valeurs arrivent en millimètres pour la cote (IDimension.GetValue3) et en
    /// mètres pour les écarts (IDimension.GetToleranceValues) : la conversion est faite ici,
    /// une fois pour toutes.
    /// </summary>
    public static class FormatteurTolerance
    {
        private const double M2MM = 1000.0;

        /// <summary>En deçà, un écart est tenu pour nul.</summary>
        private const double Epsilon = 0.0000005;

        /// <summary>
        /// Compose la spécification complète.
        /// </summary>
        /// <param name="valeurMm">Valeur nominale, en millimètres.</param>
        /// <param name="typeTolerance">Membre de swTolType_e rendu par GetToleranceType().</param>
        /// <param name="ecartMinM">Écart inférieur, en mètres.</param>
        /// <param name="ecartMaxM">Écart supérieur, en mètres.</param>
        /// <param name="prefixe">Préfixe de la cote, modificateurs déjà traduits.</param>
        /// <param name="suffixe">Suffixe de la cote.</param>
        /// <param name="ajustement">Classe d'ajustement ISO, du type « H11 », ou chaîne vide.</param>
        /// <param name="angulaire">Vrai pour une cote angulaire : la valeur est en degrés.</param>
        public static string Composer(double valeurMm, int typeTolerance,
                                      double ecartMinM, double ecartMaxM,
                                      string prefixe, string suffixe,
                                      string ajustement, bool angulaire)
        {
            prefixe = prefixe == null ? "" : prefixe.Trim();
            suffixe = suffixe == null ? "" : suffixe.Trim();
            ajustement = ajustement == null ? "" : ajustement.Trim();

            double min = ecartMinM * M2MM;
            double max = ecartMaxM * M2MM;

            // Le préfixe est collé à la valeur : Ø6, R0.3.
            string nominal = prefixe + Nombre(valeurMm) + (angulaire ? "°" : "");
            string corps;

            switch ((swTolType_e)typeTolerance)
            {
                case swTolType_e.swTolBILAT:
                    corps = nominal + " (" + Signe(max) + " / " + Signe(min) + ")";
                    break;

                case swTolType_e.swTolSYMMETRIC:
                    // Un seul écart est renseigné : celui qui n'est pas nul fait foi.
                    double ecart = Math.Abs(max) > Epsilon ? Math.Abs(max) : Math.Abs(min);
                    corps = nominal + " ±" + Nombre(ecart) + (angulaire ? "°" : "");
                    break;

                case swTolType_e.swTolLIMIT:
                    corps = prefixe + Nombre(valeurMm + max) + " / " + prefixe + Nombre(valeurMm + min);
                    break;

                case swTolType_e.swTolMIN:
                    corps = nominal + " min";
                    break;

                case swTolType_e.swTolMAX:
                    corps = nominal + " max";
                    break;

                case swTolType_e.swTolFIT:
                    corps = nominal + (ajustement == "" ? "" : " " + ajustement);
                    break;

                case swTolType_e.swTolFITWITHTOL:
                case swTolType_e.swTolFITTOLONLY:
                    corps = nominal
                        + (ajustement == "" ? "" : " " + ajustement)
                        + " (" + Signe(max) + " / " + Signe(min) + ")";
                    break;

                case swTolType_e.swTolBASIC:
                    corps = "[" + nominal + "]";
                    break;

                default:
                    corps = nominal;
                    break;
            }

            return suffixe == "" ? corps : corps + suffixe;
        }

        /// <summary>
        /// Vrai si cette cote engage le fournisseur : elle porte une tolérance explicite
        /// ou un ajustement. Les cotes libres sont couvertes par la tolérance générale et
        /// n'ont pas leur place dans le rapport.
        /// </summary>
        public static bool EstTolerancee(int typeTolerance, double ecartMinM, double ecartMaxM)
        {
            switch ((swTolType_e)typeTolerance)
            {
                case swTolType_e.swTolBILAT:
                case swTolType_e.swTolSYMMETRIC:
                case swTolType_e.swTolLIMIT:
                    // Une tolérance déclarée mais à zéro des deux côtés n'engage à rien.
                    return Math.Abs(ecartMinM) > Epsilon || Math.Abs(ecartMaxM) > Epsilon;

                case swTolType_e.swTolMIN:
                case swTolType_e.swTolMAX:
                case swTolType_e.swTolFIT:
                case swTolType_e.swTolFITWITHTOL:
                case swTolType_e.swTolFITTOLONLY:
                case swTolType_e.swTolBASIC:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Arrondit à 3 décimales et supprime les zéros de queue : 0.075 et non 0.0750,
        /// 16 et non 16.000. Le point décimal est imposé, quelle que soit la langue du poste.
        /// </summary>
        public static string Nombre(double valeur)
        {
            double arrondi = Math.Round(valeur, 3, MidpointRounding.AwayFromZero);
            if (Math.Abs(arrondi) < Epsilon) arrondi = 0;
            return arrondi.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Écart signé : « +0.075 », « -0.1 », « 0 ».</summary>
        private static string Signe(double valeurMm)
        {
            string n = Nombre(valeurMm);
            if (n == "0") return "0";
            return valeurMm > 0 ? "+" + n : n;
        }

        /// <summary>
        /// Normalise un texte lu sur le plan : SolidWorks rend les décimales avec la
        /// virgule française (« 0,02 »), le rapport les écrit avec un point.
        /// </summary>
        public static string NormaliserDecimales(string texte)
        {
            if (string.IsNullOrEmpty(texte)) return "";
            return texte.Replace(',', '.').Trim();
        }
    }
}
