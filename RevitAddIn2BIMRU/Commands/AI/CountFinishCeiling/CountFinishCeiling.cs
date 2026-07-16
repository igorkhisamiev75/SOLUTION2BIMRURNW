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
    public class CountFinishCeiling : IExternalCommand
    {
        public List<Ceiling> AllFinishListCeiling = new List<Ceiling>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            using (var t = new Transaction(doc))
            {
                t.Start("Записываем суммарную площадь отделки");

                WriteToRoomsNull(doc); // Очищаем параметры в помещениях

                var allCeilingElements2 = GetAllCeilingElements(doc);

                #region Ceiling
                if (allCeilingElements2.Count > 0)
                {
                    // Записываем суммарную площадь
                    WriteToCeilingSquare(doc);
                    doc.Regenerate();
                    // Записываем из типа в параметр экземпляра
                    WriteFinishToCeiling(doc);
                    doc.Regenerate();

                    // Объединяем отделку и площадь где это нужно
                    WriteToCeilingFinishAlliance(doc);
                    doc.Regenerate();

                    // Записываем информацию в помещения
                    WriteToRoomsCeilingSquare(doc);

                    TaskDialog.Show("Сделано ", "Отделка посчитана для потолков");
                }
                #endregion

                t.Commit();
            }
            return Result.Succeeded;
        }

        /// <summary>
        /// Получить все отделочные потолки
        /// </summary>
        private List<Ceiling> GetFinishCeilings(Document doc)
        {
            var allCeilings = GetAllCeilingElements(doc);
            var finishCeilings = new List<Ceiling>();

            foreach (var element in allCeilings)
            {
                try
                {
                    var ceiling = (Ceiling)element;
                    var typeGetCeiling = doc.GetElement(ceiling.GetTypeId());
                    var descriptionCeilingType = typeGetCeiling.LookupParameter("2BIMRU_Тип_ОТДЕЛКА").AsString();

                    if (descriptionCeilingType == "Отделка")
                    {
                        finishCeilings.Add(ceiling);
                    }
                }
                catch
                {
                    continue;
                }
            }

            return finishCeilings;
        }

        private void WriteToRoomsNull(Document doc)
        {
            // Все помещения
            var allRooms = GetAllRooms(doc);

            if (allRooms != null)
            {
                foreach (var room in allRooms)
                {
                    // Очищаем параметры отделки потолков в помещениях
                    room.get_Parameter(ParameterGuids.CellingFinishCombined).Set("Нет отделки");
                    room.get_Parameter(ParameterGuids.CellingAreaCombined).Set(" ");
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

        public void WriteToCeilingSquare(Document doc)
        {
            AllFinishListCeiling = GetFinishCeilings(doc);

            // Группируем по помещениям
            IEnumerable<IGrouping<string, Ceiling>> groups = AllFinishListCeiling.GroupBy(p2 =>
                p2.get_Parameter(ParameterGuids.RoomNumber).AsString());

            foreach (var group in groups)
            {
                List<Ceiling> ceilingsGroup = group.ToList();

                // Группировка по типу (названию)
                var groupsN = ceilingsGroup.GroupBy(p => p.Name);

                // Проходимся по сгруппированным значениям
                foreach (IGrouping<string, Ceiling> element in groupsN)
                {
                    // Суммируем площадь
                    double squareCeiling = 0;

                    foreach (var t in element)
                    {
                        var area = t.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble();
                        squareCeiling += area;
                    }

                    foreach (var t in element)
                    {
                        t.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).Set(squareCeiling);
                    }
                }
            }
        }

        public IList<Element> GetAllCeilingElements(Document document)
        {
            // Собираем все потолки
            var ceiling_collector = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType();

            return ceiling_collector.ToElements();
        }

        public void WriteFinishToCeiling(Document doc)
        {
            AllFinishListCeiling = GetFinishCeilings(doc);

            foreach (var ceiling in AllFinishListCeiling)
            {
                var typeGetCeiling = doc.GetElement(ceiling.GetTypeId());
                var nameOfBaseFinish = typeGetCeiling.LookupParameter("2BIMRU_СлоиОтделки").AsString();

                ceiling.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).Set(nameOfBaseFinish);
            }
        }

        public void WriteToCeilingFinishAlliance(Document doc)
        {
            AllFinishListCeiling = GetFinishCeilings(doc);

            // Группируем по помещениям
            IEnumerable<IGrouping<string, Ceiling>> groupByRooms = AllFinishListCeiling.GroupBy(p2 =>
                p2.get_Parameter(ParameterGuids.RoomNumber).AsString());

            foreach (var grouping in groupByRooms)
            {
                string pirog = "";
                string square = "";

                // Группируем по типу потолка
                IEnumerable<IGrouping<string, Ceiling>> groupByType = grouping.GroupBy(p2 => p2.Name);

                if (groupByType.Count() == 1)
                {
                    // Если в помещении только один тип отделки
                    foreach (var ceiling in grouping)
                    {
                        pirog = ceiling.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString();
                        square = ceiling.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).AsValueString();

                        ceiling.get_Parameter(ParameterGuids.CellingFinishCombined).Set(pirog);
                        ceiling.get_Parameter(ParameterGuids.CellingAreaCombined).Set(square);
                    }

                    continue;
                }
                else
                {
                    // Если в помещении несколько типов отделки
                    List<Ceiling> ceilings = grouping.ToList();

                    // Удаляем дубликаты, группируем по имени
                    var ceilingsGroup = ceilings.GroupBy(x => x.Name)
                        .Select(x => x.First()).ToList();

                    pirog = "";
                    square = "";

                    foreach (var ceiling in ceilingsGroup)
                    {
                        // Получаем количество слоев в отделке
                        var layerInCeiling = ceiling.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString();
                        int countLayerInCeiling = layerInCeiling.Count(x => x == '\n');

                        pirog += ceiling.get_Parameter(ParameterGuids.WallFloorCellingFinishFromType).AsString() + "\n\n";
                        square += ceiling.get_Parameter(ParameterGuids.WallFloorCellingFinishArea).AsValueString();

                        for (int i = 0; i < countLayerInCeiling; i++)
                        {
                            square += "\n\n";
                        }
                    }

                    foreach (var ceiling in grouping)
                    {
                        ceiling.get_Parameter(ParameterGuids.CellingFinishCombined).Set(pirog);
                        ceiling.get_Parameter(ParameterGuids.CellingAreaCombined).Set(square);
                    }
                }
            }
        }

        private void WriteToRoomsCeilingSquare(Document doc)
        {
            // Получаем все помещения
            var allRooms = GetAllRooms(doc);

            // Получаем все отделочные потолки
            AllFinishListCeiling = GetFinishCeilings(doc);

            if (allRooms != null && AllFinishListCeiling.Count > 0)
            {
                foreach (var room in allRooms)
                {
                    var numberRoom = room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();

                    // Ищем потолок, соответствующий текущему помещению
                    foreach (var ceiling in AllFinishListCeiling)
                    {
                        var numberRoomInCeiling = ceiling.get_Parameter(ParameterGuids.RoomNumber).AsString();

                        if (numberRoom == numberRoomInCeiling)
                        {
                            // Получаем объединенную площадь и отделку
                            var squareCeiling = ceiling.get_Parameter(ParameterGuids.CellingAreaCombined).AsString();
                            var finishCeiling = ceiling.get_Parameter(ParameterGuids.CellingFinishCombined).AsString();

                            // Записываем в помещение
                            room.get_Parameter(ParameterGuids.CellingAreaCombined).Set(squareCeiling);
                            room.get_Parameter(ParameterGuids.CellingFinishCombined).Set(finishCeiling);

                            break; // Нашли нужный потолок, выходим из цикла
                        }
                    }
                }
            }
        }
    }
}