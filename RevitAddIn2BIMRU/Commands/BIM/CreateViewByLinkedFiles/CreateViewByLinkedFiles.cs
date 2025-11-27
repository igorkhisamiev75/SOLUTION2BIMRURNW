using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;


namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    class CreateViewByLinkedFiles : IExternalCommand
    {
       
        private Document _doc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
           
            _doc = uiDoc.Document;
           

            // get a ViewFamilyType for a 3D View
            ViewFamilyType viewFamilyType =
                (from v in new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                 where v.ViewFamily == ViewFamily.ThreeDimensional
                 select v).First();

            //find the linked files
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            ICollection<ElementId> elementIdSet = collector.OfCategory(BuiltInCategory.OST_RvtLinks)
              .OfClass(typeof(RevitLinkInstance)).ToElementIds();

          

            List<ElementId> elementIds = new List<ElementId>();

            Category sectionsCate = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_Levels); //категория для скрытия элементов

            using (Transaction t = new Transaction(_doc, "Create view "))
            {
                IEnumerable<RevitLinkInstance> linkedInstances = FindLinkedInstances();

                foreach (RevitLinkInstance linkedInstance in linkedInstances)
                {
                    var idd=linkedInstance.Id;

                    elementIds.Add(idd);
                }
              

                // loop through all levels
                foreach (RevitLinkInstance linkFile in linkedInstances)
                {
                    t.Start();



                    try
                    {

                        // Create the 3d view
                        View3D view = View3D.CreateIsometric(_doc, viewFamilyType.Id);

                        // Set the name of the view
                        view.Name = "3D View " + linkFile.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM).AsValueString();

                        ElementId notForDel = linkFile.GetTypeId();

                        List<ElementId> elementIds2 = new List<ElementId>();

                        foreach (RevitLinkInstance notForDel3 in linkedInstances)
                        {

                            if(notForDel != notForDel3.GetTypeId())
                            {
                                elementIds2.Add(notForDel3.Id);
                            }
                            
                        }

                        view.HideElements(elementIds2);
                        
                        view.DetailLevel = ViewDetailLevel.Fine;
                        view.DisplayStyle = DisplayStyle.FlatColors;
                        view.SetCategoryHidden(sectionsCate.Id, true);

                        elementIds2.Clear();


                        // Set the name of the transaction
                        // A transaction can be renamed after it has been started
                        t.SetName("Create view " + view.Name);

                    }

                    catch
                    {

                    }
                   
                    t.Commit();

                }

                return Result.Succeeded;

            }
        }

        private IEnumerable<RevitLinkInstance> FindLinkedInstances()
        {
            var collector = new FilteredElementCollector(_doc);
            return collector
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();
        }
    }
}

