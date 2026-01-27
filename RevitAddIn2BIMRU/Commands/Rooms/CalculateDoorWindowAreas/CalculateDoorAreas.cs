#region Namespaces
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

using OperationCanceledException = Autodesk.Revit.Exceptions.OperationCanceledException;
#endregion

namespace RevitAddIn2BIMRU.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CalculateDoorWindowAreas : IExternalCommand
    {
        // Имена параметров для помещений
        private const string PARAM_DOOR_AREA = "ПлощадьПроемов_Двери";
        private const string PARAM_WINDOW_AREA = "ПлощадьПроемов_Окна";
        private const string PARAM_PARTITION_AREA = "ПлощадьПроемов_РазделителиПомещений";
        private const string PARAM_CURTAIN_AREA = "ПлощадьПроемов_Витражи";

        // GUID параметров из файла ФОП_v1.txt
        private readonly Dictionary<string, string> parameterGuids = new Dictionary<string, string>()
        {
            { PARAM_DOOR_AREA, "738a3f70-6719-4208-aa05-220a0473f6cb" },
            { PARAM_WINDOW_AREA, "f68ac920-41ca-4c34-a7b5-92113f55cac8" },
            { PARAM_PARTITION_AREA, "208be939-133a-4d21-81ff-d55e8ac6a644" },
            { PARAM_CURTAIN_AREA, "47bc1af3-9fcf-46a6-9e4e-2505f33ecf5b" }
        };

        private Document doc;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            doc = uidoc.Document;

            try
            {
                // Проверяем наличие общих параметров в проекте
                bool parametersExist = CheckParametersExist();

                if (!parametersExist)
                {
                    TaskDialogResult dialogResult = TaskDialog.Show("Параметры не найдены",
                        "Не найдены необходимые общие параметры в проекте.\n\n" +
                        "Возможные причины:\n" +
                        "1. Файл общих параметров ФОП_v1.txt не подключен к проекту\n" +
                        "2. Параметры не привязаны к категории 'Помещения'\n\n" +
                        "Создать параметры?",
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                    if (dialogResult == TaskDialogResult.Yes)
                    {
                        if (!CreateSharedParametersInProject())
                        {
                            TaskDialog.Show("Ошибка", "Не удалось создать параметры.");
                            return Result.Cancelled;
                        }
                    }
                    else
                    {
                        TaskDialog.Show("Информация", "Расчет площадей отменен.");
                        return Result.Cancelled;
                    }
                }

                parametersExist = CheckParametersExist();
                if (!parametersExist)
                {
                    TaskDialog.Show("Ошибка", "Параметры не найдены. Завершение работы.");
                    return Result.Cancelled;
                }

                using (Transaction trans = new Transaction(doc, "Расчет площадей проемов"))
                {
                    trans.Start();

                    // 1. Собираем все помещения
                    List<Room> rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .ToList();

                    if (rooms.Count == 0)
                    {
                        TaskDialog.Show("Информация", "В проекте не найдено помещений.");
                        return Result.Cancelled;
                    }

                    // 2. Собираем все двери
                    List<FamilyInstance> doors = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .ToList();

                    // 3. Собираем все окна
                    List<FamilyInstance> windows = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Windows)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .ToList();

                    // 4. Собираем все витражи (стены с "Витраж" в имени типа)
                    List<Wall> curtainWalls = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Walls)
                        .WhereElementIsNotElementType()
                        .OfClass(typeof(Wall))
                        .Cast<Wall>()
                        .Where(w => w.WallType != null &&
                                   w.WallType.Name != null &&
                                   w.WallType.Name.IndexOf("Витраж", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                    if (doors.Count == 0 && windows.Count == 0 && curtainWalls.Count == 0)
                    {
                        TaskDialog.Show("Информация", "В проекте не найдено дверей, окон и витражей.");
                        return Result.Cancelled;
                    }

                    // Словари для хранения суммарных площадей по помещениям
                    Dictionary<ElementId, double> roomDoorAreas = new Dictionary<ElementId, double>();
                    Dictionary<ElementId, double> roomWindowAreas = new Dictionary<ElementId, double>();
                    Dictionary<ElementId, double> roomPartitionAreas = new Dictionary<ElementId, double>();
                    Dictionary<ElementId, double> roomCurtainAreas = new Dictionary<ElementId, double>();

                    int processedDoors = 0;
                    int processedWindows = 0;

                    // Обрабатываем двери - используем геометрический подход для определения помещений
                    foreach (FamilyInstance door in doors)
                    {
                        List<Room> doorRooms = FindRoomsForDoor(door, rooms);

                        double doorArea = GetDoorArea(door);

                        if (doorArea > 0.001 && doorRooms.Count > 0)
                        {
                            processedDoors++;

                            // Распределяем площадь между всеми найденными помещениями
                            foreach (Room room in doorRooms)
                            {
                                AddAreaToRoom(roomDoorAreas, room.Id, doorArea);
                            }
                        }
                    }

                    // Обрабатываем окна
                    foreach (FamilyInstance window in windows)
                    {
                        double windowArea = GetWindowArea(window);

                        if (windowArea > 0.001)
                        {
                            processedWindows++;

                            List<Room> windowRooms = FindRoomsForWindow(window, rooms);

                            if (windowRooms.Count > 0)
                            {
                                foreach (Room room in windowRooms)
                                {
                                    AddAreaToRoom(roomWindowAreas, room.Id, windowArea);
                                }
                            }
                        }
                    }

                    // 5. Рассчитываем площади разделителей помещений
                    CalculatePartitionAreas(rooms, roomPartitionAreas);

                    // 6. Рассчитываем площади витражей
                    CalculateCurtainAreas(rooms, curtainWalls, roomCurtainAreas);

                    // 7. Записываем результаты в параметры помещений
                    int roomsWithDoors = 0;
                    int roomsWithWindows = 0;
                    int roomsWithPartitions = 0;
                    int roomsWithCurtains = 0;

                    foreach (Room room in rooms)
                    {
                        bool hasDoorArea = roomDoorAreas.ContainsKey(room.Id);
                        bool hasWindowArea = roomWindowAreas.ContainsKey(room.Id);
                        bool hasPartitionArea = roomPartitionAreas.ContainsKey(room.Id);
                        bool hasCurtainArea = roomCurtainAreas.ContainsKey(room.Id);

                        double totalDoorArea = hasDoorArea ? roomDoorAreas[room.Id] : 0;
                        double totalWindowArea = hasWindowArea ? roomWindowAreas[room.Id] : 0;
                        double totalPartitionArea = hasPartitionArea ? roomPartitionAreas[room.Id] : 0;
                        double totalCurtainArea = hasCurtainArea ? roomCurtainAreas[room.Id] : 0;

                        // Записываем площадь дверей
                        if (SetRoomParameter(room, PARAM_DOOR_AREA, totalDoorArea) && hasDoorArea)
                        {
                            roomsWithDoors++;
                        }

                        // Записываем площадь окон
                        if (SetRoomParameter(room, PARAM_WINDOW_AREA, totalWindowArea) && hasWindowArea)
                        {
                            roomsWithWindows++;
                        }

                        // Записываем площадь разделителей помещений
                        if (SetRoomParameter(room, PARAM_PARTITION_AREA, totalPartitionArea) && hasPartitionArea)
                        {
                            roomsWithPartitions++;
                        }

                        // Записываем площадь витражей
                        if (SetRoomParameter(room, PARAM_CURTAIN_AREA, totalCurtainArea) && hasCurtainArea)
                        {
                            roomsWithCurtains++;
                        }
                    }

                    trans.Commit();

                    TaskDialog.Show("Результаты расчета",
                        $"ОБРАБОТАНО:\n" +
                        $"• Всего помещений: {rooms.Count}\n" +
                        $"• Помещений с дверями: {roomsWithDoors}\n" +
                        $"• Помещений с окнами: {roomsWithWindows}\n" +
                        $"• Помещений с разделителями: {roomsWithPartitions}\n" +
                        $"• Помещений с витражаи: {roomsWithCurtains}\n\n" +
                        $"ДВЕРИ:\n" +
                        $"• Всего дверей: {doors.Count}\n" +
                        $"• Дверей обработано: {processedDoors}\n\n" +
                        $"ОКНА:\n" +
                        $"• Всего окон: {windows.Count}\n" +
                        $"• Окон обработано: {processedWindows}");
                }

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", $"Произошла ошибка:\n{ex.Message}");
                return Result.Failed;
            }
        }

        // Метод для поиска помещений, в которых находится дверь
        private List<Room> FindRoomsForDoor(FamilyInstance door, List<Room> allRooms)
        {
            List<Room> doorRooms = new List<Room>();

            try
            {
                // Метод 1: Используем геометрический подход (аналогично окнам)
                BoundingBoxXYZ doorBbox = door.get_BoundingBox(null);
                if (doorBbox == null) return doorRooms;

                List<XYZ> testPoints = new List<XYZ>();
                XYZ center = (doorBbox.Min + doorBbox.Max) / 2.0;
                testPoints.Add(center);

                // Точки внутри дверного проема
                testPoints.Add(new XYZ(doorBbox.Min.X, doorBbox.Min.Y, center.Z));
                testPoints.Add(new XYZ(doorBbox.Max.X, doorBbox.Min.Y, center.Z));
                testPoints.Add(new XYZ(doorBbox.Min.X, doorBbox.Max.Y, center.Z));
                testPoints.Add(new XYZ(doorBbox.Max.X, doorBbox.Max.Y, center.Z));

                // Точки посередине
                testPoints.Add(new XYZ(center.X, doorBbox.Min.Y, center.Z));
                testPoints.Add(new XYZ(center.X, doorBbox.Max.Y, center.Z));
                testPoints.Add(new XYZ(doorBbox.Min.X, center.Y, center.Z));
                testPoints.Add(new XYZ(doorBbox.Max.X, center.Y, center.Z));

                // Точки с небольшим смещением
                double offset = 0.05;
                testPoints.Add(new XYZ(center.X + offset, center.Y, center.Z));
                testPoints.Add(new XYZ(center.X - offset, center.Y, center.Z));
                testPoints.Add(new XYZ(center.X, center.Y + offset, center.Z));
                testPoints.Add(new XYZ(center.X, center.Y - offset, center.Z));

                // Проверяем каждую точку
                foreach (XYZ point in testPoints)
                {
                    foreach (Room room in allRooms)
                    {
                        if (room.IsPointInRoom(point) && !doorRooms.Contains(room))
                        {
                            doorRooms.Add(room);
                        }
                    }
                }

                // Метод 2: Если геометрический метод не нашел помещений, пробуем найти через host (стену)
                if (doorRooms.Count == 0 && door.Host != null)
                {
                    Wall hostWall = door.Host as Wall;
                    if (hostWall != null)
                    {
                        doorRooms = GetRoomsAdjacentToWall(hostWall, allRooms);
                    }
                }

                // Метод 3: Если все еще не нашли, используем стандартный Room (для проектов с одной фазой)
                if (doorRooms.Count == 0)
                {
                    try
                    {
                        Room doorRoom = door.Room;
                        if (doorRoom != null && !doorRooms.Contains(doorRoom))
                        {
                            doorRooms.Add(doorRoom);
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибку
                    }
                }
            }
            catch
            {
                // В случае ошибки пробуем стандартный метод
                try
                {
                    Room doorRoom = door.Room;
                    if (doorRoom != null && !doorRooms.Contains(doorRoom))
                    {
                        doorRooms.Add(doorRoom);
                    }
                }
                catch
                {
                    // Игнорируем ошибку
                }
            }

            return doorRooms;
        }

        // Метод для получения помещений, прилегающих к стене
        private List<Room> GetRoomsAdjacentToWall(Wall wall, List<Room> allRooms)
        {
            List<Room> adjacentRooms = new List<Room>();

            try
            {
                // Получаем геометрию стены
                LocationCurve locationCurve = wall.Location as LocationCurve;
                if (locationCurve == null) return adjacentRooms;

                Curve curve = locationCurve.Curve;
                XYZ startPoint = curve.GetEndPoint(0);
                XYZ endPoint = curve.GetEndPoint(1);
                XYZ midPoint = (startPoint + endPoint) / 2.0;

                // Получаем направление стены
                XYZ wallDirection = (endPoint - startPoint).Normalize();
                XYZ wallNormal = new XYZ(-wallDirection.Y, wallDirection.X, 0).Normalize();

                // Создаем точки с обеих сторон стены
                double offset = 0.1; // 10 см в футах
                XYZ point1 = midPoint + wallNormal * offset;
                XYZ point2 = midPoint - wallNormal * offset;

                // Добавляем дополнительные точки вдоль стены
                List<XYZ> testPoints = new List<XYZ>();
                testPoints.Add(point1);
                testPoints.Add(point2);

                // Точки на четвертях длины стены
                XYZ quarterPoint1 = startPoint + (endPoint - startPoint) * 0.25;
                XYZ quarterPoint2 = startPoint + (endPoint - startPoint) * 0.75;

                testPoints.Add(quarterPoint1 + wallNormal * offset);
                testPoints.Add(quarterPoint1 - wallNormal * offset);
                testPoints.Add(quarterPoint2 + wallNormal * offset);
                testPoints.Add(quarterPoint2 - wallNormal * offset);

                // Проверяем точки на принадлежность помещениям
                foreach (XYZ point in testPoints)
                {
                    foreach (Room room in allRooms)
                    {
                        try
                        {
                            if (room.IsPointInRoom(point) && !adjacentRooms.Contains(room))
                            {
                                adjacentRooms.Add(room);
                            }
                        }
                        catch
                        {
                            // Пропускаем ошибки
                        }
                    }
                }
            }
            catch
            {
                // Пропускаем ошибки
            }

            return adjacentRooms;
        }

        // Метод для проверки наличия общих параметров в проекте
        private bool CheckParametersExist()
        {
            try
            {
                List<Room> testRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Take(1)
                    .Cast<Room>()
                    .ToList();

                if (testRooms.Count == 0)
                {
                    return false;
                }

                Room testRoom = testRooms.First();

                foreach (var paramName in parameterGuids.Keys)
                {
                    Parameter param = testRoom.LookupParameter(paramName);
                    if (param == null)
                    {
                        foreach (Parameter p in testRoom.Parameters)
                        {
                            if (p.Definition.Name == paramName)
                            {
                                param = p;
                                break;
                            }
                        }
                    }

                    if (param == null)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Метод для создания общих параметров в проекте
        private bool CreateSharedParametersInProject()
        {
            try
            {
                DefinitionFile definitionFile = doc.Application.OpenSharedParameterFile();
                if (definitionFile == null)
                {
                    TaskDialog.Show("Файл параметров не подключен",
                        "Файл общих параметров не подключен к проекту.\n\n" +
                        "Пожалуйста:\n" +
                        "1. Перейдите в Управление → Параметры проекта\n" +
                        "2. Во вкладке 'Общие параметры' нажмите 'Выбрать'\n" +
                        "3. Выберите файл ФОП_v1.txt\n" +
                        "4. Запустите команду снова");
                    return false;
                }

                using (Transaction trans = new Transaction(doc, "Добавление параметров к помещениям"))
                {
                    trans.Start();

                    int parametersAdded = 0;
                    Category roomCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms);

                    if (roomCategory == null)
                    {
                        return false;
                    }

                    BindingMap bindingMap = doc.ParameterBindings;
                    List<ExternalDefinition> parametersToAdd = new List<ExternalDefinition>();

                    foreach (DefinitionGroup group in definitionFile.Groups)
                    {
                        foreach (ExternalDefinition definition in group.Definitions)
                        {
                            string paramName = definition.Name;
                            if (parameterGuids.ContainsKey(paramName))
                            {
                                parametersToAdd.Add(definition);
                            }
                        }
                    }

                    if (parametersToAdd.Count == 0)
                    {
                        return false;
                    }

                    foreach (ExternalDefinition definition in parametersToAdd)
                    {
                        string paramName = definition.Name;

                        bool isBound = false;
                        DefinitionBindingMapIterator it = bindingMap.ForwardIterator();
                        while (it.MoveNext())
                        {
                            Definition def = it.Key;
                            if (def.Name == paramName)
                            {
                                isBound = true;
                                break;
                            }
                        }

                        if (!isBound)
                        {
                            try
                            {
                                CategorySet categorySet = new CategorySet();
                                categorySet.Insert(roomCategory);
                                InstanceBinding instanceBinding = new InstanceBinding(categorySet);

                                if (bindingMap.Insert(definition, instanceBinding))
                                {
                                    parametersAdded++;
                                }
                            }
                            catch
                            {
                                // Пропускаем ошибку
                            }
                        }
                    }

                    trans.Commit();

                    if (parametersAdded > 0)
                    {
                        TaskDialog.Show("Успех",
                            $"Успешно добавлено {parametersAdded} параметров к категории помещений.");
                        return true;
                    }
                    else
                    {
                        TaskDialog.Show("Информация",
                            "Все параметры уже привязаны к проекту.");
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        // Метод для расчета площадей витражей
        private void CalculateCurtainAreas(List<Room> rooms, List<Wall> curtainWalls,
                                         Dictionary<ElementId, double> roomCurtainAreas)
        {
            try
            {
                double offsetFeet = UnitUtils.ConvertToInternalUnits(0.2, UnitTypeId.Meters);

                foreach (Room room in rooms)
                {
                    double totalCurtainArea = 0;

                    try
                    {
                        BoundingBoxXYZ roomBbox = room.get_BoundingBox(null);
                        if (roomBbox == null) continue;

                        BoundingBoxXYZ expandedBbox = new BoundingBoxXYZ();
                        expandedBbox.Min = new XYZ(roomBbox.Min.X - offsetFeet, roomBbox.Min.Y - offsetFeet, roomBbox.Min.Z - offsetFeet);
                        expandedBbox.Max = new XYZ(roomBbox.Max.X + offsetFeet, roomBbox.Max.Y + offsetFeet, roomBbox.Max.Z + offsetFeet);

                        foreach (Wall curtainWall in curtainWalls)
                        {
                            BoundingBoxXYZ curtainBbox = curtainWall.get_BoundingBox(null);
                            if (curtainBbox == null) continue;

                            if (BoundingBoxesIntersect(expandedBbox, curtainBbox))
                            {
                                double curtainArea = GetCurtainWallArea(curtainWall);
                                if (curtainArea > 0)
                                {
                                    totalCurtainArea += curtainArea;
                                }
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    if (totalCurtainArea > 0)
                    {
                        AddAreaToRoom(roomCurtainAreas, room.Id, totalCurtainArea);
                    }
                }
            }
            catch
            {
                // Пропускаем ошибку
            }
        }

        // Метод для проверки пересечения BoundingBox
        private bool BoundingBoxesIntersect(BoundingBoxXYZ bbox1, BoundingBoxXYZ bbox2)
        {
            if (bbox1 == null || bbox2 == null) return false;

            return (bbox1.Min.X <= bbox2.Max.X && bbox1.Max.X >= bbox2.Min.X &&
                    bbox1.Min.Y <= bbox2.Max.Y && bbox1.Max.Y >= bbox2.Min.Y &&
                    bbox1.Min.Z <= bbox2.Max.Z && bbox1.Max.Z >= bbox2.Min.Z);
        }

        // Метод для получения площади витража
        private double GetCurtainWallArea(Wall curtainWall)
        {
            try
            {
                Parameter areaParam = curtainWall.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (areaParam != null && areaParam.HasValue)
                {
                    double area = areaParam.AsDouble();
                    area = UnitUtils.ConvertFromInternalUnits(area, UnitTypeId.SquareMeters);

                    if (area > 0 && area < 1000)
                    {
                        return area;
                    }
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // Метод для расчета площадей разделителей помещений
        private void CalculatePartitionAreas(List<Room> rooms,
                                           Dictionary<ElementId, double> roomPartitionAreas)
        {
            try
            {
                SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
                options.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center;

                foreach (Room room in rooms)
                {
                    double totalPartitionArea = 0;
                    double roomHeightMeters = 0;

                    try
                    {
                        roomHeightMeters = GetRoomHeightInMeters(room);

                        if (roomHeightMeters > 0)
                        {
                            IList<IList<BoundarySegment>> boundaries = room.GetBoundarySegments(options);

                            if (boundaries != null)
                            {
                                foreach (IList<BoundarySegment> boundaryLoop in boundaries)
                                {
                                    foreach (BoundarySegment segment in boundaryLoop)
                                    {
                                        Curve curve = segment.GetCurve();
                                        if (curve != null)
                                        {
                                            double segmentLengthMeters = GetCurveLengthInMeters(curve);

                                            if (IsPartitionSegment(segment, room))
                                            {
                                                double segmentArea = segmentLengthMeters * roomHeightMeters;

                                                if (segmentArea > 0 && segmentArea < 1000)
                                                {
                                                    totalPartitionArea += segmentArea;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    if (totalPartitionArea > 0)
                    {
                        AddAreaToRoom(roomPartitionAreas, room.Id, totalPartitionArea);
                    }
                }
            }
            catch
            {
                // Пропускаем ошибку
            }
        }

        // Метод для получения длины кривой в метрах
        private double GetCurveLengthInMeters(Curve curve)
        {
            try
            {
                double lengthFeet = curve.Length;
                double lengthMeters = UnitUtils.ConvertFromInternalUnits(lengthFeet, UnitTypeId.Meters);
                return lengthMeters;
            }
            catch
            {
                return 0;
            }
        }

        // Метод для определения высоты помещения в метрах
        private double GetRoomHeightInMeters(Room room)
        {
            try
            {
                double heightFeet = 0;

                Parameter heightParam = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
                if (heightParam != null && heightParam.HasValue)
                {
                    heightFeet = heightParam.AsDouble();
                }
                else
                {
                    heightParam = room.LookupParameter("Высота");
                    if (heightParam != null && heightParam.HasValue)
                    {
                        heightFeet = heightParam.AsDouble();
                    }
                    else
                    {
                        heightParam = room.LookupParameter("Height");
                        if (heightParam != null && heightParam.HasValue)
                        {
                            heightFeet = heightParam.AsDouble();
                        }
                        else
                        {
                            BoundingBoxXYZ bbox = room.get_BoundingBox(null);
                            if (bbox != null)
                            {
                                heightFeet = bbox.Max.Z - bbox.Min.Z;
                            }
                            else
                            {
                                return 3.0;
                            }
                        }
                    }
                }

                double heightMeters = UnitUtils.ConvertFromInternalUnits(heightFeet, UnitTypeId.Meters);
                return heightMeters;
            }
            catch
            {
                return 3.0;
            }
        }

        // Метод для определения, является ли сегмент разделителем помещений
        private bool IsPartitionSegment(BoundarySegment segment, Room currentRoom)
        {
            try
            {
                Element boundaryElement = doc.GetElement(segment.ElementId);
                if (boundaryElement == null) return false;

                Category category = boundaryElement.Category;
                if (category == null) return false;

                ElementId roomSeparationId = new ElementId(BuiltInCategory.OST_RoomSeparationLines);

                // Универсальный способ для всех версий Revit
                // Способ 1: Прямое сравнение ElementId (работает во всех версиях)
                if (category.Id == roomSeparationId)
                {
                    return true;
                }

                // Способ 2: Через числовое значение с условной компиляцией
#if REVIT2026_OR_GREATER
                // Для Revit 2026 и выше используем Value
                if (category.Id.Value == (long)BuiltInCategory.OST_RoomSeparationLines)
                {
                    return true;
                }
#elif REVIT2020_OR_GREATER
                // Для Revit 2020-2025 используем IntegerValue
                if (category.Id.IntegerValue == (int)BuiltInCategory.OST_RoomSeparationLines)
                {
                    return true;
                }
#else
                // Для более старых версий также используем IntegerValue
                if (category.Id.IntegerValue == (int)BuiltInCategory.OST_RoomSeparationLines)
                {
                    return true;
                }
#endif

                // Способ 3: Проверка через имя категории (резервный вариант)
                string categoryName = category.Name ?? "";
                if (categoryName.Contains("Room Separation") ||
                    categoryName.Contains("Разделитель помещений") ||
                    categoryName.Contains("RoomSeparation"))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        // Вспомогательный метод для получения числового значения ElementId (универсальный)
        private long GetElementIdValue(ElementId elementId)
        {
            if (elementId == null) return -1;

#if REVIT2026_OR_GREATER
            // Для Revit 2026 и выше используем Value
            return elementId.Value;
#elif REVIT2020_OR_GREATER
            // Для Revit 2020-2025 используем IntegerValue
            return elementId.IntegerValue;
#else
            // Для более старых версий также используем IntegerValue
            return elementId.IntegerValue;
#endif
        }

        // Метод для поиска помещений, в которых находится окно
        private List<Room> FindRoomsForWindow(FamilyInstance window, List<Room> allRooms)
        {
            List<Room> windowRooms = new List<Room>();

            try
            {
                BoundingBoxXYZ windowBbox = window.get_BoundingBox(null);
                if (windowBbox == null) return windowRooms;

                List<XYZ> testPoints = new List<XYZ>();
                XYZ center = (windowBbox.Min + windowBbox.Max) / 2.0;
                testPoints.Add(center);

                testPoints.Add(new XYZ(windowBbox.Min.X, windowBbox.Min.Y, center.Z));
                testPoints.Add(new XYZ(windowBbox.Max.X, windowBbox.Min.Y, center.Z));
                testPoints.Add(new XYZ(windowBbox.Min.X, windowBbox.Max.Y, center.Z));
                testPoints.Add(new XYZ(windowBbox.Max.X, windowBbox.Max.Y, center.Z));

                testPoints.Add(new XYZ(center.X, windowBbox.Min.Y, center.Z));
                testPoints.Add(new XYZ(center.X, windowBbox.Max.Y, center.Z));
                testPoints.Add(new XYZ(windowBbox.Min.X, center.Y, center.Z));
                testPoints.Add(new XYZ(windowBbox.Max.X, center.Y, center.Z));

                double offset = 0.05;
                testPoints.Add(new XYZ(center.X + offset, center.Y, center.Z));
                testPoints.Add(new XYZ(center.X - offset, center.Y, center.Z));
                testPoints.Add(new XYZ(center.X, center.Y + offset, center.Z));
                testPoints.Add(new XYZ(center.X, center.Y - offset, center.Z));

                foreach (XYZ point in testPoints)
                {
                    foreach (Room room in allRooms)
                    {
                        if (room.IsPointInRoom(point) && !windowRooms.Contains(room))
                        {
                            windowRooms.Add(room);
                        }
                    }
                }

                if (windowRooms.Count == 0)
                {
                    Room windowRoom = window.Room;
                    if (windowRoom != null && !windowRooms.Contains(windowRoom))
                    {
                        windowRooms.Add(windowRoom);
                    }
                }
            }
            catch
            {
                Room windowRoom = window.Room;
                if (windowRoom != null && !windowRooms.Contains(windowRoom))
                {
                    windowRooms.Add(windowRoom);
                }
            }

            return windowRooms;
        }

        // Метод для записи значения в параметр помещения
        private bool SetRoomParameter(Room room, string paramName, double value)
        {
            try
            {
                if (value <= 0.001)
                {
                    return false;
                }

                Parameter param = room.LookupParameter(paramName);

                if (param == null)
                {
                    foreach (Parameter p in room.Parameters)
                    {
                        if (p.Definition.Name == paramName)
                        {
                            param = p;
                            break;
                        }
                    }
                }

                if (param == null || param.IsReadOnly)
                {
                    return false;
                }

                if (param.StorageType == StorageType.Double)
                {
                    double valueInInternalUnits = UnitUtils.ConvertToInternalUnits(value, UnitTypeId.SquareMeters);
                    return param.Set(valueInInternalUnits);
                }
                else if (param.StorageType == StorageType.String)
                {
                    string stringValue = value.ToString("F3");
                    return param.Set(stringValue);
                }
                else if (param.StorageType == StorageType.Integer)
                {
                    int intValue = (int)Math.Round(value);
                    return param.Set(intValue);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Метод для получения площади двери из параметров типа
        private double GetDoorArea(FamilyInstance door)
        {
            try
            {
                ElementId doorTypeId = door.GetTypeId();
                if (doorTypeId == null || doorTypeId == ElementId.InvalidElementId)
                {
                    return 0;
                }

                Element doorType = doc.GetElement(doorTypeId);
                if (doorType == null) return 0;

                double area = GetAreaFromDirectParameter(doorType, new[] { "Площадь", "Area", "Площадь проема", "Проем", "Площадь двери", "Door Area" });
                if (area > 0)
                {
                    return area;
                }

                area = GetAreaFromDirectParameter(door, new[] { "Площадь", "Area", "Площадь проема", "Проем", "Площадь двери", "Door Area" });
                if (area > 0)
                {
                    return area;
                }

                return GetAreaFromWidthHeight(door, "Ширина", "Высота", BuiltInParameter.DOOR_WIDTH, BuiltInParameter.DOOR_HEIGHT);
            }
            catch
            {
                return 0;
            }
        }

        // Метод для получения площади окна из параметров типа
        private double GetWindowArea(FamilyInstance window)
        {
            try
            {
                ElementId windowTypeId = window.GetTypeId();
                if (windowTypeId == null || windowTypeId == ElementId.InvalidElementId)
                    return 0;

                Element windowType = doc.GetElement(windowTypeId);
                if (windowType == null) return 0;

                double area = GetAreaFromDirectParameter(windowType, new[] { "Площадь", "Area", "Площадь проема", "Проем", "Площадь остекления", "Window Area" });
                if (area > 0) return area;

                area = GetAreaFromDirectParameter(window, new[] { "Площадь", "Area", "Площадь проема", "Проем", "Площадь остекления", "Window Area" });
                if (area > 0) return area;

                return GetAreaFromWidthHeight(window, "Ширина", "Высота", BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.WINDOW_HEIGHT);
            }
            catch
            {
                return 0;
            }
        }

        // Общий метод получения площади из готового параметра
        private double GetAreaFromDirectParameter(Element element, string[] paramNames)
        {
            foreach (string paramName in paramNames)
            {
                Parameter areaParam = element.LookupParameter(paramName);

                if (areaParam != null && areaParam.HasValue)
                {
                    try
                    {
                        double area = areaParam.AsDouble();

                        if (Math.Abs(area) > 0.0001)
                        {
                            area = UnitUtils.ConvertFromInternalUnits(area, UnitTypeId.SquareMeters);

                            if (area > 0.01 && area < 100)
                            {
                                return area;
                            }
                        }
                    }
                    catch
                    {
                        try
                        {
                            string areaStr = areaParam.AsString();
                            if (double.TryParse(areaStr, out double area))
                            {
                                if (area > 0.01 && area < 100)
                                {
                                    return area;
                                }
                            }
                        }
                        catch
                        {
                            // Пропускаем ошибку
                        }
                    }
                }
            }

            return 0;
        }

        // Общий метод вычисления площади из ширины и высоты
        private double GetAreaFromWidthHeight(Element element, string widthParamName, string heightParamName,
                                            BuiltInParameter builtInWidthParam, BuiltInParameter builtInHeightParam)
        {
            double width = 0;
            double height = 0;

            Parameter widthParam = element.LookupParameter(widthParamName);
            if (widthParam == null || !widthParam.HasValue)
                widthParam = element.get_Parameter(builtInWidthParam);
            if (widthParam == null || !widthParam.HasValue)
                widthParam = element.LookupParameter("Ширина проема");
            if (widthParam == null || !widthParam.HasValue)
                widthParam = element.LookupParameter("Width");

            if (widthParam != null && widthParam.HasValue)
            {
                width = widthParam.AsDouble();
                width = UnitUtils.ConvertFromInternalUnits(width, UnitTypeId.Meters);
            }

            Parameter heightParam = element.LookupParameter(heightParamName);
            if (heightParam == null || !heightParam.HasValue)
            {
                heightParam = element.LookupParameter("Высота проема");
                if (heightParam == null || !heightParam.HasValue)
                    heightParam = element.get_Parameter(builtInHeightParam);
                if (heightParam == null || !heightParam.HasValue)
                    heightParam = element.LookupParameter("Height");
            }

            if (heightParam != null && heightParam.HasValue)
            {
                height = heightParam.AsDouble();
                height = UnitUtils.ConvertFromInternalUnits(height, UnitTypeId.Meters);
            }

            if (width > 0.01 && height > 0.01)
            {
                double area = width * height;

                if (area > 0.01 && area < 100)
                {
                    return area;
                }
            }

            return 0;
        }

        // Вспомогательный метод для добавления площади в словарь
        private void AddAreaToRoom(Dictionary<ElementId, double> dictionary, ElementId roomId, double area)
        {
            if (dictionary.ContainsKey(roomId))
            {
                dictionary[roomId] += area;
            }
            else
            {
                dictionary[roomId] = area;
            }
        }
    }
}