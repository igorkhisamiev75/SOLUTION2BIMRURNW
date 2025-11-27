#region Namespace

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

#endregion

namespace RevitAddIn2BIMRU.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class CreateLightingInRoom : IExternalCommand
    {
        private Application _app;
        private Document _doc;
        public ElementId linkDocId = new ElementId(BuiltInCategory.OST_RvtLinks);
        public List<ElementId> roomids = new List<ElementId>();
        public List<Room> projectRooms = new List<Room>();
        int countlightComplite = 0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            //_app = uiApp.Application;
            _doc = uiDoc.Document;


            using (var t = new Transaction(_doc))
            {
                t.Start("Create");

                CreateLightingFixtreInRooms();
                CreateLightingInLinkedFiles();

                TaskDialog.Show("Complite!", $"Размещено {countlightComplite} светильника");
                t.Commit();

            }

            return Result.Succeeded;
        }

        private void CreateLightingFixtreInRooms()
        {
            FamilySymbol symbol = GetLightingFixtures();

            var Rooms = GetAllRooms();

            if (Rooms != null)
            {
                int i = 0;
                foreach (var room in Rooms)
                {
                    var areaRoom = room.get_Parameter(BuiltInParameter.ROOM_AREA).AsDouble();
                    var volumeRoom = room.get_Parameter(BuiltInParameter.ROOM_VOLUME).AsDouble();
                    var heighRoom = volumeRoom / areaRoom;

                    var level = room.LevelId;

                    Level level1 = GetLevelFromRoomById(level);

                    LocationPoint lpPoint = (LocationPoint)room.Location;

                    XYZ pointFoLigh = lpPoint.Point;

                    XYZ placeXyzPoint = new XYZ(pointFoLigh.X, pointFoLigh.Y, heighRoom);

                    FamilyInstance instLight = _doc.Create.NewFamilyInstance(
                            placeXyzPoint, symbol, level1, StructuralType.NonStructural);
                    i++;
                }

                countlightComplite = i;
            }

        }

        private void CreateLightingInLinkedFiles()
        {
            //List<Room> roomsLink = null;
            //Transform transform=null;

            FamilySymbol symbol = GetLightingFixtures(); //светильник для размещения

            IEnumerable<RevitLinkInstance> linkedInstances = FindLinkedInstances(); //список линкованных файлов

            if (linkedInstances != null)
            {
                int j = 0;
                foreach (var linkInstance in linkedInstances)
                {
                    Document linkDocument = linkInstance.GetLinkDocument();
                    var transform = linkInstance.GetTotalTransform();
                    //var targetPoint = transform.OfPoint(pointPF);

                    //create room's from linked files
                    var roomsLink = GetAllRoomsLinked(linkDocument);

                    if (roomsLink != null)
                    {
                        foreach (var room in roomsLink)
                        {

                            var areaRoom = room.get_Parameter(BuiltInParameter.ROOM_AREA).AsDouble();
                            var volumeRoom = room.get_Parameter(BuiltInParameter.ROOM_VOLUME).AsDouble();
                            var heightRoom = volumeRoom / areaRoom;

                            if (areaRoom != 0)
                            {
                                var level = room.LevelId;

                                Level level1 = GetLevelFromRoomById(level);

                                if (level1 == null)
                                {
                                    level1 = GetLevelFromRoomByName(room);
                                }

                                LocationPoint lpPoint = (LocationPoint)room.Location;

                                XYZ pointFoLigh = lpPoint.Point;

                                XYZ targetPoint = transform.OfPoint(pointFoLigh);

                                XYZ placeXyzPoint = new XYZ(targetPoint.X, targetPoint.Y, heightRoom);

                                FamilyInstance instLight = _doc.Create.NewFamilyInstance(
                                        placeXyzPoint, symbol, level1, StructuralType.NonStructural);
                                j++;
                            }

                        }
                    }

                }

                countlightComplite += j;
            }

        }

        private FamilySymbol GetLightingFixtures()
        {
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            List<ElementId> _added_element_ids = new List<ElementId>();
            collector.OfCategory(BuiltInCategory.OST_LightingFixtures);
            collector.OfClass(typeof(FamilySymbol));

            FamilySymbol symbol = collector.FirstElement() as FamilySymbol;
            return symbol;
        }

        public Level GetLevelFromRoomById(ElementId elementId)
        {
            FilteredElementCollector lvlCollector = new FilteredElementCollector(_doc);
            ICollection<Element> lvlCollection = lvlCollector.OfClass(typeof(Level)).ToElements();

            foreach (Element l in lvlCollection)
            {
                if (l.Id == elementId)
                {
                    return (Level)l;
                }

            }
            return null;

        }

        public Level GetLevelFromRoomByName(Room room)
        {
            FilteredElementCollector lvlCollector = new FilteredElementCollector(_doc);
            ICollection<Element> lvlCollection = lvlCollector.OfClass(typeof(Level)).ToElements();

            foreach (Element l in lvlCollection)
            {
                if (l.Name == room.Level.Name)
                {
                    return (Level)l;
                }

            }
            return null;

        }


        public IList<Element> GetAllRooms()
        {
            // List all Room's
            var room_collector = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();
            IList<ElementId> room_eids = room_collector.ToElementIds() as IList<ElementId>;
            var allRooms = room_collector.ToElements();

            return allRooms;
        }

        public List<Room> GetAllRoomsLinked(Document document)
        {
            FilteredElementCollector roomCollect = new FilteredElementCollector(document);
            roomCollect.OfCategory(BuiltInCategory.OST_Rooms);
            Room room = null;
            projectRooms = new List<Room>();

            foreach (Element elem in roomCollect)
            {
                room = elem as Room;
                if (room != null)
                {
                    projectRooms.Add(room);
                }
            }

            return projectRooms;
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