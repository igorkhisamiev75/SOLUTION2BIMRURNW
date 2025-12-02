#region Namespaces

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using System.Collections.Generic;
using System.Linq;

#endregion

namespace RevitAddIn2BIMRU.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    public class DisconnectWallsOptimized : IExternalCommand
    {
        Document _doc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            _doc = uiDoc.Document;

            try
            {
                // Показываем диалог с опциями
                TaskDialog dialog = new TaskDialog("Запрет примыкания стен");
                dialog.MainInstruction = "Выберите действие:";
                dialog.CommonButtons = TaskDialogCommonButtons.Cancel;

                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Запретить примыкание на всех концах выбранных стен");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Запретить примыкание на ближнем конце каждой стены");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Выбрать конкретный конец для каждой стены");

                TaskDialogResult result = dialog.Show();

                if (result == TaskDialogResult.Cancel)
                    return Result.Cancelled;

                // Выбираем стены
                IList<Reference> references = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new WallSelectionFilter(),
                    "Выберите стены");

                if (references == null || references.Count == 0)
                    return Result.Cancelled;

                List<Wall> walls = new List<Wall>();
                foreach (Reference reference in references)
                {
                    Wall wall = _doc.GetElement(reference) as Wall;
                    if (wall != null && wall.Location is LocationCurve)
                    {
                        walls.Add(wall);
                    }
                }

                if (walls.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Не выбрано ни одной подходящей стены");
                    return Result.Cancelled;
                }

                using (Transaction tx = new Transaction(_doc))
                {
                    tx.Start("Запрет примыкания стен");

                    int disallowedCount = 0;

                    switch (result)
                    {
                        case TaskDialogResult.CommandLink1:
                            // Запретить на всех концах
                            disallowedCount = DisallowAllEnds(walls);
                            break;

                        case TaskDialogResult.CommandLink2:
                            // Запретить на ближнем конце
                            disallowedCount = DisallowClosestEnd(walls, uiDoc);
                            break;

                        case TaskDialogResult.CommandLink3:
                            // Запретить на выбранном конце для каждой стены
                            disallowedCount = DisallowSelectedEndForEachWall(walls, uiDoc);
                            break;
                    }

                    tx.Commit();

                    TaskDialog.Show("Готово",
                        $"Обработано стен: {walls.Count}\n" +
                        $"Запрещено примыканий: {disallowedCount}");
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Произошла ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        private int DisallowAllEnds(List<Wall> walls)
        {
            int count = 0;
            foreach (Wall wall in walls)
            {
                WallUtils.DisallowWallJoinAtEnd(wall, 0);
                WallUtils.DisallowWallJoinAtEnd(wall, 1);
                count += 2;
            }
            return count;
        }

        private int DisallowClosestEnd(List<Wall> walls, UIDocument uiDoc)
        {
            int count = 0;

            TaskDialog.Show("Информация",
                "Щелкните точку для определения ближних концов стен.\n" +
                "Ближайший к этой точке конец каждой стены будет отключен от примыкания.");

            XYZ referencePoint = uiDoc.Selection.PickPoint("Выберите точку-ориентир");

            foreach (Wall wall in walls)
            {
                LocationCurve lc = wall.Location as LocationCurve;
                if (lc == null) continue;

                Curve curve = lc.Curve;
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);

                double distToStart = referencePoint.DistanceTo(start);
                double distToEnd = referencePoint.DistanceTo(end);

                int endIndex = (distToStart < distToEnd) ? 0 : 1;
                WallUtils.DisallowWallJoinAtEnd(wall, endIndex);
                count++;
            }

            return count;
        }

        private int DisallowSelectedEndForEachWall(List<Wall> walls, UIDocument uiDoc)
        {
            int count = 0;

            foreach (Wall wall in walls)
            {
                LocationCurve lc = wall.Location as LocationCurve;
                if (lc == null) continue;

                Curve curve = lc.Curve;
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);

                // Запрашиваем выбор конца
                TaskDialog dialog = new TaskDialog($"Стена {wall.Id}");
                dialog.MainInstruction = "Выберите конец для запрета примыкания:";
                dialog.CommonButtons = TaskDialogCommonButtons.Cancel;

                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Первый конец (начало стены)");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    $"Второй конец (конец стены)");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Пропустить эту стену");

                TaskDialogResult result = dialog.Show();

                if (result == TaskDialogResult.Cancel)
                    break;
                else if (result == TaskDialogResult.CommandLink1)
                {
                    WallUtils.DisallowWallJoinAtEnd(wall, 0);
                    count++;
                }
                else if (result == TaskDialogResult.CommandLink2)
                {
                    WallUtils.DisallowWallJoinAtEnd(wall, 1);
                    count++;
                }
                // CommandLink3 - пропускаем стену
            }

            return count;
        }

        /// <summary>
        /// Фильтр для выбора только стен
        /// </summary>
        public class WallSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Wall;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}