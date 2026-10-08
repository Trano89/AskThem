using System;
using System.Collections.Generic;
using System.Threading;
using AskThem.Models;

namespace AskThem.Services
{
    /// <summary>
    /// Le suivi des demandes, en arrière-plan, tant qu'AskThem est ouvert.
    ///
    /// Toutes les deux minutes — et aussitôt après une demande — il cherche dans les éléments
    /// envoyés les messages qu'AskThem a préparés, archive ce qui est parti, écrit la base des
    /// demandes quand elle est libre, et signale les rappels arrivés à échéance.
    ///
    /// Il tourne sur un fil STA à lui : Outlook se pilote en COM, et le fil de l'interface ne
    /// doit jamais attendre Outlook ni le réseau.
    /// </summary>
    public static class SuiviEnvois
    {
        private static Thread _fil;
        private static readonly AutoResetEvent Reveil = new AutoResetEvent(false);
        private static volatile bool _arret;
        private static DateTime _prochainsRappels = DateTime.MinValue;

        /// <summary>
        /// Des rappels sont échus pour l'utilisateur de ce poste. Levé sur le fil du suivi :
        /// l'abonné repasse sur le fil de l'interface.
        /// </summary>
        public static event Action<List<DemandeSuivie>> RappelsEchus;

        public static void Demarrer(Func<AppConfig> config, Action<string> journal)
        {
            if (_fil != null) return;
            _fil = new Thread(delegate () { Boucle(config, journal); });
            _fil.SetApartmentState(ApartmentState.STA);
            _fil.IsBackground = true;
            _fil.Name = "Suivi des demandes";
            _fil.Start();
        }

        /// <summary>Une demande vient d'être préparée, ou un rappel traité : passe sans attendre.</summary>
        public static void Reveiller()
        {
            Reveil.Set();
        }

        /// <summary>Relit les rappels au prochain passage, même si l'heure n'est pas venue.</summary>
        public static void RelireRappels()
        {
            _prochainsRappels = DateTime.MinValue;
            Reveil.Set();
        }

        public static void Arreter()
        {
            _arret = true;
            Reveil.Set();
        }

        private static void Boucle(Func<AppConfig> config, Action<string> journal)
        {
            // Le démarrage d'abord : la fenêtre, l'inventaire, le coffre.
            Reveil.WaitOne(TimeSpan.FromSeconds(15));
            while (!_arret)
            {
                try
                {
                    Passe(config(), journal);
                }
                catch (Exception ex)
                {
                    LogService.Write("Suivi des demandes : " + ex.Message);
                }
                Reveil.WaitOne(TimeSpan.FromMinutes(2));
            }
        }

        private static void Passe(AppConfig config, Action<string> journal)
        {
            int constates = ArchiveEnAttente.Reprendre(config, journal);

            // Le classeur est aussi refait une fois par jour, et après une mise à jour de sa
            // mise en page : sans cela, son Gantt restait arrêté au jour de la dernière demande.
            if (BaseSuivi.EnAttente() > 0 || BaseSuivi.VueARafraichir(config))
            {
                string message;
                if (!BaseSuivi.Vider(config, out message) && message != "") LogService.Write(message);
            }

            // Les rappels se relisent toutes les demi-heures, et après un envoi constaté : relire
            // le classeur toutes les deux minutes n'apporterait rien.
            if (constates == 0 && DateTime.Now < _prochainsRappels) return;
            _prochainsRappels = DateTime.Now.AddMinutes(30);

            List<DemandeSuivie> echus = new List<DemandeSuivie>();
            foreach (DemandeSuivie d in BaseSuivi.Miennes(config))
                if (d.RappelEchu(System.Environment.UserName, DateTime.Today)) echus.Add(d);

            Action<List<DemandeSuivie>> abonne = RappelsEchus;
            if (echus.Count > 0 && abonne != null)
            {
                try { abonne(echus); }
                catch (Exception ex) { LogService.Write("Rappels : " + ex.Message); }
            }
        }
    }
}
