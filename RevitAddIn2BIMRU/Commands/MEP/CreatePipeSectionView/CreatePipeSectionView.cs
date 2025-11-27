using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;

namespace RevitAddIn2BIMRU.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class CreatePipeSectionView : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // Выбор элемента (трубы, лотка или воздуховода)
                Reference elementRef = uiDoc.Selection.PickObject(ObjectType.Element,
                    new DuctPipeTraySelectionFilter(), "Выберите трубу, лоток или воздуховод");

                Element element = doc.GetElement(elementRef);
                Curve elementCurve = null;
                XYZ elementDirection = null;
                double elementWidth = 0;

                // Определяем тип элемента и получаем его геометрию
                if (element is Pipe pipe)
                {
                    LocationCurve lc = pipe.Location as LocationCurve;
                    elementCurve = lc?.Curve;
                    elementWidth = pipe.Diameter;
                }
                else if (element is CableTray cableTray)
                {
                    LocationCurve lc = cableTray.Location as LocationCurve;
                    elementCurve = lc?.Curve;
                    elementWidth = cableTray.Width;
                }
                else if (element is Duct duct)
                {
                    LocationCurve lc = duct.Location as LocationCurve;
                    elementCurve = lc?.Curve;
                    try
                    {
                        elementWidth = duct.Width;
                    }
                    catch { elementWidth = duct.Diameter; }





                }

                if (elementCurve == null)
                {
                    message = "Выбранный элемент не имеет линейной геометрии.";
                    return Result.Failed;
                }

                elementDirection = (elementCurve.GetEndPoint(1) - elementCurve.GetEndPoint(0)).Normalize();
                XYZ midpoint = elementCurve.Evaluate(0.5, true);

                using (Transaction trans = new Transaction(doc, "Create Section View"))
                {
                    trans.Start();

                    // Определяем размеры области разреза
                    double length = elementCurve.Length;
                    double offset = elementWidth * 1;


                    // Создаем ограничивающий ящик
                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                    sectionBox.Min = new XYZ(-length / 2 - offset, -offset, -offset);
                    sectionBox.Max = new XYZ(length / 2 + offset, offset, offset);

                    // Определяем ориентацию вида
                    XYZ viewDirection, upDirection, rightDirection;

                    // Для вертикальных труб
                    if (Math.Abs(elementDirection.Z) > 0.9)
                    {
                        viewDirection = XYZ.BasisY;
                        upDirection = XYZ.BasisZ;
                        rightDirection = XYZ.BasisX;
                    }
                    // Для горизонтальных труб
                    else
                    {
                        viewDirection = elementDirection.CrossProduct(XYZ.BasisZ).Normalize();
                        if (viewDirection.IsZeroLength())
                            viewDirection = XYZ.BasisX;

                        upDirection = XYZ.BasisZ;
                        rightDirection = viewDirection.CrossProduct(upDirection).Normalize();
                    }

                    // Настраиваем преобразование
                    Transform transform = Transform.Identity;
                    transform.Origin = midpoint;
                    transform.BasisX = rightDirection;
                    transform.BasisY = upDirection;
                    transform.BasisZ = -viewDirection; // Отрицательное значение для правильного направления взгляда

                    sectionBox.Transform = transform;

                    // Создаем вид разреза
                    ViewSection sectionView = ViewSection.CreateSection(doc,
                        doc.GetDefaultElementTypeId(ElementTypeGroup.ViewTypeSection),
                        sectionBox);

                    if (sectionView == null)
                    {
                        message = "Не удалось создать вид разреза.";
                        return Result.Failed;
                    }

                    sectionView.Name = $"Разрез- {element.Category.Name} {element.Id}";
                    trans.Commit();

                    // Активируем созданный вид
                    uiDoc.ActiveView = sectionView;
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", ex.ToString());
                return Result.Failed;
            }
        }
    }


    public class DuctPipeTraySelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            // Разрешаем выбор труб, кабельных лотков и воздуховодов
            return elem is Pipe || elem is CableTray || elem is Duct;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}