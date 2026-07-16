using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAddIn2BIMRU.Commands
{
    // Класс для хранения всех GUID параметров
    
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CountFinishWall : IExternalCommand
    {
        public List<Wall> AllFinishWall = new List<Wall>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            using (var t = new Transaction(doc))
            {
                t.Start("Записываем суммарную площадь отделки");

                WriteToRoomsNull(doc);

                var allWallElements2 = GetAllWallElements(doc);

                #region Walls
                if (allWallElements2.Count > 0)
                {
                    WriteToWallSquare(doc);
                    WriteFinishToWall(doc);
                    WriteToWallFinishAlliance(doc);
                    WriteToRooms(doc);
                    TaskDialog.Show("Сделано", "Отделка посчитана для стен");
                }
                #endregion

                t.Commit();
            }
            return Result.Succeeded;
        }

        public void WriteToWallSquare(Document doc)
        {
            var allWallElements = GetAllWallElements(doc);
            AllFinishWall.Clear();

            foreach (var element in allWallElements)
            {
                try
                {
                    var wall = (Wall)element;
                    var descriptionWallType = wall.WallType.LookupParameter("2BIMRU_Тип_ОТДЕЛКА").AsString();

                    if (descriptionWallType == "Отделка")
                    {
                        AllFinishWall.Add(wall);
                    }
                }
                catch (Exception e)
                {
                    continue;
                }
            }

            IEnumerable<IGrouping<string, Wall>> groups = AllFinishWall.GroupBy(p2 =>
                p2.get_Parameter(ParameterGuids.RoomNumber).AsString());

            foreach (var group in groups)
            {
                List<Wall> WallGroup = new List<Wall>();
                WallGroup.Clear();

                foreach (Wall elWall in group)
                {
                    WallGroup.Add(elWall);
                }

                IEnumerable<IGrouping<string, Wall>> groupsN = WallGroup.GroupBy(p => p.Name);

                foreach (IGrouping<string, Element> element in groupsN)
                {
                    IList<string> numroomList = new List<string>();
                    numroomList.Clear();

                    double squeWall = 0;

                    foreach (Element t in element)
                    {
                        var otd = t.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble();
                        squeWall += otd;
                    }

                    foreach (var t in element)
                    {
                        t.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).Set(squeWall);
                    }
                }
                doc.Regenerate();
            }
        }

        public void WriteFinishToWall(Document doc)
        {
            var allWallElements = GetAllWallElements(doc);

            foreach (var element in allWallElements)
            {
                try
                {
                    var wall = (Wall)element;
                    var descriptionWallType = wall.WallType.LookupParameter("2BIMRU_Тип_ОТДЕЛКА").AsString();

                    if (descriptionWallType == "Отделка")
                    {
                        AllFinishWall.Add(wall);
                    }
                }
                catch (Exception e)
                {
                    continue;
                }
            }

            foreach (var wall in AllFinishWall)
            {
                var NameOfBaseFinish = wall.WallType.LookupParameter("2BIMRU_СлоиОтделки").AsString();
                wall.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).Set(NameOfBaseFinish);
            }
        }

        public void WriteToWallFinishAlliance(Document doc)
        {
            var allWallElements = GetAllWallElements(doc);

            foreach (var element in allWallElements)
            {
                try
                {
                    var wall = (Wall)element;
                    var descriptionWallType = wall.WallType.LookupParameter("2BIMRU_Тип_ОТДЕЛКА").AsString();

                    if (descriptionWallType == "Отделка")
                    {
                        AllFinishWall.Add(wall);
                    }
                }
                catch (Exception e)
                {
                    continue;
                }
            }

            IEnumerable<IGrouping<string, Wall>> groupByRooms = AllFinishWall.GroupBy(p2 =>
                p2.get_Parameter(ParameterGuids.RoomNumber).AsString());

            foreach (var grouping in groupByRooms)
            {
                string pirog = "";
                string square = "";

                IEnumerable<IGrouping<string, Wall>> groupByType = grouping.GroupBy(p2 => p2.Name);

                if (groupByType.Count() == 1)
                {
                    square = "";
                    Wall firstWall = groupByType.FirstOrDefault().FirstOrDefault();

                    var square2 = firstWall.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).AsValueString();

                    string LayerInWall = firstWall.get_Parameter(ParameterGuids.WallFinishCombined).AsString();
                   
                    int countLayerInWall = LayerInWall != null? LayerInWall.Where
                        (x => "\n".IndexOf(x) != -1).Count(): 0;
                    int countForSquare = countLayerInWall / 2;

                    for (int i = 0; i < countForSquare; i++)
                    {
                        square += " \n";
                    }

                    square = square + square2;

                    foreach (Wall wall in grouping)
                    {
                        pirog = wall.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString();
                        wall.get_Parameter(ParameterGuids.WallFinishCombined).Set(pirog);
                        wall.get_Parameter(ParameterGuids.WallAreaCombined).Set(square);
                    }

                    continue;
                }
                else
                {
                    List<Wall> listWall = new List<Wall>();

                    foreach (Wall wall2 in grouping)
                    {
                        listWall.Add(wall2);
                    }

                    List<Wall> newList = listWall
                        .GroupBy(x => x.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM).AsValueString())
                        .Select(x => x.First())
                        .ToList();

                    List<Wall> newList2 = newList
                        .OrderBy(x => x.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM).AsValueString())
                        .ToList();

                    pirog = "";
                    square = "";

                    foreach (Wall wall in newList2)
                    {
                        //нужно найти сколько слоёв в стене

                        string countProbels = "";
                        string LayerInWall = wall.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString();
                        
                        int countLayerInWall = LayerInWall.Where(x => "\n".IndexOf(x) != -1).Count();
                        int countForSquare = countLayerInWall / 2;

                        if (countForSquare % 2 == 0)
                        {
                            for (int i = 0; i <= countForSquare; i++)
                            {
                                countProbels += " \n";
                            }
                        }

                        if (countForSquare % 2 == 1)
                        {
                            for (int i = 0; i <= countForSquare; i++)
                            {
                                countProbels += " \n";
                            }
                        }

                        pirog += wall.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString() + "\n\n";

                        if (countLayerInWall == 0)
                        {
                            square += wall.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).AsValueString() + "\n\n";
                        }

                        if (countLayerInWall > 0)
                        {
                            square += countProbels + wall.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).AsValueString() + countProbels;
                        }
                    }

                    foreach (var wall in grouping)
                    {
                        wall.get_Parameter(ParameterGuids.WallFinishCombined).Set(pirog);
                        wall.get_Parameter(ParameterGuids.WallAreaCombined).Set(square);
                    }
                }
            }
        }

        private void WriteToRooms(Document doc)
        {
            var allRooms = GetAllRooms(doc);
            var allWallElements = GetAllWallElements(doc);
            AllFinishWall.Clear();

            foreach (var VARIABLE in allWallElements)
            {
                try
                {
                    var wall = (Wall)VARIABLE;
                    var descriptionWallType = wall.WallType.LookupParameter("2BIMRU_Тип_ОТДЕЛКА").AsString();

                    if (descriptionWallType == "Отделка")
                    {
                        AllFinishWall.Add(wall);
                    }
                }
                catch (Exception e)
                {
                    continue;
                }
            }

            if (allRooms != null)
            {
                foreach (var room in allRooms)
                {
                    var numberRoom = room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();

                    foreach (var wall in AllFinishWall)
                    {
                        var numberRoomInWall = wall.get_Parameter(ParameterGuids.RoomNumber).AsString();

                        if (numberRoom == numberRoomInWall)
                        {
                            var squareWall = wall.get_Parameter(ParameterGuids.WallAreaCombined).AsString();
                            room.get_Parameter(ParameterGuids.WallAreaCombined).Set(squareWall);

                            var finishWall = wall.get_Parameter(ParameterGuids.WallFinishCombined).AsString();
                            room.get_Parameter(ParameterGuids.WallFinishCombined).Set(finishWall);

                            continue;
                        }
                    }
                }
            }
        }

        public IList<Element> GetAllWallElements(Document document)
        {
            var floor_collector = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType();

            IList<Element> allFloors = floor_collector.ToElements();
            return allFloors;
        }

        public IList<Element> GetAllRooms(Document doc)
        {
            var room_collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();
            IList<ElementId> room_eids = room_collector.ToElementIds() as IList<ElementId>;
            var allRooms = room_collector.ToElements();

            return allRooms;
        }

        private void WriteToRoomsNull(Document doc)
        {
            var allRooms = GetAllRooms(doc);

            if (allRooms != null)
            {
                foreach (var room in allRooms)
                {
                    //room.get_Parameter(ParameterGuids.RoomFinishWall).Set("Нет отделки2");
                    room.get_Parameter(ParameterGuids.WallFinishCombined).Set("Нет отделки");
                    room.get_Parameter(ParameterGuids.WallAreaCombined).Set(" ");
                }
            }
        }
    }
}