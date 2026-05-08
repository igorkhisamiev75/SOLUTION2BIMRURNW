using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RevitAddIn2BIMRU.Commands.SP
{
    [Transaction(TransactionMode.Manual)]
    public class PanelInfoToWallParameter : IExternalCommand
    {
        private Document _doc;
        private UIDocument _uidoc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;

            try
            {
                if (_uidoc.Selection.GetElementIds().Count != 1)
                {
                    TaskDialog.Show("Ошибка", "Выберите одну витражную стену");
                    return Result.Cancelled;
                }

                Wall wall = _doc.GetElement(_uidoc.Selection.GetElementIds().First()) as Wall;

                if (wall == null || wall.CurtainGrid == null)
                {
                    TaskDialog.Show("Ошибка", "Выбранный элемент не является витражной стеной");
                    return Result.Failed;
                }

                Element panel = GetFirstPanelFromWall(wall);

                if (panel == null)
                {
                    TaskDialog.Show("Ошибка", "В выбранной витражной стене нет панелей");
                    return Result.Failed;
                }

                // Получаем значение ADSK_Группирование
                string groupingValue = wall.LookupParameter("ADSK_Группирование")?.AsString();
                if (string.IsNullOrEmpty(groupingValue))
                {
                    TaskDialog.Show("Ошибка", "Параметр ADSK_Группирование стены не заполнен");
                    return Result.Failed;
                }

                // Собираем строки для каждого параметра
                var ppLines = new List<string>();
                var nameLines = new List<string>();
                var colLines = new List<string>();
                var unitLines = new List<string>();

                int counter = 1;

                // Первая строка: 2BIM_ПВ_1
                AddLine(panel, counter, "2BIM_ПВ_1", "2BIM_ПВ_Расчет1", "м³", ppLines, nameLines, colLines, unitLines, true);
                counter++;

                // Остальные строки: 3–13, без 2
                for (int i = 3; i <= 13; i++)
                {
                    AddLine(panel, counter, $"2BIM_ПВ_{i}", $"2BIM_ПВ_Расчет{i}", "шт.", ppLines, nameLines, colLines, unitLines, false);
                    counter++;
                }

                // Записываем в параметры стены
                using (Transaction t = new Transaction(_doc, "Заполнение многострочных параметров"))
                {
                    t.Start();
                    SetParameter(wall, "2BIM_ПВ_МногострочныйПП", ppLines);
                    SetParameter(wall, "2BIM_ПВ_Многострочный", nameLines);
                    SetParameter(wall, "2BIM_ПВ_МногострочныйКол", colLines);
                    SetParameter(wall, "2BIM_ПВ_МногострочныйЕдИзм", unitLines);
                    t.Commit();
                }

                // Создаем спецификацию
                ViewSchedule schedule = CreatePanelScheduleForWall(wall, groupingValue);

                if (schedule != null)
                {
                    // Диалог выбора листа
                    ShowSheetSelectionDialog(schedule, groupingValue);
                }

                TaskDialog.Show("Успешно", "Параметры стены успешно заполнены и спецификация создана");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", ex.Message);
                return Result.Failed;
            }
        }

        // ---------------------------------------------------------
        // Получение первой панели
        // ---------------------------------------------------------
        private Element GetFirstPanelFromWall(Wall wall)
        {
            foreach (ElementId id in wall.CurtainGrid.GetPanelIds())
                return _doc.GetElement(id);

            return null;
        }

        // ---------------------------------------------------------
        // Добавление строки в списки
        // ---------------------------------------------------------
        private void AddLine(Element panel, int number, string nameParam, string calcParam, string unit,
            List<string> ppLines, List<string> nameLines, List<string> colLines, List<string> unitLines, bool isVolume)
        {
            string name = GetParam(panel, nameParam);
            string value = GetParam(panel, calcParam);

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(value))
                return;

            double numberValue = ParseDouble(value);
            if (numberValue < 0) numberValue = 0;

            ppLines.Add(number.ToString());
            nameLines.Add(name ?? "");
            colLines.Add(isVolume ? numberValue.ToString("F2") : numberValue.ToString("F0"));
            unitLines.Add(unit);
        }

        // ---------------------------------------------------------
        // Работа с параметрами Revit
        // ---------------------------------------------------------
        private string GetParam(Element e, string name)
        {
            var p = e.LookupParameter(name);
            return p?.AsValueString() ?? p?.AsString() ?? "";
        }

        private double ParseDouble(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Replace(',', '.');
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double d))
            {
                return Math.Max(0, d);
            }
            return 0;
        }

        private void SetParameter(Wall wall, string paramName, List<string> lines)
        {
            var p = wall.LookupParameter(paramName);
            if (p == null)
                TaskDialog.Show("Ошибка", $"Параметр {paramName} не найден");
            else
                p.Set(string.Join(Environment.NewLine, lines));
        }

        // ---------------------------------------------------------
        // Методы для работы со спецификацией
        // ---------------------------------------------------------

        /// <summary>
        /// Создание спецификации с параметрами многострочной строки стены
        /// </summary>
        private ViewSchedule CreatePanelScheduleForWall(Wall wall, string groupingValue)
        {
            string scheduleName = GenerateScheduleNameForWall(groupingValue);
            ViewSchedule schedule = null;

            using (Transaction t = new Transaction(_doc, "Создание спецификации панелей витража"))
            {
                t.Start();

                // Удаляем существующую спецификацию с таким же именем
                DeleteExistingPanelSchedule(scheduleName);

                // Создаем новую спецификацию для стен (не для панелей!)
                schedule = ViewSchedule.CreateSchedule(_doc, new ElementId(BuiltInCategory.OST_Walls));
                schedule.Name = scheduleName;

                if (schedule != null)
                {
                    // Настраиваем поля, фильтры и сортировку
                    ConfigureWallSchedule(schedule, groupingValue);
                }

                t.Commit();
            }

            return schedule;
        }

        /// <summary>
        /// Генерация имени спецификации
        /// </summary>
        private string GenerateScheduleNameForWall(string groupingValue)
        {
            return $"2bim_ШАЛАБУХИ_{groupingValue}_{DateTime.Now:yyyyMMdd}";
        }

        /// <summary>
        /// Удаление существующей спецификации
        /// </summary>
        private void DeleteExistingPanelSchedule(string scheduleName)
        {
            ViewSchedule existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(vs => vs.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                _doc.Delete(existing.Id);
            }
        }

        /// <summary>
        /// Настройка спецификации стены с нужными полями
        /// </summary>
        private void ConfigureWallSchedule(ViewSchedule schedule, string groupingValue)
        {
            ScheduleDefinition def = schedule.Definition;

            // Очистка полей по умолчанию
            ClearAllScheduleFields(def);

            // Добавляем поля в правильном порядке:
            // 1. 2BIM_ПВ_МногострочныйПП -> П/п
            // 2. 2BIM_ПВ_Многострочный -> Наименование
            // 3. 2BIM_ПВ_МногострочныйКол -> Кол-во, шт.
            // 4. 2BIM_ПВ_МногострочныйЕдИзм -> Ед. измр.
            // 5. ADSK_Примечание -> Примечание
            // 6. ADSK_Группирование (скрытое поле для фильтра)

            ScheduleFieldId ppFieldId = AddScheduleField(def, "2BIM_ПВ_МногострочныйПП", "П/п");
            ScheduleFieldId nameFieldId = AddScheduleField(def, "2BIM_ПВ_Многострочный", "Наименование");
            ScheduleFieldId countFieldId = AddScheduleField(def, "2BIM_ПВ_МногострочныйКол", "Кол-во, шт.");
            ScheduleFieldId unitFieldId = AddScheduleField(def, "2BIM_ПВ_МногострочныйЕдИзм", "Ед. измр.");
            ScheduleFieldId commentFieldId = AddScheduleField(def, "ADSK_Примечание", "Примечание");

            // Добавляем поле ADSK_Группирование для фильтра (будет скрыто)
            ScheduleFieldId groupFieldId = AddScheduleField(def, "ADSK_Группирование", "Группирование");

            // Настраиваем выравнивание для полей
            SetScheduleFieldAlignment(def, ppFieldId, ScheduleHorizontalAlignment.Center);
            SetScheduleFieldAlignment(def, nameFieldId, ScheduleHorizontalAlignment.Left);
            SetScheduleFieldAlignment(def, countFieldId, ScheduleHorizontalAlignment.Center);
            SetScheduleFieldAlignment(def, unitFieldId, ScheduleHorizontalAlignment.Center);
            SetScheduleFieldAlignment(def, commentFieldId, ScheduleHorizontalAlignment.Left);

            // Добавляем фильтр по группировке
            AddGroupingFilterToSchedule(def, groupFieldId, groupingValue);

            // Скрываем поле ADSK_Группирование
            HideGroupingFieldInSchedule(def, groupFieldId);

            // Настраиваем сортировку по П/п
            SetupScheduleSorting(def, ppFieldId);

            // Отключаем "для каждого экземпляра"
            def.IsItemized = false;
        }

        /// <summary>
        /// Очистка всех полей спецификации
        /// </summary>
        private void ClearAllScheduleFields(ScheduleDefinition def)
        {
            while (def.GetFieldCount() > 0)
            {
                ScheduleFieldId fieldId = def.GetFieldId(0);
                def.RemoveField(fieldId);
            }
        }

        /// <summary>
        /// Добавление поля в спецификацию
        /// </summary>
        private ScheduleFieldId AddScheduleField(ScheduleDefinition def, string paramName, string columnHeading)
        {
            try
            {
                IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

                foreach (SchedulableField sf in schedulableFields)
                {
                    if (sf.GetName(_doc).Equals(paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        ScheduleField field = def.AddField(sf);
                        field.ColumnHeading = columnHeading;
                        return field.FieldId;
                    }
                }

                // Если параметр не найден, выводим предупреждение
                TaskDialog.Show("Предупреждение", $"Параметр '{paramName}' не найден в проекте");
            }
            catch (Exception ex)
            {
                // Логирование ошибки, но не прерываем выполнение
                TaskDialog.Show("Ошибка", $"Не удалось добавить поле '{paramName}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Настройка выравнивания для поля
        /// </summary>
        private void SetScheduleFieldAlignment(ScheduleDefinition def, ScheduleFieldId fieldId, ScheduleHorizontalAlignment alignment)
        {
            if (fieldId != null)
            {
                try
                {
                    // Получаем поле по индексу
                    for (int i = 0; i < def.GetFieldCount(); i++)
                    {
                        ScheduleFieldId currentFieldId = def.GetFieldId(i);
                        if (currentFieldId.IntegerValue == fieldId.IntegerValue)
                        {
                            ScheduleField field = def.GetField(currentFieldId);
                            field.HorizontalAlignment = alignment;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Игнорируем ошибки выравнивания
                }
            }
        }

        /// <summary>
        /// Добавление фильтра по группировке
        /// </summary>
        private void AddGroupingFilterToSchedule(ScheduleDefinition def, ScheduleFieldId groupFieldId, string groupingValue)
        {
            if (groupFieldId != null && !string.IsNullOrEmpty(groupingValue))
            {
                ScheduleFilter filter = new ScheduleFilter(groupFieldId, ScheduleFilterType.Equal, groupingValue);
                def.AddFilter(filter);
            }
        }

        /// <summary>
        /// Скрытие поля ADSK_Группирование
        /// </summary>
        private void HideGroupingFieldInSchedule(ScheduleDefinition def, ScheduleFieldId groupFieldId)
        {
            if (groupFieldId != null)
            {
                try
                {
                    for (int i = 0; i < def.GetFieldCount(); i++)
                    {
                        ScheduleFieldId currentFieldId = def.GetFieldId(i);
                        if (currentFieldId.IntegerValue == groupFieldId.IntegerValue)
                        {
                            ScheduleField field = def.GetField(currentFieldId);
                            field.IsHidden = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Игнорируем ошибки скрытия поля
                }
            }
        }

        /// <summary>
        /// Настройка сортировки по П/п
        /// </summary>
        private void SetupScheduleSorting(ScheduleDefinition def, ScheduleFieldId ppFieldId)
        {
            // Очищаем существующие поля сортировки
            IList<ScheduleSortGroupField> sortFields = def.GetSortGroupFields();
            int fieldCount = sortFields.Count;

            for (int i = 0; i < fieldCount; i++)
            {
                def.RemoveSortGroupField(0);
            }

            // Добавляем сортировку по П/п
            if (ppFieldId != null)
            {
                ScheduleSortGroupField sortField = new ScheduleSortGroupField(ppFieldId)
                {
                    ShowHeader = false,    // Галочка "Заголовок" убрана
                    ShowFooter = false,    // Галочка "Итоги" убрана
                    ShowBlankLine = false  // Галочка "Отделять данные пустой строчкой" убрана
                };

                def.AddSortGroupField(sortField);
            }
        }

        /// <summary>
        /// Диалог выбора листа для размещения спецификации
        /// </summary>
        private void ShowSheetSelectionDialog(ViewSchedule schedule, string groupingValue)
        {
            Window wnd = new Window
            {
                Title = $"Выбор листа для спецификации {groupingValue}",
                Width = 500,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(15) };

            TextBlock infoText = new TextBlock
            {
                Text = $"Создана спецификация: {schedule.Name}\n\nХотите разместить её на листе?",
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
                DisplayMemberPath = "SheetDisplayName",
                Margin = new Thickness(0, 0, 0, 10)
            };

            var scheduleSheets = GetAvailableSheetsForSchedule();
            sheetsList.ItemsSource = scheduleSheets;

            if (scheduleSheets.Any())
                sheetsList.SelectedIndex = 0;

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
                if (sheetsList.SelectedItem is SheetItemForSchedule selectedSheet)
                {
                    bool openSheet = openSheetCheckBox.IsChecked ?? true;
                    PlaceScheduleOnSelectedSheet(schedule, selectedSheet.Sheet, openSheet);
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

        /// <summary>
        /// Получение списка листов для размещения
        /// </summary>
        private List<SheetItemForSchedule> GetAvailableSheetsForSchedule()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Select(s => new SheetItemForSchedule
                {
                    Sheet = s,
                    SheetDisplayName = $"{s.SheetNumber} - {s.Name}"
                })
                .OrderBy(s => s.Sheet.SheetNumber)
                .ToList();
        }

        /// <summary>
        /// Размещение спецификации на листе
        /// </summary>
        private void PlaceScheduleOnSelectedSheet(ViewSchedule schedule, ViewSheet sheet, bool openSheetAfter)
        {
            try
            {
                using (Transaction t = new Transaction(_doc, "Размещение спецификации"))
                {
                    t.Start();

                    // Создаем экземпляр спецификации на листе
                    // Используем стандартную позицию (0,0) - левый нижний угол
                    XYZ location = new XYZ(0, 0, 0);
                    ScheduleSheetInstance.Create(_doc, sheet.Id, schedule.Id, location);

                    t.Commit();
                }

                if (openSheetAfter)
                    _uidoc.ActiveView = sheet;

                TaskDialog.Show("Успешно", $"✅ Спецификация размещена на листе {sheet.SheetNumber} - {sheet.Name}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"❌ Ошибка при размещении: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Класс для хранения информации о листе в диалоге выбора для спецификации
    /// </summary>
    public class SheetItemForSchedule
    {
        public ViewSheet Sheet { get; set; }
        public string SheetDisplayName { get; set; }
    }
}