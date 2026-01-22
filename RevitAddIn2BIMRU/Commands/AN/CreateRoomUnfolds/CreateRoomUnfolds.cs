using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using System.Collections.Generic;
using System.Linq;
// Для WPF используем алиасы
using Win = System.Windows;
using WinControls = System.Windows.Controls;
using WinThreading = System.Windows.Threading;

namespace RevitAddIn2BIMRU.Commands.AN
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateRoomUnfolds : IExternalCommand
    {
        // Константа для отступа внутрь помещения (10 мм)
        private const double OffsetInward = 0.1; // 10 мм в метрах

        // Вспомогательный класс для отображения в ListBox
        private class TitleblockItem
        {
            public FamilySymbol Titleblock { get; set; }
            public string DisplayText { get; set; }
            public string Description { get; set; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            string roomName = "";
            string roomNumber = "";
            ViewSheet createdSheet = null;

            try
            {
                // 1. Выбор помещения
                Reference roomRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new RoomSelectionFilter(),
                    "Выберите помещение");

                Room room = doc.GetElement(roomRef) as Room;
                if (room == null)
                {
                    message = "Выбранный элемент не является помещением";
                    return Result.Failed;
                }

                // Сохраняем имя и номер помещения
                roomName = string.IsNullOrEmpty(room.Name) ? "Без названия" : room.Name;
                roomNumber = room.Number;

                // 2. Получение всех доступных типов рамок
                List<FamilySymbol> availableTitleblocks = GetAvailableTitleblocks(doc);

                if (availableTitleblocks.Count == 0)
                {
                    message = "В проекте не найдено ни одной рамки (основной надписи)";
                    TaskDialog.Show("Ошибка", "Пожалуйста, загрузите семейство рамки в проект.");
                    return Result.Failed;
                }

                // 3. Показываем WPF диалог выбора рамки
                FamilySymbol selectedTitleblock = null;

                if (availableTitleblocks.Count == 1)
                {
                    // Если только одна рамка - используем её без диалога
                    selectedTitleblock = availableTitleblocks[0];
                }
                else
                {
                    // Создаем и показываем WPF окно
                    selectedTitleblock = ShowWpfTitleblockSelectionDialog(availableTitleblocks);
                    if (selectedTitleblock == null)
                    {
                        return Result.Cancelled; // Пользователь нажал Отмена
                    }
                }

                // 4. Получение границ помещения
                SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
                    StoreFreeBoundaryFaces = true
                };

                System.Collections.Generic.IList<System.Collections.Generic.IList<BoundarySegment>> boundaries = room.GetBoundarySegments(options);
                if (boundaries == null || boundaries.Count == 0)
                {
                    message = "Не удалось получить границы помещения";
                    return Result.Failed;
                }

                // 5. Получение геометрии помещения
                BoundingBoxXYZ roomBox = room.get_BoundingBox(null);
                double roomHeight = roomBox.Max.Z - roomBox.Min.Z;

                // 6. Создание сечений
                List<ViewSection> sections = new List<ViewSection>();
                ViewFamilyType sectionType = GetSectionViewFamilyType(doc);

                using (Transaction trans = new Transaction(doc, "Создание разрезов с отступом"))
                {
                    trans.Start();

                    int sectionNumber = 1;

                    foreach (System.Collections.Generic.IList<BoundarySegment> boundary in boundaries)
                    {
                        foreach (BoundarySegment segment in boundary)
                        {
                            Curve curve = segment.GetCurve();
                            if (curve == null) continue;

                            XYZ p = curve.GetEndPoint(0);
                            XYZ q = curve.GetEndPoint(1);
                            XYZ v = q - p;

                            double w = v.GetLength();
                            double offset = 0.2;

                            XYZ curveStart = curve.GetEndPoint(0);
                            XYZ curveEnd = curve.GetEndPoint(1);

                            // Вектор вдоль стены
                            XYZ wallDirection = (curveEnd - curveStart).Normalize();

                            // Вектор вида (перпендикулярно стене)
                            XYZ viewDirection = wallDirection.CrossProduct(XYZ.BasisZ).Normalize();

                            // Проверка направления внутрь помещения
                            XYZ testPoint = curveStart + viewDirection * 0.1;
                            if (!IsPointInRoom(room, testPoint))
                            {
                                viewDirection = -viewDirection;
                            }

                            XYZ upDirection = XYZ.BasisZ;
                            XYZ rightDirection = viewDirection.CrossProduct(upDirection);

                            XYZ min = new XYZ(-w * 0.5 - 0.1, -0.1, -offset);
                            XYZ max = new XYZ(w * 0.5 + 0.1, roomHeight + 0.1, offset);

                            XYZ midpoint = p + 0.5 * v;
                            XYZ walldir = v.Normalize();
                            XYZ up = XYZ.BasisZ;
                            XYZ viewdir = walldir.CrossProduct(up);

                            Transform t = Transform.Identity;
                            t.Origin = midpoint;
                            t.BasisX = walldir;
                            t.BasisY = up;
                            t.BasisZ = viewdir;

                            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                            sectionBox.Transform = t;
                            sectionBox.Min = min;
                            sectionBox.Max = max;

                            // Создание сечения
                            ViewSection section = ViewSection.CreateSection(doc, sectionType.Id, sectionBox);

                            // Создаем базовое имя для разреза
                            string baseSectionName = $"Разрез {roomNumber}-{sectionNumber++}";

                            // Получаем уникальное имя
                            string uniqueName = GetUniqueViewName(doc, baseSectionName);
                            section.Name = uniqueName;

                            // Установка масштаба 1:50
                            Parameter scaleParam = section.get_Parameter(BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC);
                            if (scaleParam != null && !scaleParam.IsReadOnly)
                            {
                                scaleParam.Set(50);
                            }

                            sections.Add(section);
                        }
                    }

                    trans.Commit();
                }

                // 7. Создание листа с выбранной рамкой и размещение видов
                if (sections.Count > 0)
                {
                    using (Transaction trans = new Transaction(doc, "Размещение на листе"))
                    {
                        trans.Start();

                        // Создаем лист с выбранной рамкой
                        ViewSheet sheet;
                        try
                        {
                            sheet = CreateSheetWithTitleblock(doc, selectedTitleblock);
                            if (sheet == null)
                            {
                                message = "Не удалось создать лист с выбранной рамкой";
                                return Result.Failed;
                            }

                            // Формируем название листа
                            sheet.Name = $"Разрезы {roomNumber}_{roomName}";
                            sheet.SheetNumber = GetNextAvailableSheetNumber(doc);
                            createdSheet = sheet; // Сохраняем ссылку на созданный лист
                        }
                        catch (System.Exception ex)
                        {
                            message = $"Ошибка при создании листа: {ex.Message}";
                            return Result.Failed;
                        }

                        // Получаем границы листа
                        double sheetMinU = sheet.Outline.Min.U;
                        double sheetMinV = sheet.Outline.Min.V;
                        double sheetMaxU = sheet.Outline.Max.U;
                        double sheetMaxV = sheet.Outline.Max.V;

                        double sheetWidth = sheetMaxU - sheetMinU;
                        double sheetHeight = sheetMaxV - sheetMinV;

                        // Фиксированные параметры для размещения
                        double viewportHeight = sheetHeight * 0.6; // Высота вида 60% от высоты листа
                        double viewportWidth = sheetWidth * 0.15;  // Ширина вида 15% от ширины листа

                        // Отступы
                        double marginLeft = sheetWidth * 0.05;    // 5% от левого края
                        double marginBottom = sheetHeight * 0.15; // 15% от нижнего края
                        double spacingBetweenViews = sheetWidth * 0.02; // 2% между видами

                        // Начальная позиция для первого вида
                        double currentX = sheetMinU + marginLeft;
                        double currentY = sheetMinV + marginBottom;

                        // Создаем все виды на одном листе в одну линию
                        foreach (ViewSection section in sections)
                        {
                            // Создаем вид на листе
                            XYZ viewportLocation = new XYZ(currentX, currentY, 0);
                            Viewport viewport = Viewport.Create(doc, sheet.Id, section.Id, viewportLocation);

                            // Настраиваем номер детали
                            Parameter detailNumParam = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                            if (detailNumParam != null && !detailNumParam.IsReadOnly)
                            {
                                detailNumParam.Set(section.Name);
                            }

                            // Добавляем текстовую метку под видом
                            AddViewLabel(doc, sheet, section.Name,
                                new XYZ(currentX + viewportWidth / 2, currentY - sheetHeight * 0.03, 0));

                            // Переходим к следующей позиции
                            currentX += viewportWidth + spacingBetweenViews;
                        }

                        // Добавляем заголовок листа
                        AddSheetTitle(doc, sheet, $"Развертка помещения {roomNumber} - {roomName}",
                            new XYZ(sheetMinU + sheetWidth / 2, sheetMaxV - sheetHeight * 0.08, 0));

                        trans.Commit();

                        // Финальный вопрос: открыть созданный лист?
                        TaskDialog openSheetDialog = new TaskDialog("Создание завершено");
                        openSheetDialog.MainInstruction = "Разрезы созданы и размещены на листе";
                        openSheetDialog.MainContent =
                            $"Создано разрезов: {sections.Count}\n" +
                            $"Лист: {sheet.SheetNumber} - {sheet.Name}\n" +
                            $"Рамка: {selectedTitleblock.Name}";
                        openSheetDialog.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                        openSheetDialog.DefaultButton = TaskDialogResult.Yes;
                        openSheetDialog.FooterText = "Открыть созданный лист?";

                        if (openSheetDialog.Show() == TaskDialogResult.Yes && createdSheet != null)
                        {
                            // Открываем созданный лист
                            uiApp.ActiveUIDocument.ActiveView = createdSheet;
                        }
                    }
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Пользователь отменил операцию выбора помещения
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", ex.ToString());
                return Result.Failed;
            }
        }

        /// <summary>
        /// Добавляет метку под видом
        /// </summary>
        private void AddViewLabel(Document doc, ViewSheet sheet, string text, XYZ location)
        {
            try
            {
                // Создаем текстовую заметку
                TextNoteType textType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstOrDefault() as TextNoteType;

                if (textType != null)
                {
                    // Создаем текстовую заметку
                    TextNote note = TextNote.Create(doc, sheet.Id, location, text, textType.Id);

                    // Настраиваем выравнивание по центру
                    Parameter alignParam = note.get_Parameter(BuiltInParameter.TEXT_ALIGN_VERT);
                    if (alignParam != null && !alignParam.IsReadOnly)
                    {
                        alignParam.Set((int)VerticalTextAlignment.Top);
                    }

                    Parameter horAlignParam = note.get_Parameter(BuiltInParameter.TEXT_ALIGN_HORZ);
                    if (horAlignParam != null && !horAlignParam.IsReadOnly)
                    {
                        horAlignParam.Set((int)HorizontalTextAlignment.Center);
                    }

                    // Устанавливаем размер шрифта (2.5 мм)
                    Parameter sizeParam = note.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    if (sizeParam != null && !sizeParam.IsReadOnly)
                    {
                        sizeParam.Set(0.0025); // 2.5 мм в метрах
                    }
                }
            }
            catch
            {
                // Если не удалось добавить текст, просто игнорируем
            }
        }

        /// <summary>
        /// Добавляет заголовок листа
        /// </summary>
        private void AddSheetTitle(Document doc, ViewSheet sheet, string title, XYZ location)
        {
            try
            {
                // Создаем текстовую заметку
                TextNoteType textType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstOrDefault() as TextNoteType;

                if (textType != null)
                {
                    // Создаем текстовую заметку
                    TextNote note = TextNote.Create(doc, sheet.Id, location, title, textType.Id);

                    // Настраиваем выравнивание по центру
                    Parameter alignParam = note.get_Parameter(BuiltInParameter.TEXT_ALIGN_VERT);
                    if (alignParam != null && !alignParam.IsReadOnly)
                    {
                        alignParam.Set((int)VerticalTextAlignment.Bottom);
                    }

                    Parameter horAlignParam = note.get_Parameter(BuiltInParameter.TEXT_ALIGN_HORZ);
                    if (horAlignParam != null && !horAlignParam.IsReadOnly)
                    {
                        horAlignParam.Set((int)HorizontalTextAlignment.Center);
                    }

                    // Устанавливаем размер шрифта (6 мм)
                    Parameter sizeParam = note.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    if (sizeParam != null && !sizeParam.IsReadOnly)
                    {
                        sizeParam.Set(0.006); // 6 мм в метрах
                    }

                    // Устанавливаем жирный шрифт
                    Parameter boldParam = note.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD);
                    if (boldParam != null && !boldParam.IsReadOnly)
                    {
                        boldParam.Set(1);
                    }
                }
            }
            catch
            {
                // Если не удалось добавить текст, просто игнорируем
            }
        }

        /// <summary>
        /// Создает и показывает WPF окно для выбора рамки
        /// </summary>
        private FamilySymbol ShowWpfTitleblockSelectionDialog(List<FamilySymbol> titleblocks)
        {
            FamilySymbol selectedTitleblock = null;

            // Используем Dispatcher для запуска WPF окна в UI потоке
            WinThreading.Dispatcher.CurrentDispatcher.Invoke(() =>
            {
                // Создаем главное окно
                var mainWindow = new Win.Window
                {
                    Title = "Выберите рамку (основную надпись)",
                    Width = 500,
                    Height = 400,
                    WindowStartupLocation = Win.WindowStartupLocation.CenterScreen,
                    ResizeMode = Win.ResizeMode.NoResize,
                    SizeToContent = Win.SizeToContent.Manual
                };

                // Создаем контейнер
                var mainPanel = new WinControls.DockPanel
                {
                    Margin = new Win.Thickness(10)
                };

                // Создаем заголовок
                var header = new WinControls.TextBlock
                {
                    Text = "Выберите рамку для листа:",
                    FontSize = 14,
                    FontWeight = Win.FontWeights.Bold,
                    Margin = new Win.Thickness(0, 0, 0, 10)
                };
                WinControls.DockPanel.SetDock(header, WinControls.Dock.Top);
                mainPanel.Children.Add(header);

                // Создаем ListBox для отображения рамок
                var listBox = new WinControls.ListBox
                {
                    Margin = new Win.Thickness(0, 0, 0, 10),
                    SelectionMode = WinControls.SelectionMode.Single,
                    HorizontalContentAlignment = Win.HorizontalAlignment.Stretch
                };

                // Заполняем список
                foreach (var titleblock in titleblocks)
                {
                    string description = $"Семейство: {titleblock.Family.Name}";
                    listBox.Items.Add(new TitleblockItem
                    {
                        Titleblock = titleblock,
                        DisplayText = titleblock.Name,
                        Description = description
                    });
                }

                // Создаем StackPanel для деталей
                var detailsPanel = new WinControls.StackPanel
                {
                    Orientation = WinControls.Orientation.Vertical,
                    Margin = new Win.Thickness(5)
                };

                // Создаем TextBlock для отображения деталей
                var detailsText = new WinControls.TextBlock
                {
                    Text = "Детали выбранной рамки:",
                    FontWeight = Win.FontWeights.Bold,
                    Margin = new Win.Thickness(0, 0, 0, 5)
                };
                detailsPanel.Children.Add(detailsText);

                var selectedDetails = new WinControls.TextBlock
                {
                    Text = "Выберите рамку из списка...",
                    TextWrapping = Win.TextWrapping.Wrap
                };
                detailsPanel.Children.Add(selectedDetails);

                // Обработчик выбора элемента
                listBox.SelectionChanged += (sender, e) =>
                {
                    if (listBox.SelectedItem is TitleblockItem selectedItem)
                    {
                        selectedDetails.Text =
                            $"Название: {selectedItem.Titleblock.Name}\n" +
                            $"Семейство: {selectedItem.Titleblock.Family.Name}\n" +
                            $"ID: {selectedItem.Titleblock.Id}";
                    }
                };

                // Выбираем первый элемент по умолчанию
                if (listBox.Items.Count > 0)
                    listBox.SelectedIndex = 0;

                // Создаем контейнер для списка и деталей
                var contentGrid = new WinControls.Grid();
                contentGrid.ColumnDefinitions.Add(new WinControls.ColumnDefinition { Width = new Win.GridLength(2, Win.GridUnitType.Star) });
                contentGrid.ColumnDefinitions.Add(new WinControls.ColumnDefinition { Width = new Win.GridLength(1, Win.GridUnitType.Star) });

                // Размещаем элементы
                WinControls.Grid.SetColumn(listBox, 0);
                WinControls.Grid.SetColumn(detailsPanel, 1);

                contentGrid.Children.Add(listBox);
                contentGrid.Children.Add(detailsPanel);

                WinControls.DockPanel.SetDock(contentGrid, WinControls.Dock.Top);
                mainPanel.Children.Add(contentGrid);

                // Создаем панель для кнопок
                var buttonPanel = new WinControls.StackPanel
                {
                    Orientation = WinControls.Orientation.Horizontal,
                    HorizontalAlignment = Win.HorizontalAlignment.Right,
                    Margin = new Win.Thickness(0, 10, 0, 0)
                };

                // Создаем кнопку OK
                var okButton = new WinControls.Button
                {
                    Content = "OK",
                    IsDefault = true,
                    Width = 80,
                    Height = 25,
                    Margin = new Win.Thickness(5, 0, 5, 0)
                };

                // Создаем кнопку Отмена
                var cancelButton = new WinControls.Button
                {
                    Content = "Отмена",
                    IsCancel = true,
                    Width = 80,
                    Height = 25,
                    Margin = new Win.Thickness(5, 0, 5, 0)
                };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);

                WinControls.DockPanel.SetDock(buttonPanel, WinControls.Dock.Bottom);
                mainPanel.Children.Add(buttonPanel);

                // Устанавливаем содержимое окна
                mainWindow.Content = mainPanel;

                // Обработчики кнопок
                okButton.Click += (sender, e) =>
                {
                    if (listBox.SelectedItem is TitleblockItem selectedItem)
                    {
                        selectedTitleblock = selectedItem.Titleblock;
                        mainWindow.DialogResult = true;
                    }
                    else
                    {
                        Win.MessageBox.Show("Пожалуйста, выберите рамку из списка.",
                                          "Внимание",
                                          Win.MessageBoxButton.OK,
                                          Win.MessageBoxImage.Warning);
                    }
                };

                cancelButton.Click += (sender, e) =>
                {
                    mainWindow.DialogResult = false;
                };

                // Показываем окно как диалоговое
                mainWindow.ShowDialog();
            });

            return selectedTitleblock;
        }

        /// <summary>
        /// Получает список всех доступных рамок (основных надписей) в проекте
        /// </summary>
        private List<FamilySymbol> GetAvailableTitleblocks(Document doc)
        {
            List<FamilySymbol> titleblocks = new List<FamilySymbol>();

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType();

            foreach (Element element in collector)
            {
                FamilySymbol titleblock = element as FamilySymbol;
                if (titleblock != null && titleblock.Family != null)
                {
                    titleblocks.Add(titleblock);
                }
            }

            return titleblocks;
        }

        /// <summary>
        /// Создает лист с указанной рамкой
        /// </summary>
        private ViewSheet CreateSheetWithTitleblock(Document doc, FamilySymbol titleblock)
        {
            // Активируем тип рамки, если не активен
            if (!titleblock.IsActive)
            {
                titleblock.Activate();
                doc.Regenerate();
            }

            // Создаем лист
            return ViewSheet.Create(doc, titleblock.Id);
        }

        /// <summary>
        /// Генерирует следующий доступный номер листа
        /// </summary>
        private string GetNextAvailableSheetNumber(Document doc)
        {
            // Собираем все существующие номера листов
            System.Collections.Generic.HashSet<string> existingNumbers = new System.Collections.Generic.HashSet<string>();

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet));

            foreach (Element elem in collector)
            {
                if (elem is ViewSheet sheet)
                {
                    string sheetNumber = sheet.SheetNumber;
                    if (!string.IsNullOrEmpty(sheetNumber))
                    {
                        existingNumbers.Add(sheetNumber);
                    }
                }
            }

            // Ищем свободный номер в формате "101", "102", и т.д.
            int baseNumber = 101;
            string newNumber;

            do
            {
                newNumber = baseNumber.ToString();
                baseNumber++;
            }
            while (existingNumbers.Contains(newNumber) && baseNumber < 1000);

            return newNumber;
        }

        /// <summary>
        /// Получает тип семейства для сечений
        /// </summary>
        private ViewFamilyType GetSectionViewFamilyType(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType));

            foreach (Element elem in collector)
            {
                ViewFamilyType viewFamilyType = elem as ViewFamilyType;
                if (viewFamilyType != null && viewFamilyType.ViewFamily == ViewFamily.Section)
                {
                    return viewFamilyType;
                }
            }

            return null;
        }

        /// <summary>
        /// Получает уникальное имя для вида
        /// </summary>
        private string GetUniqueViewName(Document doc, string baseName)
        {
            System.Collections.Generic.HashSet<string> existingNames = new System.Collections.Generic.HashSet<string>();

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.View));

            foreach (Element elem in collector)
            {
                if (elem is Autodesk.Revit.DB.View view && !string.IsNullOrEmpty(view.Name))
                {
                    existingNames.Add(view.Name);
                }
            }

            if (!existingNames.Contains(baseName))
                return baseName;

            int counter = 1;
            string newName;
            do
            {
                newName = $"{baseName}_{counter++}";
            }
            while (existingNames.Contains(newName));

            return newName;
        }

        private bool IsPointInRoom(Room room, XYZ point)
        {
            BoundingBoxXYZ bb = room.get_BoundingBox(null);
            if (bb == null) return false;

            return point.X > bb.Min.X && point.X < bb.Max.X &&
                   point.Y > bb.Min.Y && point.Y < bb.Max.Y &&
                   point.Z > bb.Min.Z && point.Z < bb.Max.Z;
        }
    }

    /// <summary>
    /// Фильтр для выбора помещений
    /// </summary>
    public class RoomSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Room;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}