#region Namespace

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;

#endregion

namespace RevitAddIn2BIMRU.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class SumAllLightingFixtureInRoom : IExternalCommand
    {
        private Application _app;
        private Document _doc;
        public ElementId linkDocId = new ElementId(BuiltInCategory.OST_RvtLinks);
        public List<ElementId> roomids = new List<ElementId>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

            _doc = uiDoc.Document;

            using (var t = new Transaction(_doc))
            {
                t.Start("Select all lighting fixtures");

                IList<Element> sElements = GetAllElements();

                WriteNameRoomInElementMethod(uiApp, sElements);
                SumAllLightningFixInRoom();

                TaskDialog.Show("Выполнено", "Чисто Мишане за 10-ку обновил плагин ");

                t.Commit();

            }

            return Result.Succeeded;
        }

        public void WriteNameRoomInElementMethod(UIApplication uiApp, IList<Element> sElements) //проставляем помещения в светильники
        {
          


            foreach (var e in sElements)
            {
                Space myRoom;
                var nameRoom = "";
                var familyInstance = (FamilyInstance)e;

                if (familyInstance.HasSpatialElementCalculationPoint) // if calculation point == true
                {
                    XYZ scp = familyInstance.GetSpatialElementCalculationPoint();

                    myRoom = FindSpace(_doc, scp);

                    if (myRoom != null)
                    {
                        nameRoom = myRoom.get_Parameter(BuiltInParameter.SPACE_ASSOC_ROOM_NUMBER).AsString() + " " +
                                   myRoom.get_Parameter(BuiltInParameter.SPACE_ASSOC_ROOM_NAME).AsString();

                        e.get_Parameter(new Guid("c78f0a7d-b68b-4d21-a247-1c8c6ced8bc5"))
                            .Set(nameRoom);

                    }

        

                }

                else
                {
                    var loc = e.Location;
                    var locPoint = (LocationPoint)loc;
                    var pointPF = locPoint.Point;

                    myRoom = FindSpace(_doc, pointPF);
                    if (myRoom != null)
                    {
                        nameRoom = myRoom.get_Parameter(BuiltInParameter.SPACE_ASSOC_ROOM_NUMBER).AsString() + " " +
                                  myRoom.get_Parameter(BuiltInParameter.SPACE_ASSOC_ROOM_NAME).AsString();

                        e.get_Parameter(new Guid("c78f0a7d-b68b-4d21-a247-1c8c6ced8bc5"))
                            .Set(nameRoom);
                    }

               
                }


            }
        }

        public void SumAllLightningFixInRoom() // sum and set count lightning
        {
            //все светильники в которые прописали ADSK_Зона bd940efd-bae8-43c6-a839-454b44ff6baa

            var listLightFixtturel = GetAllElements();
            var sharedGUIDZONA = "c78f0a7d-b68b-4d21-a247-1c8c6ced8bc5";

            //число светильников
            var sharedGUIDCOUNTLIGHTFIX = "bd940efd-bae8-43c6-a839-454b44ff6baa";

            foreach (var svet in listLightFixtturel)
            {
                var countSvet = 0;

                foreach (var element in listLightFixtturel)
                    if (svet.get_Parameter(new Guid(sharedGUIDZONA)).AsString() ==
                        element.get_Parameter(new Guid(sharedGUIDZONA)).AsString() &&
                        getFamilyName(svet) == getFamilyName(element))
                        countSvet++;

                svet.get_Parameter(new Guid(sharedGUIDCOUNTLIGHTFIX))
                    .Set(countSvet.ToString());
            }
        }

        public string getFamilyName(Element e)
        {
            var sharedADSKNaimenovanie = "e6e0f5cd-3e26-485b-9342-23882b20eb43";
            var sharedADSKNaimenovanie2 = "f194bf60-b880-4217-b793-1e0c30dda5e9";

          

            ElementType type = _doc.GetElement(e.GetTypeId()) as ElementType;

            var nameADSKNaimenovanie = type.get_Parameter(new Guid(sharedADSKNaimenovanie2)).AsString();

            return nameADSKNaimenovanie;
        }

        public IList<Element> GetAllElements()
        {
            // List all lighting fixtures
            var lightingFixCollector = new FilteredElementCollector(_doc).OfClass(typeof(FamilyInstance));
            lightingFixCollector.OfCategory(BuiltInCategory.OST_LightingFixtures);
            var plFixList = lightingFixCollector.ToElements();

            return plFixList;
        }


        private static Space FindSpace(Document document, XYZ point)
        {
            return document
                .Phases
                .Cast<Phase>()
                .Select(x => document.GetSpaceAtPoint(point, x))
                .FirstOrDefault(x => x != null);

        }

    }
}