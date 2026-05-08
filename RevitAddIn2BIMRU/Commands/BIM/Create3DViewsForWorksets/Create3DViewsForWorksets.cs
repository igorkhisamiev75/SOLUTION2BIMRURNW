#region Namespace

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#endregion

namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    public class Create3DViewsForWorksets : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // Спрашиваем у пользователя какие рабочие наборы обрабатывать
                TaskDialog dialog = new TaskDialog("Настройки создания видов");
                dialog.MainInstruction = "Какие рабочие наборы обрабатывать?";
                dialog.MainContent = "Выберите тип рабочих наборов для создания 3D видов:";

                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "📋 Только пользовательские рабочие наборы");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "✅ Все рабочие наборы (включая системные)");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "❌ Отмена");

                dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
                dialog.DefaultButton = TaskDialogResult.CommandLink1;

                TaskDialogResult result = dialog.Show();

                List<Workset> worksets = new List<Workset>();

                if (result == TaskDialogResult.CommandLink1)
                {
                    // Только пользовательские
                    worksets = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .ToWorksets()
                        .ToList();
                }
                else if (result == TaskDialogResult.CommandLink2)
                {
                    // Все рабочие наборы
                    worksets = new FilteredWorksetCollector(doc)
                        .ToWorksets()
                        .ToList();
                }
                else
                {
                    return Result.Cancelled;
                }

                if (worksets.Count == 0)
                {
                    TaskDialog.Show("Информация", "Не найдено рабочих наборов.");
                    return Result.Cancelled;
                }

                // Создаем 3D виды
                List<string> createdViews = new List<string>();
                List<string> skippedViews = new List<string>();

                using (Transaction t = new Transaction(doc, "Создание 3D видов"))
                {
                    t.Start();

                    // Тип 3D вида
                    ViewFamilyType view3DType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .First(v => v.ViewFamily == ViewFamily.ThreeDimensional);

                    foreach (Workset workset in worksets)
                    {
                        string viewName = $"3D - {workset.Name}";

                        // Проверяем уникальность
                        if (ViewExists(doc, viewName))
                        {
                            skippedViews.Add($"{viewName} (уже существует)");
                            continue;
                        }

                        // Создаем вид
                        View3D view3D = View3D.CreateIsometric(doc, view3DType.Id);
                        if (view3D == null)
                        {
                            skippedViews.Add($"{viewName} (ошибка создания)");
                            continue;
                        }

                        view3D.Name = viewName;

                        // Настраиваем видимость рабочих наборов
                        if (ConfigureViewWorksetVisibility(view3D, workset.Id, worksets))
                        {
                            createdViews.Add(viewName);
                        }
                        else
                        {
                            skippedViews.Add($"{viewName} (ошибка настройки)");
                        }
                    }

                    t.Commit();
                }

                // Показываем результаты
                ShowResults(createdViews, skippedViews, worksets.Count);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Настраивает видимость рабочих наборов в виде
        /// </summary>
        private bool ConfigureViewWorksetVisibility(View3D view, WorksetId targetWorksetId, List<Workset> allWorksets)
        {
            try
            {
                // 1. Показываем только целевой рабочий набор
                ShowWorksetInView(view, targetWorksetId);

                // 2. Скрываем все остальные рабочие наборы
                foreach (Workset workset in allWorksets)
                {
                    if (workset.Id != targetWorksetId)
                    {
                        HideWorksetInView(view, workset.Id);
                    }
                }

                // 3. Настраиваем параметры вида
                SetViewDisplayProperties(view);

                return true;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка настройки вида", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Включает видимость рабочего набора в виде
        /// </summary>
        private void ShowWorksetInView(View view, WorksetId worksetId)
        {
            view.SetWorksetVisibility(worksetId, WorksetVisibility.Visible);
        }

        /// <summary>
        /// Выключает видимость рабочего набора в виде
        /// </summary>
        private void HideWorksetInView(View view, WorksetId worksetId)
        {
            view.SetWorksetVisibility(worksetId, WorksetVisibility.Hidden);
        }

        /// <summary>
        /// Настраивает свойства отображения вида
        /// </summary>
        private void SetViewDisplayProperties(View3D view)
        {
            try
            {
                // Детализация - детальная
                view.get_Parameter(BuiltInParameter.VIEW_DETAIL_LEVEL).Set(2);

                // Отключаем тени для производительности
                //view.get_Parameter(BuiltInParameter.VIEW_SHADOWS).Set(0);

                // Стиль графики - скрытая линия
                ElementId hiddenLineId = GetHiddenLineStyleId(view.Document);
                if (hiddenLineId != ElementId.InvalidElementId)
                {
                    view.get_Parameter(BuiltInParameter.MODEL_GRAPHICS_STYLE).Set(hiddenLineId);
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        /// <summary>
        /// Получает ID стиля "Скрытая линия"
        /// </summary>
        private ElementId GetHiddenLineStyleId(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(GraphicsStyle));

            foreach (GraphicsStyle style in collector)
            {
                if (style.GraphicsStyleCategory != null)
                {
                    string name = style.GraphicsStyleCategory.Name;
                    if (name.Contains("Скрытая") || name.Contains("Hidden"))
                    {
                        return style.Id;
                    }
                }
            }

            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// Проверяет существование вида
        /// </summary>
        private bool ViewExists(Document doc, string viewName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Показывает результаты работы
        /// </summary>
        private void ShowResults(List<string> createdViews, List<string> skippedViews, int totalWorksets)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Обработано рабочих наборов: {totalWorksets}");
            sb.AppendLine($"Создано видов: {createdViews.Count}");
            sb.AppendLine($"Пропущено: {skippedViews.Count}");
            sb.AppendLine();

            if (createdViews.Count > 0)
            {
                sb.AppendLine("Созданные виды:");
                foreach (string viewName in createdViews)
                {
                    sb.AppendLine($"  • {viewName}");
                }
                sb.AppendLine();
            }

            if (skippedViews.Count > 0)
            {
                sb.AppendLine("Пропущенные:");
                foreach (string skipped in skippedViews)
                {
                    sb.AppendLine($"  • {skipped}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("На каждом созданном виде включен только один рабочий набор.");

            TaskDialog.Show("Результаты", sb.ToString());
        }
    }
}