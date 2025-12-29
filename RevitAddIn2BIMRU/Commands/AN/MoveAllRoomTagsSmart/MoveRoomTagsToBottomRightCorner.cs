#region Namespace

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;

#endregion

namespace RevitAddIn2BIMRU.Commands.AN
{ 
    [Transaction(TransactionMode.Manual)]
    public class MoveAllRoomTagsSmart : IExternalCommand
    {
        // Значения по умолчанию для масштаба 1:100
        private double _offsetXmm = 1500.0; // 1.5 метра для масштаба 1:100
        private double _offsetYmm = 1500.0; // 1.5 метра для масштаба 1:100

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            Autodesk.Revit.DB.View view = doc.ActiveView;

            // Показываем WPF окно для настройки отступов
            var settingsWindow = new OffsetSettingsWindow(_offsetXmm, _offsetYmm);
            settingsWindow.Owner = System.Windows.Application.Current?.MainWindow;
            settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            if (settingsWindow.ShowDialog() == true)
            {
                _offsetXmm = settingsWindow.OffsetX;
                _offsetYmm = settingsWindow.OffsetY;
            }
            else
            {
                return Result.Cancelled; // Пользователь отменил
            }

            // Конвертация мм в футы
            double offsetXft = _offsetXmm / 304.8;
            double offsetYft = _offsetYmm / 304.8;

            using (Transaction t = new Transaction(doc, $"Перемещение марок (X:{_offsetXmm}мм Y:{_offsetYmm}мм)"))
            {
                t.Start();

                try
                {
                    // Собираем все марки
                    var tags = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_RoomTags)
                        .WhereElementIsNotElementType()
                        .ToList();

                    int moved = 0;
                    int failed = 0;
                    List<string> failedDetails = new List<string>();

                    foreach (Element tag in tags)
                    {
                        string result = ProcessTag(tag, offsetXft, offsetYft);
                        if (result == "OK")
                        {
                            moved++;
                        }
                        else
                        {
                            failed++;
                            failedDetails.Add(result);
                        }
                    }

                    t.Commit();

                    // Показываем результаты
                    ShowResults(moved, failed, failedDetails);

                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                    t.RollBack();
                    return Result.Failed;
                }
            }
        }

        private string ProcessTag(Element tag, double offsetXft, double offsetYft)
        {
            try
            {
                // Текущее положение марки
                LocationPoint tagLoc = tag.Location as LocationPoint;
                if (tagLoc == null) return "Неверный тип расположения марки";

                XYZ currentPoint = tagLoc.Point;

                // Ищем помещение
                Room room = FindRoomContainingPoint(tag.Document, currentPoint);
                if (room == null) return "Не найдено помещение";

                // Получаем все уникальные углы помещения
                List<XYZ> corners = GetAllRoomCorners(room);
                if (corners.Count == 0) return "Не удалось получить углы помещения";

                // Находим самую правую точку
                double maxX = corners.Max(p => p.X);
                var rightmostCorners = corners.Where(p => Math.Abs(p.X - maxX) < 0.001).ToList();

                // Среди самых правых находим самую нижнюю
                double minY = rightmostCorners.Min(p => p.Y);
                var bottomRightCorners = rightmostCorners.Where(p => Math.Abs(p.Y - minY) < 0.001).ToList();

                XYZ targetCorner = bottomRightCorners.First();

                // Ищем точку внутри помещения с учетом отступа
                XYZ insidePoint = FindInsidePointWithOffset(room, targetCorner, offsetXft, offsetYft);
                if (insidePoint == null) return "Не найдена точка внутри помещения";

                // Перемещаем марку
                ElementTransformUtils.MoveElement(tag.Document, tag.Id, insidePoint - currentPoint);

                return "OK";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }

        private List<XYZ> GetAllRoomCorners(Room room)
        {
            List<XYZ> allPoints = new List<XYZ>();

            try
            {
                var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                if (boundaries == null) return allPoints;

                // Собираем все конечные точки кривых
                foreach (var boundary in boundaries)
                {
                    foreach (var segment in boundary)
                    {
                        Curve curve = segment.GetCurve();
                        if (curve != null)
                        {
                            allPoints.Add(curve.GetEndPoint(0));
                            allPoints.Add(curve.GetEndPoint(1));
                        }
                    }
                }

                // Убираем дубликаты
                return RemoveDuplicates(allPoints);
            }
            catch
            {
                return allPoints;
            }
        }

        private List<XYZ> RemoveDuplicates(List<XYZ> points)
        {
            List<XYZ> unique = new List<XYZ>();

            foreach (XYZ point in points)
            {
                if (!unique.Any(p =>
                    Math.Abs(p.X - point.X) < 0.001 &&
                    Math.Abs(p.Y - point.Y) < 0.001 &&
                    Math.Abs(p.Z - point.Z) < 0.001))
                {
                    unique.Add(point);
                }
            }

            return unique;
        }

        /// <summary>
        /// Находит точку внутри помещения с применением отступа
        /// </summary>
        private XYZ FindInsidePointWithOffset(Room room, XYZ corner, double offsetXft, double offsetYft)
        {
            // Пробуем сразу точку с отступом
            XYZ pointWithOffset = new XYZ(
                corner.X - offsetXft,
                corner.Y + offsetYft,
                corner.Z);

            if (room.IsPointInRoom(pointWithOffset))
            {
                return pointWithOffset;
            }

            // Если точка с отступом не внутри, ищем альтернативу
            return FindBestPointWithOffset(room, corner, offsetXft, offsetYft);
        }

        /// <summary>
        /// Находит лучшую точку с учетом отступа
        /// </summary>
        private XYZ FindBestPointWithOffset(Room room, XYZ corner, double offsetXft, double offsetYft)
        {
            // Пробуем разные проценты от желаемого отступа
            double[] percentages = { 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1, 0.0 };

            foreach (double percentage in percentages)
            {
                double appliedOffsetX = offsetXft * percentage;
                double appliedOffsetY = offsetYft * percentage;

                XYZ testPoint = new XYZ(
                    corner.X - appliedOffsetX,
                    corner.Y + appliedOffsetY,
                    corner.Z);

                if (room.IsPointInRoom(testPoint))
                {
                    return testPoint;
                }
            }

            // Если не нашли, используем центр помещения
            return GetRoomCenter(room);
        }

        private XYZ GetRoomCenter(Room room)
        {
            try
            {
                if (room.Location is LocationPoint locationPoint)
                {
                    return locationPoint.Point;
                }

                // Альтернативный способ через bounding box
                BoundingBoxXYZ bbox = room.get_BoundingBox(null);
                if (bbox != null)
                {
                    return new XYZ(
                        (bbox.Min.X + bbox.Max.X) / 2,
                        (bbox.Min.Y + bbox.Max.Y) / 2,
                        bbox.Min.Z);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private Room FindRoomContainingPoint(Document doc, XYZ point)
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>();

            return rooms.FirstOrDefault(r => r.IsPointInRoom(point));
        }

        private void ShowResults(int moved, int failed, List<string> failedDetails)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Отступ по X: {_offsetXmm} мм ({_offsetXmm / 1000.0:F2} м)");
            sb.AppendLine($"Отступ по Y: {_offsetYmm} мм ({_offsetYmm / 1000.0:F2} м)");
            sb.AppendLine($"Успешно перемещено: {moved}");
            sb.AppendLine($"Не удалось: {failed}");

            if (failed > 0 && failedDetails.Count > 0)
            {
                sb.AppendLine("\nОсновные ошибки:");
                int showCount = Math.Min(failedDetails.Count, 5);
                for (int i = 0; i < showCount; i++)
                {
                    string shortMsg = failedDetails[i].Length > 60 ?
                        failedDetails[i].Substring(0, 60) + "..." : failedDetails[i];
                    sb.AppendLine($"  • {shortMsg}");
                }

                if (failedDetails.Count > 5)
                {
                    sb.AppendLine($"  ... и еще {failedDetails.Count - 5} ошибок");
                }
            }

            sb.AppendLine("\nПримечание: Для маленьких помещений полный отступ может не применяться.");

            TaskDialog.Show("Результат перемещения марок", sb.ToString());
        }
    }

    /// <summary>
    /// WPF окно для настройки отступов
    /// </summary>
    public partial class OffsetSettingsWindow : Window
    {
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }

        public OffsetSettingsWindow(double defaultOffsetX, double defaultOffsetY)
        {
            InitializeComponent();
            OffsetX = defaultOffsetX;
            OffsetY = defaultOffsetY;

            txtOffsetX.Text = defaultOffsetX.ToString();
            txtOffsetY.Text = defaultOffsetY.ToString();

            UpdatePreview();
        }

        private void InitializeComponent()
        {
            this.Title = "Настройка отступов марки";
            this.Width = 500;
            this.Height = 400;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowStyle = WindowStyle.SingleBorderWindow;

            // Основной контейнер
            var mainGrid = new System.Windows.Controls.Grid();
            mainGrid.Margin = new Thickness(10);

            // Определение строк
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Заголовок
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Описание
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Отступ X
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Отступ Y
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Рекомендации
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Пространство
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Кнопки

            // Заголовок
            var lblTitle = new TextBlock
            {
                Text = "Укажите отступы марки от правого нижнего угла помещения:",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            System.Windows.Controls.Grid.SetRow(lblTitle, 0);

            // Пояснение
            var lblExplanation = new TextBlock
            {
                Text = "X - отступ влево от правой границы\nY - отступ вверх от нижней границы",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };
            System.Windows.Controls.Grid.SetRow(lblExplanation, 1);

            // Отступ по X
            var xGrid = new System.Windows.Controls.Grid();
            xGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            xGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            xGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            xGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            xGrid.Margin = new Thickness(0, 0, 0, 10);

            var lblOffsetX = new TextBlock
            {
                Text = "Отступ по X (горизонтальный):",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            System.Windows.Controls.Grid.SetColumn(lblOffsetX, 0);

            txtOffsetX = new System.Windows.Controls.TextBox
            {
                Text = "1500",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            System.Windows.Controls.Grid.SetColumn(txtOffsetX, 1);

            var lblMmX = new TextBlock
            {
                Text = "мм",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 10, 0)
            };
            System.Windows.Controls.Grid.SetColumn(lblMmX, 2);

            lblPreviewX = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Blue,
                Margin = new Thickness(5, 0, 0, 0)
            };
            System.Windows.Controls.Grid.SetColumn(lblPreviewX, 3);

            xGrid.Children.Add(lblOffsetX);
            xGrid.Children.Add(txtOffsetX);
            xGrid.Children.Add(lblMmX);
            xGrid.Children.Add(lblPreviewX);
            System.Windows.Controls.Grid.SetRow(xGrid, 2);

            // Отступ по Y
            var yGrid = new System.Windows.Controls.Grid();
            yGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            yGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            yGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            yGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            yGrid.Margin = new Thickness(0, 0, 0, 20);

            var lblOffsetY = new TextBlock
            {
                Text = "Отступ по Y (вертикальный):",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            System.Windows.Controls.Grid.SetColumn(lblOffsetY, 0);

            txtOffsetY = new System.Windows.Controls.TextBox
            {
                Text = "1500",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(txtOffsetY, 1);

            var lblMmY = new TextBlock
            {
                Text = "мм",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 10, 0)
            };
            Grid.SetColumn(lblMmY, 2);

            lblPreviewY = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Blue,
                Margin = new Thickness(5, 0, 0, 0)
            };
            Grid.SetColumn(lblPreviewY, 3);

            yGrid.Children.Add(lblOffsetY);
            yGrid.Children.Add(txtOffsetY);
            yGrid.Children.Add(lblMmY);
            yGrid.Children.Add(lblPreviewY);
            System.Windows.Controls.Grid.SetRow(yGrid, 3);

            // Рекомендации
            var border = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = Brushes.LightYellow,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 20)
            };

            var recommendations = new TextBlock
            {
                Text = "Рекомендации для масштаба 1:100:\n" +
                       "• 1500 мм = 1.5 м (стандартный)\n" +
                       "• 1000 мм = 1.0 м (маленький)\n" +
                       "• 2000 мм = 2.0 м (большой)\n" +
                       "•  500 мм = 0.5 м (очень маленький)",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            border.Child = recommendations;
            System.Windows.Controls.Grid.SetRow(border, 4);

            // Кнопки
            var buttonGrid = new Grid();
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            buttonGrid.Margin = new Thickness(0, 20, 0, 0);

            var btnOk = new Button
            {
                Content = "Применить",
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            Grid.SetColumn(btnOk, 1);

            var btnCancel = new Button
            {
                Content = "Отмена",
                Width = 100,
                Height = 30,
                IsCancel = true
            };
            Grid.SetColumn(btnCancel, 2);

            buttonGrid.Children.Add(btnOk);
            buttonGrid.Children.Add(btnCancel);
            System.Windows.Controls.Grid.SetRow(buttonGrid, 6);

            // Добавляем все элементы в основной грид
            mainGrid.Children.Add(lblTitle);
            mainGrid.Children.Add(lblExplanation);
            mainGrid.Children.Add(xGrid);
            mainGrid.Children.Add(yGrid);
            mainGrid.Children.Add(border);
            mainGrid.Children.Add(buttonGrid);

            this.Content = mainGrid;

            // Обработчики событий
            btnOk.Click += BtnOk_Click;
            btnCancel.Click += BtnCancel_Click;
            txtOffsetX.TextChanged += TxtOffset_TextChanged;
            txtOffsetY.TextChanged += TxtOffset_TextChanged;
        }

        private TextBox txtOffsetX;
        private TextBox txtOffsetY;
        private TextBlock lblPreviewX;
        private TextBlock lblPreviewY;

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtOffsetX.Text, out double offsetX) &&
                double.TryParse(txtOffsetY.Text, out double offsetY))
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                this.DialogResult = true;
            }
            else
            {
                MessageBox.Show("Пожалуйста, введите числовые значения для отступов.", "Ошибка ввода",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void TxtOffset_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (double.TryParse(txtOffsetX.Text, out double offsetXmm))
            {
                lblPreviewX.Text = $"({offsetXmm / 1000.0:F2} м)";
            }
            else
            {
                lblPreviewX.Text = "";
            }

            if (double.TryParse(txtOffsetY.Text, out double offsetYmm))
            {
                lblPreviewY.Text = $"({offsetYmm / 1000.0:F2} м)";
            }
            else
            {
                lblPreviewY.Text = "";
            }
        }
    }

    /// <summary>
    /// Расширенное WPF окно для настройки отступов с предустановками
    /// </summary>
    public partial class AdvancedOffsetSettingsWindow : Window
    {
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }

        // Предустановленные значения
        private Dictionary<string, (double X, double Y, string Description)> _presets =
            new Dictionary<string, (double X, double Y, string Description)>
            {
                {"Очень маленький", (500, 500, "500 мм = 0.5 м")},
                {"Маленький", (1000, 1000, "1000 мм = 1.0 м")},
                {"Стандартный", (1500, 1500, "1500 мм = 1.5 м")},
                {"Большой", (2000, 2000, "2000 мм = 2.0 м")},
                {"Очень большой", (3000, 3000, "3000 мм = 3.0 м")},
                {"Только по X", (1500, 0, "X: 1500 мм, Y: 0 мм")},
                {"Только по Y", (0, 1500, "X: 0 мм, Y: 1500 мм")},
                {"Пользовательский", (1500, 1500, "Задать свои значения")}
            };

        public AdvancedOffsetSettingsWindow(double defaultOffsetX, double defaultOffsetY)
        {
            InitializeComponent();
            OffsetX = defaultOffsetX;
            OffsetY = defaultOffsetY;

            txtOffsetX.Text = defaultOffsetX.ToString();
            txtOffsetY.Text = defaultOffsetY.ToString();

            // Заполняем комбобокс предустановками
            cmbPresets.ItemsSource = _presets.Keys;

            // Ищем подходящую предустановку
            string matchedPreset = "Пользовательский";
            foreach (var preset in _presets)
            {
                if (Math.Abs(preset.Value.X - defaultOffsetX) < 1 &&
                    Math.Abs(preset.Value.Y - defaultOffsetY) < 1)
                {
                    matchedPreset = preset.Key;
                    break;
                }
            }

            cmbPresets.SelectedItem = matchedPreset;
            UpdatePreview();
            UpdateDescription();
        }

        private void InitializeComponent()
        {
            this.Title = "Настройка отступов марки";
            this.Width = 500;
            this.Height = 450;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowStyle = WindowStyle.SingleBorderWindow;

            // Основной контейнер
            var mainGrid = new Grid();
            mainGrid.Margin = new Thickness(10);

            // Определение строк
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Заголовок
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Предустановки
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Описание предустановки
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Отступ X
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Отступ Y
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Примечание
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Пространство
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Кнопки

            // Заголовок
            var lblTitle = new TextBlock
            {
                Text = "Настройка отступов от правого нижнего угла помещения",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            System.Windows.Controls.Grid.SetRow(lblTitle, 0);

            // Предустановки
            var presetGrid = new Grid();
            presetGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            presetGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            presetGrid.Margin = new Thickness(0, 0, 0, 5);

            var lblPresets = new TextBlock
            {
                Text = "Предустановки:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(lblPresets, 0);

            cmbPresets = new ComboBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 150
            };
            Grid.SetColumn(cmbPresets, 1);

            presetGrid.Children.Add(lblPresets);
            presetGrid.Children.Add(cmbPresets);
            System.Windows.Controls.Grid.SetRow(presetGrid, 1);

            // Описание предустановки
            lblDescription = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = Brushes.DarkGreen,
                Margin = new Thickness(0, 0, 0, 15)
            };
            System.Windows.Controls.Grid.SetRow(lblDescription, 2);

            // Отступ по X
            var xGrid = new Grid();
            xGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            xGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            xGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            xGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            xGrid.Margin = new Thickness(0, 0, 0, 10);

            var lblOffsetX = new TextBlock
            {
                Text = "Отступ по X (влево):",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(lblOffsetX, 0);

            txtOffsetX = new System.Windows.Controls.TextBox
            {
                Text = "1500",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(txtOffsetX, 1);

            var lblMmX = new TextBlock
            {
                Text = "мм",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 10, 0)
            };
            Grid.SetColumn(lblMmX, 2);

            lblPreviewX = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Blue,
                Margin = new Thickness(5, 0, 0, 0)
            };
            System.Windows.Controls.Grid.SetColumn(lblPreviewX, 3);

            xGrid.Children.Add(lblOffsetX);
            xGrid.Children.Add(txtOffsetX);
            xGrid.Children.Add(lblMmX);
            xGrid.Children.Add(lblPreviewX);
            System.Windows.Controls.Grid.SetRow(xGrid, 3);

            // Отступ по Y
            var yGrid = new System.Windows.Controls.Grid();
            yGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            yGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            yGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            yGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            yGrid.Margin = new Thickness(0, 0, 0, 15);

            var lblOffsetY = new TextBlock
            {
                Text = "Отступ по Y (вверх):",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            System.Windows.Controls.Grid.SetColumn(lblOffsetY, 0);

            txtOffsetY = new System.Windows.Controls.TextBox
            {
                Text = "1500",
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(txtOffsetY, 1);

            var lblMmY = new TextBlock
            {
                Text = "мм",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 10, 0)
            };
            Grid.SetColumn(lblMmY, 2);

            lblPreviewY = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Blue,
                Margin = new Thickness(5, 0, 0, 0)
            };
            Grid.SetColumn(lblPreviewY, 3);

            yGrid.Children.Add(lblOffsetY);
            yGrid.Children.Add(txtOffsetY);
            yGrid.Children.Add(lblMmY);
            yGrid.Children.Add(lblPreviewY);
            System.Windows.Controls.Grid.SetRow(yGrid, 4);

            // Примечание
            var noteText = new TextBlock
            {
                Text = "Примечание:\n" +
                       "• Для маленьких помещений отступ может быть автоматически уменьшен\n" +
                       "• X - отступ влево от правой границы\n" +
                       "• Y - отступ вверх от нижней границы",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkGray,
                Margin = new Thickness(0, 0, 0, 20)
            };
            System.Windows.Controls.Grid.SetRow(noteText, 5);

            // Кнопки
            var buttonGrid = new Grid();
            buttonGrid.HorizontalAlignment = HorizontalAlignment.Right;
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            var btnOk = new Button
            {
                Content = "Применить",
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            System.Windows.Controls.Grid.SetColumn(btnOk, 0);

            var btnCancel = new Button
            {
                Content = "Отмена",
                Width = 100,
                Height = 30,
                IsCancel = true
            };
            System.Windows.Controls.Grid.SetColumn(btnCancel, 1);

            buttonGrid.Children.Add(btnOk);
            buttonGrid.Children.Add(btnCancel);
            System.Windows.Controls.Grid.SetRow(buttonGrid, 7);

            // Добавляем все элементы в основной грид
            mainGrid.Children.Add(lblTitle);
            mainGrid.Children.Add(presetGrid);
            mainGrid.Children.Add(lblDescription);
            mainGrid.Children.Add(xGrid);
            mainGrid.Children.Add(yGrid);
            mainGrid.Children.Add(noteText);
            mainGrid.Children.Add(buttonGrid);

            this.Content = mainGrid;

            // Обработчики событий
            btnOk.Click += BtnOk_Click;
            btnCancel.Click += BtnCancel_Click;
            txtOffsetX.TextChanged += TxtOffset_TextChanged;
            txtOffsetY.TextChanged += TxtOffset_TextChanged;
            cmbPresets.SelectionChanged += CmbPresets_SelectionChanged;
        }

        private TextBox txtOffsetX;
        private TextBox txtOffsetY;
        private ComboBox cmbPresets;
        private TextBlock lblPreviewX;
        private TextBlock lblPreviewY;
        private TextBlock lblDescription;

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtOffsetX.Text, out double offsetX) &&
                double.TryParse(txtOffsetY.Text, out double offsetY))
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                this.DialogResult = true;
            }
            else
            {
                MessageBox.Show("Пожалуйста, введите числовые значения для отступов.", "Ошибка ввода",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void TxtOffset_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
            UpdatePresetSelection();
        }

        private void CmbPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPresets.SelectedItem != null)
            {
                string presetName = cmbPresets.SelectedItem.ToString();
                if (_presets.ContainsKey(presetName))
                {
                    var preset = _presets[presetName];
                    txtOffsetX.Text = preset.X.ToString();
                    txtOffsetY.Text = preset.Y.ToString();
                    UpdatePreview();
                    UpdateDescription();
                }
            }
        }

        private void UpdatePreview()
        {
            if (double.TryParse(txtOffsetX.Text, out double offsetXmm))
            {
                lblPreviewX.Text = $"({offsetXmm / 1000.0:F2} м)";
            }
            else
            {
                lblPreviewX.Text = "";
            }

            if (double.TryParse(txtOffsetY.Text, out double offsetYmm))
            {
                lblPreviewY.Text = $"({offsetYmm / 1000.0:F2} м)";
            }
            else
            {
                lblPreviewY.Text = "";
            }
        }

        private void UpdateDescription()
        {
            if (double.TryParse(txtOffsetX.Text, out double offsetXmm) &&
                double.TryParse(txtOffsetY.Text, out double offsetYmm))
            {
                lblDescription.Text = $"X: {offsetXmm} мм, Y: {offsetYmm} мм  ({offsetXmm / 1000:F1} м × {offsetYmm / 1000:F1} м)";
            }
            else
            {
                lblDescription.Text = "";
            }
        }

        private void UpdatePresetSelection()
        {
            if (double.TryParse(txtOffsetX.Text, out double offsetXmm) &&
                double.TryParse(txtOffsetY.Text, out double offsetYmm))
            {
                // Ищем совпадение с предустановками
                string matchedPreset = "Пользовательский";
                foreach (var preset in _presets)
                {
                    if (preset.Key != "Пользовательский" &&
                        Math.Abs(preset.Value.X - offsetXmm) < 1 &&
                        Math.Abs(preset.Value.Y - offsetYmm) < 1)
                    {
                        matchedPreset = preset.Key;
                        break;
                    }
                }

                // Обновляем выбранную предустановку
                if (cmbPresets.SelectedItem == null || cmbPresets.SelectedItem.ToString() != matchedPreset)
                {
                    cmbPresets.SelectedItem = matchedPreset;
                    UpdateDescription();
                }
            }
        }
    }
}