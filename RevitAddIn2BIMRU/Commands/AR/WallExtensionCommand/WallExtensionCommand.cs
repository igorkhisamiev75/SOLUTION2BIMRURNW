using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using ComboBox = System.Windows.Forms.ComboBox;

namespace RevitAddIn2BIMRU.Commands.AR
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WallExtensionCommand : IExternalCommand
    {
        // Коэффициент преобразования футы -> мм
        private const double FeetToMm = 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // Собираем все типы стен в проекте
                FilteredElementCollector wallTypeCollector = new FilteredElementCollector(doc);
                ICollection<Element> wallTypes = wallTypeCollector.OfClass(typeof(WallType)).ToElements();

                if (wallTypes.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "В проекте не найдены типы стен");
                    return Result.Failed;
                }

                // Собираем все уровни в проекте
                FilteredElementCollector levelCollector = new FilteredElementCollector(doc);
                ICollection<Element> levels = levelCollector.OfClass(typeof(Level)).ToElements();

                if (levels.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "В проекте не найдены уровни");
                    return Result.Failed;
                }

                // Открываем формы для выбора параметров
                using (var form = new WallExtensionForm(wallTypes, levels, doc))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        // Получаем выбранные параметры из формы
                        WallType selectedBaseWallType = form.SelectedBaseWallType;
                        WallType selectedNewWallType = form.SelectedNewWallType;
                        Level selectedLevel = form.SelectedLevel;

                        // Выполняем создание новых стен
                        CreateExtendedWalls(doc, selectedBaseWallType, selectedNewWallType, selectedLevel);

                        TaskDialog.Show("Успех", "Новые стены успешно созданы");
                        return Result.Succeeded;
                    }
                }

                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", $"Произошла ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        private void CreateExtendedWalls(Document doc, WallType baseWallType, WallType newWallType, Level baseLevel)
        {
            // Находим все стены выбранного типа на выбранном уровне
            FilteredElementCollector wallCollector = new FilteredElementCollector(doc);
            var walls = wallCollector
                .OfClass(typeof(Wall))
                .Where(w => w.GetTypeId() == baseWallType.Id && GetWallLevel(w as Wall)?.Id == baseLevel.Id)
                .Cast<Wall>();

            if (!walls.Any())
            {
                throw new Exception($"На уровне '{baseLevel.Name}' не найдены стены типа '{baseWallType.Name}'");
            }

            int createdWallsCount = 0;
            int skippedWallsCount = 0;
            List<string> skippedWallsInfo = new List<string>();

            using (Transaction trans = new Transaction(doc, "Create Extended Walls"))
            {
                trans.Start();

                foreach (Wall baseWall in walls)
                {
                    var result = CreateExtendedWall(doc, baseWall, newWallType, baseLevel);
                    if (result.Success)
                    {
                        createdWallsCount++;
                    }
                    else
                    {
                        skippedWallsCount++;
                        if (!string.IsNullOrEmpty(result.Message))
                        {
                            skippedWallsInfo.Add(result.Message);
                        }
                    }
                }

                trans.Commit();
            }

            // Показываем результат с информацией о пропущенных стенах
            string resultMessage = $"Создано {createdWallsCount} новых стен";

            if (skippedWallsCount > 0)
            {
                resultMessage += $"\nПропущено {skippedWallsCount} стен:";
                if (skippedWallsInfo.Count > 0)
                {
                    resultMessage += $"\n- {string.Join("\n- ", skippedWallsInfo.Take(5))}";
                    if (skippedWallsInfo.Count > 5)
                    {
                        resultMessage += $"\n- ... и еще {skippedWallsInfo.Count - 5} стен";
                    }
                }
            }

            TaskDialog.Show("Результат", resultMessage);
        }

        private (bool Success, string Message) CreateExtendedWall(Document doc, Wall baseWall, WallType newWallType, Level baseLevel)
        {
            try
            {
                // Получаем геометрию базовой стены
                LocationCurve locationCurve = baseWall.Location as LocationCurve;
                if (locationCurve == null)
                    return (false, "Стена не имеет LocationCurve");

                Curve wallCurve = locationCurve.Curve;

                // Получаем высоту базовой стены
                double baseWallHeight = GetWallUnconnectedHeight(baseWall);

                // Получаем базовое смещение базовой стены
                double baseWallBaseOffset = GetWallBaseOffset(baseWall);

                // Получаем отметку верха базовой стены
                double baseWallTopElevation = GetWallTopElevation(baseWall);

                // Ищем перекрытие над стеной
                Floor floorAbove = FindFloorAboveWall(doc, baseWall);

                if (floorAbove == null)
                {
                    return (false, "Не найдено перекрытие над стеной");
                }

                // Получаем отметку низа перекрытия
                double floorBottomElevation = GetFloorBottomElevation(floorAbove);

                // Проверяем, что перекрытие выше верха стены
                if (floorBottomElevation <= baseWallTopElevation)
                {
                    double difference = (baseWallTopElevation - floorBottomElevation) * FeetToMm;
                    return (false, $"Перекрытие ниже или на том же уровне, что и верх стены. Разница: {Math.Round(difference, 1)} мм");
                }

                // Вычисляем высоту новой стены (от верха базовой стены до низа перекрытия)
                double newWallHeight = floorBottomElevation - baseWallTopElevation;
                double newWallHeightMm = newWallHeight * FeetToMm;

                // Проверяем минимальную допустимую высоту стены (100 мм)
                const double minWallHeightMm = 100.0;
                if (newWallHeightMm < minWallHeightMm)
                {
                    return (false, $"Высота новой стены слишком мала: {Math.Round(newWallHeightMm, 1)} мм (минимум {minWallHeightMm} мм)");
                }

                // ВЫЧИСЛЯЕМ БАЗОВОЕ СМЕЩЕНИЕ ДЛЯ НОВОЙ СТЕНЫ
                // Высота базовой стены + ее базовое смещение = общее смещение от уровня
                double newWallBaseOffset = baseWallHeight + baseWallBaseOffset;

                // Создаем новую стену
                Wall newWall = null;

                if (wallCurve is Line)
                {
                    Line line = wallCurve as Line;
                    // Создаем стену с рассчитанным базовым смещением
                    newWall = Wall.Create(doc, line, newWallType.Id, baseLevel.Id, newWallBaseOffset, newWallHeight, false, false);
                }
                else if (wallCurve is Arc)
                {
                    Arc arc = wallCurve as Arc;
                    newWall = Wall.Create(doc, arc, newWallType.Id, baseLevel.Id, newWallBaseOffset, newWallHeight, false, false);
                }
                else
                {
                    // Для сложных кривых создаем упрощенную версию
                    XYZ startPoint = wallCurve.GetEndPoint(0);
                    XYZ endPoint = wallCurve.GetEndPoint(1);

                    // Проверяем длину сегмента (минимум 100 мм)
                    double segmentLength = startPoint.DistanceTo(endPoint);
                    double segmentLengthMm = segmentLength * FeetToMm;
                    const double minSegmentLengthMm = 100.0;

                    if (segmentLengthMm < minSegmentLengthMm)
                    {
                        return (false, $"Длина сегмента стены слишком мала: {Math.Round(segmentLengthMm, 1)} мм");
                    }

                    Line simplifiedLine = Line.CreateBound(startPoint, endPoint);
                    newWall = Wall.Create(doc, simplifiedLine, newWallType.Id, baseLevel.Id, newWallBaseOffset, newWallHeight, false, false);
                }

                if (newWall != null)
                {
                    // ПРОВЕРЯЕМ И КОРРЕКТИРУЕМ БАЗОВОЕ СМЕЩЕНИЕ
                    Parameter baseOffsetParam = newWall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                    if (baseOffsetParam != null && !baseOffsetParam.IsReadOnly)
                    {
                        double currentOffset = baseOffsetParam.AsDouble();
                        if (Math.Abs(currentOffset - newWallBaseOffset) > 0.001)
                        {
                            baseOffsetParam.Set(newWallBaseOffset);
                        }
                    }

                    // Присоединяем стену к перекрытию
                    AttachWallToFloor(doc, newWall, floorAbove);

                    // Устанавливаем верхнее ограничение как несвязанное
                    SetWallTopUnconnected(newWall, newWallHeight);

                    return (true, null);
                }

                return (false, "Не удалось создать стену");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка создания: {ex.Message}");
            }
        }

        private double GetWallBaseOffset(Wall wall)
        {
            // Получаем базовое смещение стены
            Parameter baseOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            if (baseOffsetParam != null && baseOffsetParam.HasValue)
            {
                return baseOffsetParam.AsDouble();
            }
            return 0.0;
        }

        private Floor FindFloorAboveWall(Document doc, Wall wall)
        {
            try
            {
                // Получаем BoundingBox стены
                BoundingBoxXYZ wallBBox = wall.get_BoundingBox(null);
                if (wallBBox == null)
                    return null;

                // Расширяем BoundingBox на 2 метра вверх для поиска перекрытия
                double searchHeight = 2.0 / 0.3048; // 2 метра в футах
                XYZ minPoint = wallBBox.Min;
                XYZ maxPoint = new XYZ(wallBBox.Max.X, wallBBox.Max.Y, wallBBox.Max.Z + searchHeight);

                // Создаем Outline для поиска
                Outline outline = new Outline(minPoint, maxPoint);
                BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(outline);

                // Находим все перекрытия, пересекающиеся с расширенным BoundingBox
                var floors = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .WherePasses(bboxFilter)
                    .Cast<Floor>()
                    .Where(f => IsFloorAboveWall(f, wall))
                    .OrderBy(f => GetFloorBottomElevation(f)) // Сортируем по ближайшему снизу
                    .ToList();

                return floors.FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка поиска перекрытия: {ex.Message}");
                return null;
            }
        }

        private bool IsFloorAboveWall(Floor floor, Wall wall)
        {
            try
            {
                // Проверяем, что перекрытие находится над стеной
                BoundingBoxXYZ floorBBox = floor.get_BoundingBox(null);
                BoundingBoxXYZ wallBBox = wall.get_BoundingBox(null);

                if (floorBBox == null || wallBBox == null)
                    return false;

                // Проверяем пересечение по X и Y
                bool xIntersection = !(floorBBox.Max.X < wallBBox.Min.X || floorBBox.Min.X > wallBBox.Max.X);
                bool yIntersection = !(floorBBox.Max.Y < wallBBox.Min.Y || floorBBox.Min.Y > wallBBox.Max.Y);

                // Проверяем, что перекрытие выше стены
                bool isAbove = floorBBox.Min.Z > wallBBox.Max.Z;

                return xIntersection && yIntersection && isAbove;
            }
            catch
            {
                return false;
            }
        }

        private void AttachWallToFloor(Document doc, Wall wall, Floor floor)
        {
            try
            {
                // Присоединяем стену к перекрытию
                JoinGeometryUtils.JoinGeometry(doc, wall, floor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка присоединения стены к перекрытию: {ex.Message}");
                // Продолжаем выполнение даже если не удалось присоединить
            }
        }

        private double GetFloorBottomElevation(Floor floor)
        {
            try
            {
                // Пытаемся получить BoundingBox перекрытия
                BoundingBoxXYZ bbox = floor.get_BoundingBox(null);
                if (bbox != null)
                {
                    return bbox.Min.Z;
                }
            }
            catch
            {
                // Игнорируем ошибки BoundingBox
            }

            // Альтернативный способ через уровень и смещение
            try
            {
                Parameter levelParam = floor.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                Parameter offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);

                if (levelParam != null && levelParam.HasValue && offsetParam != null && offsetParam.HasValue)
                {
                    ElementId levelId = levelParam.AsElementId();
                    if (levelId != ElementId.InvalidElementId)
                    {
                        Level level = floor.Document.GetElement(levelId) as Level;
                        if (level != null)
                        {
                            return level.Elevation + offsetParam.AsDouble();
                        }
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки параметров
            }

            // Fallback: через геометрию
            try
            {
                Options options = new Options();
                options.ComputeReferences = true;
                options.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geom = floor.get_Geometry(options);
                if (geom != null)
                {
                    double minZ = double.MaxValue;

                    foreach (GeometryObject obj in geom)
                    {
                        if (obj is Solid solid && solid.Faces.Size > 0)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                try
                                {
                                    // Получаем BoundingBoxUV грани
                                    BoundingBoxUV bboxUV = face.GetBoundingBox();

                                    // Берем среднюю точку UV для оценки
                                    UV midPointUV = new UV(
                                        (bboxUV.Min.U + bboxUV.Max.U) / 2,
                                        (bboxUV.Min.V + bboxUV.Max.V) / 2);

                                    // Оцениваем точку на поверхности грани
                                    XYZ pointOnFace = face.Evaluate(midPointUV);

                                    if (pointOnFace != null && pointOnFace.Z < minZ)
                                    {
                                        minZ = pointOnFace.Z;
                                    }
                                }
                                catch
                                {
                                    // Пропускаем ошибки на отдельных гранях
                                }
                            }
                        }
                    }

                    if (minZ < double.MaxValue)
                    {
                        return minZ;
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки геометрии
            }

            // Если ничего не сработало, пытаемся получить отметку из уровня перекрытия
            try
            {
                Level floorLevel = GetElementLevel(floor);
                if (floorLevel != null)
                {
                    return floorLevel.Elevation;
                }
            }
            catch
            {
                // Игнорируем ошибки
            }

            return 0.0;
        }

        private void SetWallTopUnconnected(Wall wall, double height)
        {
            try
            {
                // Устанавливаем верхнее ограничение на "Несвязанное"
                Parameter topConstraintParam = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                if (topConstraintParam != null && !topConstraintParam.IsReadOnly)
                {
                    topConstraintParam.Set(ElementId.InvalidElementId);
                }

                // Устанавливаем высоту стены
                Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                if (heightParam != null && !heightParam.IsReadOnly)
                {
                    heightParam.Set(height);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка установки параметров стены: {ex.Message}");
            }
        }

        private Level GetElementLevel(Element element)
        {
            Parameter levelParam = element.get_Parameter(BuiltInParameter.LEVEL_PARAM);
            if (levelParam != null && levelParam.HasValue)
            {
                ElementId levelId = levelParam.AsElementId();
                if (levelId != ElementId.InvalidElementId)
                {
                    return element.Document.GetElement(levelId) as Level;
                }
            }

            // Альтернативный способ для стен
            if (element is Wall wall)
            {
                return GetWallLevel(wall);
            }

            return null;
        }

        private double GetWallUnconnectedHeight(Wall wall)
        {
            // Получаем высоту стены через параметр WALL_USER_HEIGHT_PARAM
            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (heightParam != null && heightParam.HasValue)
            {
                return heightParam.AsDouble();
            }

            // Вычисляем разницу между верхом и низом
            return GetWallTopElevation(wall) - GetWallBaseElevation(wall);
        }

        private double GetWallBaseElevation(Wall wall)
        {
            Level baseLevel = GetWallLevel(wall);
            if (baseLevel != null)
            {
                // Получаем базовое смещение
                Parameter baseOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (baseOffsetParam != null && baseOffsetParam.HasValue)
                {
                    return baseLevel.Elevation + baseOffsetParam.AsDouble();
                }
                return baseLevel.Elevation;
            }
            return 0.0;
        }

        private double GetWallTopElevation(Wall wall)
        {
            // Способ 1: через параметр высоты
            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (heightParam != null && heightParam.HasValue)
            {
                double baseElevation = GetWallBaseElevation(wall);
                return baseElevation + heightParam.AsDouble();
            }

            // Способ 2: через ограничение верха
            Parameter topConstraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
            if (topConstraint != null && topConstraint.HasValue)
            {
                ElementId topLevelId = topConstraint.AsElementId();
                if (topLevelId != ElementId.InvalidElementId)
                {
                    Level topLevel = wall.Document.GetElement(topLevelId) as Level;
                    if (topLevel != null)
                    {
                        return topLevel.Elevation;
                    }
                }
            }

            // Способ 3: через BoundingBox
            BoundingBoxXYZ bbox = wall.get_BoundingBox(null);
            if (bbox != null)
            {
                return bbox.Max.Z;
            }

            // Способ 4: через LocationCurve для линейных стен
            LocationCurve locationCurve = wall.Location as LocationCurve;
            if (locationCurve != null)
            {
                Curve curve = locationCurve.Curve;
                XYZ startPoint = curve.GetEndPoint(0);
                XYZ endPoint = curve.GetEndPoint(1);

                // Берем максимальную Z-координату из точек кривой
                double maxZ = Math.Max(startPoint.Z, endPoint.Z);

                // Добавляем половину толщины стены (предполагаем вертикальную стену)
                WallType wallType = wall.Document.GetElement(wall.GetTypeId()) as WallType;
                if (wallType != null)
                {
                    Parameter widthParam = wallType.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
                    if (widthParam != null && widthParam.HasValue)
                    {
                        maxZ += widthParam.AsDouble() / 2;
                    }
                }

                return maxZ;
            }

            throw new Exception("Не удалось определить высоту стены");
        }

        private Level GetWallLevel(Wall wall)
        {
            Parameter baseConstraint = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            if (baseConstraint != null && baseConstraint.HasValue)
            {
                ElementId levelId = baseConstraint.AsElementId();
                if (levelId != ElementId.InvalidElementId)
                {
                    return wall.Document.GetElement(levelId) as Level;
                }
            }
            return null;
        }
    }

    public partial class WallExtensionForm : System.Windows.Forms.Form
    {
        public WallType SelectedBaseWallType { get; private set; }
        public WallType SelectedNewWallType { get; private set; }
        public Level SelectedLevel { get; private set; }

        private Document _document;
        private List<WallType> _wallTypes;
        private List<Level> _levels;

        public WallExtensionForm(ICollection<Element> wallTypes, ICollection<Element> levels, Document doc)
        {
            InitializeComponent();
            _document = doc;
            InitializeData(wallTypes, levels);
        }

        private void InitializeData(ICollection<Element> wallTypes, ICollection<Element> levels)
        {
            // Преобразуем элементы в типы стен
            _wallTypes = wallTypes.Cast<WallType>().ToList();
            _levels = levels.Cast<Level>().OrderBy(l => l.Elevation).ToList();

            // Заполняем комбобоксы
            comboBaseWallType.DataSource = _wallTypes;
            comboBaseWallType.DisplayMember = "Name";

            comboNewWallType.DataSource = _wallTypes.ToList(); // Копируем список
            comboNewWallType.DisplayMember = "Name";

            // Устанавливаем тип стены по умолчанию для новой стены
            SetDefaultNewWallType();

            // Показываем отметки уровней в мм для удобства
            DisplayLevelsInMm();
        }

        private void DisplayLevelsInMm()
        {
            // Обновляем отображение уровней с отметками в мм
            comboLevel.DisplayMember = null;
            comboLevel.DataSource = _levels.Select(l => new
            {
                Level = l,
                Display = $"{l.Name} ({Math.Round(l.Elevation * 304.8, 0)} мм)"
            }).ToList();
            comboLevel.DisplayMember = "Display";
            comboLevel.ValueMember = "Level";
        }

        private void SetDefaultNewWallType()
        {
            var defaultWallType = _wallTypes.FirstOrDefault(w => w.Name == "АР_В_Сетка-1");
            if (defaultWallType != null)
            {
                comboNewWallType.SelectedItem = defaultWallType;
            }
            else
            {
                // Если тип по умолчанию не найден, выбираем первый доступный
                comboNewWallType.SelectedIndex = 0;
            }
        }

        private void InitializeComponent()
        {
            this.label1 = new Label();
            this.comboBaseWallType = new ComboBox();
            this.label2 = new Label();
            this.comboNewWallType = new ComboBox();
            this.label3 = new Label();
            this.comboLevel = new ComboBox();
            this.btnOk = new Button();
            this.btnCancel = new Button();
            this.lblInfo = new Label();
            this.SuspendLayout();

            // label1
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 15);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(150, 13);
            this.label1.Text = "Тип стены для расширения:";

            // comboBaseWallType
            this.comboBaseWallType.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBaseWallType.FormattingEnabled = true;
            this.comboBaseWallType.Location = new System.Drawing.Point(168, 12);
            this.comboBaseWallType.Name = "comboBaseWallType";
            this.comboBaseWallType.Size = new System.Drawing.Size(300, 21);
            this.comboBaseWallType.TabIndex = 0;

            // label2
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 45);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(118, 13);
            this.label2.Text = "Тип новой стены:";

            // comboNewWallType
            this.comboNewWallType.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboNewWallType.FormattingEnabled = true;
            this.comboNewWallType.Location = new System.Drawing.Point(168, 42);
            this.comboNewWallType.Name = "comboNewWallType";
            this.comboNewWallType.Size = new System.Drawing.Size(300, 21);
            this.comboNewWallType.TabIndex = 1;

            // label3
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 75);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(122, 13);
            this.label3.Text = "Базовый уровень:";

            // comboLevel
            this.comboLevel.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboLevel.FormattingEnabled = true;
            this.comboLevel.Location = new System.Drawing.Point(168, 72);
            this.comboLevel.Name = "comboLevel";
            this.comboLevel.Size = new System.Drawing.Size(300, 21);
            this.comboLevel.TabIndex = 2;

            // lblInfo
            this.lblInfo.AutoSize = true;
            this.lblInfo.Location = new System.Drawing.Point(12, 105);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(456, 39);
            this.lblInfo.Text = "Новая стена будет создана от верха выбранных стен до низа перекрытия над ними.\r\nПрограмма найдет ближайшее перекрытие над каждой стеной и установит высоту новой стены до его низа.\r\nСтена будет присоединена к перекрытию.";
            this.lblInfo.ForeColor = System.Drawing.Color.Blue;

            // btnOk
            this.btnOk.DialogResult = DialogResult.OK;
            this.btnOk.Location = new System.Drawing.Point(312, 150);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(75, 23);
            this.btnOk.TabIndex = 3;
            this.btnOk.Text = "Создать";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new EventHandler(this.btnOk_Click);

            // btnCancel
            this.btnCancel.DialogResult = DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(393, 150);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 4;
            this.btnCancel.Text = "Отмена";
            this.btnCancel.UseVisualStyleBackColor = true;

            // Form
            this.AcceptButton = this.btnOk;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(480, 185);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.lblInfo);
            this.Controls.Add(this.comboLevel);
            this.Controls.Add(this.comboNewWallType);
            this.Controls.Add(this.comboBaseWallType);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "WallExtensionForm";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "Создание расширенных стен";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            // Устанавливаем выбранные значения
            if (comboLevel.SelectedValue is Level selectedLevel)
            {
                SelectedLevel = selectedLevel;
            }
            else if (comboLevel.SelectedItem != null)
            {
                var selectedItem = comboLevel.SelectedItem;
                var propertyInfo = selectedItem.GetType().GetProperty("Level");
                if (propertyInfo != null)
                {
                    SelectedLevel = propertyInfo.GetValue(selectedItem) as Level;
                }
            }

            SelectedBaseWallType = comboBaseWallType.SelectedItem as WallType;
            SelectedNewWallType = comboNewWallType.SelectedItem as WallType;

            if (SelectedBaseWallType == null || SelectedNewWallType == null || SelectedLevel == null)
            {
                MessageBox.Show("Пожалуйста, выберите все параметры", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.None;
            }
        }

        private Label label1;
        private ComboBox comboBaseWallType;
        private Label label2;
        private ComboBox comboNewWallType;
        private Label label3;
        private ComboBox comboLevel;
        private Button btnOk;
        private Button btnCancel;
        private Label lblInfo;
    }
}