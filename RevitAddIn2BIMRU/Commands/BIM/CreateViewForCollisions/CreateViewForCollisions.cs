#region Namespace
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using System.Drawing;

using Button = System.Windows.Forms.Button;
using Label = System.Windows.Forms.Label;
#endregion

namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateViewForCollisions : IExternalCommand
    {
        private Document _doc;
        private UIDocument _uiDoc;
        private List<View3D> _createdViews = new List<View3D>();

        // База данных: FamilySymbolId -> List<Element> (обобщенные модели с этим Symbol)
        private Dictionary<ElementId, List<Element>> _symbolToElementsMap = new Dictionary<ElementId, List<Element>>();
        // База данных: ElementId -> FamilySymbolId (для быстрого поиска)
        private Dictionary<ElementId, ElementId> _elementToSymbolMap = new Dictionary<ElementId, ElementId>();

        // Настройки цветов и прозрачности
        private System.Drawing.Color _collision1Color = System.Drawing.Color.Orange;
        private System.Drawing.Color _collision2Color = System.Drawing.Color.Green;
        private int _transparency = 80;
        private string _reportFilePath = string.Empty;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            _uiDoc = commandData.Application.ActiveUIDocument;
            _doc = _uiDoc.Document;

            try
            {
                // Показываем форму настроек
                using (var settingsForm = new CollisionSettingsForm())
                {
                    // Устанавливаем начальные значения
                    settingsForm.Collision1Color = _collision1Color;
                    settingsForm.Collision2Color = _collision2Color;
                    settingsForm.Transparency = _transparency;

                    if (settingsForm.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;

                    // Получаем настройки из формы через свойства
                    _collision1Color = settingsForm.Collision1Color;
                    _collision2Color = settingsForm.Collision2Color;
                    _transparency = settingsForm.Transparency;
                    _reportFilePath = settingsForm.ReportFilePath;

                    if (string.IsNullOrEmpty(_reportFilePath))
                    {
                        TaskDialog.Show("Ошибка", "Файл отчета не выбран.");
                        return Result.Cancelled;
                    }

                    ViewFamilyType viewFamilyType = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .First(v => v.ViewFamily == ViewFamily.ThreeDimensional);

                    ElementId solidFillPatternId = GetSolidFillPatternId();

                    // Загружаем отчет
                    XmlDocument xml = new XmlDocument();
                    xml.Load(_reportFilePath);
                    var clashResults = xml.SelectNodes("//clashresult");

                    if (clashResults == null || clashResults.Count == 0)
                    {
                        TaskDialog.Show("Информация", "В отчёте не найдено коллизий.");
                        return Result.Cancelled;
                    }

                    // Создаем и показываем форму прогресса
                    using (var progressForm = new ProgressForm2())
                    {
                        progressForm.Show();
                        System.Windows.Forms.Application.DoEvents();

                        // 2. Создаем базу данных обобщенных моделей, дверей, окон, импостов витража, панелей витража и соединительных деталей
                        progressForm.StatusLabel.Text = "Построение базы данных элементов...";
                        progressForm.ProgressBar.Value = 10;
                        System.Windows.Forms.Application.DoEvents();

                        BuildExtendedElementsDatabase();

                        int viewCount = 0;
                        int totalClashes = clashResults.Count;
                        int currentClash = 0;

                        using (Transaction t = new Transaction(_doc, "Создание видов коллизий"))
                        {
                            t.Start();

                            // 3. Проверяем каждый конфликт из отчета
                            foreach (XmlNode clash in clashResults)
                            {
                                if (progressForm.CancelRequested)
                                {
                                    t.RollBack();
                                    TaskDialog.Show("Отменено", "Операция отменена пользователем.");
                                    return Result.Cancelled;
                                }

                                currentClash++;
                                string name = clash.Attributes["name"]?.Value ?? "Без_имени";

                                progressForm.StatusLabel.Text = $"Обработка коллизии {currentClash} из {totalClashes}: {name}";
                                progressForm.ProgressBar.Value = 10 + (int)((double)currentClash / totalClashes * 80);
                                System.Windows.Forms.Application.DoEvents();

                                // Ищем элементы для этой коллизии с разделением на две группы
                                List<Element> collision1Elements;
                                List<Element> collision2Elements;
                                List<Element> allCollisionElements = FindElementsForCollision(clash, out collision1Elements, out collision2Elements);

                                if (allCollisionElements.Count > 0)
                                {
                                    Debug.WriteLine($"Найдено элементов: {allCollisionElements.Count}");
                                    Debug.WriteLine($"Коллизия 1: {collision1Elements.Count} элементов");
                                    Debug.WriteLine($"Коллизия 2: {collision2Elements.Count} элементов");

                                    // Получаем ID для имени вида
                                    List<ElementId> collision1Ids, collision2Ids;
                                    GetCollisionIdsFromXml(clash, out collision1Ids, out collision2Ids);
                                    List<ElementId> allReportIds = collision1Ids.Concat(collision2Ids).ToList();

                                    if (CreateCollisionView(name, allCollisionElements, collision1Elements, collision2Elements, allReportIds, viewFamilyType, solidFillPatternId))
                                    {
                                        viewCount++;
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine($"Элементы не найдены");
                                }
                            }

                            t.Commit();
                        }

                        progressForm.StatusLabel.Text = "Завершено!";
                        progressForm.ProgressBar.Value = 100;
                        progressForm.Close();
                        System.Windows.Forms.Application.DoEvents();
                        System.Threading.Thread.Sleep(500);

                        TaskDialog.Show("Результат", $"Создано {viewCount} 3D видов для коллизий");

                        return Result.Succeeded;
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// 2. Создаем базу данных обобщенных моделей, дверей, окон, импостов витража, панелей витража и соединительных деталей
        /// </summary>
        private void BuildExtendedElementsDatabase()
        {
            _symbolToElementsMap.Clear();
            _elementToSymbolMap.Clear();

            // Собираем обобщенные модели
            var genericModels = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType()
                .ToList();

            // Собираем двери
            var doors = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .ToList();

            // Собираем окна
            var windows = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .ToList();

            // Собираем импосты витража (стоечные профили витражей)
            var curtainWallMullions = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_CurtainWallMullions)
                .WhereElementIsNotElementType()
                .ToList();

            // Собираем панели витража
            var curtainPanels = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                .WhereElementIsNotElementType()
                .ToList();

            // Собираем соединительные детали воздуховодов
            var ductFittings = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_DuctFitting)
                .WhereElementIsNotElementType()
                .ToList();

            // Собираем соединительные детали труб
            var pipeFittings = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_PipeFitting)
                .WhereElementIsNotElementType()
                .ToList();

            Debug.WriteLine($"=== ПОСТРОЕНИЕ БАЗЫ ДАННЫХ ===");
            Debug.WriteLine($"Найдено обобщенных моделей: {genericModels.Count}");
            Debug.WriteLine($"Найдено дверей: {doors.Count}");
            Debug.WriteLine($"Найдено окон: {windows.Count}");
            Debug.WriteLine($"Найдено импостов витража: {curtainWallMullions.Count}");
            Debug.WriteLine($"Найдено панелей витража: {curtainPanels.Count}");
            Debug.WriteLine($"Найдено соединительных деталей воздуховодов: {ductFittings.Count}");
            Debug.WriteLine($"Найдено соединительных деталей труб: {pipeFittings.Count}");

            // Обрабатываем обобщенные модели
            ProcessElementsCollection(genericModels, "Обобщенная модель");

            // Обрабатываем двери
            ProcessElementsCollection(doors, "Дверь");

            // Обрабатываем окна
            ProcessElementsCollection(windows, "Окно");

            // Обрабатываем импосты витража
            ProcessElementsCollection(curtainWallMullions, "Импост витража");

            // Обрабатываем панели витража
            ProcessElementsCollection(curtainPanels, "Панель витража");

            // Обрабатываем соединительные детали воздуховодов
            ProcessElementsCollection(ductFittings, "Соединительная деталь воздуховода");

            // Обрабатываем соединительные детали труб
            ProcessElementsCollection(pipeFittings, "Соединительная деталь трубы");

            Debug.WriteLine($"База построена: {_symbolToElementsMap.Count} FamilySymbolId, {_elementToSymbolMap.Count} элементов");
        }

        /// <summary>
        /// Обрабатывает коллекцию элементов и добавляет их в базу данных
        /// </summary>
        private void ProcessElementsCollection(List<Element> elements, string elementType)
        {
            foreach (Element element in elements)
            {
                try
                {
                    // Получаем FamilySymbolId из геометрии элемента
                    ElementId familySymbolId = GetFamilySymbolIdFromGeometry(element);

                    if (familySymbolId != null && familySymbolId != ElementId.InvalidElementId)
                    {
                        // Сохраняем в базу: ElementId -> FamilySymbolId
                        _elementToSymbolMap[element.Id] = familySymbolId;

                        // Сохраняем в базу: FamilySymbolId -> List<Element>
                        if (!_symbolToElementsMap.ContainsKey(familySymbolId))
                            _symbolToElementsMap[familySymbolId] = new List<Element>();

                        _symbolToElementsMap[familySymbolId].Add(element);

                        Debug.WriteLine($"{elementType} {element.Id} -> FamilySymbolId {familySymbolId}");
                    }
                    else
                    {
                        Debug.WriteLine($"{elementType} {element.Id}: FamilySymbolId не найден");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка {elementType.ToLower()} {element.Id}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Получаем FamilySymbolId из геометрии элемента
        /// </summary>
        private ElementId GetFamilySymbolIdFromGeometry(Element element)
        {
            try
            {
                Options options = new Options
                {
                    ComputeReferences = true,
                    DetailLevel = ViewDetailLevel.Medium,
                    IncludeNonVisibleObjects = true
                };

                GeometryElement geometry = element.get_Geometry(options);
                if (geometry == null)
                    return null;

                // Ищем GeometryInstance в геометрии
                foreach (GeometryObject geomObj in geometry)
                {
                    if (geomObj is GeometryInstance geometryInstance)
                    {
                        // Пытаемся получить FamilySymbolId через Reflection
                        ElementId symbolId = FindSymbolIdByReflection(geometryInstance);
                        if (symbolId != null && symbolId != ElementId.InvalidElementId)
                        {
                            return symbolId;
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Поиск SymbolId через Reflection
        /// </summary>
        private ElementId FindSymbolIdByReflection(GeometryInstance geometryInstance)
        {
            try
            {
                Type geometryInstanceType = geometryInstance.GetType();

                string[] possiblePropertyNames = {
                    "Symbol", "FamilySymbol", "SymbolId", "SymbolElement",
                    "GetSymbolId", "SymbolReference", "FamilySymbolId"
                };

                foreach (string propName in possiblePropertyNames)
                {
                    try
                    {
                        var property = geometryInstanceType.GetProperty(propName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (property != null)
                        {
                            object value = property.GetValue(geometryInstance);
                            if (value is ElementId elementId && elementId != ElementId.InvalidElementId)
                            {
                                return elementId;
                            }
                            else if (value is Element symbolElement)
                            {
                                return symbolElement.Id;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                }

                // Пробуем методы
                string[] possibleMethodNames = {
                    "GetSymbol", "GetSymbolId", "GetFamilySymbol"
                };

                foreach (string methodName in possibleMethodNames)
                {
                    try
                    {
                        var method = geometryInstanceType.GetMethod(methodName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                            null, Type.EmptyTypes, null);

                        if (method != null)
                        {
                            object result = method.Invoke(geometryInstance, null);
                            if (result is ElementId elementId && elementId != ElementId.InvalidElementId)
                            {
                                return elementId;
                            }
                            else if (result is Element symbolElement)
                            {
                                return symbolElement.Id;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Получает ID элементов из XML с разделением на две коллизии
        /// </summary>
        private void GetCollisionIdsFromXml(XmlNode clashNode, out List<ElementId> collision1Ids, out List<ElementId> collision2Ids)
        {
            collision1Ids = new List<ElementId>();
            collision2Ids = new List<ElementId>();

            // Ищем элементы clashobject в XML
            var clashObjects = clashNode.SelectNodes(".//clashobject");

            if (clashObjects != null && clashObjects.Count >= 2)
            {
                // Первый clashobject - первая коллизия
                ParseClashObjectIds(clashObjects[0], collision1Ids);

                // Второй clashobject - вторая коллизия  
                ParseClashObjectIds(clashObjects[1], collision2Ids);
            }
            else
            {
                // Альтернативный поиск по различным структурам XML
                TryAlternativeXmlParsing(clashNode, collision1Ids, collision2Ids);
            }

            Debug.WriteLine($"XML parsing: Collision1 IDs: [{string.Join(", ", collision1Ids)}], Collision2 IDs: [{string.Join(", ", collision2Ids)}]");
        }

        /// <summary>
        /// Парсит ID из clashobject узла
        /// </summary>
        private void ParseClashObjectIds(XmlNode clashObjectNode, List<ElementId> ids)
        {
            if (clashObjectNode == null) return;

            // Ищем значения в различных возможных структурах
            var valueNodes = clashObjectNode.SelectNodes(".//value");
            if (valueNodes != null)
            {
                foreach (XmlNode valueNode in valueNodes)
                {
                    string valueText = valueNode.InnerText.Trim();
                    if (int.TryParse(valueText, out int id) && id > 0)
                    {
                        ids.Add(new ElementId(id));
                    }
                }
            }

            // Альтернативный поиск по атрибутам
            var attributes = clashObjectNode.Attributes;
            if (attributes != null)
            {
                foreach (XmlAttribute attr in attributes)
                {
                    if (int.TryParse(attr.Value, out int id) && id > 0)
                    {
                        ids.Add(new ElementId(id));
                    }
                }
            }
        }

        /// <summary>
        /// Альтернативные методы парсинга XML структуры
        /// </summary>
        private void TryAlternativeXmlParsing(XmlNode clashNode, List<ElementId> collision1Ids, List<ElementId> collision2Ids)
        {
            // Попробуем найти по различным XPath выражениям
            string[] possibleXPaths = {
                ".//clashobject1", ".//clashobject2",
                ".//object1", ".//object2",
                ".//item1", ".//item2",
                ".//leftobject", ".//rightobject",
                ".//first", ".//second"
            };

            for (int i = 0; i < possibleXPaths.Length; i += 2)
            {
                var node1 = clashNode.SelectSingleNode(possibleXPaths[i]);
                var node2 = clashNode.SelectSingleNode(possibleXPaths[i + 1]);

                if (node1 != null && node2 != null)
                {
                    ParseClashObjectIds(node1, collision1Ids);
                    ParseClashObjectIds(node2, collision2Ids);
                    return;
                }
            }

            // Если не нашли разделение, пробуем разделить пополам все найденные ID
            var allIds = GetAllIdsFromClashNode(clashNode);
            int half = allIds.Count / 2;
            collision1Ids.AddRange(allIds.Take(half));
            collision2Ids.AddRange(allIds.Skip(half));
        }

        /// <summary>
        /// Получает все ID из узла коллизии
        /// </summary>
        private List<ElementId> GetAllIdsFromClashNode(XmlNode clashNode)
        {
            List<ElementId> ids = new List<ElementId>();
            var valueNodes = clashNode.SelectNodes(".//value");
            if (valueNodes != null)
            {
                foreach (XmlNode valueNode in valueNodes)
                {
                    string valueText = valueNode.InnerText.Trim();
                    if (int.TryParse(valueText, out int id) && id > 0)
                        ids.Add(new ElementId(id));
                }
            }
            return ids;
        }

        /// <summary>
        /// 3. Поиск элементов для коллизии с правильным разделением
        /// </summary>
        private List<Element> FindElementsForCollision(XmlNode clashNode, out List<Element> collision1Elements, out List<Element> collision2Elements)
        {
            // Получаем ID с разделением из XML
            List<ElementId> collision1Ids, collision2Ids;
            GetCollisionIdsFromXml(clashNode, out collision1Ids, out collision2Ids);

            var allCollisionElements = new List<Element>();
            collision1Elements = new List<Element>();
            collision2Elements = new List<Element>();

            Debug.WriteLine($"Поиск элементов: Коллизия1 IDs: {collision1Ids.Count}, Коллизия2 IDs: {collision2Ids.Count}");

            // Ищем элементы для первой коллизии
            foreach (ElementId reportId in collision1Ids)
            {
                var elements = FindElementsById(reportId);
                foreach (var element in elements)
                {
                    if (!allCollisionElements.Any(e => e.Id == element.Id))
                    {
                        allCollisionElements.Add(element);
                        collision1Elements.Add(element);
                    }
                }
            }

            // Ищем элементы для второй коллизии
            foreach (ElementId reportId in collision2Ids)
            {
                var elements = FindElementsById(reportId);
                foreach (var element in elements)
                {
                    if (!allCollisionElements.Any(e => e.Id == element.Id))
                    {
                        allCollisionElements.Add(element);
                        collision2Elements.Add(element);
                    }
                }
            }

            Debug.WriteLine($"Найдено элементов: Всего {allCollisionElements.Count}, Коллизия1: {collision1Elements.Count}, Коллизия2: {collision2Elements.Count}");

            return allCollisionElements;
        }

        /// <summary>
        /// Поиск элементов по ID (в базе данных или напрямую)
        /// </summary>
        private List<Element> FindElementsById(ElementId reportId)
        {
            var result = new List<Element>();

            Debug.WriteLine($"  Поиск ID {reportId}:");

            // Сначала ищем в базе по FamilySymbolId
            if (_symbolToElementsMap.ContainsKey(reportId))
            {
                var elements = _symbolToElementsMap[reportId];
                result.AddRange(elements);
                Debug.WriteLine($"    Найден как FamilySymbolId -> {elements.Count} элементов");
            }

            // Если не найден как FamilySymbolId, ищем напрямую как ElementId
            if (result.Count == 0)
            {
                Element directElement = _doc.GetElement(reportId);
                if (directElement != null)
                {
                    result.Add(directElement);
                    Debug.WriteLine($"    Найден как прямой ElementId");
                }
            }

            if (result.Count == 0)
            {
                Debug.WriteLine($"    Не найден в базе (возможно, в связанной модели)");
            }

            return result;
        }

        /// <summary>
        /// Создание 3D вида для коллизии
        /// </summary>
        private bool CreateCollisionView(string clashName, List<Element> allElements, List<Element> collision1Elements, List<Element> collision2Elements, List<ElementId> reportIds, ViewFamilyType viewFamilyType, ElementId solidFillPatternId)
        {
            try
            {
                View3D view = View3D.CreateIsometric(_doc, viewFamilyType.Id);

                // Формируем имя вида с ID
                string ids = string.Join("_", reportIds.Take(3));
                string collisionInfo = "Коллизия ";
                //string collisionInfo = $"Коллизия-{collision1Elements.Count}_Коллизия-{collision2Elements.Count}";

                string viewName = $"{clashName}_{collisionInfo}_{ids}";
                if (viewName.Length > 200)
                    viewName = viewName.Substring(0, 200);

                view.Name = viewName;
                view.DisplayStyle = DisplayStyle.Shading;
                view.DetailLevel = ViewDetailLevel.Fine;
                view.Discipline = ViewDiscipline.Coordination;

                // Устанавливаем секционную коробку
                BoundingBoxXYZ sectionBox = CreateSectionBoxFromElements(allElements);
                if (sectionBox != null)
                    view.SetSectionBox(sectionBox);

                // Применяем графические настройки с разными цветами
                ApplyColorOverrides(view, collision1Elements, collision2Elements, solidFillPatternId);

                _createdViews.Add(view);

                Debug.WriteLine($"Создан вид: {viewName}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка создания вида: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Применение графических настроек с разными цветами для двух коллизий
        /// БЕЗ прозрачности для элементов коллизии
        /// </summary>
        private void ApplyColorOverrides(View3D view, List<Element> collision1Elements, List<Element> collision2Elements, ElementId solidFillPatternId)
        {
            // Настройки для элементов первой коллизии - БЕЗ ПРОЗРАЧНОСТИ
            OverrideGraphicSettings collision1Ogs = new OverrideGraphicSettings();
            collision1Ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(_collision1Color.R, _collision1Color.G, _collision1Color.B));
            collision1Ogs.SetProjectionLineWeight(5);
            collision1Ogs.SetSurfaceForegroundPatternId(solidFillPatternId);
            collision1Ogs.SetSurfaceForegroundPatternColor(new Autodesk.Revit.DB.Color(_collision1Color.R, _collision1Color.G, _collision1Color.B));
            collision1Ogs.SetSurfaceTransparency(0); // Убрана прозрачность для коллизии

            // Настройки для элементов второй коллизии - БЕЗ ПРОЗРАЧНОСТИ
            OverrideGraphicSettings collision2Ogs = new OverrideGraphicSettings();
            collision2Ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(_collision2Color.R, _collision2Color.G, _collision2Color.B));
            collision2Ogs.SetProjectionLineWeight(5);
            collision2Ogs.SetSurfaceForegroundPatternId(solidFillPatternId);
            collision2Ogs.SetSurfaceForegroundPatternColor(new Autodesk.Revit.DB.Color(_collision2Color.R, _collision2Color.G, _collision2Color.B));
            collision2Ogs.SetSurfaceTransparency(0); // Убрана прозрачность для коллизии

            // Настройки для остальных элементов (прозрачность)
            OverrideGraphicSettings transparentOgs = new OverrideGraphicSettings();
            transparentOgs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(128, 128, 128));
            transparentOgs.SetProjectionLineWeight(1);
            transparentOgs.SetSurfaceForegroundPatternId(solidFillPatternId);
            transparentOgs.SetSurfaceForegroundPatternColor(new Autodesk.Revit.DB.Color(200, 200, 200));
            transparentOgs.SetSurfaceTransparency((byte)_transparency); // Прозрачность для всех остальных

            // Получаем все элементы в секционной коробке
            var allElementsInView = new FilteredElementCollector(_doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElements();

            // Применяем настройки ко всем элементам в виде
            foreach (Element element in allElementsInView)
            {
                try
                {
                    // Если элемент из первой коллизии - БЕЗ ПРОЗРАЧНОСТИ
                    if (collision1Elements.Any(e => e.Id == element.Id))
                    {
                        view.SetElementOverrides(element.Id, collision1Ogs);
                    }
                    // Если элемент из второй коллизии - БЕЗ ПРОЗРАЧНОСТИ
                    else if (collision2Elements.Any(e => e.Id == element.Id))
                    {
                        view.SetElementOverrides(element.Id, collision2Ogs);
                    }
                    // Остальные элементы - С ПРОЗРАЧНОСТЬЮ
                    else
                    {
                        view.SetElementOverrides(element.Id, transparentOgs);
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }

            Debug.WriteLine($"Раскрашено: {collision1Elements.Count} элементов цветом 1 (без прозрачности), {collision2Elements.Count} элементов цветом 2 (без прозрачности), {allElementsInView.Count - collision1Elements.Count - collision2Elements.Count} прозрачных");
        }
        private BoundingBoxXYZ CreateSectionBoxFromElements(List<Element> elements)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();
            foreach (var el in elements)
            {
                var bb = el.get_BoundingBox(null);
                if (bb != null)
                    boxes.Add(bb);
            }
            if (boxes.Count == 0) return null;

            double minX = boxes.Min(b => b.Min.X);
            double minY = boxes.Min(b => b.Min.Y);
            double minZ = boxes.Min(b => b.Min.Z);
            double maxX = boxes.Max(b => b.Max.X);
            double maxY = boxes.Max(b => b.Max.Y);
            double maxZ = boxes.Max(b => b.Max.Z);

            double offset = Math.Max((maxX - minX) * 0.2, 1.0);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX - offset, minY - offset, minZ - offset),
                Max = new XYZ(maxX + offset, maxY + offset, maxZ + offset)
            };
        }

        private ElementId GetSolidFillPatternId()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id
                ?? ElementId.InvalidElementId;
        }
    }

    // Остальные классы форм остаются без изменений...
    public partial class CollisionSettingsForm : System.Windows.Forms.Form
    {
        public System.Drawing.Color Collision1Color { get; set; }
        public System.Drawing.Color Collision2Color { get; set; }
        public int Transparency { get; set; }
        public string ReportFilePath { get; set; }

        private Button btnOpenReport;
        private Button btnColor1;
        private Button btnColor2;
        private Button btnTransparency;
        private Button btnOK;
        private Button btnCancel;
        private Label lblReport;
        private Label lblColor1;
        private Label lblColor2;
        private Label lblTransparency;
        private ColorDialog colorDialog;

        public CollisionSettingsForm()
        {
            InitializeComponent();
            // Устанавливаем цвета по умолчанию: оранжевый и зеленый
            Collision1Color = System.Drawing.Color.Orange;
            Collision2Color = System.Drawing.Color.Green;
            Transparency = 80;
            ReportFilePath = string.Empty;
        }

        private void InitializeComponent()
        {
            this.btnOpenReport = new Button();
            this.btnColor1 = new Button();
            this.btnColor2 = new Button();
            this.btnTransparency = new Button();
            this.btnOK = new Button();
            this.btnCancel = new Button();
            this.lblReport = new Label();
            this.lblColor1 = new Label();
            this.lblColor2 = new Label();
            this.lblTransparency = new Label();
            this.colorDialog = new ColorDialog();

            this.btnOpenReport.Location = new System.Drawing.Point(120, 20);
            this.btnOpenReport.Name = "btnOpenReport";
            this.btnOpenReport.Size = new System.Drawing.Size(150, 30);
            this.btnOpenReport.Text = "Открыть отчет";
            this.btnOpenReport.Click += new EventHandler(this.btnOpenReport_Click);

            this.btnColor1.Location = new System.Drawing.Point(120, 60);
            this.btnColor1.Name = "btnColor1";
            this.btnColor1.Size = new System.Drawing.Size(150, 30);
            this.btnColor1.BackColor = Collision1Color;
            this.btnColor1.Text = "Выбрать цвет";
            this.btnColor1.Click += new EventHandler(this.btnColor1_Click);

            this.btnColor2.Location = new System.Drawing.Point(120, 100);
            this.btnColor2.Name = "btnColor2";
            this.btnColor2.Size = new System.Drawing.Size(150, 30);
            this.btnColor2.BackColor = Collision2Color;
            this.btnColor2.Text = "Выбрать цвет";
            this.btnColor2.Click += new EventHandler(this.btnColor2_Click);

            this.btnTransparency.Location = new System.Drawing.Point(120, 140);
            this.btnTransparency.Name = "btnTransparency";
            this.btnTransparency.Size = new System.Drawing.Size(150, 30);
            this.btnTransparency.Text = "Настроить прозрачность";
            this.btnTransparency.Click += new EventHandler(this.btnTransparency_Click);

            this.btnOK.Location = new System.Drawing.Point(50, 190);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(100, 30);
            this.btnOK.Text = "OK";
            this.btnOK.Click += new EventHandler(this.btnOK_Click);

            this.btnCancel.Location = new System.Drawing.Point(170, 190);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 30);
            this.btnCancel.Text = "Отмена";
            this.btnCancel.Click += new EventHandler(this.btnCancel_Click);

            this.lblReport.Location = new System.Drawing.Point(20, 25);
            this.lblReport.Name = "lblReport";
            this.lblReport.Size = new System.Drawing.Size(90, 30);
            this.lblReport.Text = "Отчет:";

            this.lblColor1.Location = new System.Drawing.Point(20, 65);
            this.lblColor1.Name = "lblColor1";
            this.lblColor1.Size = new System.Drawing.Size(90, 20);
            this.lblColor1.Text = "Коллизия 1:";

            this.lblColor2.Location = new System.Drawing.Point(20, 105);
            this.lblColor2.Name = "lblColor2";
            this.lblColor2.Size = new System.Drawing.Size(90, 20);
            this.lblColor2.Text = "Коллизия 2:";

            this.lblTransparency.Location = new System.Drawing.Point(20, 145);
            this.lblTransparency.Name = "lblTransparency";
            this.lblTransparency.Size = new System.Drawing.Size(90, 30);
            this.lblTransparency.Text = "Прозрачность:";

            this.ClientSize = new System.Drawing.Size(320, 240);
            this.Controls.Add(this.btnOpenReport);
            this.Controls.Add(this.btnColor1);
            this.Controls.Add(this.btnColor2);
            this.Controls.Add(this.btnTransparency);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.lblReport);
            this.Controls.Add(this.lblColor1);
            this.Controls.Add(this.lblColor2);
            this.Controls.Add(this.lblTransparency);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Text = "Настройки коллизий";
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        private void btnOpenReport_Click(object sender, EventArgs e)
        {
            System.Windows.Forms.OpenFileDialog ofd = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Выберите XML отчет Navisworks",
                Filter = "XML files|*.xml"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                ReportFilePath = ofd.FileName;
                lblReport.Text = $"Отчет: {System.IO.Path.GetFileName(ReportFilePath)}";
            }
        }

        private void btnColor1_Click(object sender, EventArgs e)
        {
            colorDialog.Color = Collision1Color;
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                Collision1Color = colorDialog.Color;
                btnColor1.BackColor = Collision1Color;
            }
        }

        private void btnColor2_Click(object sender, EventArgs e)
        {
            colorDialog.Color = Collision2Color;
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                Collision2Color = colorDialog.Color;
                btnColor2.BackColor = Collision2Color;
            }
        }

        private void btnTransparency_Click(object sender, EventArgs e)
        {
            using (var transparencyForm = new TransparencySettingsForm(Transparency))
            {
                if (transparencyForm.ShowDialog() == DialogResult.OK)
                {
                    Transparency = transparencyForm.TransparencyValue;
                    lblTransparency.Text = $"Прозрачность: {100 - Transparency}%";
                }
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(ReportFilePath))
            {
                System.Windows.Forms.MessageBox.Show("Пожалуйста, выберите файл отчета с коллизиями.", "Внимание",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }

    public class TransparencySettingsForm : System.Windows.Forms.Form
    {
        public int TransparencyValue { get; private set; }

        private TrackBar trackBar;
        private Label lblValue;
        private Button btnOK;
        private Button btnCancel;

        public TransparencySettingsForm(int currentTransparency)
        {
            TransparencyValue = currentTransparency;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.trackBar = new TrackBar();
            this.lblValue = new Label();
            this.btnOK = new Button();
            this.btnCancel = new Button();

            // trackBar
            this.trackBar.Location = new System.Drawing.Point(20, 20);
            this.trackBar.Size = new System.Drawing.Size(250, 45);
            this.trackBar.Minimum = 0;
            this.trackBar.Maximum = 100;
            this.trackBar.Value = TransparencyValue;
            this.trackBar.TickFrequency = 10;
            this.trackBar.ValueChanged += new EventHandler(this.trackBar_ValueChanged);

            // lblValue
            this.lblValue.Location = new System.Drawing.Point(280, 25);
            this.lblValue.Size = new System.Drawing.Size(50, 20);
            this.lblValue.Text = $"{100 - TransparencyValue}%";

            // btnOK
            this.btnOK.Location = new System.Drawing.Point(70, 80);
            this.btnOK.Size = new System.Drawing.Size(75, 25);
            this.btnOK.Text = "OK";
            this.btnOK.Click += new EventHandler(this.btnOK_Click);

            // btnCancel
            this.btnCancel.Location = new System.Drawing.Point(155, 80);
            this.btnCancel.Size = new System.Drawing.Size(75, 25);
            this.btnCancel.Text = "Отмена";
            this.btnCancel.Click += new EventHandler(this.btnCancel_Click);

            // Form
            this.ClientSize = new System.Drawing.Size(350, 120);
            this.Controls.Add(this.trackBar);
            this.Controls.Add(this.lblValue);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Text = "Настройка прозрачности";
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        private void trackBar_ValueChanged(object sender, EventArgs e)
        {
            TransparencyValue = trackBar.Value;
            lblValue.Text = $"{100 - TransparencyValue}%";
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }

    public partial class ProgressForm2 : System.Windows.Forms.Form
    {
        public System.Windows.Forms.ProgressBar ProgressBar => progressBar;
        public Label StatusLabel => labelStatus;
        public bool CancelRequested { get; private set; }

        public ProgressForm2()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.labelStatus = new Label();
            this.buttonCancel = new Button();

            this.SuspendLayout();

            // progressBar
            this.progressBar.Location = new System.Drawing.Point(15, 30);
            this.progressBar.Size = new System.Drawing.Size(350, 23);
            this.progressBar.Style = ProgressBarStyle.Continuous;

            // labelStatus
            this.labelStatus.AutoSize = true;
            this.labelStatus.Location = new System.Drawing.Point(15, 10);
            this.labelStatus.Text = "Подготовка...";

            // buttonCancel
            this.buttonCancel.Location = new System.Drawing.Point(290, 60);
            this.buttonCancel.Size = new System.Drawing.Size(75, 23);
            this.buttonCancel.Text = "Отмена";
            this.buttonCancel.Click += (s, e) => { CancelRequested = true; };

            // ProgressForm
            this.ClientSize = new System.Drawing.Size(380, 95);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.buttonCancel);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "Обработка коллизий";
            this.TopMost = true;
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.ProgressBar progressBar;
        private Label labelStatus;
        private Button buttonCancel;
    }
}