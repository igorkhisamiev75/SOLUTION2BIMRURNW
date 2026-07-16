using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAddIn2BIMRU.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CountFinishFloor : IExternalCommand
    {
        public List<Floor> AllFinishFloor = new List<Floor>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            using (var t = new Transaction(doc))
            {
                t.Start("Записываем суммарную площадь отделки");

                WriteToRoomsNull(doc); // Очищаем параметры в помещениях

                var allFloorElements2 = GetAllWFloorElements(doc);

                #region Floor

                if (allFloorElements2.Count > 0)
                {
                    // Записываем суммарную площадь
                    WriteToFloorsSquare(doc);

                    // Записываем из типа в параметр экземпляра
                    WriteFinishToFloors(doc);

                    // Объединяем отделку и площадь где это нужно
                    WriteToFloorsFinishAlliance(doc);

                    doc.Regenerate();

                    // Записываем информацию в помещения
                    WriteToRoomsFloorsSquare(doc);

                    TaskDialog.Show("Сделано", "Отделка посчитана для полов");
                }
                #endregion

                t.Commit();
            }
            return Result.Succeeded;
        }

        /// <summary>
        /// Получить все отделочные перекрытия
        /// </summary>
        private List<Floor> GetFinishFloors(Document doc)
        {
            var allFloors = GetAllWFloorElements(doc);
            var finishFloors = new List<Floor>();

            foreach (var element in allFloors)
            {
                try
                {
                    var floor = (Floor)element;
                    var descriptionType = floor.FloorType.LookupParameter("2BIMRU_Тип_ОТДЕЛКА").AsString();
                    if (descriptionType == "Отделка")
                    {
                        finishFloors.Add(floor);
                    }
                }
                catch
                {
                    continue;
                }
            }

            return finishFloors;
        }

        private void WriteToRoomsNull(Document doc)
        {
            // Все помещения
            var allRooms = GetAllRooms(doc);

            if (allRooms != null)
            {
                foreach (var room in allRooms)
                {
                    // Очищаем параметры отделки полов в помещениях
                    room.get_Parameter(ParameterGuids.FloorFinishCombined).Set("Нет отделки");
                    room.get_Parameter(ParameterGuids.FloorAreaCombined).Set(" ");
                }
            }
        }

        public IList<Element> GetAllRooms(Document doc)
        {
            // Собираем все помещения
            var room_collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            return room_collector.ToElements();
        }

        public void WriteToFloorsSquare(Document doc)
        {
            AllFinishFloor = GetFinishFloors(doc);

            // Группируем по помещениям
            IEnumerable<IGrouping<string, Floor>> groups = AllFinishFloor.GroupBy(p2 =>
                p2.get_Parameter(ParameterGuids.RoomNumber).AsString());

            foreach (var group in groups)
            {
                List<Floor> floorGroup = group.ToList();

                // Группировка по типу (названию)
                var groupsN = floorGroup.GroupBy(p => p.Name);

                // Проходимся по сгруппированным значениям
                foreach (IGrouping<string, Floor> element in groupsN)
                {
                    // Суммируем площадь
                    double squareFloor = 0;

                    foreach (var t in element)
                    {
                        var area = t.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble();
                        squareFloor += area;
                    }

                    foreach (var t in element)
                    {
                        t.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).Set(squareFloor);
                    }
                }
                doc.Regenerate();
            }
        }

        public void WriteFinishToFloors(Document doc)
        {
            AllFinishFloor = GetFinishFloors(doc);

            foreach (var floor in AllFinishFloor)
            {
                var nameOfBaseFinish = floor.FloorType.LookupParameter("2BIMRU_СлоиОтделки").AsString();
                floor.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).Set(nameOfBaseFinish);
            }
        }

        public void WriteToFloorsFinishAlliance(Document doc)
        {
            AllFinishFloor = GetFinishFloors(doc);

            // Группируем по помещениям
            IEnumerable<IGrouping<string, Floor>> groupByRooms = AllFinishFloor.GroupBy(p2 =>
                p2.get_Parameter(ParameterGuids.RoomNumber).AsString());

            foreach (var grouping in groupByRooms)
            {
                string pirog = "";
                string square = "";

                // Группируем по типу перекрытия
                IEnumerable<IGrouping<string, Floor>> groupByType = grouping.GroupBy(p2 => p2.Name);

                if (groupByType.Count() == 1)
                {
                    // Если в помещении только один тип отделки
                    foreach (var floor in grouping)
                    {
                        pirog = floor.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString();
                        square = floor.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).AsValueString();

                        floor.get_Parameter(ParameterGuids.FloorFinishCombined).Set(pirog);
                        floor.get_Parameter(ParameterGuids.FloorAreaCombined).Set(square);
                    }
                }
                else
                {
                    // Если в помещении несколько типов отделки
                    List<Floor> floorList = grouping.ToList();

                    // Удаляем дубликаты, группируем по имени
                    var wallsGroup = floorList.GroupBy(x => x.Name)
                        .Select(x => x.First()).ToList();

                    pirog = "";
                    square = "";

                    foreach (var floor in wallsGroup)
                    {
                        // Получаем количество слоев в отделке
                        var layerInFloor = floor.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString();
                        int countLayerInFloor = layerInFloor.Count(x => x == '\n');

                        pirog += floor.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString() + "\n\n";
                        square += floor.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).AsValueString();

                        for (int i = 0; i < countLayerInFloor; i++)
                        {
                            square += "\n\n";
                        }
                    }

                    foreach (var floor in grouping)
                    {
                        floor.get_Parameter(ParameterGuids.FloorFinishCombined).Set(pirog);
                        floor.get_Parameter(ParameterGuids.FloorAreaCombined).Set(square);
                    }
                }
            }
        }

        private void WriteToRoomsFloorsSquare(Document doc)
        {
            // Получаем все помещения
            var allRooms = GetAllRooms(doc);

            // Получаем все отделочные перекрытия
            AllFinishFloor = GetFinishFloors(doc);

            if (allRooms != null && AllFinishFloor.Count > 0)
            {
                foreach (var room in allRooms)
                {
                    var numberRoom = room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();

                    // Ищем перекрытие, соответствующее текущему помещению
                    foreach (var floor in AllFinishFloor)
                    {
                        var numberRoomInFloor = floor.get_Parameter(ParameterGuids.RoomNumber).AsString();

                        if (numberRoom == numberRoomInFloor)
                        {
                            // Получаем объединенную площадь и отделку
                            var squareFloor = floor.get_Parameter(ParameterGuids.FloorAreaCombined).AsString();
                            var finishFloor = floor.get_Parameter(ParameterGuids.FloorFinishCombined).AsString();

                            // Записываем в помещение
                            room.get_Parameter(ParameterGuids.FloorAreaCombined).Set(squareFloor);
                            room.get_Parameter(ParameterGuids.FloorFinishCombined).Set(finishFloor);

                            break; // Нашли нужное перекрытие, выходим из цикла
                        }
                    }
                }
            }
        }

        public IList<Element> GetAllWFloorElements(Document document)
        {
            // Собираем все перекрытия
            var floor_collector = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType();

            return floor_collector.ToElements();
        }
    }
}