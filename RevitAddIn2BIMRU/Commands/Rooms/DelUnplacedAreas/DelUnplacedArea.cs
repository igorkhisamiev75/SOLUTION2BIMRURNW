#region Namespace
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

#endregion

namespace RevitAddIn2BIMRU.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DelUnplacedAreas : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            IList<Element> areas = GetAllAreas(doc);

            using (var t = new Transaction(doc))
            {
                t.Start("Del unplaced Areas");

                try
                {
                    // delete unplaced areas
                    int k = 0;

                    foreach (var area in areas)
                    {
                        double areaValue = area.get_Parameter(BuiltInParameter.ROOM_AREA).AsDouble();

                        if (areaValue <= 0)
                        {
                            k++;
                            doc.Delete(area.Id);
                        }
                    }

                    t.Commit();

                    TaskDialog.Show("Revit", $"Удалено неразмещенных зон: {k}");
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Revit", ex.Message);
                    return Result.Failed;
                }
            }
        }

        public IList<Element> GetAllAreas(Document doc)
        {
            // List all Area's
            var area_collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType();
            IList<Element> allAreas = area_collector.ToElements();

            return allAreas;
        }
    }
}