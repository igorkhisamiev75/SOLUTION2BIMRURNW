using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;

namespace RevitAddIn2BIMRU.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WallTablePlugin : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                // 1. Выбираем стены на виде
                List<Wall> selectedWalls = SelectWalls(uidoc);

                if (selectedWalls == null || selectedWalls.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Стены не выбраны");
                    return Result.Cancelled;
                }

                // 2. Проверяем относятся ли они к одному типу
                ElementId wallTypeId = selectedWalls.First().GetTypeId();
                bool sameType = selectedWalls.All(w => w.GetTypeId() == wallTypeId);

                if (!sameType)
                {
                    TaskDialog.Show("Ошибка", "Выбранные стены относятся к разным типам. Пожалуйста, выберите стены одного типа.");
                    return Result.Failed;
                }

                // Получаем тип стены
                WallType wallType = doc.GetElement(wallTypeId) as WallType;
                if (wallType == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось получить тип стены");
                    return Result.Failed;
                }

                // 3. Получаем суммарный объем, площадь и другие данные
                double totalVolume = 0;
                double totalArea = 0;
                string note = "";

                foreach (Wall wall in selectedWalls)
                {
                    totalVolume += GetWallVolume(wall);
                    totalArea += GetWallArea(wall);
                    if (string.IsNullOrEmpty(note))
                    {
                        note = GetParameterValue(wall, "ADSK_Примечание");
                    }
                }

                // Конвертируем объем из кубических футов в кубические метры
                double totalVolumeInM3 = totalVolume * 0.0283168;
                // Конвертируем площадь из квадратных футов в квадратные метры
                double totalAreaInM2 = totalArea * 0.092903;

                // Получаем значения многострочных параметров
                string nameValue = GetParameterValue(wallType, "2BIMRU_Наименование");
                string designationValue = GetParameterValue(wallType, "2BIMRU_Обозначение");
                string unitValue = GetParameterValue(wallType, "2BIMRU_Ед. изм");

                // Разбиваем на строки
                string[] nameLines = nameValue.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                string[] designationLines = designationValue.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                string[] unitLines = unitValue.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                // Определяем максимальное количество строк
                int maxRowCount = Math.Max(nameLines.Length, Math.Max(designationLines.Length, unitLines.Length));
                if (maxRowCount == 0) maxRowCount = 1;

                // Показываем форму выбора коэффициентов
                CoefficientsWindow coeffWindow = new CoefficientsWindow(maxRowCount);
                if (coeffWindow.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                // Получаем коэффициенты и тип расчета из формы
                Dictionary<int, double> coefficients = coeffWindow.GetCoefficients();
                bool useArea = coeffWindow.UseAreaCalculation;

                // Создаем данные для таблицы
                WallSummaryData summaryData = new WallSummaryData
                {
                    TotalVolume = totalVolumeInM3,
                    TotalArea = totalAreaInM2,
                    Mark = "",
                    DesignationLines = designationLines,
                    NameLines = nameLines,
                    UnitLines = unitLines,
                    Note = note,
                    Count = selectedWalls.Count,
                    MaxRowCount = maxRowCount,
                    Coefficients = coefficients,
                    UseAreaCalculation = useArea
                };

                // 4. Формируем таблицу на листе
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .ToList();

                if (sheets.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "В проекте нет листов");
                    return Result.Failed;
                }

                SheetSelectionWindow sheetWindow = new SheetSelectionWindow(sheets);
                if (sheetWindow.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                ViewSheet selectedSheet = sheetWindow.SelectedSheet;

                using (Transaction trans = new Transaction(doc, "Создание таблицы стен"))
                {
                    trans.Start();
                    CreateWallTable(doc, selectedSheet, summaryData);
                    trans.Commit();
                }

                TaskDialog.Show("Успех", $"Таблица успешно создана на листе {selectedSheet.SheetNumber}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Произошла ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        private List<Wall> SelectWalls(UIDocument uidoc)
        {
            try
            {
                IList<Reference> references = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new WallSelectionFilter(),
                    "Выберите стены для подсчета");

                List<Wall> walls = new List<Wall>();

                foreach (Reference reference in references)
                {
                    Wall wall = uidoc.Document.GetElement(reference) as Wall;
                    if (wall != null)
                    {
                        walls.Add(wall);
                    }
                }

                return walls;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        private double GetWallVolume(Wall wall)
        {
            Parameter volumeParam = wall.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (volumeParam != null && volumeParam.HasValue)
            {
                return volumeParam.AsDouble();
            }

            Options opt = new Options();
            opt.ComputeReferences = true;
            opt.DetailLevel = ViewDetailLevel.Fine;

            GeometryElement geoElem = wall.get_Geometry(opt);
            double volume = 0;

            foreach (GeometryObject geoObj in geoElem)
            {
                Solid solid = geoObj as Solid;
                if (solid != null && solid.Volume > 0)
                {
                    volume += solid.Volume;
                }
            }

            return volume;
        }

        private double GetWallArea(Wall wall)
        {
            // Получаем параметр площади стены
            Parameter areaParam = wall.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (areaParam != null && areaParam.HasValue)
            {
                return areaParam.AsDouble();
            }

            // Альтернативный расчет через геометрию
            Options opt = new Options();
            opt.ComputeReferences = true;
            opt.DetailLevel = ViewDetailLevel.Fine;

            GeometryElement geoElem = wall.get_Geometry(opt);
            double area = 0;

            foreach (GeometryObject geoObj in geoElem)
            {
                Solid solid = geoObj as Solid;
                if (solid != null && solid.Volume > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        area += face.Area;
                    }
                }
            }

            // Делим на 2, так как у стены 2 стороны
            return area / 2;
        }

        private string GetParameterValue(Element element, string paramName)
        {
            Parameter param = element.LookupParameter(paramName);
            if (param != null && param.HasValue)
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        return param.AsString();
                    case StorageType.Integer:
                        return param.AsInteger().ToString();
                    case StorageType.Double:
                        return param.AsDouble().ToString("0.##");
                    default:
                        return "";
                }
            }
            return "";
        }

        private ElementId GetTextNoteType(Document doc, string typeName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            TextNoteType textNoteType = collector
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == typeName);

            if (textNoteType != null)
            {
                return textNoteType.Id;
            }

            textNoteType = collector
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();

            if (textNoteType != null)
            {
                return textNoteType.Id;
            }

            return ElementId.InvalidElementId;
        }

        private string CalculateVolumeForRow(double baseVolume, double baseArea, int rowIndex, Dictionary<int, double> coefficients, bool useArea = false)
        {
            if (coefficients.ContainsKey(rowIndex))
            {
                double baseValue = useArea ? baseArea : baseVolume;
                double calculatedValue = baseValue * coefficients[rowIndex];
                return $"{calculatedValue:0.##}";
            }
            return "";
        }

        private void CreateWallTable(Document doc, ViewSheet sheet, WallSummaryData data)
        {
            double startX = 0.1;
            double startY = 0.1;
            double rowHeight = 0.02;

            double headerOffsetUp = 0.00500;
            double contentOffsetFromTop = -0.005;

            Dictionary<string, double> columnWidths = new Dictionary<string, double>
            {
                { "Марка", 0.05 },
                { "Обозначение", 0.197 },
                { "Наименование", 0.213 },
                { "Кол-во", 0.05 },
                { "Ед.изм", 0.05 },
                { "Прим.", 0.098 }
            };

            List<string> columns = new List<string>
            {
                "Марка", "Обозначение", "Наименование",
                "Кол-во", "Ед.изм", "Прим."
            };

            ElementId headerTextTypeId = GetTextNoteType(doc, "ADSK_Основной текст_2.5_Ж");
            ElementId contentTextTypeId = GetTextNoteType(doc, "ADSK_Основной текст_2.5");

            if (headerTextTypeId == ElementId.InvalidElementId)
            {
                headerTextTypeId = GetTextNoteType(doc, "");
            }
            if (contentTextTypeId == ElementId.InvalidElementId)
            {
                contentTextTypeId = GetTextNoteType(doc, "");
            }

            int totalRowCount = 1 + data.MaxRowCount;

            // Рисуем горизонтальные линии
            for (int i = 0; i <= totalRowCount; i++)
            {
                double y = startY - i * rowHeight;
                Line horLine = Line.CreateBound(
                    new XYZ(startX, y, 0),
                    new XYZ(startX + columnWidths.Values.Sum(), y, 0));
                doc.Create.NewDetailCurve(sheet, horLine);
            }

            // Рисуем вертикальные линии
            double currentX = startX;
            foreach (var col in columns)
            {
                Line vertLine = Line.CreateBound(
                    new XYZ(currentX, startY, 0),
                    new XYZ(currentX, startY - totalRowCount * rowHeight, 0));
                doc.Create.NewDetailCurve(sheet, vertLine);
                currentX += columnWidths[col];
            }

            Line lastVertLine = Line.CreateBound(
                new XYZ(startX + columnWidths.Values.Sum(), startY, 0),
                new XYZ(startX + columnWidths.Values.Sum(), startY - totalRowCount * rowHeight, 0));
            doc.Create.NewDetailCurve(sheet, lastVertLine);

            // Заголовки
            currentX = startX;
            double headerY = startY - headerOffsetUp;

            foreach (var col in columns)
            {
                XYZ textPoint = new XYZ(currentX + 0.002, headerY, 0);
                TextNote textNote = TextNote.Create(doc, sheet.Id, textPoint, col, headerTextTypeId);
                currentX += columnWidths[col];
            }

            // Данные
            for (int row = 0; row < data.MaxRowCount; row++)
            {
                currentX = startX;
                double dataRowTopY = startY - (1 + row) * rowHeight;
                double contentY = dataRowTopY + contentOffsetFromTop;

                string currentDesignation = row < data.DesignationLines.Length ? data.DesignationLines[row].Trim() : "";
                string currentName = row < data.NameLines.Length ? data.NameLines[row].Trim() : "";
                string currentUnit = row < data.UnitLines.Length ? data.UnitLines[row].Trim() : "";
                string volumeText = CalculateVolumeForRow(data.TotalVolume, data.TotalArea, row, data.Coefficients, data.UseAreaCalculation);

                AddTextToCell(doc, sheet, currentX, contentY, columnWidths["Марка"], rowHeight, data.Mark, contentTextTypeId);
                currentX += columnWidths["Марка"];

                AddTextToCell(doc, sheet, currentX, contentY, columnWidths["Обозначение"], rowHeight, currentDesignation, contentTextTypeId);
                currentX += columnWidths["Обозначение"];

                AddTextToCell(doc, sheet, currentX, contentY, columnWidths["Наименование"], rowHeight, currentName, contentTextTypeId);
                currentX += columnWidths["Наименование"];

                AddTextToCell(doc, sheet, currentX, contentY, columnWidths["Кол-во"], rowHeight, volumeText, contentTextTypeId);
                currentX += columnWidths["Кол-во"];

                AddTextToCell(doc, sheet, currentX, contentY, columnWidths["Ед.изм"], rowHeight, currentUnit, contentTextTypeId);
                currentX += columnWidths["Ед.изм"];

                string note = (row == 0) ? data.Note : "";
                AddTextToCell(doc, sheet, currentX, contentY, columnWidths["Прим."], rowHeight, note, contentTextTypeId);
            }
        }

        private void AddTextToCell(Document doc, ViewSheet sheet, double x, double y,
            double width, double height, string text, ElementId textNoteTypeId)
        {
            if (string.IsNullOrEmpty(text))
                return;

            XYZ textPoint = new XYZ(x + 0.002, y, 0);
            TextNote textNote = TextNote.Create(doc, sheet.Id, textPoint, text, textNoteTypeId);
        }
    }

    public class WallSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Wall;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    public class WallSummaryData
    {
        public double TotalVolume { get; set; }
        public double TotalArea { get; set; }
        public int Count { get; set; }
        public string Mark { get; set; }
        public string[] DesignationLines { get; set; }
        public string[] NameLines { get; set; }
        public string[] UnitLines { get; set; }
        public string Note { get; set; }
        public int MaxRowCount { get; set; }
        public Dictionary<int, double> Coefficients { get; set; }
        public bool UseAreaCalculation { get; set; }

        public WallSummaryData()
        {
            Mark = "";
            DesignationLines = new string[0];
            NameLines = new string[0];
            UnitLines = new string[0];
            Note = "";
            Count = 0;
            TotalVolume = 0;
            TotalArea = 0;
            MaxRowCount = 1;
            Coefficients = new Dictionary<int, double>();
            UseAreaCalculation = false;
        }
    }

    // Форма выбора коэффициентов
    public class CoefficientsWindow : Window
    {
        private TabControl tabControl;
        private Grid gasConcreteGrid;
        private Grid polygran190Grid;
        private Grid polygran130Grid;
        private Grid polygran80Grid;
        private Grid customGrid;
        private List<TextBox> customTextBoxes;
        private int rowCount;

        public bool UseAreaCalculation { get; private set; }

        public CoefficientsWindow(int rowCount)
        {
            this.rowCount = rowCount;
            this.Title = "Выбор коэффициентов расчета";
            this.Width = 450;
            this.Height = 350;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = System.Windows.Media.Brushes.White;
            this.ResizeMode = ResizeMode.NoResize;

            customTextBoxes = new List<TextBox>();
            UseAreaCalculation = false;

            var mainGrid = new System.Windows.Controls.Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Создаем вкладки
            tabControl = new TabControl();
            tabControl.SelectionChanged += TabControl_SelectionChanged;

            // Вкладка "Газобетон" (преднастроенные коэффициенты)
            var gasConcreteTab = new TabItem { Header = "Газобетон" };
            gasConcreteGrid = CreateGasConcretePanel();
            gasConcreteTab.Content = gasConcreteGrid;
            tabControl.Items.Add(gasConcreteTab);

            // Вкладка "Полигран 190 ПГ" (преднастроенные коэффициенты)
            var polygran190Tab = new TabItem { Header = "Полигран 190 ПГ" };
            polygran190Grid = CreatePolygran190Panel();
            polygran190Tab.Content = polygran190Grid;
            tabControl.Items.Add(polygran190Tab);

            // Вкладка "Полигран 130 ПГ" (преднастроенные коэффициенты)
            var polygran130Tab = new TabItem { Header = "Полигран 130 ПГ" };
            polygran130Grid = CreatePolygran130Panel();
            polygran130Tab.Content = polygran130Grid;
            tabControl.Items.Add(polygran130Tab);

            // Вкладка "Полигран 80 ПГ" (преднастроенные коэффициенты)
            var polygran80Tab = new TabItem { Header = "Полигран 80 ПГ" };
            polygran80Grid = CreatePolygran80Panel();
            polygran80Tab.Content = polygran80Grid;
            tabControl.Items.Add(polygran80Tab);

            // Вкладка "Свой расчет"
            var customTab = new TabItem { Header = "Свой расчет" };
            customGrid = CreateCustomPanel();
            customTab.Content = customGrid;
            tabControl.Items.Add(customTab);

            mainGrid.Children.Add(tabControl);
            System.Windows.Controls.Grid.SetRow(tabControl, 0);

            // Кнопки
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 10)
            };

            var okButton = new Button { Content = "OK", Width = 80, Height = 25, Margin = new Thickness(0, 0, 10, 0) };
            okButton.Click += OkButton_Click;

            var cancelButton = new Button { Content = "Отмена", Width = 80, Height = 25 };
            cancelButton.Click += CancelButton_Click;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            mainGrid.Children.Add(buttonPanel);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 1);

            this.Content = mainGrid;
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Вкладки Полигран используют расчет по площади
            if (tabControl.SelectedIndex == 1 || tabControl.SelectedIndex == 2 || tabControl.SelectedIndex == 3)
            {
                UseAreaCalculation = true;
            }
            else
            {
                UseAreaCalculation = false;
            }
        }

        private Grid CreateGasConcretePanel()
        {
            var grid = new Grid();
            grid.Margin = new Thickness(10);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Заголовок
            var headerText = new TextBlock
            {
                Text = "Преднастроенные коэффициенты для газобетона:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 12
            };
            System.Windows.Controls.Grid.SetRow(headerText, 0);
            grid.Children.Add(headerText);

            // Информация о коэффициентах
            var infoPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            infoPanel.Children.Add(new TextBlock { Text = "1-я строка: базовый объем (м³)", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "2-я строка: объем × 5.2", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "3-я строка: объем × 25", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "Остальные строки: без расчета", Margin = new Thickness(0, 2, 0, 2) });

            System.Windows.Controls.Grid.SetRow(infoPanel, 1);
            grid.Children.Add(infoPanel);

            return grid;
        }

        private Grid CreatePolygran190Panel()
        {
            var grid = new Grid();
            grid.Margin = new Thickness(10);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Заголовок
            var headerText = new TextBlock
            {
                Text = "Преднастроенные коэффициенты для Полигран 190 ПГ:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 12
            };
            System.Windows.Controls.Grid.SetRow(headerText, 0);
            grid.Children.Add(headerText);

            // Информация о коэффициентах
            var infoPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            infoPanel.Children.Add(new TextBlock { Text = "1-я строка: площадь стены (м²)", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "2-я строка: площадь × 17.6", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "3-я строка: площадь × 0.9", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "Остальные строки: без расчета", Margin = new Thickness(0, 2, 0, 2) });

            System.Windows.Controls.Grid.SetRow(infoPanel, 1);
            grid.Children.Add(infoPanel);

            return grid;
        }

        private Grid CreatePolygran130Panel()
        {
            var grid = new Grid();
            grid.Margin = new Thickness(10);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Заголовок
            var headerText = new TextBlock
            {
                Text = "Преднастроенные коэффициенты для Полигран 130 ПГ:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 12
            };
            System.Windows.Controls.Grid.SetRow(headerText, 0);
            grid.Children.Add(headerText);

            // Информация о коэффициентах
            var infoPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            infoPanel.Children.Add(new TextBlock { Text = "1-я строка: площадь стены (м²)", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "2-я строка: площадь × 13.7", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "3-я строка: площадь × 0.65", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "Остальные строки: без расчета", Margin = new Thickness(0, 2, 0, 2) });

            System.Windows.Controls.Grid.SetRow(infoPanel, 1);
            grid.Children.Add(infoPanel);

            return grid;
        }

        private Grid CreatePolygran80Panel()
        {
            var grid = new Grid();
            grid.Margin = new Thickness(10);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Заголовок
            var headerText = new TextBlock
            {
                Text = "Преднастроенные коэффициенты для Полигран 80 ПГ:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 12
            };
            System.Windows.Controls.Grid.SetRow(headerText, 0);
            grid.Children.Add(headerText);

            // Информация о коэффициентах
            var infoPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            infoPanel.Children.Add(new TextBlock { Text = "1-я строка: площадь стены (м²)", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "2-я строка: площадь × 9.8", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "3-я строка: площадь × 0.4", Margin = new Thickness(0, 2, 0, 2) });
            infoPanel.Children.Add(new TextBlock { Text = "Остальные строки: без расчета", Margin = new Thickness(0, 2, 0, 2) });

            System.Windows.Controls.Grid.SetRow(infoPanel, 1);
            grid.Children.Add(infoPanel);

            return grid;
        }

        private Grid CreateCustomPanel()
        {
            var grid = new Grid();
            grid.Margin = new Thickness(10);

            // Добавляем строки для каждой строки таблицы
            for (int i = 0; i <= rowCount; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Заголовок
            var headerText = new TextBlock
            {
                Text = "Введите свои коэффициенты для каждой строки (расчет по объему):",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 12
            };
            System.Windows.Controls.Grid.SetRow(headerText, 0);
            grid.Children.Add(headerText);

            // Поля для ввода коэффициентов
            for (int i = 0; i < rowCount; i++)
            {
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 5, 0, 5)
                };

                var label = new TextBlock
                {
                    Text = $"Строка {i + 1}:",
                    Width = 80,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };

                var textBox = new TextBox
                {
                    Width = 150,
                    Text = (i == 0) ? "1" : (i == 1) ? "5.2" : (i == 2) ? "25" : "0",
                    Margin = new Thickness(0, 0, 10, 0)
                };

                var infoLabel = new TextBlock
                {
                    Text = "(объем × коэффициент)",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 10,
                    Foreground = System.Windows.Media.Brushes.Gray
                };

                rowPanel.Children.Add(label);
                rowPanel.Children.Add(textBox);
                rowPanel.Children.Add(infoLabel);

                System.Windows.Controls.Grid.SetRow(rowPanel, i + 1);
                grid.Children.Add(rowPanel);

                customTextBoxes.Add(textBox);
            }

            // Информация о пустых строках
            var infoText = new TextBlock
            {
                Text = "Примечание: если коэффициент = 0, строка останется пустой",
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 10,
                Foreground = System.Windows.Media.Brushes.Gray
            };
            System.Windows.Controls.Grid.SetRow(infoText, rowCount + 1);
            grid.Children.Add(infoText);

            return grid;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public Dictionary<int, double> GetCoefficients()
        {
            var coefficients = new Dictionary<int, double>();

            // Проверяем, какая вкладка активна
            if (tabControl.SelectedIndex == 0) // Газобетон
            {
                coefficients[0] = 1.0;      // 1-я строка - объем
                coefficients[1] = 5.2;      // 2-я строка - объем × 5.2
                coefficients[2] = 25.0;     // 3-я строка - объем × 25
                // Остальные строки не добавляем
                UseAreaCalculation = false;
            }
            else if (tabControl.SelectedIndex == 1) // Полигран 190 ПГ
            {
                coefficients[0] = 1.0;      // 1-я строка - площадь
                coefficients[1] = 17.6;     // 2-я строка - площадь × 17.6
                coefficients[2] = 0.9;      // 3-я строка - площадь × 0.9
                // Остальные строки не добавляем
                UseAreaCalculation = true;
            }
            else if (tabControl.SelectedIndex == 2) // Полигран 130 ПГ
            {
                coefficients[0] = 1.0;      // 1-я строка - площадь
                coefficients[1] = 13.7;     // 2-я строка - площадь × 13.7
                coefficients[2] = 0.65;     // 3-я строка - площадь × 0.65
                // Остальные строки не добавляем
                UseAreaCalculation = true;
            }
            else if (tabControl.SelectedIndex == 3) // Полигран 80 ПГ
            {
                coefficients[0] = 1.0;      // 1-я строка - площадь
                coefficients[1] = 9.8;      // 2-я строка - площадь × 9.8
                coefficients[2] = 0.4;      // 3-я строка - площадь × 0.4
                // Остальные строки не добавляем
                UseAreaCalculation = true;
            }
            else // Свой расчет
            {
                for (int i = 0; i < customTextBoxes.Count; i++)
                {
                    if (double.TryParse(customTextBoxes[i].Text, out double value))
                    {
                        if (value != 0)
                        {
                            coefficients[i] = value;
                        }
                    }
                }
                UseAreaCalculation = false;
            }

            return coefficients;
        }
    }

    public class SheetSelectionWindow : Window
    {
        private ListBox sheetsListBox;
        private Button okButton;
        private Button cancelButton;

        public ViewSheet SelectedSheet { get; private set; }
        private List<ViewSheet> sheets;

        public SheetSelectionWindow(List<ViewSheet> sheets)
        {
            this.sheets = sheets;
            this.Title = "Выбор листа";
            this.Width = 400;
            this.Height = 300;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = System.Windows.Media.Brushes.White;

            var mainGrid = new System.Windows.Controls.Grid();
            mainGrid.Margin = new Thickness(10);
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerText = new TextBlock
            {
                Text = "Выберите лист для размещения таблицы:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = System.Windows.Media.Brushes.Black
            };
            System.Windows.Controls.Grid.SetRow(headerText, 0);
            mainGrid.Children.Add(headerText);

            sheetsListBox = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                SelectionMode = SelectionMode.Single,
                Background = System.Windows.Media.Brushes.White,
                Foreground = System.Windows.Media.Brushes.Black
            };

            foreach (var sheet in sheets)
            {
                sheetsListBox.Items.Add($"{sheet.SheetNumber} - {sheet.Name}");
            }

            if (sheetsListBox.Items.Count > 0)
            {
                sheetsListBox.SelectedIndex = 0;
            }

            System.Windows.Controls.Grid.SetRow(sheetsListBox, 1);
            mainGrid.Children.Add(sheetsListBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 25,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.LightGray,
                Foreground = System.Windows.Media.Brushes.Black
            };
            okButton.Click += OkButton_Click;

            cancelButton = new Button
            {
                Content = "Отмена",
                Width = 80,
                Height = 25,
                Background = System.Windows.Media.Brushes.LightGray,
                Foreground = System.Windows.Media.Brushes.Black
            };
            cancelButton.Click += CancelButton_Click;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            this.Content = mainGrid;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sheetsListBox.SelectedIndex >= 0)
            {
                SelectedSheet = sheets[sheetsListBox.SelectedIndex];
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}