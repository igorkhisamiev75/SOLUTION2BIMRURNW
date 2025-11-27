using System.Collections;
using System.Windows;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;


namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class DeletedFilter : Window, IExternalCommand
    {
        private Application _app;
        private Document _doc;

        public ICollection<Element> filtersCollection = new List<Element>();
        
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {

            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

           
            _doc = uiDoc.Document;

            filtersCollection = GetAllFilters();
            

            ViewWPF vpViewWpf = new ViewWPF(filtersCollection);

            vpViewWpf.ShowDialog();

            var checkFilters = vpViewWpf.checkFilters;

            using (var t = new Transaction(_doc))
            {
                t.Start("Run");
                uiApp = commandData.Application;
                uiDoc = uiApp.ActiveUIDocument;

                ViewFiltersDelete(_doc, checkFilters);
                t.Commit();

            }


            return Result.Succeeded;
        }


        public void ViewFiltersDelete(Document doc, List<Element> checkFilters)
        {
            ArrayList oViewFiltersToDelete = new ArrayList();

            FilteredElementCollector collector = new FilteredElementCollector(doc);
            FilteredElementIterator itor = collector.OfClass(typeof(FilterElement)).GetElementIterator();
            itor.Reset();

            // Iterate through each object found
            while (itor.MoveNext())
            {
                // Get the object
                FilterElement imp = itor.Current as FilterElement;

                foreach (var VARIABLE in checkFilters)
                {
                    if (imp.Name == VARIABLE.Name)
                    {
                        oViewFiltersToDelete.Add(imp.Id);
                    }

                }

            }

            foreach (ElementId oElemID in oViewFiltersToDelete)
            {
                try
                {
                    doc.Delete(oElemID);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        public ICollection<Element> GetAllFilters()
        {
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            FilteredElementIterator itor = collector.OfClass(typeof(FilterElement)).GetElementIterator();
            itor.Reset();

            while (itor.MoveNext())
            {
                // Get the object
                FilterElement imp = itor.Current as FilterElement;

                filtersCollection.Add(imp);
            }

            return filtersCollection;
        }
    }
}
