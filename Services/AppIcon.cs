using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace AskThem.Services
{
    /// <summary>
    /// Icône des fenêtres. Elle est chargée depuis les ressources de l'assembly :
    /// la barre des tâches affiche l'icône de la FENÊTRE, pas celle du fichier, et
    /// l'extraction depuis l'exécutable ne renvoie qu'une seule taille — insuffisant
    /// pour que Windows choisisse la bonne selon le contexte.
    /// </summary>
    public static class AppIcon
    {
        private static Icon _icone;
        private static bool _tente;

        /// <summary>Retourne l'icône, ou null si elle est introuvable.</summary>
        public static Icon Get()
        {
            if (_tente) return _icone;
            _tente = true;

            // 1) Ressource embarquée : contient toutes les tailles, de 16 à 256.
            try
            {
                Assembly a = Assembly.GetExecutingAssembly();
                foreach (string nom in a.GetManifestResourceNames())
                {
                    if (!nom.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) continue;
                    using (Stream flux = a.GetManifestResourceStream(nom))
                    {
                        if (flux == null) continue;
                        _icone = new Icon(flux);
                        return _icone;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Icône embarquée illisible : " + ex.Message);
            }

            // 2) Repli : l'icône du fichier exécutable.
            try
            {
                string exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe)) _icone = Icon.ExtractAssociatedIcon(exe);
            }
            catch (Exception)
            {
                _icone = null;
            }
            return _icone;
        }

        /// <summary>Applique l'icône à une fenêtre, sans échouer si elle est indisponible.</summary>
        public static void Apply(System.Windows.Forms.Form form)
        {
            try
            {
                Icon i = Get();
                if (i == null || form == null) return;
                form.Icon = i;
                form.ShowIcon = true;
            }
            catch (Exception) { }
        }
    }
}
