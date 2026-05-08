using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using TextBox = System.Windows.Controls.TextBox;

namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Room3DViewCreator : IExternalCommand
    {
        private Document _doc;
        private UIDocument _uidoc;
        private List<TitleBlockInfo> _availableTitleBlocks;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                _uidoc = commandData.Application.ActiveUIDocument;
                _doc = _uidoc.Document;

                // Получаем все доступные рамки
                _availableTitleBlocks = GetAvailableTitleBlocks();

                if (_availableTitleBlocks.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "В проекте нет загруженных рамок (титульных листов)");
                    return Result.Failed;
                }

                // 1. Получаем все помещения из модели
                List<Room> rooms = GetAllRooms();

                if (rooms.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "В проекте нет помещений");
                    return Result.Failed;
                }

                // 2. Показываем диалог выбора помещений
                var selectedRooms = ShowRoomSelectionDialog(rooms);

                if (selectedRooms == null || selectedRooms.Count == 0)
                {
                    return Result.Cancelled;
                }

                // 3. Показываем диалог выбора рамки
                var selectedTitleBlock = ShowTitleBlockSelectionDialog();

                if (selectedTitleBlock == null)
                {
                    return Result.Cancelled;
                }

                // 4. Создаем виды и листы для выбранных помещений
                bool success = CreateViewsAndSheetsForRooms(selectedRooms, selectedTitleBlock.ElementId);

                if (success)
                {
                    TaskDialog.Show("Успешно", $"Создано {selectedRooms.Count} видов и листов для помещений");
                }
                else
                {
                    TaskDialog.Show("Предупреждение", "Не все виды и листы были созданы. Проверьте журнал ошибок.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Критическая ошибка", $"Произошла ошибка: {ex.Message}\n\nПлагин будет закрыт.");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Получение всех доступных рамок
        /// </summary>
        private List<TitleBlockInfo> GetAvailableTitleBlocks()
        {
            try
            {
                return new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.IsActive) // Только активные рамки
                    .Select(fs => new TitleBlockInfo
                    {
                        ElementId = fs.Id,
                        Name = fs.Family.Name + " - " + fs.Name
                    })
                    .ToList();
            }
            catch (Exception)
            {
                return new List<TitleBlockInfo>();
            }
        }

        /// <summary>
        /// Получение всех помещений из модели
        /// </summary>
        private List<Room> GetAllRooms()
        {
            try
            {
                return new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .OfClass(typeof(SpatialElement))
                    .Cast<Room>()
                    .Where(r => r != null && r.Number != null && r.Name != null)
                    .OrderBy(r => r.Number)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<Room>();
            }
        }

        /// <summary>
        /// Диалог выбора помещений
        /// </summary>
        private List<Room> ShowRoomSelectionDialog(List<Room> rooms)
        {
            List<Room> selectedRooms = new List<Room>();

            try
            {
                Window wnd = new Window
                {
                    Title = "Выбор помещений для создания 3D видов",
                    Width = 600,
                    Height = 550,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true
                };

                StackPanel mainPanel = new StackPanel { Margin = new Thickness(15) };

                TextBlock infoText = new TextBlock
                {
                    Text = $"Найдено помещений: {rooms.Count}\nВыберите помещения для создания 3D видов:",
                    Margin = new Thickness(0, 0, 0, 15),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12
                };
                mainPanel.Children.Add(infoText);

                // Поиск
                StackPanel searchPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                searchPanel.Children.Add(new Label { Content = "Поиск:", Width = 50 });
                TextBox searchBox = new TextBox { Width = 200 };
                searchPanel.Children.Add(searchBox);

                Button selectAllBtn = new Button { Content = "Выбрать все", Width = 100, Margin = new Thickness(10, 0, 5, 0) };
                Button deselectAllBtn = new Button { Content = "Сбросить все", Width = 100 };

                searchPanel.Children.Add(selectAllBtn);
                searchPanel.Children.Add(deselectAllBtn);
                mainPanel.Children.Add(searchPanel);

                // Список помещений
                ListBox roomsList = new ListBox
                {
                    Height = 300,
                    SelectionMode = SelectionMode.Multiple,
                    Margin = new Thickness(0, 0, 0, 10),
                    DisplayMemberPath = "DisplayName"
                };

                var roomItems = rooms.Select(r => new RoomItem { Room = r, DisplayName = $"{r.Number} - {r.Name}" }).ToList();
                roomsList.ItemsSource = roomItems;
                roomsList.SelectedItems.Clear();

                // Поиск
                searchBox.TextChanged += (s, e) =>
                {
                    try
                    {
                        string filter = searchBox.Text.ToLower();
                        if (string.IsNullOrEmpty(filter))
                        {
                            roomsList.ItemsSource = roomItems;
                        }
                        else
                        {
                            roomsList.ItemsSource = roomItems.Where(r =>
                                r.Room.Number.ToLower().Contains(filter) ||
                                r.Room.Name.ToLower().Contains(filter)).ToList();
                        }
                    }
                    catch (Exception) { }
                };

                selectAllBtn.Click += (s, e) =>
                {
                    try { roomsList.SelectAll(); } catch (Exception) { }
                };

                deselectAllBtn.Click += (s, e) =>
                {
                    try { roomsList.SelectedItems.Clear(); } catch (Exception) { }
                };

                mainPanel.Children.Add(roomsList);

                // Кнопки
                StackPanel buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                Button createBtn = new Button
                {
                    Content = "✅ Далее",
                    Width = 120,
                    Height = 35,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };

                Button cancelBtn = new Button
                {
                    Content = "✖ Отмена",
                    Width = 100,
                    Height = 35,
                    IsCancel = true
                };

                createBtn.Click += (s, e) =>
                {
                    try
                    {
                        selectedRooms = roomsList.SelectedItems.Cast<RoomItem>().Select(item => item.Room).ToList();

                        if (selectedRooms.Count == 0)
                        {
                            MessageBox.Show("Выберите хотя бы одно помещение", "Предупреждение",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        wnd.DialogResult = true;
                        wnd.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                buttonPanel.Children.Add(createBtn);
                buttonPanel.Children.Add(cancelBtn);
                mainPanel.Children.Add(buttonPanel);

                wnd.Content = mainPanel;

                return wnd.ShowDialog() == true ? selectedRooms : null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии диалога: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// Диалог выбора рамки
        /// </summary>
        private TitleBlockInfo ShowTitleBlockSelectionDialog()
        {
            TitleBlockInfo selectedBlock = null;

            try
            {
                Window wnd = new Window
                {
                    Title = "Выбор рамки для листов",
                    Width = 500,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true
                };

                StackPanel mainPanel = new StackPanel { Margin = new Thickness(15) };

                TextBlock infoText = new TextBlock
                {
                    Text = "Выберите тип рамки для создания листов:",
                    Margin = new Thickness(0, 0, 0, 15),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                };
                mainPanel.Children.Add(infoText);

                // Список рамок
                ListBox titleBlocksList = new ListBox
                {
                    Height = 200,
                    Margin = new Thickness(0, 0, 0, 15),
                    DisplayMemberPath = "Name"
                };

                titleBlocksList.ItemsSource = _availableTitleBlocks;
                if (_availableTitleBlocks.Count > 0)
                    titleBlocksList.SelectedIndex = 0;

                mainPanel.Children.Add(titleBlocksList);

                // Кнопки
                StackPanel buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                Button okBtn = new Button
                {
                    Content = "✅ Выбрать",
                    Width = 100,
                    Height = 35,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };

                Button cancelBtn = new Button
                {
                    Content = "✖ Отмена",
                    Width = 100,
                    Height = 35,
                    IsCancel = true
                };

                okBtn.Click += (s, e) =>
                {
                    try
                    {
                        selectedBlock = titleBlocksList.SelectedItem as TitleBlockInfo;
                        if (selectedBlock == null)
                        {
                            MessageBox.Show("Выберите тип рамки", "Предупреждение",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        wnd.DialogResult = true;
                        wnd.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                buttonPanel.Children.Add(okBtn);
                buttonPanel.Children.Add(cancelBtn);
                mainPanel.Children.Add(buttonPanel);

                wnd.Content = mainPanel;

                return wnd.ShowDialog() == true ? selectedBlock : null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии диалога: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// Создание видов и листов для выбранных помещений
        /// </summary>
        private bool CreateViewsAndSheetsForRooms(List<Room> rooms, ElementId titleBlockId)
        {
            bool allSuccess = true;
            List<ViewSheet> createdSheets = new List<ViewSheet>();

            // Используем одну транзакцию для всех операций
            using (TransactionGroup transGroup = new TransactionGroup(_doc, "Создание видов и листов для помещений"))
            {
                try
                {
                    transGroup.Start();

                    foreach (Room room in rooms)
                    {
                        try
                        {
                            // Создаем 3D вид для помещения в отдельной транзакции
                            View3D view3D = null;
                            using (Transaction t = new Transaction(_doc, $"Создание вида для помещения {room.Number}"))
                            {
                                t.Start();
                                view3D = CreateRoom3DView(room);

                                if (view3D != null)
                                {
                                    t.Commit();
                                }
                                else
                                {
                                    t.RollBack();
                                    allSuccess = false;
                                    continue;
                                }
                            }

                            // Создаем лист и размещаем вид в отдельной транзакции
                            if (view3D != null)
                            {
                                using (Transaction t = new Transaction(_doc, $"Создание листа для помещения {room.Number}"))
                                {
                                    t.Start();
                                    ViewSheet sheet = CreateSheetWithView(room, view3D, titleBlockId);

                                    if (sheet != null)
                                    {
                                        createdSheets.Add(sheet);
                                        t.Commit();
                                    }
                                    else
                                    {
                                        t.RollBack();
                                        allSuccess = false;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            allSuccess = false;
                            TaskDialog.Show("Ошибка", $"Ошибка при создании для помещения {room.Number}: {ex.Message}\n\nПродолжаем со следующим помещением.");
                        }
                    }

                    if (createdSheets.Count > 0)
                    {
                        transGroup.Assimilate();

                        // Открываем первый созданный лист
                        try
                        {
                            _uidoc.ActiveView = createdSheets[0];
                        }
                        catch (Exception) { }
                    }
                    else
                    {
                        transGroup.RollBack();
                    }
                }
                catch (Exception ex)
                {
                    transGroup.RollBack();
                    TaskDialog.Show("Ошибка", $"Критическая ошибка: {ex.Message}");
                    return false;
                }
            }

            return allSuccess && createdSheets.Count > 0;
        }

        /// <summary>
        /// Создание 3D вида, обрезанного по помещению
        /// </summary>
        private View3D CreateRoom3DView(Room room)
        {
            try
            {
                // Генерируем уникальное имя для вида
                string viewName = GenerateUniqueViewName(room);

                // Создаем новый 3D вид
                View3D view3D = CreateNew3DView(viewName);
                if (view3D == null) return null;

                // Получаем границы помещения
                BoundingBoxXYZ roomBoundingBox = GetRoomBoundingBox(room);
                if (roomBoundingBox == null) return null;

                // Настраиваем секцию (обрезание) по границам помещения
                SetupViewSection(view3D, roomBoundingBox);

                // Настраиваем параметры вида
                ConfigureViewSettings(view3D);

                return view3D;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Генерация уникального имени для вида
        /// </summary>
        private string GenerateUniqueViewName(Room room)
        {
            try
            {
                string baseName = $"3D_{room.Number}_{room.Name}";
                // Очищаем имя от недопустимых символов
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    baseName = baseName.Replace(c, '_');
                }

                string viewName = baseName;
                int counter = 1;

                // Проверяем существующие виды
                var existingViews = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(v => !v.IsTemplate)
                    .Select(v => v.Name)
                    .ToList();

                while (existingViews.Contains(viewName))
                {
                    viewName = $"{baseName}_{counter}";
                    counter++;
                }

                return viewName;
            }
            catch (Exception)
            {
                return $"3D_Room_{DateTime.Now.Ticks}";
            }
        }

        /// <summary>
        /// Создание нового 3D вида
        /// </summary>
        private View3D CreateNew3DView(string viewName)
        {
            try
            {
                // Получаем существующий 3D вид как шаблон
                ViewFamilyType viewFamilyType = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

                if (viewFamilyType == null) return null;

                // Создаем вид
                View3D view3D = View3D.CreateIsometric(_doc, viewFamilyType.Id);
                view3D.Name = viewName;

                return view3D;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Получение ограничивающего бокса помещения
        /// </summary>
        private BoundingBoxXYZ GetRoomBoundingBox(Room room)
        {
            try
            {
                // Получаем геометрию помещения
                SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
                IList<IList<BoundarySegment>> boundaries = room.GetBoundarySegments(options);

                if (boundaries == null || boundaries.Count == 0)
                    return null;

                // Собираем все точки контура
                List<XYZ> points = new List<XYZ>();

                foreach (var boundary in boundaries)
                {
                    foreach (BoundarySegment segment in boundary)
                    {
                        Curve curve = segment.GetCurve();
                        if (curve != null)
                        {
                            points.Add(curve.GetEndPoint(0));
                            points.Add(curve.GetEndPoint(1));
                        }
                    }
                }

                if (points.Count == 0) return null;

                // Вычисляем минимальные/максимальные координаты
                double minX = points.Min(p => p.X);
                double minY = points.Min(p => p.Y);
                double minZ = room.Level != null ? room.Level.Elevation : 0;
                double maxX = points.Max(p => p.X);
                double maxY = points.Max(p => p.Y);
                double maxZ = minZ + (room.UnboundedHeight > 0 ? room.UnboundedHeight : 10); // Если высота не задана, берем 3 метра (~10 футов)

                // Проверяем корректность значений
                if (double.IsInfinity(minX) || double.IsNaN(minX)) return null;

                // Создаем ограничивающий бокс
                BoundingBoxXYZ boundingBox = new BoundingBoxXYZ();
                boundingBox.Min = new XYZ(minX, minY, minZ);
                boundingBox.Max = new XYZ(maxX, maxY, maxZ);

                return boundingBox;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Настройка секции (обрезания) вида по границам помещения
        /// </summary>
        private void SetupViewSection(View3D view3D, BoundingBoxXYZ roomBoundingBox)
        {
            try
            {
                // Включаем секционирование
                view3D.IsSectionBoxActive = true;

                // Создаем и устанавливаем бокс секции
                BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();

                // Добавляем небольшой отступ (10 см) от границ помещения
                double offset = 0.328; // 10 см в футах (~0.328 фута)

                sectionBox.Min = new XYZ(
                    roomBoundingBox.Min.X - offset,
                    roomBoundingBox.Min.Y - offset,
                    roomBoundingBox.Min.Z - offset
                );

                sectionBox.Max = new XYZ(
                    roomBoundingBox.Max.X + offset,
                    roomBoundingBox.Max.Y + offset,
                    roomBoundingBox.Max.Z + offset
                );

                view3D.SetSectionBox(sectionBox);
            }
            catch (Exception)
            {
                // Если не удалось настроить секцию, просто продолжаем без нее
            }
        }

        /// <summary>
        /// Настройка параметров вида
        /// </summary>
        private void ConfigureViewSettings(View3D view3D)
        {
            try
            {
                view3D.DisplayStyle = DisplayStyle.ShadingWithEdges;
                view3D.Scale = 100;
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Создание листа и размещение на нем вида
        /// </summary>
        private ViewSheet CreateSheetWithView(Room room, View3D view3D, ElementId titleBlockId)
        {
            try
            {
                // Генерируем имя для листа
                string sheetName = $"3D - {room.Number} - {room.Name}";

                // Создаем лист с выбранной рамкой
                ViewSheet sheet = CreateSheet(sheetName, titleBlockId);
                if (sheet == null) return null;

                // Размещаем вид на листе
                bool placed = PlaceViewOnSheet(sheet, view3D);

                return placed ? sheet : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Создание нового листа с указанной рамкой
        /// </summary>
        private ViewSheet CreateSheet(string sheetName, ElementId titleBlockId)
        {
            try
            {
                // Получаем FamilySymbol для рамки
                FamilySymbol titleBlockSymbol = _doc.GetElement(titleBlockId) as FamilySymbol;
                if (titleBlockSymbol == null) return null;

                // Убеждаемся, что символ активен
                if (!titleBlockSymbol.IsActive)
                {
                    titleBlockSymbol.Activate();
                }

                // Создаем лист
                ViewSheet sheet = ViewSheet.Create(_doc, titleBlockId);

                // Генерируем номер листа
                sheet.SheetNumber = GenerateSheetNumber();
                sheet.Name = sheetName;

                return sheet;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Генерация номера листа
        /// </summary>
        private string GenerateSheetNumber()
        {
            try
            {
                // Находим максимальный номер листа
                var sheets = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Select(s => s.SheetNumber)
                    .ToList();

                if (sheets.Count == 0) return "001";

                // Ищем числовые номера
                var numbers = sheets
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();

                if (numbers.Count == 0) return "001";

                int maxNumber = numbers.Max();
                return (maxNumber + 1).ToString("D3");
            }
            catch (Exception)
            {
                return "001";
            }
        }

        /// <summary>
        /// Размещение вида на листе
        /// </summary>
        private bool PlaceViewOnSheet(ViewSheet sheet, View3D view3D)
        {
            try
            {
                // Получаем границы листа
                BoundingBoxXYZ sheetBBox = sheet.get_BoundingBox(null);
                if (sheetBBox == null) return false;

                // Рассчитываем центр листа
                XYZ center = new XYZ(
                    (sheetBBox.Min.X + sheetBBox.Max.X) / 2,
                    (sheetBBox.Min.Y + sheetBBox.Max.Y) / 2,
                    0
                );

                // Создаем экземпляр вида на листе
                Viewport viewport = Viewport.Create(_doc, sheet.Id, view3D.Id, center);

                return viewport != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Класс для отображения помещения в списке
        /// </summary>
        private class RoomItem : INotifyPropertyChanged
        {
            public Room Room { get; set; }
            public string DisplayName { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        /// <summary>
        /// Класс для информации о рамке
        /// </summary>
        private class TitleBlockInfo
        {
            public ElementId ElementId { get; set; }
            public string Name { get; set; }
        }
    }
}