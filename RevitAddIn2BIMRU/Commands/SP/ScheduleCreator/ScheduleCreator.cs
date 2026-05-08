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

namespace RevitAddIn2BIMRU.Commands.SP
{
    [Transaction(TransactionMode.Manual)]
    public class ScheduleCreator : IExternalCommand
    {
        private Document _doc;
        private UIDocument _uidoc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;

            // 1. Получаем выбранную стену
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

            string groupingValue = wall.LookupParameter("ADSK_Группирование")?.AsString();
            if (string.IsNullOrEmpty(groupingValue))
            {
                TaskDialog.Show("Ошибка", "Параметр ADSK_Группирование стены не заполнен");
                return Result.Failed;
            }

            // 2. Создаем спецификацию
            ViewSchedule schedule = CreateCurtainPanelSchedule(groupingValue);
            if (schedule == null)
            {
                TaskDialog.Show("Ошибка", "Не удалось создать спецификацию.");
                return Result.Failed;
            }

            // 3. Диалог выбора листа и размещения
            ShowScheduleSheetDialog(schedule, groupingValue);

            return Result.Succeeded;
        }

        /// <summary>
        /// Создание спецификации панелей витража
        /// </summary>
        public ViewSchedule CreateCurtainPanelSchedule(string groupingValue)
        {
            string scheduleName = GenerateScheduleName(groupingValue);
            ViewSchedule schedule = null;

            using (Transaction t = new Transaction(_doc, "Создание спецификации панелей витража"))
            {
                t.Start();

                // Удаляем существующую спецификацию с таким же именем
                DeleteExistingSchedule(scheduleName);

                // Создаем новую спецификацию
                schedule = CreateNewSchedule(scheduleName);

                if (schedule != null)
                {
                    // Настраиваем поля, фильтры и сортировку
                    ConfigureSchedule(schedule, groupingValue);
                }

                t.Commit();
            }

            return schedule;
        }

        /// <summary>
        /// Генерация имени спецификации
        /// </summary>
        private string GenerateScheduleName(string groupingValue)
        {
            return $"2bim_{groupingValue}_{DateTime.Now:yyyyMMdd}";
        }

        /// <summary>
        /// Удаление существующей спецификации
        /// </summary>
        private void DeleteExistingSchedule(string scheduleName)
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
        /// Создание новой спецификации
        /// </summary>
        private ViewSchedule CreateNewSchedule(string scheduleName)
        {
            ViewSchedule schedule = ViewSchedule.CreateSchedule(_doc, new ElementId(BuiltInCategory.OST_CurtainWallPanels));
            schedule.Name = scheduleName;
            return schedule;
        }

        /// <summary>
        /// Настройка спецификации (поля, фильтры, сортировка)
        /// </summary>
        private void ConfigureSchedule(ViewSchedule schedule, string groupingValue)
        {
            ScheduleDefinition def = schedule.Definition;

            // Очистка полей по умолчанию
            ClearAllFields(def);

            // Добавляем поля в правильном порядке
            ScheduleFieldId groupFieldId = AddField(def, "ADSK_Группирование", "Номер фасада");
            ScheduleFieldId typeMarkFieldId = AddField(def, "Маркировка типоразмера", "Марка");
            ScheduleFieldId positionFieldId = AddField(def, "ADSK_Позиция", "Позиция");
            ScheduleFieldId designationFieldId = AddField(def, "2BIM_ПВ_Обозначение", "Обозначение");
            ScheduleFieldId widthFieldId = AddField(def, "2BIM_ПВ_Ширина", "Длина, рабочая ширина панели, мм");

            // Добавляем поле Count (Количество)
            ScheduleField countField = AddCountField(def, "Кол-во, шт.");

            // ЗАМЕНА: ADSK_Масса на 2BIM_ПВ_Масса
            ScheduleFieldId massFieldId = AddField(def, "2BIM_ПВ_Масса", "Масса, кг");

            ScheduleFieldId commentFieldId = AddField(def, "Примечание", "Примечание");

            // Настраиваем выравнивание для полей
            SetFieldAlignment(def, designationFieldId, ScheduleHorizontalAlignment.Center);
            SetFieldAlignment(def, widthFieldId, ScheduleHorizontalAlignment.Center);
            if (countField != null)
            {
                SetFieldAlignment(def, countField.FieldId, ScheduleHorizontalAlignment.Center);
            }
            SetFieldAlignment(def, massFieldId, ScheduleHorizontalAlignment.Right);

            // Добавляем фильтр по группировке
            AddGroupingFilter(def, groupFieldId, groupingValue);

            // Настраиваем сортировку (БЕЗ ЗАГОЛОВКОВ И БЕЗ ПУСТЫХ СТРОК)
            SetupSorting(def, designationFieldId, widthFieldId);

            // Отключаем "для каждого экземпляра"
            def.IsItemized = false;
        }

        /// <summary>
        /// Очистка всех полей спецификации
        /// </summary>
        private void ClearAllFields(ScheduleDefinition def)
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
        private ScheduleFieldId AddField(ScheduleDefinition def, string paramName, string columnHeading)
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
        /// Добавление поля Count (Количество)
        /// </summary>
        private ScheduleField AddCountField(ScheduleDefinition def, string columnHeading)
        {
            try
            {
                ScheduleField field = def.AddField(ScheduleFieldType.Count);
                field.ColumnHeading = columnHeading;
                return field;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Не удалось добавить поле количества: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Настройка выравнивания для поля
        /// </summary>
        private void SetFieldAlignment(ScheduleDefinition def, ScheduleFieldId fieldId, ScheduleHorizontalAlignment alignment)
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
        private void AddGroupingFilter(ScheduleDefinition def, ScheduleFieldId groupFieldId, string groupingValue)
        {
            if (groupFieldId != null && !string.IsNullOrEmpty(groupingValue))
            {
                ScheduleFilter filter = new ScheduleFilter(groupFieldId, ScheduleFilterType.Equal, groupingValue);
                def.AddFilter(filter);
            }
        }

        /// <summary>
        /// Настройка сортировки (БЕЗ ЗАГОЛОВКОВ И БЕЗ ПУСТЫХ СТРОК)
        /// </summary>
        private void SetupSorting(ScheduleDefinition def, ScheduleFieldId designationFieldId, ScheduleFieldId widthFieldId)
        {
            // Очищаем существующие поля сортировки
            IList<ScheduleSortGroupField> sortFields = def.GetSortGroupFields();
            int fieldCount = sortFields.Count;
            for (int i = 0; i < fieldCount; i++)
            {
                def.RemoveSortGroupField(0);
            }

            // Добавляем сортировку по обозначению
            if (designationFieldId != null)
            {
                ScheduleSortGroupField обозначениеGroup = new ScheduleSortGroupField(designationFieldId)
                {
                    ShowHeader = false,           // Галочка "Заголовок" убрана
                    ShowFooter = false,            // Галочка "Итоги" убрана
                    ShowBlankLine = false          // Галочка "Отделять данные пустой строчкой" убрана
                };
                def.AddSortGroupField(обозначениеGroup);
            }

            // Добавляем сортировку по ширине
            if (widthFieldId != null)
            {
                ScheduleSortGroupField ширинаGroup = new ScheduleSortGroupField(widthFieldId)
                {
                    ShowHeader = false,           // Галочка "Заголовок" убрана
                    ShowFooter = false,            // Галочка "Итоги" убрана
                    ShowBlankLine = false          // Галочка "Отделять данные пустой строчкой" убрана
                };
                def.AddSortGroupField(ширинаGroup);
            }
        }

        #region Методы выбора листа и размещения (переименованы)

        private void ShowScheduleSheetDialog(ViewSchedule schedule, string groupingValue)
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
                DisplayMemberPath = "DisplayName",
                Margin = new Thickness(0, 0, 0, 10)
            };

            var scheduleSheets = GetScheduleSheets();
            sheetsList.ItemsSource = scheduleSheets;
            if (scheduleSheets.Any()) sheetsList.SelectedIndex = 0;

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
                if (sheetsList.SelectedItem is ScheduleSheetItem selectedSheet)
                {
                    bool openSheet = openSheetCheckBox.IsChecked ?? true;
                    PlaceScheduleOnSheet(schedule, selectedSheet.Sheet, openSheet);
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

        private List<ScheduleSheetItem> GetScheduleSheets()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Select(s => new ScheduleSheetItem { Sheet = s, DisplayName = $"{s.SheetNumber} - {s.Name}" })
                .OrderBy(s => s.Sheet.SheetNumber)
                .ToList();
        }

        private void PlaceScheduleOnSheet(ViewSchedule schedule, ViewSheet sheet, bool openSheetAfter)
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

        #endregion

        #region Дополнительные методы создания спецификаций (опционально)

        /// <summary>
        /// Создание спецификации с возможностью кастомной настройки
        /// </summary>
        public ViewSchedule CreateCustomSchedule(string groupingValue, Action<ScheduleDefinition> additionalSetup = null)
        {
            string scheduleName = GenerateScheduleName(groupingValue);
            ViewSchedule schedule = null;

            using (Transaction t = new Transaction(_doc, "Создание кастомной спецификации"))
            {
                t.Start();

                DeleteExistingSchedule(scheduleName);
                schedule = CreateNewSchedule(scheduleName);

                if (schedule != null)
                {
                    ScheduleDefinition def = schedule.Definition;
                    ClearAllFields(def);

                    // Базовая настройка с правильным порядком полей
                    ScheduleFieldId groupFieldId = AddField(def, "ADSK_Группирование", "Номер фасада");
                    ScheduleFieldId typeMarkFieldId = AddField(def, "Маркировка типоразмера", "Марка");
                    ScheduleFieldId positionFieldId = AddField(def, "ADSK_Позиция", "Позиция");
                    ScheduleFieldId designationFieldId = AddField(def, "2BIM_ПВ_Обозначение", "Обозначение");
                    ScheduleFieldId widthFieldId = AddField(def, "2BIM_ПВ_Ширина", "Длина, рабочая ширина панели, мм");
                    ScheduleField countField = AddCountField(def, "Кол-во, шт.");

                    // ЗАМЕНА: ADSK_Масса на 2BIM_ПВ_Масса
                    ScheduleFieldId massFieldId = AddField(def, "2BIM_ПВ_Масса", "Масса, кг");

                    ScheduleFieldId commentFieldId = AddField(def, "Примечание", "Примечание");

                    // Настраиваем выравнивание
                    SetFieldAlignment(def, designationFieldId, ScheduleHorizontalAlignment.Center);
                    SetFieldAlignment(def, widthFieldId, ScheduleHorizontalAlignment.Center);
                    if (countField != null)
                    {
                        SetFieldAlignment(def, countField.FieldId, ScheduleHorizontalAlignment.Center);
                    }
                    SetFieldAlignment(def, massFieldId, ScheduleHorizontalAlignment.Right);

                    // Фильтр
                    AddGroupingFilter(def, groupFieldId, groupingValue);

                    // Настраиваем сортировку
                    SetupSorting(def, designationFieldId, widthFieldId);

                    def.IsItemized = false;

                    // Дополнительная кастомная настройка
                    additionalSetup?.Invoke(def);
                }

                t.Commit();
            }

            return schedule;
        }

        /// <summary>
        /// Создание спецификации с настраиваемыми заголовками сортировки
        /// </summary>
        public ViewSchedule CreateScheduleWithSorting(string groupingValue, bool showHeaders = false)
        {
            string scheduleName = GenerateScheduleName(groupingValue);
            ViewSchedule schedule = null;

            using (Transaction t = new Transaction(_doc, "Создание спецификации с настройкой сортировки"))
            {
                t.Start();

                DeleteExistingSchedule(scheduleName);
                schedule = CreateNewSchedule(scheduleName);

                if (schedule != null)
                {
                    ScheduleDefinition def = schedule.Definition;
                    ClearAllFields(def);

                    // Добавляем поля в правильном порядке
                    ScheduleFieldId groupFieldId = AddField(def, "ADSK_Группирование", "Номер фасада");
                    ScheduleFieldId typeMarkFieldId = AddField(def, "Маркировка типоразмера", "Марка");
                    ScheduleFieldId positionFieldId = AddField(def, "ADSK_Позиция", "Позиция");
                    ScheduleFieldId designationFieldId = AddField(def, "2BIM_ПВ_Обозначение", "Обозначение");
                    ScheduleFieldId widthFieldId = AddField(def, "2BIM_ПВ_Ширина", "Длина, рабочая ширина панели, мм");
                    ScheduleField countField = AddCountField(def, "Кол-во, шт.");

                    // ЗАМЕНА: ADSK_Масса на 2BIM_ПВ_Масса
                    ScheduleFieldId massFieldId = AddField(def, "2BIM_ПВ_Масса", "Масса, кг");

                    ScheduleFieldId commentFieldId = AddField(def, "Примечание", "Примечание");

                    // Настраиваем выравнивание
                    SetFieldAlignment(def, designationFieldId, ScheduleHorizontalAlignment.Center);
                    SetFieldAlignment(def, widthFieldId, ScheduleHorizontalAlignment.Center);
                    if (countField != null)
                    {
                        SetFieldAlignment(def, countField.FieldId, ScheduleHorizontalAlignment.Center);
                    }
                    SetFieldAlignment(def, massFieldId, ScheduleHorizontalAlignment.Right);

                    // Фильтр
                    if (groupFieldId != null && !string.IsNullOrEmpty(groupingValue))
                    {
                        def.AddFilter(new ScheduleFilter(groupFieldId, ScheduleFilterType.Equal, groupingValue));
                    }

                    // Сортировка с настраиваемыми заголовками
                    SetupSorting(def, designationFieldId, widthFieldId);

                    def.IsItemized = false;
                }

                t.Commit();
            }

            return schedule;
        }

        #endregion
    }

    /// <summary>
    /// Класс для хранения информации о листе в диалоге выбора для спецификации
    /// </summary>
    public class ScheduleSheetItem
    {
        public ViewSheet Sheet { get; set; }
        public string DisplayName { get; set; }
    }
}