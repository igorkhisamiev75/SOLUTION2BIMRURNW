using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace RevitAddIn2BIMRU.Commands.AN
{
    [Transaction(TransactionMode.Manual)]
    class ConvertGrids3Dto2D : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                using (Transaction t = new Transaction(doc, "Convert Grids 2D/3D"))
                {
                    t.Start();

                    ConvertGridsExtentType(doc, uiDoc);

                    t.Commit();
                }

                TaskDialog.Show("Готово", "Оси переведены в 2D/3D");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Не удалось выполнить операцию: {ex.Message}");
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        public void ConvertGridsExtentType(Document doc, UIDocument uiDoc)
        {
            ICollection<Element> grids = GetAllGridsElements(doc);

            if (grids.Count == 0)
            {
                TaskDialog.Show("Информация", "В проекте не найдены оси");
                return;
            }

            // Check the first grid to determine current state
            Grid firstGrid = grids.First() as Grid;
            if (firstGrid == null) return;

            bool isCurrently3D = firstGrid.GetDatumExtentTypeInView(DatumEnds.End0, doc.ActiveView) == DatumExtentType.Model;

            // Determine target state (toggle between 2D and 3D)
            DatumExtentType targetExtentType = isCurrently3D ? DatumExtentType.ViewSpecific : DatumExtentType.Model;

            foreach (Element element in grids)
            {
                Grid grid = element as Grid;
                if (grid == null) continue;

                try
                {
                    grid.SetDatumExtentType(DatumEnds.End0, doc.ActiveView, targetExtentType);
                    grid.SetDatumExtentType(DatumEnds.End1, doc.ActiveView, targetExtentType);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Предупреждение", $"Не удалось изменить ось {grid.Name}: {ex.Message}");
                }
            }
        }

        public ICollection<Element> GetAllGridsElements(Document document)
        {
            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType()
                .ToElements();
        }
    }
}