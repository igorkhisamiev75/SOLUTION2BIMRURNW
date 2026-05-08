#region Namespaces
using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
#endregion

namespace RevitAddIn2BIMRU.Commands.SP
{
    [Transaction(TransactionMode.Manual)]
    public class SectionFromCurtainWall : IExternalCommand
    {
        private Document _doc;
        private UIDocument _uidoc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                _uidoc = commandData.Application.ActiveUIDocument;
                _doc = _uidoc.Document;

                // 1. Выбор витражной стены
                Wall curtainWall = GetSelectedCurtainWall();
                if (curtainWall == null) return Result.Cancelled;

                // 2. Получение параметра группировки
                string groupingValue = GetGroupingValue(curtainWall);
                if (string.IsNullOrEmpty(groupingValue)) return Result.Cancelled;

                // 3. Создание фасадного разреза (смотрит на стену)
                ViewSection section = CreateFacadeSection(curtainWall, groupingValue);
                if (section == null)
                {
                    TaskDialog.Show("Ошибка", "Не удалось создать разрез.");
                    return Result.Failed;
                }

                // 4. Показ созданного разреза
                _uidoc.ActiveView = section;

                // 5. Диалог выбора листа и размещения
                ShowSheetSelectionDialog(section, groupingValue);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Критическая ошибка: {ex.Message}\n\nСтек: {ex.StackTrace}");
                return Result.Failed;
            }
        }

        private Wall GetSelectedCurtainWall()
        {
            if (_uidoc.Selection.GetElementIds().Count != 1)
            {
                TaskDialog.Show("Ошибка", "Выберите одну витражную стену");
                return null;
            }

            Element selectedElement = _doc.GetElement(_uidoc.Selection.GetElementIds().First());
            Wall wall = selectedElement as Wall;

            if (wall == null)
            {
                TaskDialog.Show("Ошибка", "Выбранный элемент не является стеной");
                return null;
            }

            if (wall.CurtainGrid == null)
            {
                TaskDialog.Show("Ошибка", "Выбранная стена не является витражной");
                return null;
            }

            return wall;
        }

        private string GetGroupingValue(Wall wall)
        {
            Parameter param = wall.LookupParameter("ADSK_Группирование");
            if (param == null || string.IsNullOrEmpty(param.AsString()))
            {
                TaskDialog.Show("Ошибка", "Параметр 'ADSK_Группирование' не найден или пуст");
                return null;
            }
            return param.AsString();
        }

        private ViewSection CreateFacadeSection(Wall wall, string groupingValue)
        {
            using (Transaction t = new Transaction(_doc, "Создание фасадного разреза"))
            {
                t.Start();
                try
                {
                    LocationCurve lc = wall.Location as LocationCurve;
                    if (lc == null) { t.RollBack(); return null; }

                    Curve curve = lc.Curve;
                    XYZ p = curve.GetEndPoint(0);
                    XYZ q = curve.GetEndPoint(1);
                    XYZ mid = (p + q) / 2;

                    XYZ wallDir = (q - p).Normalize(); // вдоль стены
                    XYZ up = XYZ.BasisZ;              // вертикаль
                    XYZ viewDir = wallDir.CrossProduct(up).Normalize(); // на стену
                    XYZ right = up.CrossProduct(viewDir); // вдоль стены (X)

                    BoundingBoxXYZ bb = wall.get_BoundingBox(null);
                    double wallHeight = bb.Max.Z - bb.Min.Z;
                    double wallLength = (q - p).GetLength();
                    double wallThickness = wall.WallType.Width;

                    // Конвертируем 200 мм в футы (1 фут = 304.8 мм)
                    double offsetInFeet = 100 / 304.8; // ≈ 0.656168 фута

                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                    sectionBox.Min = new XYZ(
                        -wallLength / 2 - offsetInFeet,  // слева отступ
                        -offsetInFeet,                    // снизу отступ
                        -offsetInFeet                      // сзади отступ
                    );
                    sectionBox.Max = new XYZ(
                        wallLength / 2 + offsetInFeet,    // справа отступ
                        wallHeight + offsetInFeet,         // сверху отступ
                        wallThickness + offsetInFeet        // спереди отступ
                    );

                    Transform tform = Transform.Identity;
                    tform.Origin = mid;
                    tform.BasisX = right;     // X вдоль стены
                    tform.BasisY = up;        // Y вертикаль
                    tform.BasisZ = viewDir;   // Z смотрим на стену
                    sectionBox.Transform = tform;

                    // Тип семейства разреза
                    ViewFamilyType vft = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(x => x.ViewFamily == ViewFamily.Section);

                    if (vft == null) { t.RollBack(); return null; }

                    ViewSection section = ViewSection.CreateSection(_doc, vft.Id, sectionBox);
                    if (section == null) { t.RollBack(); return null; }

                    section.Name = $"Фасад_{groupingValue}_{DateTime.Now:yyyyMMdd_HHmm}";

                    // Масштаб 1:100
                    Parameter scaleParam = section.get_Parameter(BuiltInParameter.VIEW_SCALE);
                    if (scaleParam != null && !scaleParam.IsReadOnly) scaleParam.Set(100);

                    t.Commit();
                    return section;
                }
                catch
                {
                    t.RollBack();
                    return null;
                }
            }
        }

        #region Методы выбора листа и размещения
        private void ShowSheetSelectionDialog(ViewSection section, string groupingValue)
        {
            Window wnd = new Window
            {
                Title = $"Выбор листа для разреза {groupingValue}",
                Width = 500,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(15) };

            TextBlock infoText = new TextBlock
            {
                Text = $"Создан разрез: {section.Name}\n\nХотите разместить его на листе?",
                Margin = new Thickness(0, 0, 0, 15),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            };
            panel.Children.Add(infoText);

            Label sheetsLabel = new Label
            {
                Content = "Доступные листы:",
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(0, 0, 0, 5)
            };
            panel.Children.Add(sheetsLabel);

            ListBox sheetsList = new ListBox
            {
                Height = 200,
                DisplayMemberPath = "DisplayName",
                Margin = new Thickness(0, 0, 0, 10)
            };

            var sheets = GetSheets();
            sheetsList.ItemsSource = sheets;
            if (sheets.Any()) sheetsList.SelectedIndex = 0;

            panel.Children.Add(sheetsList);

            CheckBox openSheetCheckBox = new CheckBox
            {
                Content = "Открыть лист после размещения",
                IsChecked = true,
                Margin = new Thickness(0, 5, 0, 10)
            };
            panel.Children.Add(openSheetCheckBox);

            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button placeBtn = new Button
            {
                Content = "✅ Разместить",
                Width = 120,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };

            Button laterBtn = new Button
            {
                Content = "⏰ Позже",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0)
            };

            Button cancelBtn = new Button
            {
                Content = "✖ Отмена",
                Width = 100,
                Height = 35,
                IsCancel = true
            };

            placeBtn.Click += (s, e) =>
            {
                if (sheetsList.SelectedItem is SheetInfo selectedSheet)
                {
                    bool openSheet = openSheetCheckBox.IsChecked ?? true;
                    PlaceSectionOnSheet(section, selectedSheet.Sheet, true, openSheet);
                    wnd.Close();
                }
                else
                {
                    MessageBox.Show("Выберите лист для размещения", "Предупреждение",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            laterBtn.Click += (s, e) => wnd.Close();
            cancelBtn.Click += (s, e) => wnd.Close();

            buttonPanel.Children.Add(placeBtn);
            buttonPanel.Children.Add(laterBtn);
            buttonPanel.Children.Add(cancelBtn);
            panel.Children.Add(buttonPanel);

            wnd.Content = panel;
            wnd.ShowDialog();
        }

        private List<SheetInfo> GetSheets()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Select(s => new SheetInfo { Sheet = s, DisplayName = $"{s.SheetNumber} - {s.Name}" })
                .OrderBy(s => s.Sheet.SheetNumber)
                .ToList();
        }

        private void PlaceSectionOnSheet(ViewSection section, ViewSheet sheet, bool createViewport, bool openSheetAfter)
        {
            try
            {
                using (Transaction t = new Transaction(_doc, "Размещение разреза"))
                {
                    t.Start();
                    if (createViewport)
                    {
                        XYZ center = new XYZ(0.5, 0.5, 0);
                        Viewport.Create(_doc, sheet.Id, section.Id, center);
                    }
                    t.Commit();
                }

                if (openSheetAfter)
                    _uidoc.ActiveView = sheet;

                TaskDialog.Show("Успешно", $"✅ Разрез размещён на листе {sheet.SheetNumber} - {sheet.Name}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"❌ Ошибка при размещении: {ex.Message}");
            }
        }
        #endregion
    }

    public class SheetInfo
    {
        public ViewSheet Sheet { get; set; }
        public string DisplayName { get; set; }
    }
}