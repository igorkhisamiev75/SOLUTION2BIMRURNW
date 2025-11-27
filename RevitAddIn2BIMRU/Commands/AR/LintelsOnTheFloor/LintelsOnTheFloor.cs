using System.Windows;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;


namespace RevitAddIn2BIMRU.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LintelsOnTheFloor : Window, IExternalCommand
    {
        private Application _app;
        private Autodesk.Revit.DB.Document _doc;

        //Levels
        public List<Level> levelCollection = new List<Level>();
        //обобщенные модели
        public ICollection<Element> lintelsCollection = new List<Element>();

        //выбарнный уровень
        Level checkedlevel;
        //имя семейства
        string selectedElenet;
        string selectedElenet2;
        //лоические
        public bool doorCheck;
        public bool windowCheck;


        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {

            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

           
            _doc = uiDoc.Document;

            //уровни
            levelCollection = GetAllLevels();
            //обобщенные модели
            lintelsCollection = GetGenericModel();

            //количество циклов
            int k = 0;

            //создаем форму и передаем в нее данные
            MainWindow vpViewWpf = new MainWindow(levelCollection, lintelsCollection);

            vpViewWpf.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            vpViewWpf.ShowDialog();

            checkedlevel = vpViewWpf.selectedLevel;
            selectedElenet = vpViewWpf.selectedElement;
            selectedElenet2 = vpViewWpf.selectedElement2;
            doorCheck = vpViewWpf.doorCheck;
            windowCheck = vpViewWpf.windowCheck;

            using (var t = new Transaction(_doc))
            {
                t.Start("Расставляем перемычки");
                uiApp = commandData.Application;
                uiDoc = uiApp.ActiveUIDocument;

                //семейство перемычки
                FamilySymbol elementSymbol = GetSymbol(_doc, selectedElenet, selectedElenet2);

                //TaskDialog.Show("Выполнено", $"{elementSymbol.Family}");


                if (doorCheck)

                {

                    // Получаем все двери в проекте
                    IList<Element> doors = GetAllDoors();
                    //проходимся по всем дверям

                    foreach (Element doorElement in doors)
                    {
                        try { CreatingJumperDoor(doorElement, elementSymbol, checkedlevel); }
                        catch { continue; }
                        //TaskDialog.Show("Выполнено", $"тут");


                    }


                    TaskDialog.Show("Выполнено", $"Перемычки расставлены на двери");
                }

                if (windowCheck)
                {

                    // Получаем все окна в проекте
                    IList<FamilyInstance> windows = GetAllWindows(_doc);

                    //TaskDialog.Show("Окон", $"{windows.Count}");

                    //проходимся по всем окнам
                    foreach (Element windowElement in windows)
                    {
                        try
                        {
                            CreatingJumperWindow(windowElement, elementSymbol, checkedlevel);
                        }
                        catch
                        {

                            continue;
                        }

                    }


                    TaskDialog.Show("Выполнено", $"Перемычки расставлены на окна");


                }

                _doc.Regenerate();
                t.Commit();

            }

            return Result.Succeeded;
        }

        //get all levels
        public List<Level> GetAllLevels()
        {
            // Search all level
            FilteredElementCollector filteredLevelCollector = new FilteredElementCollector(_doc);
            filteredLevelCollector.OfClass(typeof(Level));
            List<Level> levelCollection = filteredLevelCollector.Cast<Level>().ToList<Level>();

            return levelCollection;
        }

        public ICollection<Element> GetGenericModel()
        {
            // Создаем фильтр для элементов категории "Обобщенные модели"
            BuiltInCategory genericModelCategory = BuiltInCategory.OST_GenericModel;
            ElementCategoryFilter categoryFilter = new ElementCategoryFilter(genericModelCategory);

            // Используем фильтр для получения всех обобщенных моделей в проекте
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            IList<Element> genericModels = collector.WherePasses(categoryFilter).WhereElementIsNotElementType().ToElements();

            return genericModels;
        }

        public IList<Element> GetAllDoors()
        {
            BuiltInCategory doorsCategory = BuiltInCategory.OST_Doors;
            ElementCategoryFilter categoryFilter = new ElementCategoryFilter(doorsCategory);

            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            return collector.WherePasses(categoryFilter)
                          .WhereElementIsNotElementType()
                          .ToElements();

        }

        public void CreatingJumperDoor(Element element, FamilySymbol elementSymbol, Level level)
        {
            FamilyInstance door = element as FamilyInstance;

            //уровень 

            Level testLevel = _doc.GetElement(door.LevelId) as Level;

            if (testLevel.Name == level.Name)
            {
                double haightDoor = 2;
                //ВЫСОТА ДВЕРИ
                try
                {
                    haightDoor = door.LookupParameter("Высота").AsDouble();
                }

                catch

                {
                   
                    // Выбираем семейство для анализа(можно заменить на выбор пользователя)
                    //находим высоту в типе
                    FamilySymbol familyType = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .FirstOrDefault(e => e.Name == door.Name.ToString()) as FamilySymbol;

                    haightDoor = familyType.LookupParameter("Высота").AsDouble();
                  
                }


                Element wall = door.Host;


                // Get door location and orientation
                LocationPoint doorLoc = door.Location as LocationPoint;

                XYZ doorPosition = doorLoc.Point;

                // Calculate position above door
                XYZ placementPoint = new XYZ(
                    doorPosition.X,
                    doorPosition.Y,
                    doorPosition.Z
                );

                

                try
                {
                    // Create the light (aligned with door direction)
                    FamilyInstance light = _doc.Create.NewFamilyInstance(
                        placementPoint,
                        elementSymbol,
                        wall,
                        level,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                    );

                    
                    light.LookupParameter("Отметка перемычки").Set(haightDoor);

                    light.LookupParameter("Комментарии").Set(wall.Name);


                }
                catch
                {

                }

            }



        }

        public FamilySymbol GetSymbol(Autodesk.Revit.DB.Document document, string familyName, string symbolName)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Family)).OfType<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName))?.GetFamilySymbolIds()
                .Select(id => document.GetElement(id)).OfType<FamilySymbol>().FirstOrDefault(symbol => symbol.Name.Equals(symbolName));
        }

        public static IList<FamilyInstance> GetAllWindows(Autodesk.Revit.DB.Document document)
        {
            // Получаем все экземпляры окон
            var allWindows = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>();

            // Фильтруем, оставляя только основные окна
            return allWindows.Where(w => !IsNestedWindow(w)).ToList();
        }

        public void CreatingJumperWindow(Element element, FamilySymbol elementSymbol, Level level)
        {
            FamilyInstance door = element as FamilyInstance;


            //уровень 

            Level testLevel = _doc.GetElement(door.LevelId) as Level;

            //TaskDialog.Show("Выполнено", $"уровень  {testLevel} ");
            //TaskDialog.Show("Выполнено", $"уровень  {level} ");


            if (testLevel.Name == level.Name)
            {
                double haightDoor = 0;
                //ВЫСОТА 
                try
                {
                    haightDoor = door.LookupParameter("Высота").AsDouble();
                }

                catch

                {
                    // Выбираем семейство для анализа(можно заменить на выбор пользователя)
                    //находим высоту в типе
                    FamilySymbol familyType = new FilteredElementCollector(_doc)
                   .OfClass(typeof(FamilySymbol))
                   .FirstOrDefault(e => e.Name == door.Name.ToString()) as FamilySymbol;

                    haightDoor = familyType.LookupParameter("Высота").AsDouble();


                }



                if (haightDoor == null)
                {
                    haightDoor = 0;
                }

                Element wall = door.Host;
                // Get door location and orientation
                LocationPoint doorLoc = door.Location as LocationPoint;

                XYZ doorPosition = doorLoc.Point;

                // Calculate position above door
                XYZ placementPoint = new XYZ(
                    doorPosition.X,
                    doorPosition.Y,
                    doorPosition.Z
                );

                try
                {
                    // Create the light (aligned with door direction)
                    FamilyInstance light = _doc.Create.NewFamilyInstance(
                        placementPoint,
                        elementSymbol,
                        wall,
                        level,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                    );

                    light.LookupParameter("Отметка перемычки").Set(haightDoor);
                    light.LookupParameter("Комментарии").Set(wall.Name);


                }
                catch
                {

                }

            }



        }

        /// <summary>
        /// Проверяет, является ли окно вложенным
        /// </summary>
        private static bool IsNestedWindow(FamilyInstance window)
        {
            // 1. Проверяем, находится ли окно внутри другого семейства
            if (window.SuperComponent != null)
            {
                return true;
            }

            // 2. Проверяем, находится ли окно в группе
            if (window.GroupId != ElementId.InvalidElementId)
            {
                return true;
            }

            // 3. Дополнительная проверка через хост-элемент
            Element host = window.Host;
            if (host != null && !(host is Wall)) // Если хост не стена (например, другое семейство)
            {
                return true;
            }

            return false;
        }

    }
}
