using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AskThem.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AskThem.Services
{
    /// <summary>Pilote SolidWorks en arrière-plan pour lire les propriétés et exporter STEP, PDF et DXF.</summary>
    public class SolidWorksExporter : IDisposable
    {
        /// <summary>Métadonnées lues dans un document SolidWorks.</summary>
        public class DocMetadata
        {
            public string Description = "";
            public string Revision = "";
            public string Date = "";
            public string Material = "";
            public string Treatment = "";
            public string State = "";
        }

        private ISldWorks _sw;
        private bool _startedByUs;
        private PropertyNames _names;

        /// <summary>Vrai si une session SolidWorks etait deja ouverte et qu'on s'y est rattache.</summary>
        public bool AttachedToExistingSession { get { return _sw != null && !_startedByUs; } }

        public SolidWorksExporter(PropertyNames names)
        {
            _names = names != null ? names : new PropertyNames();
        }

        /// <summary>Indique qu'une session SolidWorks est deja ouverte sur le poste.</summary>
        public static bool IsSolidWorksRunning()
        {
            try { return Process.GetProcessesByName("SLDWORKS").Length > 0; }
            catch (Exception) { return false; }
        }

        /// <summary>Démarre ou récupère l'instance SolidWorks. Lève une exception si impossible.</summary>
        public void Connect()
        {
            Type t = Type.GetTypeFromProgID("SldWorks.Application");
            if (t == null)
                throw new Exception("SolidWorks n'est pas installé sur ce poste.");

            // SolidWorks est un serveur COM a instance unique : si une session est deja
            // ouverte, CreateInstance s'y rattache au lieu d'en demarrer une nouvelle.
            // Cette session appartient a l'utilisateur : on ne la masque pas et on ne la ferme pas.
            bool dejaOuverte = IsSolidWorksRunning();

            _sw = (ISldWorks)Activator.CreateInstance(t);
            _startedByUs = !dejaOuverte;

            if (_startedByUs)
            {
                _sw.Visible = false;
                _sw.UserControl = false;
            }
            _sw.CommandInProgress = true;
        }

        public void Dispose()
        {
            try
            {
                if (_sw != null)
                {
                    _sw.CommandInProgress = false;
                    if (_startedByUs) _sw.ExitApp();
                    else _sw.UserControl = true; // session de l'utilisateur : on lui rend la main
                    Marshal.ReleaseComObject(_sw);
                }
            }
            catch (Exception) { }
            finally
            {
                _sw = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // ------------------------------------------------------------------
        // Ouverture et fermeture : un document n'est ouvert QU'UNE FOIS,
        // l'appelant lit les proprietes et exporte dans la meme ouverture.
        // ------------------------------------------------------------------

        /// <summary>Ouvre un document. L'appelant doit le refermer avec CloseDocument dans un finally.</summary>
        public ModelDoc2 OpenDocument(string path)
        {
            int docType;
            string ext = Path.GetExtension(path).ToUpperInvariant();
            if (ext == ".SLDPRT") docType = (int)swDocumentTypes_e.swDocPART;
            else if (ext == ".SLDASM") docType = (int)swDocumentTypes_e.swDocASSEMBLY;
            else docType = (int)swDocumentTypes_e.swDocDRAWING;

            int errors = 0;
            int warnings = 0;
            ModelDoc2 doc = _sw.OpenDoc6(
                path,
                docType,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref errors,
                ref warnings);

            if (doc == null)
                throw new Exception("Impossible d'ouvrir : " + Path.GetFileName(path) + " (code " + errors + ")");
            return doc;
        }

        /// <summary>Referme un document et libère l'objet COM.</summary>
        public void CloseDocument(ModelDoc2 doc)
        {
            if (doc == null) return;
            string title = doc.GetTitle();
            Marshal.ReleaseComObject(doc);
            _sw.CloseDoc(title);
        }

        // ------------------------------------------------------------------
        // Lecture des proprietes
        // ------------------------------------------------------------------

        /// <summary>
        /// Lit toutes les métadonnées configurées en une seule passe.
        /// Les propriétés sont cherchées au niveau du document, puis au niveau de la
        /// configuration active — les plans du coffre les portent sous leur feuille (Blatt1),
        /// les pièces sous Default.
        /// </summary>
        public DocMetadata ReadMetadata(ModelDoc2 doc)
        {
            DocMetadata m = new DocMetadata();
            if (doc == null) return m;

            List<CustomPropertyManager> sources = new List<CustomPropertyManager>();
            try { sources.Add(doc.Extension.get_CustomPropertyManager("")); }
            catch (Exception) { }

            foreach (string cfg in GetConfigurationNames(doc))
            {
                try { sources.Add(doc.Extension.get_CustomPropertyManager(cfg)); }
                catch (Exception) { }
            }

            foreach (CustomPropertyManager cpm in sources)
            {
                if (m.Description == "") m.Description = GetFirstProp(cpm, _names.Description);
                if (m.Revision == "") m.Revision = GetFirstProp(cpm, _names.Revision);
                if (m.Date == "") m.Date = GetFirstProp(cpm, _names.Date);
                if (m.Material == "") m.Material = GetFirstProp(cpm, _names.Material);
                if (m.Treatment == "") m.Treatment = GetFirstProp(cpm, _names.Treatment);
                if (m.State == "") m.State = GetFirstProp(cpm, _names.State);
            }

            // Repli pour la matière : le matériau affecté au corps de la pièce.
            if (m.Material == "") m.Material = GetBodyMaterial(doc);
            return m;
        }

        /// <summary>Configurations à interroger : l'active d'abord, puis toutes les autres.</summary>
        private List<string> GetConfigurationNames(ModelDoc2 doc)
        {
            List<string> noms = new List<string>();
            try
            {
                if (doc.ConfigurationManager != null)
                {
                    Configuration active = doc.ConfigurationManager.ActiveConfiguration;
                    if (active != null && !string.IsNullOrWhiteSpace(active.Name)) noms.Add(active.Name);
                }
            }
            catch (Exception) { }
            try
            {
                object brut = doc.GetConfigurationNames();
                string[] tous = brut as string[];
                if (tous != null)
                {
                    foreach (string n in tous)
                        if (!string.IsNullOrWhiteSpace(n) && !noms.Contains(n)) noms.Add(n);
                }
            }
            catch (Exception) { }
            return noms;
        }

        /// <summary>Matériau affecté au corps, pour une pièce. Vide pour un assemblage ou un plan.</summary>
        private string GetBodyMaterial(ModelDoc2 doc)
        {
            try
            {
                if (doc.GetType() != (int)swDocumentTypes_e.swDocPART) return "";
                PartDoc part = doc as PartDoc;
                if (part == null) return "";
                string cfg = "";
                try { cfg = doc.ConfigurationManager.ActiveConfiguration.Name; } catch (Exception) { }
                string database = "";
                string mat = part.GetMaterialPropertyName2(cfg, out database);
                return string.IsNullOrWhiteSpace(mat) ? "" : mat.Trim();
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>Retourne la première propriété non vide parmi les noms proposés.</summary>
        private string GetFirstProp(CustomPropertyManager cpm, List<string> names)
        {
            if (cpm == null || names == null) return "";
            foreach (string name in names)
            {
                string value = GetProp(cpm, name);
                if (value != "") return value;
            }
            return "";
        }

        private string GetProp(CustomPropertyManager cpm, string name)
        {
            string val = "";
            string resolved = "";
            bool wasResolved = false;
            try
            {
                cpm.Get5(name, false, out val, out resolved, out wasResolved);
            }
            catch (Exception) { return ""; }
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved.Trim();
            if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
            return "";
        }

        // ------------------------------------------------------------------
        // Exports : le document est deja ouvert, l'appelant le referme.
        // ------------------------------------------------------------------

        /// <summary>Exporte un document 3D déjà ouvert en STEP AP203. Retourne le chemin créé.</summary>
        public string ExportStep(ModelDoc2 doc, string outputFolder, string baseName)
        {
            // AP203 : la valeur 203 correspond au protocole d'application AP203.
            _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, 203);

            string target = Path.Combine(outputFolder, baseName + ".STEP");
            int errors = 0;
            int warnings = 0;
            bool ok = doc.Extension.SaveAs(
                target,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref errors,
                ref warnings);

            if (!ok)
                throw new Exception("Échec de l'export STEP (code " + errors + ").");
            return target;
        }

        /// <summary>Exporte un dessin déjà ouvert en PDF (toutes les feuilles) et en DXF.</summary>
        public List<string> ExportDrawing(ModelDoc2 doc, string outputFolder, string baseName)
        {
            List<string> created = new List<string>();
            int errors = 0;
            int warnings = 0;

            // --- PDF : toutes les feuilles ---
            string pdfPath = Path.Combine(outputFolder, baseName + ".pdf");
            ExportPdfData pdfData = (ExportPdfData)_sw.GetExportFileData((int)swExportDataFileType_e.swExportPdfData);
            DrawingDoc drw = (DrawingDoc)doc;
            object sheetNames = drw.GetSheetNames();
            pdfData.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportSpecifiedSheets, sheetNames);
            pdfData.ViewPdfAfterSaving = false;

            bool okPdf = doc.Extension.SaveAs(
                pdfPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                pdfData,
                ref errors,
                ref warnings);
            if (okPdf) created.Add(pdfPath);

            // --- DXF ---
            string dxfPath = Path.Combine(outputFolder, baseName + ".dxf");
            errors = 0;
            warnings = 0;
            bool okDxf = doc.Extension.SaveAs(
                dxfPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref errors,
                ref warnings);
            if (okDxf) created.Add(dxfPath);

            if (created.Count == 0)
                throw new Exception("Échec de l'export du dessin (code " + errors + ").");
            return created;
        }
    }
}
