using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Text.RegularExpressions;

// Алиасы для разрешения конфликтов имен
using RevitGrid = Autodesk.Revit.DB.Grid;
using WpfGrid = System.Windows.Controls.Grid;
using WpfBinding = System.Windows.Data.Binding;
using TextBox = System.Windows.Controls.TextBox;

namespace RevitAddIn2BIMRU.Commands.SP
{
    [Transaction(TransactionMode.Manual)]

    public class SetPanelDimensions : IExternalCommand
    {
        private Document _doc; // Поле класса для доступа к Document
        private Wall _selectedWall;
        private string _groupingValue;
        private List<ElementId> _panelIds;
        private Dictionary<ElementId, double> _panelAreas;
        private double _totalArea;
        private int _totalPanelCount;

        // Данные для расчетов
        private double _totalPanelLength; // Общая длина всех панелей (сумма ширин) в мм
        private double _totalVerticalMullionLength; // Сумма всех длин вертикальных импостов в мм
        private double _totalPanelPerimeter; // Суммарная длина всех панелей = 2*(ширина+высота) для каждой панели в мм
        private int _columnsPerPanel = 2; // Количество колонн на одну панель (по умолчанию 2)

        // Данные материалов
        private List<MaterialItem> _materials;

        // Формулы для расчетов
        private List<FormulaItem> _formulas;

        // Словарь для соответствия толщины и веса на м²
        private Dictionary<int, double> _thicknessToWeightMap = new Dictionary<int, double>
        {
            { 50, 15 },    // 50 мм -> 15 кг/м²
            { 100, 25 },   // 100 мм -> 25 кг/м²
            { 150, 30 },   // 150 мм -> 30 кг/м²
            { 200, 37 },   // 200 мм -> 37 кг/м²
            { 250, 45 }    // 250 мм -> 45 кг/м²
        };

        public class MaterialItem
        {
            public int Number { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
            public double Quantity { get; set; }
        }

        public class FormulaItem
        {
            public int Number { get; set; }
            public string Expression { get; set; }
            public string Description { get; set; }
            public double Result { get; set; }
        }

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            _doc = uidoc.Document; // Сохраняем Document в поле класса

            try
            {
                // 1. Получаем выделенную СТЕНУ
                Selection sel = uidoc.Selection;
                if (sel.GetElementIds().Count != 1)
                {
                    TaskDialog.Show("Ошибка", "Выберите одну стену витража");
                    return Result.Cancelled;
                }

                ElementId wallId = sel.GetElementIds().First();
                Element element = _doc.GetElement(wallId);

                // Проверка: является ли элемент стеной
                Wall wall = element as Wall;
                if (wall == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является стеной");
                    return Result.Failed;
                }

                // Проверка: является ли стена витражом
                if (wall.CurtainGrid == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранная стена не является витражом (нет сетки)");
                    return Result.Failed;
                }

                _selectedWall = wall;

                // 2. Получаем значение ADSK_Группирование из стены
                _groupingValue = GetWallGroupingParameter(wall);

                // 3. Получаем ВСЕ панели витража стены через GetDependentElements
                _panelIds = GetCurtainWallPanelsFromWall(_doc, wall);

                if (_panelIds.Count == 0)
                {
                    TaskDialog.Show("Информация", "В выбранной стене не найдено панелей витража");
                    return Result.Succeeded;
                }

                _totalPanelCount = _panelIds.Count;

                // 4. Получаем значение параметра 2BIM_ПВ_КоличествоКолонНаОднуПанель из стены
                string columnsPerPanelStr = GetWallColumnsPerPanelParameter(wall);
                if (!string.IsNullOrEmpty(columnsPerPanelStr) && int.TryParse(columnsPerPanelStr, out int columns))
                {
                    _columnsPerPanel = columns;
                }

                // 5. Инициализируем данные материалов и формул
                InitializeMaterials();
                InitializeFormulas();

                // 6. Показываем WPF окно для ввода данных материалов и формул
                var dialog = new MaterialsInputWindow(_materials, _formulas);

                var result = dialog.ShowDialog();

                if (result != true)
                {
                    return Result.Cancelled; // Пользователь нажал Отмена
                }

                // Получаем обновленные данные из окна
                _materials = dialog.Materials;
                _formulas = dialog.Formulas;

                // 7. Словарь для хранения площадей панелей
                _panelAreas = new Dictionary<ElementId, double>();
                _totalArea = 0;
                _totalPanelLength = 0;
                _totalPanelPerimeter = 0;

                // 8. Обрабатываем каждую панель и собираем данные для расчетов
                int successCount = 0;
                int errorCount = 0;
                List<string> errors = new List<string>();
                List<string> groupingLog = new List<string>();
#if REVIT2023
                using (Transaction trans = new Transaction(_doc, "Запись размеров, площади и параметров панелей витража"))
                {
                    trans.Start();

                    foreach (ElementId panelId in _panelIds)
                    {
                        Element panel = _doc.GetElement(panelId);

                        string processResult = ProcessSinglePanel(panel, _groupingValue, out double panelArea, out int width, out int height);

                        if (processResult == null)
                        {
                            successCount++;
                            _panelAreas[panelId] = panelArea;
                            _totalArea += panelArea;
                            _totalPanelLength += width; // Сумма ширин всех панелей в мм
                            _totalPanelPerimeter += 2 * (width + height); // Сумма периметров всех панелей в мм

                            if (!string.IsNullOrEmpty(_groupingValue))
                            {
                                groupingLog.Add($"Панель ID {panelId.IntegerValue}: группировка '{_groupingValue}'");
                            }
                        }
                        else
                        {
                            errorCount++;
                            errors.Add($"Панель ID {panelId.IntegerValue}: {processResult}");
                        }
                    }

                    // 9. Получаем сумму длин вертикальных импостов (в мм)
                    _totalVerticalMullionLength = GetTotalVerticalMullionLengthFromPanels(_selectedWall);

                    // 10. Выполняем расчеты по формулам и записываем в параметры
                    if (_panelIds.Count > 0)
                    {
                        // Выполняем расчеты по формулам
                        var calculations = PerformCalculations(
                            _totalArea,
                            _totalPanelLength,
                            _totalPanelPerimeter,
                            _totalVerticalMullionLength,
                            _columnsPerPanel,
                            _totalPanelCount,
                            _formulas);

                        foreach (ElementId panelId in _panelIds)
                        {
                            Element panel = _doc.GetElement(panelId);

                            // Записываем результаты расчетов в параметры (только для 1,3-13, т.к. 2 больше не используется)
                            for (int i = 1; i <= 13; i++)
                            {
                                // Пропускаем расчет 2, так как он больше не используется
                                if (i == 2) continue;

                                string calcError = WriteCalculationParameter(panel, i, calculations[$"Calc{i}"]);
                                if (calcError != null)
                                {
                                    errors.Add($"Панель ID {panelId.IntegerValue} (2BIM_ПВ_Расчет{i}): {calcError}");
                                    errorCount++;
                                    successCount--;
                                }
                            }
                        }
                    }

                    // 11. Записываем общее количество панелей во все панели
                    if (_panelIds.Count > 0)
                    {
                        foreach (ElementId panelId in _panelIds)
                        {
                            Element panel = _doc.GetElement(panelId);
                            string countError = WriteTotalPanelCountParameter(panel, _totalPanelCount);

                            if (countError != null)
                            {
                                errors.Add($"Панель ID {panelId.IntegerValue} (общее количество): {countError}");
                                errorCount++;
                                successCount--;
                            }
                        }
                    }

                    // 12. Записываем общую площадь во все панели
                    if (_panelAreas.Count > 0)
                    {
                        string totalAreaStr = _totalArea.ToString("F2"); // Форматируем до 2 знаков после запятой

                        foreach (ElementId panelId in _panelIds)
                        {
                            if (_panelAreas.ContainsKey(panelId)) // Только успешно обработанные панели
                            {
                                Element panel = _doc.GetElement(panelId);

                                // Записываем текстовый параметр общей площади
                                string areaError = WriteTotalAreaParameter(panel, totalAreaStr);
                                if (areaError != null)
                                {
                                    errors.Add($"Панель ID {panelId.IntegerValue} (общая площадь текст): {areaError}");
                                    errorCount++;
                                    successCount--;
                                }

                                // Записываем числовой параметр общей площади
                                string totalAreaDoubleError = WriteTotalAreaDoubleParameter(panel, _totalArea);
                                if (totalAreaDoubleError != null)
                                {
                                    errors.Add($"Панель ID {panelId.IntegerValue} (общая площадь число): {totalAreaDoubleError}");
                                    errorCount++;
                                    successCount--;
                                }
                            }
                        }
                    }

                    // 13. Записываем наименования материалов в параметры 2BIM_ПВ_1 - 2BIM_ПВ_13
                    if (_materials != null && _materials.Count > 0)
                    {
                        foreach (ElementId panelId in _panelIds)
                        {
                            Element panel = _doc.GetElement(panelId);

                            for (int i = 0; i < _materials.Count; i++)
                            {
                                var material = _materials[i];
                                // Номер параметра = номер материала (начиная с 1)
                                // Пропускаем параметр 2 (он не используется)
                                int paramNumber = i + 1;
                                if (paramNumber == 2) continue; // Пропускаем запись в 2BIM_ПВ_2

                                string materialError = WriteMaterialNameParameter(panel, material, paramNumber);

                                if (materialError != null)
                                {
                                    errors.Add($"Панель ID {panelId.IntegerValue} (параметр 2BIM_ПВ_{paramNumber}): {materialError}");
                                    errorCount++;
                                    successCount--;
                                }
                            }
                        }
                    }

                    // 14. Записываем комментарии в стену
                    string commentsError = WriteWallCommentsParameter(_selectedWall, _materials);
                    if (commentsError != null)
                    {
                        errors.Add($"Стена (комментарии): {commentsError}");
                    }

                    trans.Commit();
                }
#endif
                // 15. Выводим результат
                string resultMessage = $"✅ Обработано панелей: {successCount} из {_panelIds.Count}\n";
                resultMessage += $"📊 Общая площадь всех панелей: {_totalArea:F2} м²\n";
                resultMessage += $"📏 Общая длина всех панелей (сумма ширин): {_totalPanelLength / 1000.0:F2} м\n";
                resultMessage += $"📐 Суммарная длина всех панелей (периметр): {_totalPanelPerimeter / 1000.0:F2} м\n";
                resultMessage += $"📏 Сумма длин вертикальных импостов: {_totalVerticalMullionLength / 1000.0:F2} м\n";
                resultMessage += $"🔢 Общее количество панелей: {_totalPanelCount}\n";

                if (!string.IsNullOrEmpty(_groupingValue))
                {
                    resultMessage += $"\n📋 ADSK_Группирование стены: \"{_groupingValue}\"\n";
                    resultMessage += $"🔄 Передано во все панели\n";
                }
                else
                {
                    resultMessage += $"\n⚠️ ADSK_Группирование стены: не заполнен (пусто)\n";
                }

                if (errorCount > 0)
                {
                    resultMessage += $"\n❌ Ошибок: {errorCount}\n\n";
                    resultMessage += "Детали ошибок:\n";
                    resultMessage += string.Join("\n", errors.Take(20));

                    if (errors.Count > 20)
                        resultMessage += $"\n... и еще {errors.Count - 20} ошибок";
                }
                else
                {
                    resultMessage += "\n✓ Все панели успешно обработаны!";
                }

                TaskDialog.Show("Результат", resultMessage);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Получение суммарной длины всех вертикальных импостов через высоты панелей (после записи параметров)
        /// </summary>
        private double GetTotalVerticalMullionLengthFromPanels(Wall wall)
        {
            try
            {
                if (wall.CurtainGrid == null)
                    return 0;

                // Получаем все панели стены
                List<ElementId> panelIds = GetCurtainWallPanelsFromWall(_doc, wall);

                if (panelIds.Count == 0)
                    return 0;

                // Собираем все уникальные вертикальные координаты X для определения количества рядов
                HashSet<double> xCoordinates = new HashSet<double>();
                Dictionary<double, List<double>> panelHeightsByX = new Dictionary<double, List<double>>();

                foreach (ElementId panelId in panelIds)
                {
                    Element panel = _doc.GetElement(panelId);

                    // Получаем bounding box панели для определения ее X координат
                    BoundingBoxXYZ bbox = panel.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        double minX = Math.Min(bbox.Min.X, bbox.Max.X);
                        double maxX = Math.Max(bbox.Min.X, bbox.Max.X);

                        xCoordinates.Add(minX);
                        xCoordinates.Add(maxX);

                        // Получаем высоту панели
                        double panelHeight = 0;
                        Parameter heightParam = panel.LookupParameter("2BIM_ПВ_ВысотаLmm");
                        if (heightParam != null && heightParam.HasValue && heightParam.StorageType == StorageType.Double)
                        {
                            panelHeight = heightParam.AsDouble();
                        }
                        else
                        {
                            // Если нет параметра, используем геометрию
                            if (TryGetPanelDimensions(panel, out double width, out double height, out _))
                            {
                                panelHeight = ConvertFeetToMillimeters(height);
                            }
                        }

                        // Добавляем высоту для левой и правой границы
                        if (!panelHeightsByX.ContainsKey(minX))
                            panelHeightsByX[minX] = new List<double>();
                        panelHeightsByX[minX].Add(panelHeight);

                        if (!panelHeightsByX.ContainsKey(maxX))
                            panelHeightsByX[maxX] = new List<double>();
                        panelHeightsByX[maxX].Add(panelHeight);
                    }
                }

                // Сортируем X координаты
                var sortedX = xCoordinates.OrderBy(x => x).ToList();

                // Количество вертикальных линий = количество уникальных X координат
                int verticalLineCount = sortedX.Count;

                if (verticalLineCount <= 1)
                    return 0;

                double totalLength = 0;

                // Для каждой внутренней вертикальной линии (не крайние) суммируем высоты
                for (int i = 1; i < verticalLineCount - 1; i++)
                {
                    double x = sortedX[i];
                    if (panelHeightsByX.ContainsKey(x) && panelHeightsByX[x].Count > 0)
                    {
                        // Берем максимальную высоту для этой линии (импост идет на всю высоту)
                        totalLength += panelHeightsByX[x].Max();
                    }
                }

                return totalLength;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при получении длин импостов из панелей: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Инициализация формул по умолчанию
        /// </summary>
        private void InitializeFormulas()
        {
            _formulas = new List<FormulaItem>
            {
                new FormulaItem { Number = 1, Expression = "Area * 0.05", Description = "Общая площадь * 0,05", Result = 0 },
                // Расчет 2 удален
                new FormulaItem { Number = 3, Expression = "CEILING(Area / 70)", Description = "Общая площадь / 70 (округление в большую до целого)", Result = 0 },
                new FormulaItem { Number = 4, Expression = "CEILING(Area / 35)", Description = "Общая площадь / 35 (округление в большую до целого)", Result = 0 },
                new FormulaItem { Number = 5, Expression = "Columns * Panels", Description = "Количество колонн на одну панель * Общее количество панелей", Result = 0 },
                new FormulaItem { Number = 6, Expression = "CEILING((Length / 1000) * 2 / 30)", Description = "Общая длина всех панелей * 2 / 30 (округление в большую до целого)", Result = 0 },
                new FormulaItem { Number = 7, Expression = "CEILING((Length / 1000) / 50)", Description = "Общая длина всех панелей / 50 (округление в большую до целого)", Result = 0 },
                new FormulaItem { Number = 8, Expression = "CEILING((Length / 1000) / 25)", Description = "Общая длина всех панелей / 25 (округление в большую до целого)", Result = 0 },
                new FormulaItem { Number = 9, Expression = "CEILING((MullionLength / 1000) * 3 / 30)", Description = "Сумма всех длин вертикальных импостов * 3 / 30 (округление в большую до целого)", Result = 0 },
                new FormulaItem { Number = 10, Expression = "CEILING(Columns * Panels * 3 / 100) * 100", Description = "Количество колонн на одну панель * Общее количество панелей * 3 (округление до 100)", Result = 0 },
                new FormulaItem { Number = 11, Expression = "CEILING((Perimeter / 1000) / 0.4 / 100) * 100", Description = "Суммарная длина всех панелей (периметр) / 0,4 (округление до 100)", Result = 0 },
                new FormulaItem { Number = 12, Expression = "CEILING(Area / 0.218 / 100) * 100", Description = "Общая площадь / 0,218 (округление до 100)", Result = 0 },
                new FormulaItem { Number = 13, Expression = "CEILING(Area / 0.04 / 100) * 100", Description = "Общая площадь / 0,04 (округление до 100)", Result = 0 }
            };
        }

        /// <summary>
        /// Извлечение толщины из описания типа панели
        /// </summary>
        private int ExtractThicknessFromDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
                return 0;

            try
            {
                // Ищем число после дефиса, например в "ТСП-Z-150-" нужно найти 150
                // Паттерн: ищем число, которое может быть после дефиса и перед дефисом или концом строки
                Regex regex = new Regex(@"-(\d+)-");
                Match match = regex.Match(description);

                if (match.Success && match.Groups.Count > 1)
                {
                    if (int.TryParse(match.Groups[1].Value, out int thickness))
                    {
                        return thickness;
                    }
                }

                // Альтернативный паттерн: ищем просто число в строке
                regex = new Regex(@"\d+");
                match = regex.Match(description);

                if (match.Success)
                {
                    if (int.TryParse(match.Value, out int thickness))
                    {
                        return thickness;
                    }
                }
            }
            catch (Exception)
            {
                // Игнорируем ошибки парсинга
            }

            return 0;
        }

        /// <summary>
        /// Получение веса на м² на основе толщины
        /// </summary>
        private double GetWeightPerSquareMeter(int thickness)
        {
            // Ищем точное соответствие в словаре
            if (_thicknessToWeightMap.ContainsKey(thickness))
            {
                return _thicknessToWeightMap[thickness];
            }

            // Если точного соответствия нет, интерполируем между ближайшими значениями
            var sortedThicknesses = _thicknessToWeightMap.Keys.OrderBy(t => t).ToList();

            // Если толщина меньше минимальной, используем минимальный вес
            if (thickness <= sortedThicknesses.First())
            {
                return _thicknessToWeightMap[sortedThicknesses.First()];
            }

            // Если толщина больше максимальной, используем максимальный вес
            if (thickness >= sortedThicknesses.Last())
            {
                return _thicknessToWeightMap[sortedThicknesses.Last()];
            }

            // Интерполяция между двумя ближайшими значениями
            for (int i = 0; i < sortedThicknesses.Count - 1; i++)
            {
                int lowerThickness = sortedThicknesses[i];
                int upperThickness = sortedThicknesses[i + 1];

                if (thickness > lowerThickness && thickness < upperThickness)
                {
                    double lowerWeight = _thicknessToWeightMap[lowerThickness];
                    double upperWeight = _thicknessToWeightMap[upperThickness];

                    // Линейная интерполяция
                    double ratio = (double)(thickness - lowerThickness) / (upperThickness - lowerThickness);
                    return lowerWeight + (upperWeight - lowerWeight) * ratio;
                }
            }

            return 0;
        }

        /// <summary>
        /// Расчет массы панели на основе площади и толщины
        /// </summary>
        private double CalculatePanelMass(double area, int thickness)
        {
            if (area <= 0 || thickness <= 0)
                return 0;

            double weightPerM2 = GetWeightPerSquareMeter(thickness);
            return area * weightPerM2;
        }

        /// <summary>
        /// Запись массы панели в параметр 2BIM_ПВ_Масса
        /// </summary>
        private string WritePanelMassParameter(Element panel, double mass)
        {
            try
            {
                // Ищем параметр 2BIM_ПВ_Масса
                Parameter massParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_Масса", StringComparison.OrdinalIgnoreCase))
                        {
                            massParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (massParam == null)
                {
                    massParam = panel.LookupParameter("2BIM_ПВ_Масса");
                }

                if (massParam == null)
                {
                    return "Параметр '2BIM_ПВ_Масса' не найден в панели";
                }

                if (massParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_Масса' только для чтения";
                }

                // Записываем значение в зависимости от типа параметра
                if (massParam.StorageType == StorageType.Double)
                {
                    massParam.Set(mass);
                }
                else if (massParam.StorageType == StorageType.Integer)
                {
                    massParam.Set(Convert.ToInt32(mass));
                }
                else if (massParam.StorageType == StorageType.String)
                {
                    massParam.Set(mass.ToString("F1"));
                }
                else
                {
                    return $"Параметр '2BIM_ПВ_Масса' имеет неподдерживаемый тип: {massParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи 2BIM_ПВ_Масса: {ex.Message}";
            }
        }

        /// <summary>
        /// Выполнение всех расчетов по формулам
        /// </summary>
        private Dictionary<string, double> PerformCalculations(
            double totalArea,      // в м²
            double totalLength,    // в мм
            double totalPerimeter, // в мм
            double verticalMullionLength, // в мм
            int columnsPerPanel,
            int totalPanels,
            List<FormulaItem> formulas)
        {
            var results = new Dictionary<string, double>();

            // Создаем словарь с переменными для подстановки в формулы
            var variables = new Dictionary<string, double>
            {
                { "Area", totalArea },
                { "Length", totalLength },
                { "Perimeter", totalPerimeter },
                { "MullionLength", verticalMullionLength },
                { "Columns", columnsPerPanel },
                { "Panels", totalPanels }
            };

            for (int i = 1; i <= 13; i++)
            {
                // Для расчета 2 возвращаем 0 (не используется)
                if (i == 2)
                {
                    results[$"Calc{i}"] = 0;
                    continue;
                }

                var formula = formulas.FirstOrDefault(f => f.Number == i);
                if (formula != null && !string.IsNullOrWhiteSpace(formula.Expression))
                {
                    try
                    {
                        // Вычисляем значение по формуле
                        double result = EvaluateFormula(formula.Expression, variables);
                        results[$"Calc{i}"] = result;
                        formula.Result = result; // Обновляем результат в формуле
                    }
                    catch (Exception)
                    {
                        results[$"Calc{i}"] = 0; // В случае ошибки возвращаем 0
                    }
                }
                else
                {
                    results[$"Calc{i}"] = 0;
                }
            }

            return results;
        }

        /// <summary>
        /// Упрощенный парсер математических выражений
        /// </summary>
        private double EvaluateFormula(string expression, Dictionary<string, double> variables)
        {
            try
            {
                // Заменяем переменные на их значения
                string processedExpr = expression;
                foreach (var var in variables)
                {
                    // Используем более точную замену с учетом границ слова
                    processedExpr = System.Text.RegularExpressions.Regex.Replace(
                        processedExpr,
                        @"\b" + var.Key + @"\b",
                        var.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    );
                }

                // Обрабатываем функцию CEILING рекурсивно, пока есть вхождения
                processedExpr = ProcessCeilingFunction(processedExpr);

                // Вычисляем итоговое выражение
                System.Data.DataTable finalTable = new System.Data.DataTable();
                object result = finalTable.Compute(processedExpr, "");
                return Convert.ToDouble(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка вычисления формулы: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Рекурсивная обработка функции CEILING
        /// </summary>
        private string ProcessCeilingFunction(string expression)
        {
            // Регулярное выражение для поиска CEILING с учетом вложенных скобок
            // Находит CEILING( ... ) где ... может содержать любое количество вложенных скобок
            System.Text.RegularExpressions.Regex ceilingRegex = new System.Text.RegularExpressions.Regex(
                @"CEILING\(((?:[^()]|(?<open>\()|(?<-open>\)))*(?(open)(?!)))\)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            System.Text.RegularExpressions.Match match = ceilingRegex.Match(expression);

            if (!match.Success)
                return expression;

            string innerExpr = match.Groups[1].Value;

            try
            {
                // Рекурсивно обрабатываем внутреннее выражение (на случай вложенных CEILING)
                innerExpr = ProcessCeilingFunction(innerExpr);

                // Вычисляем внутреннее выражение
                System.Data.DataTable table = new System.Data.DataTable();
                double innerValue = Convert.ToDouble(table.Compute(innerExpr, ""));

                // Применяем CEILING
                double ceilingValue = Math.Ceiling(innerValue);

                // Заменяем найденное CEILING на вычисленное значение
                string newExpr = expression.Substring(0, match.Index) +
                                ceilingValue.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                expression.Substring(match.Index + match.Length);

                // Рекурсивно обрабатываем результат на случай других CEILING
                return ProcessCeilingFunction(newExpr);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в CEILING: {ex.Message}");
                // В случае ошибки оставляем выражение как есть
                return expression;
            }
        }

        /// <summary>
        /// Запись результата расчета в параметр 2BIM_ПВ_РасчетN
        /// </summary>
        private string WriteCalculationParameter(Element panel, int index, double value)
        {
            try
            {
                string paramName = $"2BIM_ПВ_Расчет{index}";

                // Ищем параметр в панели
                Parameter calcParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null)
                    {
                        string name = param.Definition.Name;

                        if (name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                        {
                            calcParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (calcParam == null)
                {
                    calcParam = panel.LookupParameter(paramName);
                }

                if (calcParam == null)
                {
                    return $"Параметр '{paramName}' не найден в панели";
                }

                if (calcParam.IsReadOnly)
                {
                    return $"Параметр '{paramName}' только для чтения";
                }

                // Записываем значение в зависимости от типа параметра
                if (calcParam.StorageType == StorageType.Double)
                {
                    calcParam.Set(value);
                }
                else if (calcParam.StorageType == StorageType.Integer)
                {
                    calcParam.Set(Convert.ToInt32(value));
                }
                else if (calcParam.StorageType == StorageType.String)
                {
                    calcParam.Set(value.ToString("F0"));
                }
                else
                {
                    return $"Параметр '{paramName}' имеет неподдерживаемый тип: {calcParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи параметра {index}: {ex.Message}";
            }
        }

        /// <summary>
        /// Получение суммарной длины всех вертикальных импостов в стене (в мм)
        /// </summary>
        private double GetTotalVerticalMullionLength(Wall wall)
        {
            double totalLengthFeet = 0;

            try
            {
                if (wall.CurtainGrid == null)
                    return 0;

                // Получаем все импосты в документе
                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                ICollection<Mullion> mullions = collector
                    .OfClass(typeof(Mullion))
                    .Cast<Mullion>()
                    .Where(m => m.Host != null && m.Host.Id == wall.Id) // Импосты, принадлежащие нашей стене
                    .ToList();

                foreach (Mullion mullion in mullions)
                {
                    // Получаем геометрию импоста
                    Options geoOptions = new Options();
                    geoOptions.ComputeReferences = false;
                    geoOptions.DetailLevel = ViewDetailLevel.Fine;

                    GeometryElement geoElement = mullion.get_Geometry(geoOptions);

                    if (geoElement != null)
                    {
                        double mullionLength = 0;
                        XYZ direction = null;

                        // Анализируем геометрию для определения длины и ориентации
                        foreach (GeometryObject geoObj in geoElement)
                        {
                            Solid solid = geoObj as Solid;
                            if (solid != null && solid.Volume > 0)
                            {
                                // Получаем bounding box твердого тела
                                BoundingBoxXYZ bbox = solid.GetBoundingBox();
                                if (bbox != null)
                                {
                                    double xSize = bbox.Max.X - bbox.Min.X;
                                    double ySize = bbox.Max.Y - bbox.Min.Y;
                                    double zSize = bbox.Max.Z - bbox.Min.Z;

                                    // Находим максимальный размер - это длина импоста
                                    mullionLength = Math.Max(Math.Max(xSize, ySize), zSize);

                                    // Определяем направление максимального размера
                                    if (Math.Abs(mullionLength - zSize) < 0.001)
                                        direction = new XYZ(0, 0, 1);
                                    else if (Math.Abs(mullionLength - ySize) < 0.001)
                                        direction = new XYZ(0, 1, 0);
                                    else
                                        direction = new XYZ(1, 0, 0);
                                }
                                break;
                            }

                            GeometryInstance instance = geoObj as GeometryInstance;
                            if (instance != null)
                            {
                                GeometryElement instanceGeo = instance.GetInstanceGeometry();
                                foreach (GeometryObject instObj in instanceGeo)
                                {
                                    Solid instSolid = instObj as Solid;
                                    if (instSolid != null && instSolid.Volume > 0)
                                    {
                                        BoundingBoxXYZ bbox = instSolid.GetBoundingBox();
                                        if (bbox != null)
                                        {
                                            double xSize = bbox.Max.X - bbox.Min.X;
                                            double ySize = bbox.Max.Y - bbox.Min.Y;
                                            double zSize = bbox.Max.Z - bbox.Min.Z;

                                            mullionLength = Math.Max(Math.Max(xSize, ySize), zSize);

                                            if (Math.Abs(mullionLength - zSize) < 0.001)
                                                direction = new XYZ(0, 0, 1);
                                            else if (Math.Abs(mullionLength - ySize) < 0.001)
                                                direction = new XYZ(0, 1, 0);
                                            else
                                                direction = new XYZ(1, 0, 0);
                                        }
                                        break;
                                    }
                                }
                            }
                        }

                        // Если импост вертикальный (направление по Z или Y в зависимости от ориентации стены)
                        if (direction != null && mullionLength > 0)
                        {
                            // Проверяем, что импост не горизонтальный
                            // Горизонтальные импосты имеют максимальный размер по X
                            if (Math.Abs(direction.X) < 0.1) // Не горизонтальный
                            {
                                totalLengthFeet += mullionLength;
                            }
                        }
                    }

                    // Альтернативный способ - через параметры, если геометрия не дала результата
                    if (totalLengthFeet == 0)
                    {
                        // Пробуем найти параметр длины
                        Parameter lengthParam = null;

                        // Перебираем все параметры в поисках длины
                        foreach (Parameter param in mullion.Parameters)
                        {
                            if (param.Definition != null)
                            {
                                string paramName = param.Definition.Name.ToLower();
                                if (paramName.Contains("length") || paramName.Contains("длина"))
                                {
                                    if (param.StorageType == StorageType.Double && param.HasValue)
                                    {
                                        lengthParam = param;
                                        break;
                                    }
                                }
                            }
                        }

                        if (lengthParam != null)
                        {
                            // Определяем ориентацию по bounding box
                            BoundingBoxXYZ bbox = mullion.get_BoundingBox(null);
                            if (bbox != null)
                            {
                                double xSize = bbox.Max.X - bbox.Min.X;
                                double ySize = bbox.Max.Y - bbox.Min.Y;
                                double zSize = bbox.Max.Z - bbox.Min.Z;

                                double maxSize = Math.Max(Math.Max(xSize, ySize), zSize);

                                // Если максимальный размер не по X (не горизонтальный)
                                if (maxSize > xSize * 1.5)
                                {
                                    totalLengthFeet += lengthParam.AsDouble();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при получении длин импостов: {ex.Message}");
            }

            // Конвертируем футы в миллиметры
            return ConvertFeetToMillimeters(totalLengthFeet);
        }

        /// <summary>
        /// Получение значения параметра 2BIM_ПВ_КоличествоКолонНаОднуПанель из стены
        /// </summary>
        private string GetWallColumnsPerPanelParameter(Element wall)
        {
            try
            {
                // Ищем параметр 2BIM_ПВ_КоличествоКолонНаОднуПанель
                Parameter columnsParam = null;

                // Сначала через LookParameter
                columnsParam = wall.LookupParameter("2BIM_ПВ_КоличествоКолонНаОднуПанель");

                // Если не нашли, ищем через перебор параметров
                if (columnsParam == null)
                {
                    foreach (Parameter param in wall.Parameters)
                    {
                        if (param.Definition != null &&
                            param.Definition.Name.Equals("2BIM_ПВ_КоличествоКолонНаОднуПанель", StringComparison.OrdinalIgnoreCase))
                        {
                            columnsParam = param;
                            break;
                        }
                    }
                }

                if (columnsParam != null && columnsParam.HasValue)
                {
                    if (columnsParam.StorageType == StorageType.String)
                    {
                        return columnsParam.AsString();
                    }
                    else if (columnsParam.StorageType == StorageType.Integer)
                    {
                        return columnsParam.AsInteger().ToString();
                    }
                    else if (columnsParam.StorageType == StorageType.Double)
                    {
                        return columnsParam.AsDouble().ToString();
                    }
                }

                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Запись наименования материала в параметр 2BIM_ПВ_N
        /// </summary>
        private string WriteMaterialNameParameter(Element panel, MaterialItem material, int index)
        {
            try
            {
                string paramName = $"2BIM_ПВ_{index}";

                // Ищем параметр в панели
                Parameter materialParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null)
                    {
                        string name = param.Definition.Name;

                        if (name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                        {
                            materialParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (materialParam == null)
                {
                    materialParam = panel.LookupParameter(paramName);
                }

                if (materialParam == null)
                {
                    return $"Параметр '{paramName}' не найден в панели";
                }

                if (materialParam.IsReadOnly)
                {
                    return $"Параметр '{paramName}' только для чтения";
                }

                // Записываем значение (только наименование материала)
                if (materialParam.StorageType == StorageType.String)
                {
                    materialParam.Set(material.Name);
                }
                else
                {
                    return $"Параметр '{paramName}' должен быть текстовым (String), а он: {materialParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи параметра {index}: {ex.Message}";
            }
        }

        /// <summary>
        /// Конвертация миллиметров в футы (для записи в Revit)
        /// </summary>
        private double ConvertMillimetersToFeet(double mm)
        {
            return mm / 304.8;
        }

        /// <summary>
        /// Конвертация футов в миллиметры
        /// </summary>
        private double ConvertFeetToMillimeters(double feet)
        {
            return feet * 304.8;
        }

        /// <summary>
        /// Конвертация квадратных метров в квадратные футы (для записи в Revit)
        /// </summary>
        private double ConvertSquareMetersToSquareFeet(double m2)
        {
            // 1 квадратный метр = 10.76391 квадратных футов
            return m2 * 10.76391041671;
        }

        /// <summary>
        /// Конвертация квадратных футов в квадратные метры
        /// </summary>
        private double ConvertSquareFeetToSquareMeters(double sqft)
        {
            // 1 квадратный фут = 0.092903 квадратных метра
            return sqft * 0.09290304;
        }

        /// <summary>
        /// Запись комментариев в стену на основе данных материалов
        /// </summary>
        private string WriteWallCommentsParameter(Wall wall, List<MaterialItem> materials)
        {
            try
            {
                if (materials == null || materials.Count == 0)
                {
                    return null; // Ничего не записываем
                }

                // Формируем строку комментариев: Наименование1 - Количество1 ед.изм., Наименование2 - Количество2 ед.изм., ...
                List<string> commentsParts = new List<string>();

                foreach (var material in materials)
                {
                    if (material.Quantity > 0) // Добавляем только материалы с количеством > 0
                    {
                        string part = $"{material.Name} - {material.Quantity} {material.Unit}";
                        commentsParts.Add(part);
                    }
                }

                if (commentsParts.Count == 0)
                {
                    return null; // Нет материалов с количеством
                }

                string commentsValue = string.Join(", ", commentsParts);

                // Ищем параметр "Комментарии" в стене
                Parameter commentsParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in wall.Parameters)
                {
                    if (param.Definition != null)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("Комментарии", StringComparison.OrdinalIgnoreCase))
                        {
                            commentsParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (commentsParam == null)
                {
                    commentsParam = wall.LookupParameter("Комментарии");
                }

                if (commentsParam == null)
                {
                    return "Параметр 'Комментарии' не найден в стене";
                }

                if (commentsParam.IsReadOnly)
                {
                    return "Параметр 'Комментарии' только для чтения";
                }

                // Записываем значение
                if (commentsParam.StorageType == StorageType.String)
                {
                    commentsParam.Set(commentsValue);
                }
                else
                {
                    return $"Параметр 'Комментарии' должен быть текстовым (String), а он: {commentsParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи комментариев: {ex.Message}";
            }
        }

        /// <summary>
        /// Инициализация данных материалов по умолчанию (удален 2-й материал)
        /// </summary>
        private void InitializeMaterials()
        {
            _materials = new List<MaterialItem>
            {
                new MaterialItem { Number = 1, Name = "Теплоизоляционные плиты ISOVER Каркас П-34-50/Е 1170х610х50 мм (0.714 куб.м)", Unit = "м³", Quantity = 0 },
                // Материал 2 удален
                new MaterialItem { Number = 2, Name = "Теплоизоляционные плиты ISOVER Каркас П-34-150/Е 1170х610х150 мм (0.714 куб.м)", Unit = "м³", Quantity = 0 },
                new MaterialItem { Number = 3, Name = "Мембрана гидроизоляционная ветрозащитная огнезащитная Tyvek FireCurb HouseWrap без логотипа (1.5х50 м)", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 4, Name = "Соединительная лента двухстороняя Tyvek Double -sides Tape (0,05 х 25 м)", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 5, Name = "Уплотнитель колонна-сэндвич х 620", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 6, Name = "Уплотнитель сэндвича горизонтальный (10мм х 30м)", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 7, Name = "Алюминиевая клейкая лента (50 м.п.)", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 8, Name = "Уплотнитель цоколя 150х25000", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 9, Name = "Уплотнитель терморазделяющая полоса (45мм х 30м)", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 10, Name = "Саморез 5,5х32 (5,5х38) оцинк. со сверлом 12 мм", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 11, Name = "Саморез 4,2х16 оцинк с прессшайбой", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 12, Name = "Саморез 4,8х28", Unit = "шт.", Quantity = 0 },
                new MaterialItem { Number = 13, Name = "Заклепка 4,8х10 нерж. стальная", Unit = "шт.", Quantity = 0 }
            };
        }

        /// <summary>
        /// Получение значения параметра ADSK_Группирование из стены
        /// </summary>
        private string GetWallGroupingParameter(Element wall)
        {
            try
            {
                // Ищем параметр ADSK_Группирование
                Parameter groupingParam = null;

                // Сначала через LookParameter
                groupingParam = wall.LookupParameter("ADSK_Группирование");

                // Если не нашли, ищем через перебор параметров
                if (groupingParam == null)
                {
                    foreach (Parameter param in wall.Parameters)
                    {
                        if (param.Definition != null &&
                            param.Definition.Name.Equals("ADSK_Группирование", StringComparison.OrdinalIgnoreCase))
                        {
                            groupingParam = param;
                            break;
                        }
                    }
                }

                if (groupingParam != null && groupingParam.HasValue)
                {
                    // Для текстового параметра
                    if (groupingParam.StorageType == StorageType.String)
                    {
                        return groupingParam.AsString();
                    }
                    // Для параметра с выбором из списка
                    else if (groupingParam.StorageType == StorageType.Integer)
                    {
                        return groupingParam.AsValueString();
                    }
                }

                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Запись значения ADSK_Группирование в панель
        /// </summary>
        private string WritePanelGroupingParameter(Element panel, string groupingValue)
        {
            try
            {
                if (string.IsNullOrEmpty(groupingValue))
                {
                    return null; // Ничего не записываем, но это не ошибка
                }

                // Ищем параметр ADSK_Группирование в панели
                Parameter groupingParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("ADSK_Группирование", StringComparison.OrdinalIgnoreCase))
                        {
                            groupingParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (groupingParam == null)
                {
                    groupingParam = panel.LookupParameter("ADSK_Группирование");
                }

                if (groupingParam == null)
                {
                    return "Параметр 'ADSK_Группирование' не найден в панели";
                }

                if (groupingParam.IsReadOnly)
                {
                    return "Параметр 'ADSK_Группирование' только для чтения";
                }

                // Записываем значение в зависимости от типа параметра
                if (groupingParam.StorageType == StorageType.String)
                {
                    groupingParam.Set(groupingValue);
                }
                else if (groupingParam.StorageType == StorageType.Integer)
                {
                    // Для параметров с выбором из списка
                    groupingParam.SetValueString(groupingValue);
                }
                else
                {
                    return $"Параметр 'ADSK_Группирование' имеет неподдерживаемый тип: {groupingParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи ADSK_Группирование: {ex.Message}";
            }
        }

        /// <summary>
        /// Получение всех панелей витража из стены через GetDependentElements
        /// </summary>
        private List<ElementId> GetCurtainWallPanelsFromWall(Document doc, Wall wall)
        {
            List<ElementId> panelIds = new List<ElementId>();

            // Создаем фильтр для категории "Панели витража"
            ElementCategoryFilter panelFilter = new ElementCategoryFilter(BuiltInCategory.OST_CurtainWallPanels);

            // Получаем все зависимые элементы стены, которые являются панелями витража
            ICollection<ElementId> dependentIds = wall.GetDependentElements(panelFilter);

            if (dependentIds != null && dependentIds.Count > 0)
            {
                panelIds.AddRange(dependentIds);
            }

            // Дополнительно получаем панели из сетки витража (на всякий случай)
            if (wall.CurtainGrid != null)
            {
                ICollection<ElementId> gridPanelIds = wall.CurtainGrid.GetPanelIds();
                foreach (ElementId id in gridPanelIds)
                {
                    if (!panelIds.Contains(id))
                    {
                        panelIds.Add(id);
                    }
                }
            }

            return panelIds;
        }

        /// <summary>
        /// Обработка одной панели (размеры + группировка + обозначение + площадь + масса)
        /// </summary>
        private string ProcessSinglePanel(Element panel, string wallGroupingValue, out double panelArea, out int width, out int height)
        {
            panelArea = 0;
            width = 0;
            height = 0;

            try
            {
                // Проверка: является ли элемент панелью витража
                if (!IsCurtainWallPanel(panel))
                {
                    return "Элемент не является панелью витража";
                }

                // 1. ОБРАБОТКА РАЗМЕРОВ
                int roundedHeight = 0;
                int roundedWidth = 0;
                string dimensionError = ProcessPanelDimensions(panel, out roundedWidth, out roundedHeight);
                if (dimensionError != null)
                {
                    return dimensionError;
                }

                width = roundedWidth;
                height = roundedHeight;

                // 2. РАСЧЕТ ПЛОЩАДИ ПАНЕЛИ (в м²)
                panelArea = (roundedWidth * roundedHeight) / 1000000.0; // мм² -> м²

                // 3. ЗАПИСЬ ПЛОЩАДИ ПАНЕЛИ (текстовый параметр)
                string areaError = WritePanelAreaParameter(panel, panelArea);
                if (areaError != null)
                {
                    return areaError;
                }

                // 4. ЗАПИСЬ ПЛОЩАДИ ПАНЕЛИ (числовой параметр)
                string areaDoubleError = WritePanelAreaDoubleParameter(panel, panelArea);
                if (areaDoubleError != null)
                {
                    return areaDoubleError;
                }

                // 5. ОБРАБОТКА ГРУППИРОВКИ
                if (!string.IsNullOrEmpty(wallGroupingValue))
                {
                    string groupingError = WritePanelGroupingParameter(panel, wallGroupingValue);
                    if (groupingError != null)
                    {
                        return groupingError;
                    }
                }

                // 6. ОБРАБОТКА 2BIM_ПВ_ОБОЗНАЧЕНИЕ
                string designationError = WritePanelDesignationParameter(panel, roundedHeight, roundedWidth);
                if (designationError != null)
                {
                    return designationError;
                }

                // 7. РАСЧЕТ И ЗАПИСЬ МАССЫ ПАНЕЛИ
                string massError = CalculateAndWritePanelMass(panel, panelArea);
                if (massError != null)
                {
                    return massError;
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Исключение: {ex.Message}";
            }
        }

        /// <summary>
        /// Расчет и запись массы панели
        /// </summary>
        private string CalculateAndWritePanelMass(Element panel, double panelArea)
        {
            try
            {
                // Получаем тип панели
                ElementId typeId = panel.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId)
                {
                    return "Не удалось получить ID типа панели для расчета массы";
                }

                ElementType panelType = _doc.GetElement(typeId) as ElementType;
                if (panelType == null)
                {
                    return "Не удалось получить тип панели для расчета массы";
                }

                // Получаем значение параметра "Описание" из типа
                string description = GetParameterValueAsString(panelType, "Описание");

                // Извлекаем толщину из описания
                int thickness = ExtractThicknessFromDescription(description);

                if (thickness <= 0)
                {
                    // Если не удалось извлечь толщину, пробуем другие параметры
                    string typeName = GetParameterValueAsString(panelType, "Имя типа");
                    thickness = ExtractThicknessFromDescription(typeName);

                    if (thickness <= 0)
                    {
                        // Если всё равно не удалось, используем значение по умолчанию или пропускаем
                        return null; // Пропускаем расчет массы, но не считаем ошибкой
                    }
                }

                // Рассчитываем массу
                double mass = CalculatePanelMass(panelArea, thickness);

                if (mass > 0)
                {
                    // Записываем массу в параметр
                    string massError = WritePanelMassParameter(panel, mass);
                    if (massError != null)
                    {
                        return massError;
                    }
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка расчета массы: {ex.Message}";
            }
        }

        /// <summary>
        /// Обработка размеров панели
        /// </summary>
        private string ProcessPanelDimensions(Element panel, out int roundedWidth, out int roundedHeight)
        {
            roundedWidth = 0;
            roundedHeight = 0;

            // Получаем размеры панели
            if (!TryGetPanelDimensions(panel, out double width, out double height, out string sourceInfo))
            {
                return $"Не удалось получить размеры: {sourceInfo}";
            }

            // Конвертируем в миллиметры и округляем ВСЕГДА В БОЛЬШУЮ СТОРОНУ
            double widthMM = ConvertFeetToMillimeters(width);
            double heightMM = ConvertFeetToMillimeters(height);

            roundedWidth = RoundUpToNearestHundred(widthMM);
            roundedHeight = RoundUpToNearestHundred(heightMM);

            // Записываем в текстовые параметры
            string errorMessage = WritePanelDimensions(panel, roundedWidth, roundedHeight);
            if (errorMessage != null)
            {
                return errorMessage;
            }

            // Записываем в числовые параметры
            string doubleErrorMessage = WritePanelDimensionsDouble(panel, roundedWidth, roundedHeight);

            return doubleErrorMessage; // null если успешно, иначе текст ошибки
        }

        /// <summary>
        /// Запись размеров в ЧИСЛОВЫЕ параметры панели (Double) - ЗНАЧЕНИЯ В МИЛЛИМЕТРАХ
        /// </summary>
        private string WritePanelDimensionsDouble(Element panel, int width, int height)
        {
            try
            {
                // Ищем числовые параметры в элементе
                Parameter heightParam = null;
                Parameter widthParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null && param.StorageType == StorageType.Double)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_ВысотаLmm", StringComparison.OrdinalIgnoreCase))
                        {
                            heightParam = param;
                        }
                        else if (paramName.Equals("2BIM_ПВ_ШиринаLmm", StringComparison.OrdinalIgnoreCase))
                        {
                            widthParam = param;
                        }
                    }
                }

                // Проверяем найденные параметры
                if (heightParam == null)
                {
                    // Пробуем LookParameter как запасной вариант
                    heightParam = panel.LookupParameter("2BIM_ПВ_ВысотаLmm");
                    if (heightParam == null)
                    {
                        return "Параметр '2BIM_ПВ_ВысотаLmm' не найден";
                    }

                    // Проверяем, что это числовой параметр
                    if (heightParam.StorageType != StorageType.Double)
                    {
                        return $"Параметр '2BIM_ПВ_ВысотаLmm' должен быть числовым (Double), а он: {heightParam.StorageType}";
                    }
                }

                if (heightParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_ВысотаLmm' только для чтения";
                }

                if (widthParam == null)
                {
                    // Пробуем LookParameter как запасной вариант
                    widthParam = panel.LookupParameter("2BIM_ПВ_ШиринаLmm");
                    if (widthParam == null)
                    {
                        return "Параметр '2BIM_ПВ_ШиринаLmm' не найден";
                    }

                    // Проверяем, что это числовой параметр
                    if (widthParam.StorageType != StorageType.Double)
                    {
                        return $"Параметр '2BIM_ПВ_ШиринаLmm' должен быть числовым (Double), а он: {widthParam.StorageType}";
                    }
                }

                if (widthParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_ШиринаLmm' только для чтения";
                }

                // КОНВЕРТИРУЕМ МИЛЛИМЕТРЫ В ФУТЫ ДЛЯ ЗАПИСИ В REVIT
                double widthInFeet = ConvertMillimetersToFeet(width);
                double heightInFeet = ConvertMillimetersToFeet(height);

                // Записываем значения в числовые параметры (в футах, но параметр отображается в мм)
                heightParam.Set(heightInFeet);
                widthParam.Set(widthInFeet);

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи числовых размеров: {ex.Message}";
            }
        }

        /// <summary>
        /// Запись площади панели в ЧИСЛОВОЙ параметр 2BIM_ПВ_ПлощадьПанелиSm2 - ЗНАЧЕНИЕ В КВАДРАТНЫХ МЕТРАХ
        /// </summary>
        private string WritePanelAreaDoubleParameter(Element panel, double area)
        {
            try
            {
                // Ищем параметр 2BIM_ПВ_ПлощадьПанелиSm2
                Parameter areaParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null && param.StorageType == StorageType.Double)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_ПлощадьПанелиSm2", StringComparison.OrdinalIgnoreCase))
                        {
                            areaParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (areaParam == null)
                {
                    areaParam = panel.LookupParameter("2BIM_ПВ_ПлощадьПанелиSm2");
                }

                if (areaParam == null)
                {
                    return "Параметр '2BIM_ПВ_ПлощадьПанелиSm2' не найден в панели";
                }

                if (areaParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_ПлощадьПанелиSm2' только для чтения";
                }

                // Проверяем, что это числовой параметр
                if (areaParam.StorageType != StorageType.Double)
                {
                    return $"Параметр '2BIM_ПВ_ПлощадьПанелиSm2' должен быть числовым (Double), а он: {areaParam.StorageType}";
                }

                // КОНВЕРТИРУЕМ КВАДРАТНЫЕ МЕТРЫ В КВАДРАТНЫЕ ФУТЫ ДЛЯ ЗАПИСИ В REVIT
                double areaInSquareFeet = ConvertSquareMetersToSquareFeet(area);

                // Записываем значение (в квадратных футах, но параметр отображается в м²)
                areaParam.Set(areaInSquareFeet);

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи 2BIM_ПВ_ПлощадьПанелиSm2: {ex.Message}";
            }
        }

        /// <summary>
        /// Запись общей площади всех панелей в ЧИСЛОВОЙ параметр 2BIM_ПВ_ОбщаяПлощадьПанелейSm2 - ЗНАЧЕНИЕ В КВАДРАТНЫХ МЕТРАХ
        /// </summary>
        private string WriteTotalAreaDoubleParameter(Element panel, double totalArea)
        {
            try
            {
                // Ищем параметр 2BIM_ПВ_ОбщаяПлощадьПанелейSm2
                Parameter totalAreaParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null && param.StorageType == StorageType.Double)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_ОбщаяПлощадьПанелейSm2", StringComparison.OrdinalIgnoreCase))
                        {
                            totalAreaParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (totalAreaParam == null)
                {
                    totalAreaParam = panel.LookupParameter("2BIM_ПВ_ОбщаяПлощадьПанелейSm2");
                }

                if (totalAreaParam == null)
                {
                    return "Параметр '2BIM_ПВ_ОбщаяПлощадьПанелейSm2' не найден в панели";
                }

                if (totalAreaParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_ОбщаяПлощадьПанелейSm2' только для чтения";
                }

                // Проверяем, что это числовой параметр
                if (totalAreaParam.StorageType != StorageType.Double)
                {
                    return $"Параметр '2BIM_ПВ_ОбщаяПлощадьПанелейSm2' должен быть числовым (Double), а он: {totalAreaParam.StorageType}";
                }

                // КОНВЕРТИРУЕМ КВАДРАТНЫЕ МЕТРЫ В КВАДРАТНЫЕ ФУТЫ ДЛЯ ЗАПИСИ В REVIT
                double totalAreaInSquareFeet = ConvertSquareMetersToSquareFeet(totalArea);

                // Записываем значение (в квадратных футах, но параметр отображается в м²)
                totalAreaParam.Set(totalAreaInSquareFeet);

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи 2BIM_ПВ_ОбщаяПлощадьПанелейSm2: {ex.Message}";
            }
        }

        /// <summary>
        /// Запись площади панели в ТЕКСТОВЫЙ параметр 2BIM_ПВ_ПлощадьПанели
        /// </summary>
        private string WritePanelAreaParameter(Element panel, double area)
        {
            try
            {
                // Ищем параметр 2BIM_ПВ_ПлощадьПанели
                Parameter areaParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null && param.StorageType == StorageType.String)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_ПлощадьПанели", StringComparison.OrdinalIgnoreCase))
                        {
                            areaParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (areaParam == null)
                {
                    areaParam = panel.LookupParameter("2BIM_ПВ_ПлощадьПанели");
                }

                if (areaParam == null)
                {
                    return "Параметр '2BIM_ПВ_ПлощадьПанели' не найден в панели";
                }

                if (areaParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_ПлощадьПанели' только для чтения";
                }

                // Записываем значение с форматированием до 2 знаков после запятой
                string areaStr = area.ToString("F2");

                if (areaParam.StorageType == StorageType.String)
                {
                    areaParam.Set(areaStr);
                }
                else
                {
                    return $"Параметр '2BIM_ПВ_ПлощадьПанели' должен быть текстовым (String), а он: {areaParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи 2BIM_ПВ_ПлощадьПанели: {ex.Message}";
            }
        }

        /// <summary>
        /// Запись общего количества панелей в параметр 2BIM_ПВ_ОбщееКоличествоПанелей (Integer)
        /// </summary>
        private string WriteTotalPanelCountParameter(Element panel, int totalCount)
        {
            try
            {
                // Ищем параметр 2BIM_ПВ_ОбщееКоличествоПанелей
                Parameter countParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_ОбщееКоличествоПанелей", StringComparison.OrdinalIgnoreCase))
                        {
                            countParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (countParam == null)
                {
                    countParam = panel.LookupParameter("2BIM_ПВ_ОбщееКоличествоПанелей");
                }

                if (countParam == null)
                {
                    return "Параметр '2BIM_ПВ_ОбщееКоличествоПанелей' не найден в панели";
                }

                if (countParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_ОбщееКоличествоПанелей' только для чтения";
                }

                // Записываем значение в зависимости от типа параметра
                if (countParam.StorageType == StorageType.Integer)
                {
                    countParam.Set(totalCount);
                }
                else
                {
                    return $"Параметр '2BIM_ПВ_ОбщееКоличествоПанелей' должен быть целочисленным (Integer), а он: {countParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи 2BIM_ПВ_ОбщееКоличествоПанелей: {ex.Message}";
            }
        }

        /// <summary>
        /// Запись общей площади всех панелей в ТЕКСТОВЫЙ параметр 2BIM_ПВ_ОбщаяПлощадьПанелей
        /// </summary>
        private string WriteTotalAreaParameter(Element panel, string totalArea)
        {
            try
            {
                // Ищем параметр 2BIM_ПВ_ОбщаяПлощадьПанелей
                Parameter totalAreaParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null && param.StorageType == StorageType.String)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_ОбщаяПлощадьПанелей", StringComparison.OrdinalIgnoreCase))
                        {
                            totalAreaParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (totalAreaParam == null)
                {
                    totalAreaParam = panel.LookupParameter("2BIM_ПВ_ОбщаяПлощадьПанелей");
                }

                if (totalAreaParam == null)
                {
                    return "Параметр '2BIM_ПВ_ОбщаяПлощадьПанелей' не найден в панели";
                }

                if (totalAreaParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_ОбщаяПлощадьПанелей' только для чтения";
                }

                // Записываем значение
                if (totalAreaParam.StorageType == StorageType.String)
                {
                    totalAreaParam.Set(totalArea);
                }
                else
                {
                    return $"Параметр '2BIM_ПВ_ОбщаяПлощадьПанелей' должен быть текстовым (String), а он: {totalAreaParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи 2BIM_ПВ_ОбщаяПлощадьПанелей: {ex.Message}";
            }
        }

        /// <summary>
        /// Запись 2BIM_ПВ_Обозначение из комбинации параметров
        /// </summary>
        private string WritePanelDesignationParameter(Element panel, int panelHeight, int panelWidth)
        {
            try
            {
                // ПОЛУЧАЕМ ТИП ПАНЕЛИ
                ElementId typeId = panel.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId)
                {
                    return "Не удалось получить ID типа панели";
                }

                ElementType panelType = _doc.GetElement(typeId) as ElementType;
                if (panelType == null)
                {
                    return "Не удалось получить тип панели";
                }

                // 1. Получаем значение параметра "Описание" (из типа)
                string description = GetParameterValueAsString(panelType, "Описание");

                // 2. Получаем значение параметра "Комментарии к типоразмеру" (из типа)
                string typeComments = GetParameterValueAsString(panelType, "Комментарии к типоразмеру");

                // 3. Получаем значение параметра "ADSK_Обозначение" (из типа)
                string designation = GetParameterValueAsString(panelType, "ADSK_Обозначение");

                // 4. Получаем значение параметра "2BIM_ПВ_Высота" (из экземпляра) - уже округленное
                string heightValue = panelHeight.ToString();

                // Формируем строку для 2BIM_ПВ_Обозначение
                // 2BIM_ПВ_Обозначение = Описание + 2BIM_ПВ_Высота + Комментарии к типоразмеру + "-" + ADSK_Обозначение
                string designationValue = $"{description}{heightValue}{typeComments}-{designation}";

                // Ищем параметр 2BIM_ПВ_Обозначение в панели (экземпляр)
                Parameter designationParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_Обозначение", StringComparison.OrdinalIgnoreCase))
                        {
                            designationParam = param;
                            break;
                        }
                    }
                }

                // Если не нашли через перебор, пробуем LookParameter
                if (designationParam == null)
                {
                    designationParam = panel.LookupParameter("2BIM_ПВ_Обозначение");
                }

                if (designationParam == null)
                {
                    return "Параметр '2BIM_ПВ_Обозначение' не найден в панели";
                }

                if (designationParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_Обозначение' только для чтения";
                }

                // Записываем значение в зависимости от типа параметра
                if (designationParam.StorageType == StorageType.String)
                {
                    designationParam.Set(designationValue);
                }
                else
                {
                    return $"Параметр '2BIM_ПВ_Обозначение' должен быть текстовым (String), а он: {designationParam.StorageType}";
                }

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи 2BIM_ПВ_Обозначение: {ex.Message}";
            }
        }

        /// <summary>
        /// Получение значения параметра как строки
        /// </summary>
        private string GetParameterValueAsString(Element element, string paramName)
        {
            try
            {
                if (element == null) return "";

                Parameter param = null;

                // Сначала через LookParameter
                param = element.LookupParameter(paramName);

                // Если не нашли, ищем через перебор
                if (param == null)
                {
                    foreach (Parameter p in element.Parameters)
                    {
                        if (p.Definition != null && p.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                        {
                            param = p;
                            break;
                        }
                    }
                }

                if (param != null && param.HasValue)
                {
                    if (param.StorageType == StorageType.String)
                    {
                        return param.AsString() ?? "";
                    }
                    else if (param.StorageType == StorageType.Integer)
                    {
                        return param.AsInteger().ToString();
                    }
                    else if (param.StorageType == StorageType.Double)
                    {
                        // Для числовых параметров возвращаем как есть (без округления)
                        return param.AsDouble().ToString();
                    }
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Проверка, является ли элемент панелью витража
        /// </summary>
        private bool IsCurtainWallPanel(Element element)
        {
#if REVIT2023
            // Используем полное имя для избежания конфликта с System.Windows.Controls.Panel
            return element is Autodesk.Revit.DB.Panel ||
                   element.Category?.Id.IntegerValue.Equals((int)BuiltInCategory.OST_CurtainWallPanels) == true;
#endif
            return true;
                   }

        /// <summary>
        /// Получение размеров панели
        /// </summary>
        private bool TryGetPanelDimensions(Element panel, out double width, out double height, out string sourceInfo)
        {
            width = 0;
            height = 0;
            sourceInfo = "";

            try
            {
                // Сначала пробуем встроенные параметры
                Parameter builtInHeight = panel.get_Parameter(BuiltInParameter.CURTAIN_WALL_PANELS_HEIGHT);
                Parameter builtInWidth = panel.get_Parameter(BuiltInParameter.CURTAIN_WALL_PANELS_WIDTH);

                if (builtInHeight != null && builtInWidth != null)
                {
                    double h = builtInHeight.AsDouble();
                    double w = builtInWidth.AsDouble();

                    if (h > 0.001 && w > 0.001)
                    {
                        width = w;
                        height = h;
                        sourceInfo = "Встроенные параметры панели";
                        return true;
                    }
                }

                // Если нет встроенных параметров, вычисляем из геометрии
                if (TryGetTrimmedPanelDimensions(panel, out width, out height, out string geoError))
                {
                    sourceInfo = "Вычислено из геометрии (с учетом подрезки)";
                    return true;
                }

                sourceInfo = $"Ошибка: {geoError}";
                return false;
            }
            catch (Exception ex)
            {
                sourceInfo = $"Ошибка: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Получение фактических размеров панели из геометрии
        /// </summary>
        private bool TryGetTrimmedPanelDimensions(Element panel, out double width, out double height, out string errorMessage)
        {
            width = 0;
            height = 0;
            errorMessage = "";

            try
            {
                Options geoOptions = new Options();
                geoOptions.ComputeReferences = false;
                geoOptions.DetailLevel = ViewDetailLevel.Fine;
                geoOptions.IncludeNonVisibleObjects = false;

                GeometryElement geoElement = panel.get_Geometry(geoOptions);

                if (geoElement == null)
                {
                    errorMessage = "Нет геометрии";
                    return false;
                }

                Solid targetSolid = null;

                foreach (GeometryObject geoObj in geoElement)
                {
                    if (geoObj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 0)
                    {
                        targetSolid = solid;
                        break;
                    }
                    else if (geoObj is GeometryInstance instance)
                    {
                        GeometryElement instanceGeo = instance.GetInstanceGeometry();
                        foreach (GeometryObject instObj in instanceGeo)
                        {
                            if (instObj is Solid instSolid && instSolid.Faces.Size > 0 && instSolid.Volume > 0)
                            {
                                targetSolid = instSolid;
                                break;
                            }
                        }
                    }
                    if (targetSolid != null) break;
                }

                if (targetSolid == null)
                {
                    errorMessage = "Твердое тело не найдено";
                    return false;
                }

                BoundingBoxXYZ bbox = targetSolid.GetBoundingBox();
                Autodesk.Revit.DB.Transform transform = bbox.Transform;

                XYZ min = transform.OfPoint(bbox.Min);
                XYZ max = transform.OfPoint(bbox.Max);

                width = Math.Abs(max.X - min.X);
                height = Math.Abs(max.Z - min.Z);

                if (width < 0.001 || height < 0.001)
                {
                    errorMessage = $"Некорректные размеры: {width:F4} x {height:F4}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Запись размеров в ТЕКСТОВЫЕ параметры панели
        /// </summary>
        private string WritePanelDimensions(Element panel, int width, int height)
        {
            try
            {
                // Ищем текстовые параметры в элементе
                Parameter heightParam = null;
                Parameter widthParam = null;

                // Перебираем все параметры элемента
                foreach (Parameter param in panel.Parameters)
                {
                    if (param.Definition != null && param.StorageType == StorageType.String)
                    {
                        string paramName = param.Definition.Name;

                        if (paramName.Equals("2BIM_ПВ_Высота", StringComparison.OrdinalIgnoreCase))
                        {
                            heightParam = param;
                        }
                        else if (paramName.Equals("2BIM_ПВ_Ширина", StringComparison.OrdinalIgnoreCase))
                        {
                            widthParam = param;
                        }
                    }
                }

                // Проверяем найденные параметры
                if (heightParam == null)
                {
                    // Пробуем LookParameter как запасной вариант
                    heightParam = panel.LookupParameter("2BIM_ПВ_Высота");
                    if (heightParam == null)
                    {
                        return "Параметр '2BIM_ПВ_Высота' не найден";
                    }

                    // Проверяем, что это текстовый параметр
                    if (heightParam.StorageType != StorageType.String)
                    {
                        return $"Параметр '2BIM_ПВ_Высота' должен быть текстовым (String), а он: {heightParam.StorageType}";
                    }
                }

                if (heightParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_Высота' только для чтения";
                }

                if (widthParam == null)
                {
                    // Пробуем LookParameter как запасной вариант
                    widthParam = panel.LookupParameter("2BIM_ПВ_Ширина");
                    if (widthParam == null)
                    {
                        return "Параметр '2BIM_ПВ_Ширина' не найден";
                    }

                    // Проверяем, что это текстовый параметр
                    if (widthParam.StorageType != StorageType.String)
                    {
                        return $"Параметр '2BIM_ПВ_Ширина' должен быть текстовым (String), а он: {widthParam.StorageType}";
                    }
                }

                if (widthParam.IsReadOnly)
                {
                    return "Параметр '2BIM_ПВ_Ширина' только для чтения";
                }

                // Записываем значения в текстовые параметры
                heightParam.Set(height.ToString());
                widthParam.Set(width.ToString());

                return null; // успешно
            }
            catch (Exception ex)
            {
                return $"Ошибка записи размеров: {ex.Message}";
            }
        }

        /// <summary>
        /// Округление ВСЕГДА В БОЛЬШУЮ СТОРОНУ до ближайшего числа, кратного 100
        /// </summary>
        private int RoundUpToNearestHundred(double value)
        {
            if (value <= 0) return 0;
            return (int)(Math.Ceiling(value / 100.0) * 100);
        }
    }

    /// <summary>
    /// WPF окно для ввода данных материалов и формул
    /// </summary>
    public class MaterialsInputWindow : Window
    {
        public List<SetPanelDimensions.MaterialItem> Materials { get; private set; }
        public List<SetPanelDimensions.FormulaItem> Formulas { get; private set; }
        private TabControl _tabControl;

        public MaterialsInputWindow(List<SetPanelDimensions.MaterialItem> materials, List<SetPanelDimensions.FormulaItem> formulas)
        {
            Materials = materials;
            Formulas = formulas;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Title = "Ввод данных материалов и формул";
            this.Width = 1200;
            this.Height = 700;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.CanResize;

            // Создаем основной контейнер - используем алиас WpfGrid
            var mainGrid = new WpfGrid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Создаем TabControl
            _tabControl = new TabControl
            {
                Margin = new Thickness(10)
            };

            // Вкладка с материалами
            var materialsTab = new TabItem
            {
                Header = "Материалы"
            };
            materialsTab.Content = CreateMaterialsGrid();
            _tabControl.Items.Add(materialsTab);

            // Вкладка с формулами
            var formulasTab = new TabItem
            {
                Header = "Формулы для расчетов"
            };
            formulasTab.Content = CreateFormulasGrid();
            _tabControl.Items.Add(formulasTab);

            // Контейнер для кнопок
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            // Кнопка "Выполнить расчет"
            var btnCalculate = new Button
            {
                Content = "Выполнить расчет",
                Width = 150,
                Height = 30,
                Margin = new Thickness(5),
                IsDefault = true
            };
            btnCalculate.Click += BtnCalculate_Click;

            // Кнопка "Отмена"
            var btnCancel = new Button
            {
                Content = "Отмена",
                Width = 100,
                Height = 30,
                Margin = new Thickness(5),
                IsCancel = true
            };
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = false;
                this.Close();
            };

            buttonPanel.Children.Add(btnCalculate);
            buttonPanel.Children.Add(btnCancel);

            // Добавляем элементы на главный грид
            WpfGrid.SetRow(_tabControl, 0);
            WpfGrid.SetRow(buttonPanel, 1);

            mainGrid.Children.Add(_tabControl);
            mainGrid.Children.Add(buttonPanel);

            this.Content = mainGrid;
        }

        private DataGrid CreateMaterialsGrid()
        {
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Single,
                Margin = new Thickness(10),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.White),
                RowBackground = new SolidColorBrush(Colors.White),
                AlternatingRowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240))
            };

            // Колонка №
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "№",
                Binding = new WpfBinding("Number"),
                Width = 40,
                IsReadOnly = true
            });

            // Колонка Наименование
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Наименование",
                Binding = new WpfBinding("Name")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = new DataGridLength(600),
                IsReadOnly = false,
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = {
                        new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                        new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)
                    }
                },
                EditingElementStyle = new Style(typeof(TextBox))
                {
                    Setters = {
                        new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap),
                        new Setter(TextBox.AcceptsReturnProperty, true),
                        new Setter(TextBox.VerticalAlignmentProperty, VerticalAlignment.Center),
                        new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center)
                    }
                }
            });

            // Колонка ед. измр.
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "ед. измр.",
                Binding = new WpfBinding("Unit"),
                Width = 80,
                IsReadOnly = true
            });

            // Колонка Количество
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Количество",
                Binding = new WpfBinding("Quantity")
                {
                    StringFormat = "F3",
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = 100
            });

            dataGrid.ItemsSource = Materials;
            return dataGrid;
        }

        private DataGrid CreateFormulasGrid()
        {
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Single,
                Margin = new Thickness(10),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.White),
                RowBackground = new SolidColorBrush(Colors.White),
                AlternatingRowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240))
            };

            // Колонка №
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "№",
                Binding = new WpfBinding("Number"),
                Width = 40,
                IsReadOnly = true
            });

            // Колонка Описание
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Описание",
                Binding = new WpfBinding("Description"),
                Width = 300,
                IsReadOnly = true,
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = {
                        new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                        new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)
                    }
                }
            });

            // Колонка Формула (редактируемая)
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Формула",
                Binding = new WpfBinding("Expression")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = 400,
                IsReadOnly = false,
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = {
                        new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                        new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center),
                        new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas"))
                    }
                },
                EditingElementStyle = new Style(typeof(TextBox))
                {
                    Setters = {
                        new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap),
                        new Setter(TextBox.AcceptsReturnProperty, true),
                        new Setter(TextBox.VerticalAlignmentProperty, VerticalAlignment.Center),
                        new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                        new Setter(TextBox.FontFamilyProperty, new FontFamily("Consolas"))
                    }
                }
            });

            // Колонка Результат (только для просмотра)
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Результат",
                Binding = new WpfBinding("Result")
                {
                    StringFormat = "F2"
                },
                Width = 100,
                IsReadOnly = true
            });

            // Добавляем подсказку о доступных переменных
            var toolTipText = new TextBlock
            {
                Text = "Доступные переменные:\n" +
                       "Area - Общая площадь (м²)\n" +
                       "Length - Общая длина панелей (мм)\n" +
                       "Perimeter - Суммарный периметр панелей (мм)\n" +
                       "MullionLength - Сумма длин вертикальных импостов (мм)\n" +
                       "Columns - Количество колонн на панель\n" +
                       "Panels - Общее количество панелей\n\n" +
                       "Функция: CEILING(x) - округление вверх\n" +
                       "Пример: CEILING(Area / 70) даст 1 при Area = 57.84",
                TextWrapping = TextWrapping.Wrap
            };

            var toolTip = new ToolTip
            {
                Content = toolTipText,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse
            };

            dataGrid.ToolTip = toolTip;

            dataGrid.ItemsSource = Formulas;
            return dataGrid;
        }

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Обновляем данные из DataGrid на активной вкладке
                if (_tabControl.SelectedItem is TabItem selectedTab)
                {
                    if (selectedTab.Content is DataGrid dataGrid)
                    {
                        dataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                    }
                }

                // Проверяем материалы
                foreach (var material in Materials)
                {
                    if (material.Quantity < 0)
                    {
                        MessageBox.Show("Количество не может быть отрицательным", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(material.Name))
                    {
                        MessageBox.Show($"Наименование для позиции {material.Number} не может быть пустым", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Проверяем формулы (кроме номера 2, который не используется)
                foreach (var formula in Formulas)
                {
                    if (string.IsNullOrWhiteSpace(formula.Expression) && formula.Number != 2)
                    {
                        MessageBox.Show($"Формула для расчета {formula.Number} не может быть пустой", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}