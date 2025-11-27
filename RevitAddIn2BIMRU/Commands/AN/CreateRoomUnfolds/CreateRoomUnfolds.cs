//Создание объекта для генерации чисел
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.UI;

namespace RevitAddIn2BIMRU.Commands.AN
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateRoomUnfolds : IExternalCommand
    {
        // Константа для отступа внутрь помещения (10 мм)
        private const double OffsetInward = 0.1; // 10 мм в метрах

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // 1. Выбор помещения
                Reference roomRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new RoomSelectionFilter(),
                    "Выберите помещение");

                Room room = doc.GetElement(roomRef) as Room;
                if (room == null)
                {
                    message = "Выбранный элемент не является помещением";
                    return Result.Failed;
                }

                // 2. Получение границ помещения
                SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
                    StoreFreeBoundaryFaces = true
                };

                IList<IList<BoundarySegment>> boundaries = room.GetBoundarySegments(options);
                if (boundaries == null || boundaries.Count == 0)
                {
                    message = "Не удалось получить границы помещения";
                    return Result.Failed;
                }

                // 3. Получение геометрии помещения
                BoundingBoxXYZ roomBox = room.get_BoundingBox(null);
                double roomHeight = roomBox.Max.Z - roomBox.Min.Z;
                Level level = doc.GetElement(room.LevelId) as Level;

                // 4. Создание сечений
                List<ViewSection> sections = new List<ViewSection>();
                ViewFamilyType sectionType = GetSectionViewFamilyType(doc);

                using (Transaction trans = new Transaction(doc, "Создание разрезов с отступом"))
                {
                    trans.Start();

                    int sectionNumber = 1;

                    foreach (IList<BoundarySegment> boundary in boundaries)
                    {
                        foreach (BoundarySegment segment in boundary)
                        {
                            Curve curve = segment.GetCurve();
                            if (curve == null) continue;

                            XYZ p = curve.GetEndPoint(0); //первая точка стены 
                            XYZ q = curve.GetEndPoint(1); //вторая точка стены
                            XYZ v = q - p; //длина стены точка



                            //BoundingBoxXYZ bb = wall.get_BoundingBox(null);
                            //double minZ = bb.Min.Z; //высота
                            //double maxZ = bb.Max.Z;

                            double w = v.GetLength(); //длина стены
                                                      //double h = maxZ - minZ; //высота стены
                                                      //double d = wall.WallType.Width; //толщина стены

                            double offset = 0.2; //смещение разреза

                            XYZ curveStart = curve.GetEndPoint(0);
                            XYZ curveEnd = curve.GetEndPoint(1);

                            // Вектор вдоль стены
                            XYZ wallDirection = (curveEnd - curveStart).Normalize();

                            // Вектор вида (перпендикулярно стене)
                            XYZ viewDirection = wallDirection.CrossProduct(XYZ.BasisZ).Normalize();

                            // Проверка направления внутрь помещения
                            XYZ testPoint = curveStart + viewDirection * 0.1;
                            if (!IsPointInRoom(room, testPoint))
                            {
                                viewDirection = -viewDirection;
                            }

                            XYZ upDirection = XYZ.BasisZ;
                            XYZ rightDirection = viewDirection.CrossProduct(upDirection);

                            //// Создание трансформации
                            //Transform transform = Transform.Identity;
                            //transform.BasisX = rightDirection;
                            //transform.BasisY = upDirection;
                            //transform.BasisZ = rightDirection.CrossProduct(upDirection);

                            //// Центр сечения - середина стены с отступом 10 мм внутрь
                            //XYZ sectionCenter = (curveStart + curveEnd) / 2 + viewDirection * OffsetInward;

                            //TaskDialog.Show("t", $"{sectionCenter.ToString()}");

                            // Создание BoundingBox
                            //BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
                            //{
                            //    Transform = transform,
                            //    Min = new XYZ(-(curve.Length / 2 + 0.5), -0.5, -0.5),
                            //    Max = new XYZ(curve.Length / 2 + 0.5, 5.0, roomHeight + 0.5)
                            //};

                            // Установка флагов
                            //for (int i = 0; i < 3; i++)
                            //{
                            //    sectionBox.set_MinEnabled(i, true);
                            //    sectionBox.set_MaxEnabled(i, true);
                            //}

                            // Установка центра сечения
                            //sectionBox.Transform.Origin = sectionCenter;
                            //sectionBox.Transform.Origin = new XYZ(10,10,10);

                            //sectionBox.Transform.OfPoint(new XYZ(10, 10, 10));

                            //ransform trf = sectionBox.Transform;

                            XYZ min = new XYZ(-w * 0.5 - 0.1, -0.1, -offset);
                            //XYZ max = new XYZ( w, maxZ + offset, 0 ); // section view dotted line in center of wall
                            XYZ max = new XYZ(w * 0.5 + 0.1, roomHeight + 0.1, offset); // section view dotted line offset from center of wall

                            XYZ midpoint = p + 0.5 * v;
                            XYZ walldir = v.Normalize(); //Returns a new UV whose coordinates are the normalized values from this vector.
                            XYZ up = XYZ.BasisZ;
                            XYZ viewdir = walldir.CrossProduct(up);

                            Transform t = Transform.Identity;
                            t.Origin = midpoint;
                            t.BasisX = walldir;
                            t.BasisY = up;
                            t.BasisZ = viewdir;

                            BoundingBoxXYZ sectionBox2 = new BoundingBoxXYZ();
                            sectionBox2.Transform = t;
                            sectionBox2.Min = min;
                            sectionBox2.Max = max;


                            // Создание сечения
                            ViewSection section = ViewSection.CreateSection(doc, sectionType.Id, sectionBox2);

                            // Создаем базовое имя для разреза
                            string baseSectionName = $"Разрез в пом. {room.Number}-{sectionNumber++}";

                            // Получаем уникальное имя
                            string uniqueName = GetUniqueViewName(doc, baseSectionName);

                            section.Name = uniqueName;

                            // Установка масштаба 1:50
                            Autodesk.Revit.DB.Parameter scaleParam = section.get_Parameter(BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC);
                            if (scaleParam != null && !scaleParam.IsReadOnly)
                            {
                                scaleParam.Set(50);
                            }

                            sections.Add(section);
                        }
                    }

                    trans.Commit();
                }

                // 5. Создание листа и размещение видов
                if (sections.Count > 0)
                {
                    using (Transaction trans = new Transaction(doc, "Размещение на листе"))
                    {
                        trans.Start();

                        ViewFamilyType sheetType = GetFirstSheetType(doc);
                        if (sheetType == null)
                        {
                            message = "Не найден тип листа";
                            return Result.Failed;
                        }


                        // Создаем лист с конкретной рамкой
                        ViewSheet sheet;
                        try
                        {
                            sheet = CreateSheetWithSpecificTitleblock(doc, "Форма 3");

                            sheet.Name = $"Разрезы {room.Number + "_" + room.Name}";
                            sheet.SheetNumber = $"({DateTime.Now:yyyyMMdd-HHmmss})"; ; // Или ваша нумерация
                        }
                        catch (Exception ex)
                        {
                            message = ex.Message;
                            return Result.Failed;
                        }

                        // Create sheet with more robust naming
                        //ViewSheet sheet = ViewSheet.Create(doc, sheetType.Id);
                        //sheet.Name = $"Разрезы {room.Name} ({DateTime.Now:yyyyMMdd-HHmm})";

                        // Placement parameters - consider making these configurable
                        double startX = 0.1;  // 10% from left edge
                        double startY = 0.1;  // 10% from bottom edge
                        double currentX = startX;
                        double currentY = startY;

                        double viewportWidth = 0.3;  // 30% of sheet width
                        double viewportHeight = 0.2; // 20% of sheet height
                        double spacing = 0.05;      // 5% spacing

                        foreach (ViewSection section in sections)
                        {
                            // Check if we need to move to next row
                            if (currentX + viewportWidth > 0.95) // 95% of sheet width
                            {
                                currentX = startX;
                                currentY += viewportHeight + spacing;

                                // Check if we've run out of vertical space
                                if (currentY + viewportHeight > 0.95)
                                {
                                    // Create a new sheet if current one is full
                                    //sheet = ViewSheet.Create(doc, sheetType.Id);
                                    //sheet.Name = $"Разрезы {room.Name} ({DateTime.Now:yyyyMMdd-HHmm}) продолжение";
                                    currentX = startX;
                                    currentY = startY;
                                }
                            }

                            // Create viewport
                            Viewport viewport = Viewport.Create(doc, sheet.Id, section.Id, new XYZ(currentX, currentY, 0));

                            // Set detail number to section name
                            Autodesk.Revit.DB.Parameter detailNumParam = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                            if (detailNumParam != null && !detailNumParam.IsReadOnly)
                            {
                                detailNumParam.Set(section.Name);
                            }

                            currentX += viewportWidth + spacing;
                        }

                        trans.Commit();
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", ex.ToString());
                return Result.Failed;
            }
        }

        private bool IsPointInRoom(Room room, XYZ point)
        {
            // Метод проверки точки в помещении через BoundingBox
            BoundingBoxXYZ bb = room.get_BoundingBox(null);
            if (bb == null) return false;

            return point.X > bb.Min.X && point.X < bb.Max.X &&
                   point.Y > bb.Min.Y && point.Y < bb.Max.Y &&
                   point.Z > bb.Min.Z && point.Z < bb.Max.Z;
        }

        private ViewFamilyType GetSectionViewFamilyType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Section);
        }

        ViewFamilyType GetFirstSheetType(Document doc)
        {
            // Filter for title block family symbols (sheet types)
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(ViewFamilyType));

            foreach (ViewFamilyType vft in collector)
            {
                if (vft.ViewFamily == ViewFamily.Sheet)
                {
                    return vft;
                }
            }

            return null;
        }

        public ViewSheet CreateSheetWithSpecificTitleblock(Document doc, string titleblockName)
        {
            // Find all title blocks in the project
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType();

            // Find the specific title block by name
            FamilySymbol titleblock = null;
            foreach (Element e in collector)
            {
                if (e.Name.Equals(titleblockName, StringComparison.OrdinalIgnoreCase))
                {
                    titleblock = e as FamilySymbol;
                    break;
                }
            }

            //if (titleblock == null)
            //{
            //    // Try to load the title block if not found
            //    string defaultTitleBlockPath = @"C:\ProgramData\Autodesk\RVT 2021\Libraries\US Imperial\Titleblocks\OS_Titleblock.rfa";

            //    if (File.Exists(defaultTitleBlockPath))
            //    {
            //        using (Transaction loadTrans = new Transaction(doc, "Load Titleblock"))
            //        {
            //            loadTrans.Start();
            //            if (doc.LoadFamily(defaultTitleBlockPath, out Family family))
            //            {
            //                // Get the first symbol from loaded family
            //                var symbolIds = family.GetFamilySymbolIds();
            //                if (symbolIds.Count > 0)
            //                {
            //                    titleblock = doc.GetElement(symbolIds.First()) as FamilySymbol;
            //                }
            //            }
            //            loadTrans.Commit();
            //        }
            //    }

            //    if (titleblock == null)
            //    {
            //        throw new Exception($"Titleblock '{titleblockName}' not found in project and couldn't be loaded.");
            //    }
            //}

            // Check if titleblock is active
            if (!titleblock.IsActive)
            {
                titleblock.Activate();
                doc.Regenerate();
            }

            // Create the sheet
            ViewSheet sheet = ViewSheet.Create(doc, titleblock.Id);
            return sheet;
        }

        private static string GetUniqueViewName(Document doc, string baseName)
        {
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name));

            if (!existingNames.Contains(baseName)) return baseName;

            int counter = 1;
            string newName;
            do
            {
                newName = $"{baseName}_{counter++}";
            } while (existingNames.Contains(newName));

            return newName;
        }
    }

    public class RoomSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Room;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}