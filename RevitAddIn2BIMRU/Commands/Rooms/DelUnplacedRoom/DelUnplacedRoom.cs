#region Namespace
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

#endregion

namespace RevitAddIn2BIMRU.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DelUnplacedRoom : IExternalCommand

    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)

        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            IList<Element> rooms =GetAllRooms(doc);

            using (var t = new Transaction(doc))
            {
                t.Start("Удалить неразмещенные помещения");


                try
                {
                    // del rooms

                    int k = 0;

                    foreach (var room in rooms)
                    {
                        double roomSquare = room.get_Parameter(BuiltInParameter.ROOM_AREA).AsDouble();

                        if(roomSquare <= 0)
                        {
                            k++;
                            doc.Delete(room.Id);
                        }

                       
                    }

                    TaskDialog.Show("2bim", $"Чистый проект-Чистая совесть! \n Удалено {k} ненужных помещений");
                    t.Commit();
                    return Result.Succeeded;

                }



                catch (Exception ex)
                {
                    TaskDialog.Show("Revit", ex.Message);
                    return Result.Failed;
                }

            }

        }

        public IList<Element> GetAllRooms(Document doc)
        {
            // List all Room's
            var room_collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();
            IList<ElementId> room_eids = room_collector.ToElementIds() as IList<ElementId>;
            var allRooms = room_collector.ToElements();

            return allRooms;
        }

    }
}