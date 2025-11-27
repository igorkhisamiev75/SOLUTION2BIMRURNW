using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.Attributes;

namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateLevel3DViews : IExternalCommand
    {
        // Высота обзора в мм (преобразуется в футы)
        private const double ViewHeight = 2500 / 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // Получаем все уровни
                List<Level> levels = GetLevels(doc);

                if (levels.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "В проекте не найдены уровни");
                    return Result.Failed;
                }

                // Создаем 3D виды для каждого уровня в одной транзакции
                List<View3D> createdViews = Create3DViewsForLevels(doc, levels);

                // Показываем результат
                TaskDialog.Show("Успех",
                    $"Создано {createdViews.Count} 3D видов для уровней:\n" +
                    string.Join("\n", createdViews.Select(v => v.Name)));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", $"Не удалось создать 3D виды: {ex.Message}");
                return Result.Failed;
            }
        }

        private List<Level> GetLevels(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
        }

        private List<View3D> Create3DViewsForLevels(Document doc, List<Level> levels)
        {
            List<View3D> createdViews = new List<View3D>();

            // Получаем тип 3D вида
            ViewFamilyType view3DType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (view3DType == null)
            {
                throw new Exception("Не найден тип 3D вида");
            }

            // ОДНА транзакция для всех операций
            using (Transaction trans = new Transaction(doc, "Создание 3D видов по уровням"))
            {
                trans.Start();

                foreach (Level level in levels)
                {
                    // Создаем 3D вид
                    View3D view3D = View3D.CreateIsometric(doc, view3DType.Id);

                    if (view3D != null)
                    {
                        // Настраиваем вид
                        Configure3DView(view3D, level);
                        createdViews.Add(view3D);
                    }
                }

                trans.Commit();
            }

            return createdViews;
        }

        private void Configure3DView(View3D view3D, Level level)
        {
            Document doc = view3D.Document;

            // Устанавливаем имя вида
            view3D.Name = $"3D Уровень - {level.Name}";

            // Настраиваем отсечение по высоте
            BoundingBoxXYZ boundingBox = view3D.GetSectionBox();

            if (boundingBox != null)
            {
                // Получаем границы проекта
                BoundingBoxXYZ projectBounds = GetProjectBoundingBox(doc);

                // Устанавливаем отсечение по высоте уровня
                double levelElevation = level.Elevation;
                double minZ = levelElevation - 1; // 1 фут ниже уровня
                double maxZ = levelElevation + ViewHeight;

                boundingBox.Min = new XYZ(
                    projectBounds.Min.X,
                    projectBounds.Min.Y,
                    minZ
                );

                boundingBox.Max = new XYZ(
                    projectBounds.Max.X,
                    projectBounds.Max.Y,
                    maxZ
                );

                view3D.SetSectionBox(boundingBox);
            }

            // Настраиваем графику вида
            view3D.DetailLevel = ViewDetailLevel.Medium;
            view3D.DisplayStyle = DisplayStyle.ShadingWithEdges;

            // Включаем видимость нужных категорий
            SetViewCategoriesVisibility(view3D);
        }

        private BoundingBoxXYZ GetProjectBoundingBox(Document doc)
        {
            // Получаем границы всех элементов проекта
            BoundingBoxXYZ projectBounds = null;

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e is Wall || e is Floor || e is Room || e is FamilyInstance);

            foreach (Element element in elements)
            {
                BoundingBoxXYZ elementBounds = element.get_BoundingBox(null);
                if (elementBounds != null)
                {
                    if (projectBounds == null)
                    {
                        projectBounds = elementBounds;
                    }
                    else
                    {
                        projectBounds.Min = new XYZ(
                            Math.Min(projectBounds.Min.X, elementBounds.Min.X),
                            Math.Min(projectBounds.Min.Y, elementBounds.Min.Y),
                            Math.Min(projectBounds.Min.Z, elementBounds.Min.Z)
                        );

                        projectBounds.Max = new XYZ(
                            Math.Max(projectBounds.Max.X, elementBounds.Max.X),
                            Math.Max(projectBounds.Max.Y, elementBounds.Max.Y),
                            Math.Max(projectBounds.Max.Z, elementBounds.Max.Z)
                        );
                    }
                }
            }

            // Если не нашли элементов, используем дефолтные границы
            if (projectBounds == null)
            {
                projectBounds = new BoundingBoxXYZ();
                projectBounds.Min = new XYZ(-100, -100, -100);
                projectBounds.Max = new XYZ(100, 100, 100);
            }

            return projectBounds;
        }

        private void SetViewCategoriesVisibility(View3D view3D)
        {
            Document doc = view3D.Document;

            // Включаем основные категории
            Categories categories = doc.Settings.Categories;

            // Список категорий для отображения
            List<BuiltInCategory> visibleCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Furniture,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming
            };

            foreach (BuiltInCategory bic in visibleCategories)
            {
                Category category = Category.GetCategory(doc, bic);
                if (category != null)
                {
                    view3D.SetCategoryHidden(category.Id, false);
                }
            }
        }
    }
}