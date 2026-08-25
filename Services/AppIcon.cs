using System;
using System.Drawing;

namespace AskThem.Services
{
    /// <summary>
    /// Icône de l'application, extraite de l'exécutable lui-même : elle suit donc
    /// le fichier, y compris pour un exécutable unique déplacé d'un poste à l'autre.
    /// </summary>
    public static class AppIcon
    {
        private static Icon _icone;
        private static bool _tente;

        /// <summary>Retourne l'icône, ou null si elle ne peut pas être lue.</summary>
        public static Icon Get()
        {
            if (_tente) return _icone;
            _tente = true;
            try
            {
                string exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    _icone = Icon.ExtractAssociatedIcon(exe);
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
                if (i != null && form != null) form.Icon = i;
            }
            catch (Exception) { }
        }
    }
}
