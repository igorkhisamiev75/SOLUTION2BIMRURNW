using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace RevitAddIn2BIMRU.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class EnhancedApartmentProcessor : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            try
            {
                // Создаем диалог для выбора файлов
                var dialog = new System.Windows.Forms.OpenFileDialog();
                dialog.Filter = "Revit Files (*.rvt)|*.rvt";
                dialog.Multiselect = true;
                dialog.Title = "Выберите файлы для обработки";

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }

                // Шаг 1: Сбор всех данных из всех файлов
                var allData = new List<ApartmentData>();
                var debugInfo = new StringBuilder();
                debugInfo.AppendLine("=== ОТЛАДОЧНАЯ ИНФОРМАЦИЯ ===");

                foreach (string filePath in dialog.FileNames)
                {
                    debugInfo.AppendLine($"\nФайл: {Path.GetFileName(filePath)}");

                    var fileData = CollectDataFromFile(uiApp.Application, filePath, debugInfo);
                    allData.AddRange(fileData);

                    debugInfo.AppendLine($"  Найдено квартир: {fileData.Count}");
                }

                if (allData.Count == 0)
                {
                    TaskDialog.Show("Информация", "Не найдено квартир в выбранных файлах.");
                    return Result.Cancelled;
                }

                debugInfo.AppendLine($"\nВсего квартир собрано: {allData.Count}");

                // Шаг 2: Группировка и сортировка ВСЕХ данных
                debugInfo.AppendLine("\n=== ГРУППИРОВКА ДАННЫХ ===");

                // Группируем по ADSK_Тип квартиры и МГН2
                var groupedData = GroupAndSortApartments(allData, debugInfo);

                // Шаг 3: Запись данных обратно в файлы
                debugInfo.AppendLine("\n=== ЗАПИСЬ ДАННЫХ ===");
                int totalProcessed = 0;

                foreach (string filePath in dialog.FileNames)
                {
                    var fileData = allData.Where(d => d.FilePath == filePath).ToList();

                    if (fileData.Count == 0) continue;

                    debugInfo.AppendLine($"\nЗапись в файл: {Path.GetFileName(filePath)}");
                    debugInfo.AppendLine($"  Квартир для записи: {fileData.Count}");

                    int processed = WriteDataToFile(uiApp.Application, filePath, fileData, debugInfo);
                    totalProcessed += processed;

                    debugInfo.AppendLine($"  Успешно записано: {processed}");
                }

                // Шаг 4: Отчет
                StringBuilder report = new StringBuilder();
                report.AppendLine("=== РЕЗУЛЬТАТЫ ОБРАБОТКИ ===");
                report.AppendLine($"Файлов обработано: {dialog.FileNames.Length}");
                report.AppendLine($"Всего квартир найдено: {allData.Count}");
                report.AppendLine($"Успешно обработано: {totalProcessed}");
                report.AppendLine();

                // Группы для отчета
                var reportGroups = allData
                    .GroupBy(a => new { Type = a.ApartmentType ?? "Без типа", MGN2 = a.MGN2 ?? "Без МГН2" })
                    .OrderBy(g => g.Key.Type)
                    .ThenBy(g => g.Key.MGN2);

                report.AppendLine("РАСПРЕДЕЛЕНИЕ ПО ГРУППАМ:");
                foreach (var group in reportGroups)
                {
                    report.AppendLine($"\nГруппа: Тип '{group.Key.Type}', МГН2 '{group.Key.MGN2}'");
                    report.AppendLine($"  Количество квартир: {group.Count()}");

                    // Сортируем по номеру для отчета
                    foreach (var apt in group.OrderBy(a => a.OrdinalNumber))
                    {
                        report.AppendLine($"  №{apt.OrdinalNumber}: Площадь {apt.Area:F2} м² ({Path.GetFileName(apt.FilePath)})");
                    }
                }

                // Показываем отчет
                TaskDialog.Show("Результаты обработки", report.ToString());

                // Сохраняем отладочную информацию
                SaveDebugInfo(debugInfo.ToString());

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Критическая ошибка:\n{ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        private List<ApartmentData> CollectDataFromFile(Application app, string filePath, StringBuilder debugInfo)
        {
            var data = new List<ApartmentData>();

            try
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                using (Document doc = app.OpenDocumentFile(modelPath, new OpenOptions()))
                {
                    if (doc == null) return data;

                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .ToList();

                    debugInfo.AppendLine($"  Всего помещений в файле: {rooms.Count}");

                    foreach (var room in rooms)
                    {
                        // Проверяем назначение
                        var purposeParam = room.LookupParameter("Назначение");
                        string purpose = purposeParam?.AsString();

                        if (string.IsNullOrEmpty(purpose) || purpose != "Квартиры")
                            continue;

                        // Получаем параметры квартиры
                        var aptTypeParam = room.LookupParameter("ADSK_Тип квартиры");
                        var mgn2Param = room.LookupParameter("МГН2");
                        var areaParam = room.LookupParameter("ADSK_Площадь квартиры");

                        string aptType = GetParamValue(aptTypeParam);
                        string mgn2 = GetParamValue(mgn2Param);
                        double area = GetAreaValue(areaParam);

                        if (!string.IsNullOrEmpty(aptType) && !string.IsNullOrEmpty(mgn2) && area > 0)
                        {
                            data.Add(new ApartmentData
                            {
                                FilePath = filePath,
                                RoomId = room.Id.IntegerValue,
                                ApartmentType = aptType,
                                MGN2 = mgn2,
                                Area = area,
                                RoomNumber = room.Number
                            });
                        }
                        else
                        {
                            debugInfo.AppendLine($"    Пропущено помещение {room.Number}: тип='{aptType}', МГН2='{mgn2}', площадь={area}");
                        }
                    }

                    doc.Close(false);
                }
            }
            catch (Exception ex)
            {
                debugInfo.AppendLine($"  Ошибка при чтении файла: {ex.Message}");
            }

            return data;
        }

        private Dictionary<string, List<ApartmentData>> GroupAndSortApartments(List<ApartmentData> allData, StringBuilder debugInfo)
        {
            var groupedData = new Dictionary<string, List<ApartmentData>>();

            // Группируем по ключу "Тип|МГН2"
            foreach (var apt in allData)
            {
                string key = $"{apt.ApartmentType}|{apt.MGN2}";

                if (!groupedData.ContainsKey(key))
                {
                    groupedData[key] = new List<ApartmentData>();
                }

                groupedData[key].Add(apt);
            }

            debugInfo.AppendLine($"Создано групп: {groupedData.Count}");

            // Для каждой группы сортируем по площади и назначаем номера
            foreach (var key in groupedData.Keys.ToList())
            {
                var group = groupedData[key];

                // Сортируем по площади от меньшего к большему
                var sortedGroup = group.OrderBy(a => a.Area).ToList();

                debugInfo.AppendLine($"\nГруппа: {key}");
                debugInfo.AppendLine($"  Количество квартир: {sortedGroup.Count}");
                debugInfo.AppendLine($"  Площади: {string.Join(", ", sortedGroup.Select(a => $"{a.Area:F2}"))}");

                // Назначаем порядковые номера с учетом одинаковых площадей
                var areaNumberMap = new Dictionary<double, int>();
                int currentNumber = 1;

                foreach (var apt in sortedGroup)
                {
                    double roundedArea = Math.Round(apt.Area, 2);

                    if (!areaNumberMap.ContainsKey(roundedArea))
                    {
                        areaNumberMap[roundedArea] = currentNumber;
                        currentNumber++;
                    }

                    apt.OrdinalNumber = areaNumberMap[roundedArea];
                    debugInfo.AppendLine($"    Площадь {apt.Area:F2} → Номер {apt.OrdinalNumber}");
                }

                groupedData[key] = sortedGroup;
            }

            return groupedData;
        }

        private int WriteDataToFile(Application app, string filePath, List<ApartmentData> fileData, StringBuilder debugInfo)
        {
            int processed = 0;

            try
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                using (Document doc = app.OpenDocumentFile(modelPath, new OpenOptions()))
                {
                    if (doc == null) return 0;

                    using (Transaction trans = new Transaction(doc, "Нумерация квартир"))
                    {
                        trans.Start();

                        foreach (var apt in fileData)
                        {
                            try
                            {
                                ElementId roomId = new ElementId(apt.RoomId);
                                Room room = doc.GetElement(roomId) as Room;

                                if (room == null)
                                {
                                    debugInfo.AppendLine($"    Комната {apt.RoomId} не найдена");
                                    continue;
                                }

                                // Ищем параметр для записи
                                Parameter ordinalParam = room.LookupParameter("ПорядковыйНомерДляТипа");

                                if (ordinalParam == null)
                                {
                                    ordinalParam = room.LookupParameter("ADSK_Номер квартиры");
                                }

                                if (ordinalParam == null)
                                {
                                    ordinalParam = room.LookupParameter("ADSK_Номер квартиры по типу");
                                }

                                if (ordinalParam != null && !ordinalParam.IsReadOnly)
                                {
                                    // Записываем значение
                                    if (ordinalParam.StorageType == StorageType.Double)
                                        ordinalParam.Set((double)apt.OrdinalNumber);
                                    else if (ordinalParam.StorageType == StorageType.Integer)
                                        ordinalParam.Set(apt.OrdinalNumber);
                                    else if (ordinalParam.StorageType == StorageType.String)
                                        ordinalParam.Set(apt.OrdinalNumber.ToString());

                                    processed++;
                                    debugInfo.AppendLine($"    Комната {room.Number}: установлен номер {apt.OrdinalNumber}");
                                }
                                else
                                {
                                    debugInfo.AppendLine($"    Комната {room.Number}: параметр не найден или только для чтения");
                                }
                            }
                            catch (Exception ex)
                            {
                                debugInfo.AppendLine($"    Ошибка при записи комнаты {apt.RoomId}: {ex.Message}");
                            }
                        }

                        trans.Commit();
                    }

                    // Сохраняем изменения
                    if (processed > 0)
                    {
                        var saveOptions = new SaveOptions();
                        doc.Save(saveOptions);
                    }

                    doc.Close(false);
                }
            }
            catch (Exception ex)
            {
                debugInfo.AppendLine($"  Ошибка при записи в файл: {ex.Message}");
                throw;
            }

            return processed;
        }

        private string GetParamValue(Parameter param)
        {
            if (param == null || !param.HasValue) return string.Empty;

            if (param.StorageType == StorageType.String)
                return param.AsString();
            else if (param.StorageType == StorageType.Integer)
                return param.AsInteger().ToString();
            else if (param.StorageType == StorageType.Double)
                return param.AsValueString() ?? param.AsDouble().ToString();

            return string.Empty;
        }

        private double GetAreaValue(Parameter param)
        {
            if (param == null || !param.HasValue) return 0;

            if (param.StorageType == StorageType.Double)
            {
                return UnitUtils.ConvertFromInternalUnits(param.AsDouble(), UnitTypeId.SquareMeters);
            }

            return 0;
        }

        private void SaveDebugInfo(string debugInfo)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string logPath = Path.Combine(desktop, $"ApartmentDebug_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(logPath, debugInfo, Encoding.UTF8);
            }
            catch { }
        }

        private class ApartmentData
        {
            public string FilePath { get; set; }
            public int RoomId { get; set; }
            public string RoomNumber { get; set; }
            public string ApartmentType { get; set; }
            public string MGN2 { get; set; }
            public double Area { get; set; }
            public int OrdinalNumber { get; set; }
        }
    }
}