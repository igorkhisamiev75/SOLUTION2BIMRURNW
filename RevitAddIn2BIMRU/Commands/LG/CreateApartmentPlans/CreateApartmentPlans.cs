using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;

using ComboBox = System.Windows.Forms.ComboBox;
using Form = System.Windows.Forms.Form;
using TextBox = System.Windows.Forms.TextBox;
using View = Autodesk.Revit.DB.View;

namespace RevitAddIn2BIMRU.Commands.LG
{
    [Transaction(TransactionMode.Manual)]
    public class CreateApartmentPlans : IExternalCommand
    {
        private const double Eps = 0.01;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            ViewPlan activeViewPlan = uiDoc.ActiveView as ViewPlan;

            if (activeViewPlan == null)
            {
                message = "Активный вид должен быть планом этажа";
                return Result.Failed;
            }

            ICollection<Element> allRooms = GetAllRoomsInView(doc, activeViewPlan);
            if (allRooms.Count == 0)
            {
                message = "На данном виде не найдены помещения";
                return Result.Failed;
            }

            List<string> availableParams = GetAvailableRoomParameters(allRooms);
            if (availableParams.Count == 0)
            {
                message = "Не найдено ни одного параметра у помещений";
                return Result.Failed;
            }

            ApartmentPlanSettingsDialog settingsDialog = new ApartmentPlanSettingsDialog(availableParams, doc);
            if (settingsDialog.ShowDialog() != DialogResult.OK)
                return Result.Cancelled;

            string selectedParam = settingsDialog.SelectedParameterName;
            string manualCorps = settingsDialog.CorpsNumber;
            string manualSection = settingsDialog.SectionNumber;
            string manualFloor = settingsDialog.FloorNumber;
            ElementId textTypeId = settingsDialog.SelectedTextTypeId;
            ElementId tagTypeId = settingsDialog.SelectedTagTypeId;
            ElementId templateId = settingsDialog.SelectedTemplateId;

            if (string.IsNullOrEmpty(selectedParam))
            {
                message = "Не выбран параметр для номера квартиры";
                return Result.Failed;
            }

            List<string> errorMessages = new List<string>();

            try
            {
                var roomsByApartment = GroupRoomsByApartmentNumber(doc, allRooms, selectedParam);
                if (roomsByApartment.Count == 0)
                {
                    message = $"Нет помещений с заполненным параметром '{selectedParam}'";
                    return Result.Failed;
                }

                using (Transaction t = new Transaction(doc, "Создание планов квартир"))
                {
                    t.Start();
                    int createdCount = 0;

                    foreach (var group in roomsByApartment)
                    {
                        string aptNumber = group.Key;
                        List<Room> rooms = group.Value;
                        if (string.IsNullOrEmpty(aptNumber)) continue;

                        try
                        {
                            Room firstRoom = rooms[0];
                            string displayName = GetApartmentDisplayName(manualCorps, manualSection, manualFloor, aptNumber);
                            // Если имя всё ещё пустое – используем только номер квартиры
                            if (string.IsNullOrEmpty(displayName))
                                displayName = $"кв. {aptNumber}";

                            ViewPlan newView = CreateViewCopy(doc, activeViewPlan, displayName, templateId);
                            doc.Regenerate();

                            CurveLoop boundary = null;
                            string boundaryError = "";
                            try
                            {
                                boundary = GetMergedApartmentBoundary(rooms, ref boundaryError);
                            }
                            catch (Exception ex)
                            {
                                boundaryError = $"Исключение: {ex.Message}";
                            }

                            if (boundary == null || !boundary.Any())
                            {
                                boundary = GetBoundingBoxCurveLoop(rooms);
                                if (boundary != null && boundary.Any())
                                {
                                    errorMessages.Add($"Квартира {aptNumber}: использован прямоугольный контур (основной не удался: {boundaryError})");
                                }
                                else
                                {
                                    errorMessages.Add($"Квартира {aptNumber}: не удалось построить контур (основной и запасной). {boundaryError}");
                                    continue;
                                }
                            }

                            try
                            {
                                ApplyCropToView(newView, boundary);
                                HideInternalGrids(doc, newView, boundary);
                                AddApartmentLabel(doc, newView, firstRoom, boundary,
                                    manualCorps, manualSection, manualFloor, aptNumber, textTypeId, errorMessages);
                                AddRoomTags(doc, newView, rooms, tagTypeId, errorMessages);
                            }
                            catch (Exception ex)
                            {
                                errorMessages.Add($"Квартира {aptNumber}: ошибка при применении обрезки/аннотаций: {ex.Message}");
                            }

                            createdCount++;
                            doc.Regenerate();
                        }
                        catch (Exception ex)
                        {
                            errorMessages.Add($"Квартира {aptNumber}: {ex.Message}");
                        }
                    }

                    t.Commit();
                    TaskDialog.Show("Готово", $"Создано планов: {createdCount} из {roomsByApartment.Count}");
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }

            if (errorMessages.Count > 0)
            {
                string errorText = string.Join(Environment.NewLine, errorMessages);
                TaskDialog.Show("Ошибки при выполнении", errorText);
            }

            return Result.Succeeded;
        }

        // --------------------------------------------------------------
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // --------------------------------------------------------------

        private ICollection<Element> GetAllRoomsInView(Document doc, ViewPlan view) =>
            new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToElements();

        private string GetParameterValue(Element element, string paramName)
        {
            Parameter param = element.Parameters.Cast<Parameter>()
                .FirstOrDefault(p => p.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (param == null || !param.HasValue) return null;

            switch (param.StorageType)
            {
                case StorageType.String: return param.AsString();
                case StorageType.Integer: return param.AsInteger().ToString();
                case StorageType.Double: return param.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.ElementId:
#if REVIT2024_OR_GREATER
            // Revit 2024 и выше — используем Value (возвращает long)
            return param.AsElementId().Value.ToString();
#else
                    // Revit 2023 и старше — используем IntegerValue (возвращает int)
                    return param.AsElementId().IntegerValue.ToString();
#endif
                default: return null;
            }
        }

        private List<string> GetAvailableRoomParameters(ICollection<Element> rooms)
        {
            var names = new HashSet<string>();
            foreach (Element elem in rooms)
                foreach (Parameter p in elem.Parameters)
                    if (!string.IsNullOrEmpty(p.Definition.Name))
                        names.Add(p.Definition.Name);
            return names.OrderBy(n => n).ToList();
        }

        private Dictionary<string, List<Room>> GroupRoomsByApartmentNumber(Document doc, ICollection<Element> rooms, string paramName)
        {
            var groups = new Dictionary<string, List<Room>>();
            foreach (Element elem in rooms)
            {
                Room room = elem as Room;
                if (room == null) continue;
                string val = GetParameterValue(room, paramName);
                if (string.IsNullOrEmpty(val)) continue;
                if (!groups.ContainsKey(val)) groups[val] = new List<Room>();
                groups[val].Add(room);
            }
            return groups;
        }

        private string GetApartmentDisplayName(string manualCorps, string manualSection, string manualFloor, string apartmentNumber)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(manualCorps)) parts.Add($"Корпус {manualCorps}");
            if (!string.IsNullOrEmpty(manualSection)) parts.Add($"секция {manualSection}");
            if (!string.IsNullOrEmpty(manualFloor)) parts.Add($"этаж {manualFloor}");
            if (!string.IsNullOrEmpty(apartmentNumber)) parts.Add($"кв. {apartmentNumber}");
            return string.Join(", ", parts);
        }

        private ViewPlan CreateViewCopy(Document doc, ViewPlan source, string name, ElementId templateId)
        {
            var viewFamilyType = doc.GetElement(source.GetTypeId()) as ViewFamilyType
                ?? new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().First(vft => vft.ViewFamily == ViewFamily.FloorPlan);

            ViewPlan newView = ViewPlan.Create(doc, viewFamilyType.Id, source.GenLevel.Id);
            newView.Name = name;
            newView.Scale = source.Scale;
            newView.ViewTemplateId = templateId;
            return newView;
        }

        // --------------------------------------------------------------
        // ГЕОМЕТРИЧЕСКИЕ МЕТОДЫ (с Solid)
        // --------------------------------------------------------------

        private CurveLoop GetMergedApartmentBoundary(List<Room> rooms, ref string errorMessage)
        {
            errorMessage = null;
            if (rooms == null || rooms.Count == 0)
            {
                errorMessage = "Список комнат пуст";
                return null;
            }

            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
            options.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;
            List<Solid> solids = new List<Solid>();
            double offsetDistance = 0.3;

            foreach (Room room in rooms)
            {
                try
                {
                    IList<IList<BoundarySegment>> boundaries = room.GetBoundarySegments(options);
                    if (boundaries == null || boundaries.Count == 0)
                    {
                        errorMessage += $"Комната {room.Number} не имеет границ; ";
                        continue;
                    }
                    IList<BoundarySegment> outerBoundary = boundaries[0];
                    CurveLoop loop = new CurveLoop();
                    foreach (BoundarySegment seg in outerBoundary)
                    {
                        Curve curve = seg.GetCurve();
                        XYZ p0 = curve.GetEndPoint(0);
                        XYZ p1 = curve.GetEndPoint(1);
                        XYZ p0Flat = new XYZ(p0.X, p0.Y, 0);
                        XYZ p1Flat = new XYZ(p1.X, p1.Y, 0);
                        if (curve is Line)
                            loop.Append(Line.CreateBound(p0Flat, p1Flat));
                        else if (curve is Arc arc)
                        {
                            XYZ center = new XYZ(arc.Center.X, arc.Center.Y, 0);
                            XYZ xDir = new XYZ(arc.XDirection.X, arc.XDirection.Y, 0);
                            XYZ yDir = new XYZ(arc.YDirection.X, arc.YDirection.Y, 0);
                            double startParam = arc.GetEndParameter(0);
                            double endParam = arc.GetEndParameter(1);
                            loop.Append(Arc.Create(center, arc.Radius, startParam, endParam, xDir, yDir));
                        }
                    }
                    if (loop.Any() && !PointsAreEqual(loop.First().GetEndPoint(0), loop.Last().GetEndPoint(1)))
                    {
                        Line closing = Line.CreateBound(loop.Last().GetEndPoint(1), loop.First().GetEndPoint(0));
                        loop.Append(closing);
                    }
                    if (!loop.Any())
                    {
                        errorMessage += $"Комната {room.Number} дала пустой контур; ";
                        continue;
                    }

                    CurveLoop offsetLoop = null;
                    try
                    {
                        XYZ normal = new XYZ(0, 0, 1);
                        offsetLoop = CurveLoop.CreateViaOffset(loop, offsetDistance, normal);
                    }
                    catch
                    {
                        offsetLoop = loop;
                    }

                    if (offsetLoop == null || offsetLoop.IsOpen() || GetCurveLoopArea(offsetLoop) < 0.001)
                    {
                        if (!loop.IsOpen() && GetCurveLoopArea(loop) > 0.001)
                            offsetLoop = loop;
                        else
                        {
                            errorMessage += $"Невалидный контур для комнаты {room.Number}; ";
                            continue;
                        }
                    }

                    try
                    {
                        Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                            new List<CurveLoop> { offsetLoop }, XYZ.BasisZ, 0.1);
                        solids.Add(solid);
                    }
                    catch (Exception ex)
                    {
                        errorMessage += $"Ошибка выдавливания для комнаты {room.Number}: {ex.Message}; ";
                    }
                }
                catch (Exception ex)
                {
                    errorMessage += $"Общая ошибка для комнаты {room.Number}: {ex.Message}; ";
                }
            }

            if (solids.Count == 0)
            {
                errorMessage = "Не удалось создать ни одного тела: " + errorMessage;
                return null;
            }

            Solid unionSolid = solids[0];
            for (int i = 1; i < solids.Count; i++)
            {
                try
                {
                    unionSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                        unionSolid, solids[i], BooleanOperationsType.Union);
                }
                catch (Exception ex)
                {
                    errorMessage += $"Ошибка объединения: {ex.Message}; ";
                }
            }

            Face topFace = null;
            double maxArea = 0;
            foreach (Face face in unionSolid.Faces)
            {
                PlanarFace planarFace = face as PlanarFace;
                if (planarFace != null && Math.Abs(planarFace.FaceNormal.Z) > 0.99)
                {
                    double area = planarFace.Area;
                    if (area > maxArea) { maxArea = area; topFace = planarFace; }
                }
            }
            if (topFace == null)
            {
                errorMessage += "Не найдена верхняя грань; ";
                return null;
            }

            IList<CurveLoop> edgeLoops = topFace.GetEdgesAsCurveLoops();
            if (edgeLoops == null || edgeLoops.Count == 0)
            {
                errorMessage += "Нет контуров граней; ";
                return null;
            }

            CurveLoop outerLoop = null;
            double maxLoopArea = 0;
            foreach (CurveLoop candidate in edgeLoops)
            {
                CurveLoop flatLoop = new CurveLoop();
                foreach (Curve curve in candidate)
                {
                    XYZ p0 = curve.GetEndPoint(0);
                    XYZ p1 = curve.GetEndPoint(1);
                    XYZ p0Flat = new XYZ(p0.X, p0.Y, 0);
                    XYZ p1Flat = new XYZ(p1.X, p1.Y, 0);
                    if (curve is Line)
                        flatLoop.Append(Line.CreateBound(p0Flat, p1Flat));
                    else if (curve is Arc arc)
                    {
                        XYZ center = new XYZ(arc.Center.X, arc.Center.Y, 0);
                        XYZ xDir = new XYZ(arc.XDirection.X, arc.XDirection.Y, 0);
                        XYZ yDir = new XYZ(arc.YDirection.X, arc.YDirection.Y, 0);
                        double startParam = arc.GetEndParameter(0);
                        double endParam = arc.GetEndParameter(1);
                        flatLoop.Append(Arc.Create(center, arc.Radius, startParam, endParam, xDir, yDir));
                    }
                }
                double area = GetCurveLoopArea(flatLoop);
                if (area > maxLoopArea) { maxLoopArea = area; outerLoop = flatLoop; }
            }
            if (outerLoop == null)
                errorMessage += "Не удалось найти внешний контур; ";
            return outerLoop;
        }

        private CurveLoop GetBoundingBoxCurveLoop(List<Room> rooms)
        {
            if (rooms == null || rooms.Count == 0) return null;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (Room room in rooms)
            {
                BoundingBoxXYZ bbox = room.get_BoundingBox(null);
                if (bbox == null) continue;
                minX = Math.Min(minX, bbox.Min.X);
                minY = Math.Min(minY, bbox.Min.Y);
                maxX = Math.Max(maxX, bbox.Max.X);
                maxY = Math.Max(maxY, bbox.Max.Y);
            }

            if (minX == double.MaxValue) return null;

            double pad = 0.3;
            XYZ p1 = new XYZ(minX - pad, minY - pad, 0);
            XYZ p2 = new XYZ(maxX + pad, minY - pad, 0);
            XYZ p3 = new XYZ(maxX + pad, maxY + pad, 0);
            XYZ p4 = new XYZ(minX - pad, maxY + pad, 0);

            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));
            return loop;
        }

        // --------------------------------------------------------------
        // ПРИМЕНЕНИЕ ОБРЕЗКИ, СКРЫТИЕ ОСЕЙ, АННОТАЦИИ
        // --------------------------------------------------------------

        private void ApplyCropToView(ViewPlan view, CurveLoop cropBoundary)
        {
            if (cropBoundary == null || !cropBoundary.Any()) return;
            view.CropBoxActive = true;
            view.CropBoxVisible = true;
            ViewCropRegionShapeManager cropManager = view.GetCropRegionShapeManager();
            if (cropManager.CanHaveShape)
            {
                try
                {
                    cropManager.SetCropShape(cropBoundary);
                    view.CropBoxVisible = false;
                }
                catch
                {
                    BoundingBoxXYZ bounds = GetBoundsFromCurveLoop(cropBoundary);
                    if (bounds != null) { view.CropBox = bounds; view.CropBoxActive = true; view.CropBoxVisible = false; }
                }
            }
            else
            {
                BoundingBoxXYZ bounds = GetBoundsFromCurveLoop(cropBoundary);
                if (bounds != null) { view.CropBox = bounds; view.CropBoxActive = true; view.CropBoxVisible = false; }
            }
        }

        private void HideInternalGrids(Document doc, ViewPlan view, CurveLoop apartmentBoundary)
        {
            try
            {
                List<Grid> visibleGrids = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Grids)
                    .WhereElementIsNotElementType()
                    .Cast<Grid>().ToList();
                List<Grid> intersectingVerticals = new List<Grid>();
                List<Grid> intersectingHorizontals = new List<Grid>();
                foreach (Grid grid in visibleGrids)
                {
                    Line gridLine = grid.Curve as Line;
                    if (gridLine == null) continue;
                    XYZ gP0 = gridLine.GetEndPoint(0);
                    XYZ gP1 = gridLine.GetEndPoint(1);
                    XYZ gDir = (gP1 - gP0).Normalize();
                    XYZ gP0Flat = new XYZ(gP0.X, gP0.Y, 0);
                    XYZ flatDir = new XYZ(gDir.X, gDir.Y, 0).Normalize();
                    Line extendedGridLine = Line.CreateBound(gP0Flat - flatDir * 5000, gP0Flat + flatDir * 5000);
                    bool intersects = false;
                    foreach (Curve bCurve in apartmentBoundary)
                    {
                        if (extendedGridLine.Intersect(bCurve) == SetComparisonResult.Overlap)
                        { intersects = true; break; }
                    }
                    if (intersects)
                    {
                        if (Math.Abs(flatDir.Y) > Math.Abs(flatDir.X))
                            intersectingVerticals.Add(grid);
                        else
                            intersectingHorizontals.Add(grid);
                    }
                }
                List<ElementId> gridsToHide = new List<ElementId>();
                if (intersectingVerticals.Count > 2)
                {
                    var sortedVerts = intersectingVerticals.OrderBy(g => g.Curve.GetEndPoint(0).X).ToList();
                    for (int i = 1; i < sortedVerts.Count - 1; i++) gridsToHide.Add(sortedVerts[i].Id);
                }
                if (intersectingHorizontals.Count > 2)
                {
                    var sortedHoriz = intersectingHorizontals.OrderBy(g => g.Curve.GetEndPoint(0).Y).ToList();
                    for (int i = 1; i < sortedHoriz.Count - 1; i++) gridsToHide.Add(sortedHoriz[i].Id);
                }
                if (gridsToHide.Count > 0) view.HideElements(gridsToHide);
            }
            catch { }
        }

        private void AddApartmentLabel(Document doc, ViewPlan view, Room room, CurveLoop boundary,
                                       string manualCorps, string manualSection, string manualFloor,
                                       string apartmentNumber, ElementId textTypeId, List<string> errorMessages)
        {
            if (room == null || boundary == null || !boundary.Any())
            {
                errorMessages.Add($"Квартира {apartmentNumber}: комната или контур пуст");
                return;
            }
            if (textTypeId == null || textTypeId == ElementId.InvalidElementId)
            {
                errorMessages.Add($"Квартира {apartmentNumber}: не выбран тип текстовой аннотации");
                return;
            }

            TextNoteType textType = doc.GetElement(textTypeId) as TextNoteType;
            if (textType == null)
            {
                errorMessages.Add($"Квартира {apartmentNumber}: тип текста с ID {textTypeId} не найден");
                return;
            }

            string displayName = GetApartmentDisplayName(manualCorps, manualSection, manualFloor, apartmentNumber);
            // Если имя пустое – используем только номер квартиры
            if (string.IsNullOrEmpty(displayName))
                displayName = $"кв. {apartmentNumber}";

            if (string.IsNullOrEmpty(displayName))
            {
                errorMessages.Add($"Квартира {apartmentNumber}: не удалось сформировать имя для аннотации (все поля пусты)");
                return;
            }

            BoundingBoxXYZ bbox = GetBoundsFromCurveLoop(boundary);
            if (bbox == null)
            {
                errorMessages.Add($"Квартира {apartmentNumber}: не удалось получить габариты контура");
                return;
            }

            double centerX = bbox.Min.X+ 2;
            double topY = bbox.Max.Y + 4.0;
            XYZ position = new XYZ(centerX, topY, 0);

            try
            {
                TextNote.Create(doc, view.Id, position, displayName, textType.Id);
            }
            catch (Exception ex)
            {
                errorMessages.Add($"Квартира {apartmentNumber}: ошибка создания текстовой аннотации: {ex.Message}");
            }
        }

        private void AddRoomTags(Document doc, ViewPlan view, List<Room> rooms, ElementId tagSymbolId, List<string> errorMessages)
        {
            if (rooms == null || rooms.Count == 0)
            {
                errorMessages.Add("Список комнат для марок пуст");
                return;
            }
            if (tagSymbolId == null || tagSymbolId == ElementId.InvalidElementId)
            {
                errorMessages.Add("Не выбран тип марок помещений");
                return;
            }

            FamilySymbol tagSymbol = doc.GetElement(tagSymbolId) as FamilySymbol;
            if (tagSymbol == null)
            {
                errorMessages.Add($"Тип марок с ID {tagSymbolId} не найден");
                return;
            }
            if (!tagSymbol.IsActive)
            {
                try { tagSymbol.Activate(); doc.Regenerate(); }
                catch (Exception ex) { errorMessages.Add($"Ошибка активации типа марок: {ex.Message}"); return; }
            }

            foreach (Room room in rooms)
            {
                if (room.Location == null || room.Area <= 0) continue;
                try
                {
                    XYZ center = null;
                    LocationPoint loc = room.Location as LocationPoint;
                    if (loc?.Point != null) center = loc.Point;
                    else
                    {
                        BoundingBoxXYZ bbox = room.get_BoundingBox(view);
                        if (bbox != null)
                            center = new XYZ((bbox.Min.X + bbox.Max.X) / 2, (bbox.Min.Y + bbox.Max.Y) / 2, bbox.Min.Z);
                    }
                    if (center == null) continue;
                    Reference roomRef = new Reference(room);
                    IndependentTag tag = IndependentTag.Create(doc, view.Id, roomRef, true,
                        TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, center);
                    if (tag != null)
                    {
                        tag.HasLeader = false;
                        tag.ChangeTypeId(tagSymbol.Id);
                    }
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"Ошибка создания марки для комнаты {room.Number}: {ex.Message}");
                }
            }
        }

        // --------------------------------------------------------------
        // ВСПОМОГАТЕЛЬНЫЕ ГЕОМЕТРИЧЕСКИЕ ФУНКЦИИ
        // --------------------------------------------------------------

        private bool PointsAreEqual(XYZ a, XYZ b) =>
            Math.Abs(a.X - b.X) < Eps && Math.Abs(a.Y - b.Y) < Eps && Math.Abs(a.Z - b.Z) < Eps;

        private double GetCurveLoopArea(CurveLoop loop)
        {
            if (loop == null || !loop.Any()) return 0;
            List<XYZ> pts = new List<XYZ>();
            foreach (Curve c in loop)
            {
                pts.Add(c.GetEndPoint(0));
                if (!(c is Line))
                {
                    double step = 1.0 / 10;
                    for (int i = 1; i < 10; i++)
                    {
                        double param = c.GetEndParameter(0) + step * i * (c.GetEndParameter(1) - c.GetEndParameter(0));
                        pts.Add(c.Evaluate(param, true));
                    }
                }
            }
            pts.Add(loop.Last().GetEndPoint(1));
            var distinct = pts.Distinct(new XYZComparer()).ToList();
            if (distinct.Count < 3) return 0;
            double area = 0;
            for (int i = 0; i < distinct.Count - 1; i++)
                area += distinct[i].X * distinct[i + 1].Y - distinct[i + 1].X * distinct[i].Y;
            return Math.Abs(area) / 2.0;
        }

        private class XYZComparer : IEqualityComparer<XYZ>
        {
            private double eps = 0.01;
            public bool Equals(XYZ a, XYZ b) =>
                Math.Abs(a.X - b.X) < eps && Math.Abs(a.Y - b.Y) < eps && Math.Abs(a.Z - b.Z) < eps;
            public int GetHashCode(XYZ obj)
            {
                int x = (int)Math.Round(obj.X / eps);
                int y = (int)Math.Round(obj.Y / eps);
                int z = (int)Math.Round(obj.Z / eps);
                return x ^ y ^ z;
            }
        }

        private BoundingBoxXYZ GetBoundsFromCurveLoop(CurveLoop loop)
        {
            if (loop == null || !loop.Any()) return null;
            List<XYZ> points = new List<XYZ>();
            foreach (Curve curve in loop)
            {
                points.Add(curve.GetEndPoint(0));
                points.Add(curve.GetEndPoint(1));
            }
            if (points.Count == 0) return null;
            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X);
            double maxY = points.Max(p => p.Y);
            BoundingBoxXYZ bounds = new BoundingBoxXYZ();
            bounds.Min = new XYZ(minX, minY, -100);
            bounds.Max = new XYZ(maxX, maxY, 100);
            return bounds;
        }
    }

    // --------------------------------------------------------------
    // ФОРМА НАСТРОЕК
    // --------------------------------------------------------------

    public class ApartmentPlanSettingsDialog : Form
    {
        private ComboBox cmbParameters;
        private ComboBox cmbTextTypes;
        private ComboBox cmbTagTypes;
        private ComboBox cmbViewTemplates;
        private TextBox txtCorps, txtSection, txtFloor;
        private Button btnOk, btnCancel;

        public string SelectedParameterName { get; private set; }
        public string CorpsNumber { get; private set; }
        public string SectionNumber { get; private set; }
        public string FloorNumber { get; private set; }
        public ElementId SelectedTextTypeId { get; private set; }
        public ElementId SelectedTagTypeId { get; private set; }
        public ElementId SelectedTemplateId { get; private set; }

        public ApartmentPlanSettingsDialog(List<string> availableParameters, Document doc)
        {
            InitializeComponent();

            cmbParameters.Items.AddRange(availableParameters.ToArray());
            if (cmbParameters.Items.Count > 0) cmbParameters.SelectedIndex = 0;

            var textTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();
            cmbTextTypes.Items.Clear();
            cmbTextTypes.DisplayMember = "Name";
            foreach (var tt in textTypes) cmbTextTypes.Items.Add(tt);
            if (cmbTextTypes.Items.Count > 0)
            {
                var defaultText = textTypes.FirstOrDefault(t => t.Name == "ADSK_Основной текст_5");
                cmbTextTypes.SelectedItem = defaultText ?? textTypes.First();
            }

            var tagSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .Cast<FamilySymbol>()
                .ToList();
            cmbTagTypes.Items.Clear();
            cmbTagTypes.DisplayMember = "Name";
            foreach (var ts in tagSymbols) cmbTagTypes.Items.Add(ts);
            if (cmbTagTypes.Items.Count > 0) cmbTagTypes.SelectedIndex = 0;

            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                .ToList();
            cmbViewTemplates.Items.Clear();
            cmbViewTemplates.DisplayMember = "Name";
            cmbViewTemplates.Items.Add("(Без шаблона)");
            foreach (var t in templates) cmbViewTemplates.Items.Add(t);
            cmbViewTemplates.SelectedIndex = 0;

            var settings = LoadSettings();
            if (settings != null)
            {
                if (!string.IsNullOrEmpty(settings.SelectedParameterName))
                {
                    int idx = cmbParameters.Items.IndexOf(settings.SelectedParameterName);
                    if (idx >= 0) cmbParameters.SelectedIndex = idx;
                }
                txtCorps.Text = settings.CorpsNumber ?? "";
                txtSection.Text = settings.SectionNumber ?? "";
                txtFloor.Text = settings.FloorNumber ?? "";

                if (!string.IsNullOrEmpty(settings.TextTypeName))
                {
                    var item = cmbTextTypes.Items.Cast<TextNoteType>().FirstOrDefault(tt => tt.Name == settings.TextTypeName);
                    if (item != null) cmbTextTypes.SelectedItem = item;
                }

                if (!string.IsNullOrEmpty(settings.TagTypeName))
                {
                    var item = cmbTagTypes.Items.Cast<FamilySymbol>().FirstOrDefault(ts => ts.Name == settings.TagTypeName);
                    if (item != null) cmbTagTypes.SelectedItem = item;
                }

                if (!string.IsNullOrEmpty(settings.TemplateName))
                {
                    if (settings.TemplateName == "(Без шаблона)")
                        cmbViewTemplates.SelectedIndex = 0;
                    else
                    {
                        for (int i = 1; i < cmbViewTemplates.Items.Count; i++)
                        {
                            if (cmbViewTemplates.Items[i] is Autodesk.Revit.DB.View v && v.Name == settings.TemplateName)
                            {
                                cmbViewTemplates.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void InitializeComponent()
        {
            this.Text = "Настройки планов квартир";
            this.Width = 1200;
            this.Height = 700;
            this.MinimumSize = new System.Drawing.Size(900, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.AutoScaleMode = AutoScaleMode.Dpi;

            int y = 10;
            int labelWidth = 500;
            int controlLeft = 530;
            int controlWidth = this.Width - controlLeft - 40;
            int spacing = 55;

            Label lblParam = new Label
            {
                Text = "Параметр для номера квартиры:",
                Left = 10,
                Top = y,
                Width = labelWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            cmbParameters = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = controlLeft,
                Top = y,
                Width = controlWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            y += spacing;

            Label lblTextType = new Label
            {
                Text = "Тип текстовой аннотации:",
                Left = 10,
                Top = y,
                Width = labelWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            cmbTextTypes = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = controlLeft,
                Top = y,
                Width = controlWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            y += spacing;

            Label lblTagType = new Label
            {
                Text = "Тип марок помещений:",
                Left = 10,
                Top = y,
                Width = labelWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            cmbTagTypes = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = controlLeft,
                Top = y,
                Width = controlWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            y += spacing;

            Label lblTemplate = new Label
            {
                Text = "Шаблон вида (план этажа):",
                Left = 10,
                Top = y,
                Width = labelWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            cmbViewTemplates = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = controlLeft,
                Top = y,
                Width = controlWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            y += spacing + 10;

            Label lblCorps = new Label
            {
                Text = "Корпус:",
                Left = 10,
                Top = y,
                Width = labelWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            txtCorps = new TextBox
            {
                Left = controlLeft,
                Top = y,
                Width = controlWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            y += spacing;

            Label lblSection = new Label
            {
                Text = "Секция:",
                Left = 10,
                Top = y,
                Width = labelWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            txtSection = new TextBox
            {
                Left = controlLeft,
                Top = y,
                Width = controlWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            y += spacing;

            Label lblFloor = new Label
            {
                Text = "Этаж:",
                Left = 10,
                Top = y,
                Width = labelWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };
            txtFloor = new TextBox
            {
                Left = controlLeft,
                Top = y,
                Width = controlWidth,
                Height = 40,
                Font = this.Font,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            y += spacing + 20;

            System.Windows.Forms.Panel buttonPanel = new System.Windows.Forms.Panel
            {
                Left = (this.Width - 260) / 2,
                Top = y,
                Width = 260,
                Height = 50,
                Anchor = AnchorStyles.Top | AnchorStyles.None
            };
            this.Resize += (s, e) => buttonPanel.Left = (this.ClientSize.Width - buttonPanel.Width) / 2;

            btnOk = new Button
            {
                Text = "OK",
                Left = 10,
                Top = 5,
                Width = 120,
                Height = 40,
                Font = this.Font,
                DialogResult = DialogResult.OK
            };
            btnCancel = new Button
            {
                Text = "Отмена",
                Left = 140,
                Top = 5,
                Width = 120,
                Height = 40,
                Font = this.Font,
                DialogResult = DialogResult.Cancel
            };

            buttonPanel.Controls.Add(btnOk);
            buttonPanel.Controls.Add(btnCancel);

            btnOk.Click += (s, e) =>
            {
                if (cmbParameters.SelectedItem == null)
                {
                    MessageBox.Show("Выберите параметр.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }

                SelectedParameterName = cmbParameters.SelectedItem.ToString();
                CorpsNumber = txtCorps.Text.Trim();
                SectionNumber = txtSection.Text.Trim();
                FloorNumber = txtFloor.Text.Trim();
                SelectedTextTypeId = (cmbTextTypes.SelectedItem as TextNoteType)?.Id ?? ElementId.InvalidElementId;
                SelectedTagTypeId = (cmbTagTypes.SelectedItem as FamilySymbol)?.Id ?? ElementId.InvalidElementId;

                if (cmbViewTemplates.SelectedIndex == 0)
                    SelectedTemplateId = ElementId.InvalidElementId;
                else
                    SelectedTemplateId = (cmbViewTemplates.SelectedItem as Autodesk.Revit.DB.View)?.Id ?? ElementId.InvalidElementId;

                var settings = new ApartmentPlanUserSettings
                {
                    SelectedParameterName = SelectedParameterName,
                    CorpsNumber = CorpsNumber,
                    SectionNumber = SectionNumber,
                    FloorNumber = FloorNumber,
                    TextTypeName = (cmbTextTypes.SelectedItem as TextNoteType)?.Name ?? "",
                    TagTypeName = (cmbTagTypes.SelectedItem as FamilySymbol)?.Name ?? "",
                    TemplateName = cmbViewTemplates.SelectedIndex == 0 ? "(Без шаблона)" : (cmbViewTemplates.SelectedItem as Autodesk.Revit.DB.View)?.Name ?? ""
                };
                SaveSettings(settings);
            };

            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                lblParam, cmbParameters,
                lblTextType, cmbTextTypes,
                lblTagType, cmbTagTypes,
                lblTemplate, cmbViewTemplates,
                lblCorps, txtCorps,
                lblSection, txtSection,
                lblFloor, txtFloor,
                buttonPanel
            });
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        // ------------------------------------------------------------
        // Сохранение и загрузка настроек
        // ------------------------------------------------------------
        private static string GetSettingsFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "BIMv2");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Path.Combine(folder, "ApartmentPlanSettings.xml");
        }

        private static ApartmentPlanUserSettings LoadSettings()
        {
            string file = GetSettingsFilePath();
            if (File.Exists(file))
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ApartmentPlanUserSettings));
                    using (FileStream fs = new FileStream(file, FileMode.Open))
                        return (ApartmentPlanUserSettings)serializer.Deserialize(fs);
                }
                catch { return null; }
            }
            return null;
        }

        private static void SaveSettings(ApartmentPlanUserSettings settings)
        {
            string file = GetSettingsFilePath();
            XmlSerializer serializer = new XmlSerializer(typeof(ApartmentPlanUserSettings));
            using (FileStream fs = new FileStream(file, FileMode.Create))
                serializer.Serialize(fs, settings);
        }
    }

    [Serializable]
    public class ApartmentPlanUserSettings
    {
        public string SelectedParameterName { get; set; }
        public string CorpsNumber { get; set; }
        public string SectionNumber { get; set; }
        public string FloorNumber { get; set; }
        public string TextTypeName { get; set; }
        public string TagTypeName { get; set; }
        public string TemplateName { get; set; }
    }
}