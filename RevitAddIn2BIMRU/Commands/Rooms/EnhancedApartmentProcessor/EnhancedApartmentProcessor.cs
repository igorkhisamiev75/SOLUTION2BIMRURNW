using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;

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
            Autodesk.Revit.ApplicationServices.Application revitApp = uiApp.Application;

            try
            {
                // Диалог выбора файлов
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.Filter = "Revit Files (*.rvt)|*.rvt";
                dialog.Multiselect = true;
                dialog.Title = "Выберите файлы с квартирами";

                if (dialog.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;

                // Этап 1: Сбор ВСЕХ данных
                List<ApartmentData> allApartments = new List<ApartmentData>();
                string log = "=== ЛОГ ОБРАБОТКИ ===\n\n";

                foreach (string filePath in dialog.FileNames)
                {
                    log += $"Чтение файла: {Path.GetFileName(filePath)}\n";
                    try
                    {
                        var apartments = ReadApartmentsFromFile(revitApp, filePath);
                        allApartments.AddRange(apartments);
                        log += $"  Найдено квартир: {apartments.Count}\n";
                    }
                    catch (Exception ex)
                    {
                        log += $"  ОШИБКА: {ex.Message}\n";
                    }
                }

                if (allApartments.Count == 0)
                {
                    TaskDialog.Show("Информация", "Квартиры не найдены.");
                    return Result.Cancelled;
                }

                log += $"\nВсего собрано квартир: {allApartments.Count}\n";

                // Этап 2: Группировка и расчет номеров
                log += "\n=== ГРУППИРОВКА И РАСЧЕТ НОМЕРОВ ===\n";
                CalculateOrdinalNumbers(allApartments, ref log);

                // Этап 3: Запись обратно в файлы
                log += "\n=== ЗАПИСЬ В ФАЙЛЫ ===\n";
                int totalProcessed = 0;

                foreach (string filePath in dialog.FileNames)
                {
                    var fileApartments = allApartments.Where(a => a.FilePath == filePath).ToList();

                    if (fileApartments.Count == 0) continue;

                    log += $"\nФайл: {Path.GetFileName(filePath)}\n";
                    log += $"  Квартир для записи: {fileApartments.Count}\n";

                    try
                    {
                        int processed = WriteToFile(revitApp, filePath, fileApartments);
                        totalProcessed += processed;
                        log += $"  Успешно записано: {processed}\n";
                    }
                    catch (Exception ex)
                    {
                        log += $"  ОШИБКА: {ex.Message}\n";
                    }
                }

                // Этап 4: Отчет
                StringBuilder report = new StringBuilder();
                report.AppendLine("=== РЕЗУЛЬТАТЫ ОБРАБОТКИ ===");
                report.AppendLine($"Файлов обработано: {dialog.FileNames.Length}");
                report.AppendLine($"Всего квартир найдено: {allApartments.Count}");
                report.AppendLine($"Успешно пронумеровано: {totalProcessed}");
                report.AppendLine();

                // Группы для отчета
                var groups = allApartments
                    .GroupBy(a => new { Type = a.ApartmentType ?? "", MGN2 = a.MGN2 ?? "" })
                    .OrderBy(g => g.Key.Type)
                    .ThenBy(g => g.Key.MGN2);

                foreach (var group in groups)
                {
                    report.AppendLine($"Группа: Тип '{group.Key.Type}', МГН2 '{group.Key.MGN2}'");

                    foreach (var apt in group.OrderBy(a => a.OrdinalNumber))
                    {
                        report.AppendLine($"  №{apt.OrdinalNumber}: Площадь {apt.Area:F2} м²");
                    }
                    report.AppendLine();
                }

                // Показываем отчет
                TaskDialog.Show("Результаты обработки", report.ToString());

                // Сохраняем лог
                SaveLog(log);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Критическая ошибка:\n{ex.Message}");
                return Result.Failed;
            }
        }

        private List<ApartmentData> ReadApartmentsFromFile(Autodesk.Revit.ApplicationServices.Application app, string filePath)
        {
            var apartments = new List<ApartmentData>();

            try
            {
                if (!File.Exists(filePath))
                    return apartments;

                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                using (Document doc = app.OpenDocumentFile(modelPath, new OpenOptions()))
                {
                    if (doc == null) return apartments;

                    // Собираем помещения
                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .ToList();

                    foreach (var room in rooms)
                    {
                        // Проверяем назначение
                        var purposeParam = room.LookupParameter("Назначение");
                        if (purposeParam == null || purposeParam.AsString() != "Квартиры")
                            continue;

                        // Получаем параметры квартиры
                        var typeParam = room.LookupParameter("ADSK_Тип квартиры");
                        var mgn2Param = room.LookupParameter("МГН2");
                        var areaParam = room.LookupParameter("ADSK_Площадь квартиры");

                        if (typeParam != null && mgn2Param != null && areaParam != null &&
                            typeParam.HasValue && mgn2Param.HasValue && areaParam.HasValue)
                        {
                            string aptType = GetParameterValue(typeParam);
                            string mgn2 = GetParameterValue(mgn2Param);
                            double area = GetAreaValue(areaParam);

                            apartments.Add(new ApartmentData
                            {
                                FilePath = filePath,
                                RoomId = room.Id, // Храним ElementId
                                ApartmentType = aptType,
                                MGN2 = mgn2,
                                Area = area
                            });
                        }
                    }

                    doc.Close(false);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка чтения файла: {ex.Message}", ex);
            }

            return apartments;
        }

        private string GetParameterValue(Parameter param)
        {
            if (param == null || !param.HasValue) return "";

            if (param.StorageType == StorageType.String)
                return param.AsString();
            else if (param.StorageType == StorageType.Integer)
                return param.AsInteger().ToString();
            else if (param.StorageType == StorageType.Double)
                return param.AsValueString() ?? param.AsDouble().ToString();

            return "";
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

        private void CalculateOrdinalNumbers(List<ApartmentData> apartments, ref string log)
        {
            // Группируем по Типу и МГН2
            var groups = apartments
                .GroupBy(a => $"{a.ApartmentType}|{a.MGN2}")
                .ToList();

            log += $"Создано групп: {groups.Count}\n";

            foreach (var group in groups)
            {
                log += $"\nГруппа: {group.Key}\n";
                log += $"  Количество квартир: {group.Count()}\n";

                // Сортируем по площади от меньшего к большему
                var sorted = group.OrderBy(a => a.Area).ToList();

                // Назначаем номера с учетом одинаковых площадей
                Dictionary<double, int> areaNumbers = new Dictionary<double, int>();
                int currentNumber = 1;

                foreach (var apt in sorted)
                {
                    double roundedArea = Math.Round(apt.Area, 2);

                    if (!areaNumbers.ContainsKey(roundedArea))
                    {
                        areaNumbers[roundedArea] = currentNumber;
                        currentNumber++;
                    }

                    apt.OrdinalNumber = areaNumbers[roundedArea];
                    log += $"    Площадь {apt.Area:F2} м² → Номер {apt.OrdinalNumber}\n";
                }
            }
        }

        private int WriteToFile(Autodesk.Revit.ApplicationServices.Application app, string filePath, List<ApartmentData> apartments)
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

                        foreach (var apt in apartments)
                        {
                            try
                            {
                                // RoomId уже ElementId
                                Room room = doc.GetElement(apt.RoomId) as Room;

                                if (room == null) continue;

                                // Ищем параметр
                                Parameter ordinalParam = room.LookupParameter("ПорядковыйНомерДляТипа");

                                if (ordinalParam == null)
                                {
                                    ordinalParam = room.LookupParameter("ADSK_Номер квартиры");
                                }

                                if (ordinalParam != null && !ordinalParam.IsReadOnly)
                                {
                                    if (ordinalParam.StorageType == StorageType.Double)
                                        ordinalParam.Set((double)apt.OrdinalNumber);
                                    else if (ordinalParam.StorageType == StorageType.Integer)
                                        ordinalParam.Set(apt.OrdinalNumber);
                                    else if (ordinalParam.StorageType == StorageType.String)
                                        ordinalParam.Set(apt.OrdinalNumber.ToString());

                                    processed++;
                                }
                            }
                            catch (Exception ex)
                            {
                                // Пропускаем ошибки отдельных квартир
                                Debug.WriteLine($"Ошибка записи квартиры: {ex.Message}");
                            }
                        }

                        trans.Commit();
                    }

                    // Сохраняем
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
                throw new Exception($"Ошибка записи: {ex.Message}", ex);
            }

            return processed;
        }

        private void SaveLog(string logContent)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string logPath = Path.Combine(desktop, $"ApartmentLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(logPath, logContent, Encoding.UTF8);
            }
            catch { }
        }

        private class ApartmentData
        {
            public string FilePath { get; set; }
            public ElementId RoomId { get; set; } // Изменено с int на ElementId
            public string ApartmentType { get; set; }
            public string MGN2 { get; set; }
            public double Area { get; set; }
            public int OrdinalNumber { get; set; }
        }
    }
}