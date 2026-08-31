using System;
using System.Windows.Forms;

namespace AskThem
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Mise a l'echelle par moniteur : l'interface suit la densite d'ecran
            // au lieu d'etre etiree par Windows. A appeler avant toute fenetre.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            // Licence Community de QuestPDF, declaree au demarrage : LynceeTec est sous le
            // seuil de 1 000 000 USD de chiffre d'affaires annuel et n'est pas cotee.
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
