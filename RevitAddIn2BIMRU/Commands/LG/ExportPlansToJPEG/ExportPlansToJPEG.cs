using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RevitAddIn2BIMRU.Commands.LG
{
    [Transaction(TransactionMode.Manual)]
    public class ExportPlansToJPEG : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // 1. Получаем выбранные элементы
                ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Пожалуйста, выберите один или несколько планов этажей в дереве проекта перед запуском команды.");
                    return Result.Failed;
                }

                // 2. Фильтруем только ViewPlan (планы этажей)
                List<ViewPlan> viewPlans = new List<ViewPlan>();
                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem is ViewPlan viewPlan)
                    {
                        viewPlans.Add(viewPlan);
                    }
                }

                if (viewPlans.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Среди выбранных элементов нет ни одного плана этажа (ViewPlan).");
                    return Result.Failed;
                }

                // 3. Подтверждение количества экспортируемых видов
                string confirmMsg = $"Будет экспортировано {viewPlans.Count} планов в отдельные JPEG-файлы. Продолжить?";
                if (TaskDialog.Show("Подтверждение", confirmMsg, TaskDialogCommonButtons.Yes) != TaskDialogResult.Yes)
                {
                    return Result.Cancelled;
                }

                // 4. Выбор папки для сохранения
                using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.Description = "Выберите папку для сохранения JPEG-файлов";
                    folderDialog.ShowNewFolderButton = true;

                    if (folderDialog.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;

                    string outputPath = folderDialog.SelectedPath;

                    // 5. Экспорт видов
                    int exportedCount = 0;
                    foreach (ViewPlan viewPlan in viewPlans)
                    {
                        try
                        {
                            // Очищаем имя от недопустимых символов
                            string cleanName = string.Join("_", viewPlan.Name.Split(Path.GetInvalidFileNameChars()));
                            if (string.IsNullOrWhiteSpace(cleanName))
                            {
                                cleanName = "UnnamedView_" + viewPlan.Id.ToString();
                            }

                            // Базовый путь без расширения – Revit сам добавит .jpg
                            string baseFilePath = Path.Combine(outputPath, cleanName);

                            // Настройки экспорта в JPEG
                            ImageExportOptions options = new ImageExportOptions
                            {
                                FilePath = baseFilePath,
                                ExportRange = ExportRange.SetOfViews, // Экспортируем указанный набор
                                ImageResolution = ImageResolution.DPI_300,
                                ZoomType = (ZoomFitType)ZoomType.FitToPage,
                                PixelSize = 1500 // Можно регулировать размер при FitToPage
                            };

                            // Передаём ID текущего вида
                            List<ElementId> viewIds = new List<ElementId> { viewPlan.Id };
                            options.SetViewsAndSheets(viewIds);

                            // Экспорт через документ
                            doc.ExportImage(options);
                            exportedCount++;
                        }
                        catch (Exception ex)
                        {
                            TaskDialog.Show("Ошибка", $"Не удалось экспортировать вид '{viewPlan.Name}': {ex.Message}");
                        }
                    }

                    // 6. Итог
                    TaskDialog.Show("Успешно", $"Экспортировано {exportedCount} из {viewPlans.Count} планов в папку:\n{outputPath}");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка экспорта", $"Не удалось выполнить экспорт: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}