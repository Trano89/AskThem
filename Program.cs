using System;
using System.Threading;
using System.Windows.Forms;
using AskThem.Services;

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

            // Une exception non prévue dans un gestionnaire d'événement affichait la boîte
            // d'erreur brute de .NET, dont le bouton « Quitter » perdait la demande en cours.
            // Elle est désormais journalisée et l'application reste ouverte.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += new ThreadExceptionEventHandler(delegate (object s, ThreadExceptionEventArgs e)
            {
                LogService.Write("ERREUR NON TRAITÉE : " + e.Exception);
                try
                {
                    MessageBox.Show("Une erreur inattendue s'est produite : " + e.Exception.Message
                        + Environment.NewLine + Environment.NewLine
                        + "Le détail est dans le journal. AskThem reste ouvert.",
                        "AskThem", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception) { }
            });
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(
                delegate (object s, UnhandledExceptionEventArgs e)
                {
                    LogService.Write("ERREUR FATALE : " + e.ExceptionObject);
                });

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
