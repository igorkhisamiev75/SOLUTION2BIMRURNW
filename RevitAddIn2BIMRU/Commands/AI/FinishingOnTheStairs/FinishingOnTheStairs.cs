#region Namespaces
using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;


#endregion

namespace RevitAddIn2BIMRU.Commands.AI
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]

    public class FinishingOnTheStairs : IExternalCommand
    {
        
        Document _doc;
        private const double PRECISION = 0.00000001;
        private CurveArray profile;
        //private Hashtable floorTypes;
        private List<string> floorTypesName;
        public FloorType floorType;
        private Level level;
        private bool structural;
        private Autodesk.Revit.Creation.Application creApp;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {

            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
          
            _doc = uiDoc.Document;
            creApp = commandData.Application.Application.Create;

            using (Transaction t = new Transaction(_doc))
            {
                t.Start("Создать отделку на лестнице");

                //выбор лестницы

                Stairs str = SelectedElement(_doc);

                FloorType floorType = GetFloorTypeInProject("0");
#if R2019 || R2020 || R2021 || R2022
#else
            ElementId floorTypeId = Floor.GetDefaultFloorType(_doc, false);
#endif


                //Stairs str =GetStairInfo(_doc);

                ElementId lv = str.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM).AsElementId();

                //смещение лестницы снизу
                double ofsetStair = str.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET).AsDouble();

                //id лестницы

                string idStairs = str.Id.ToString();


                CurveArray temp = new CurveArray();

                CurveArray tempStair = new CurveArray();

                Level level = (Level)_doc.GetElement(lv);

                ICollection<ElementId> landings = str.GetStairsLandings();
                ICollection<ElementId> runs = str.GetStairsRuns();

                double elevation = 0;

                //создаем перекрытия на каждой площадке
                foreach (ElementId landing in landings)
                {
                    StairsLanding stairsL = _doc.GetElement(landing) as StairsLanding;
                    var cl = stairsL.GetFootprintBoundary();

                    elevation = stairsL.BaseElevation;
                    CurveLoop profile = new CurveLoop();

                    foreach (var item in cl)
                    {
                        temp.Append(item);
                        profile.Append((Line)item);
                    }



                    //profile.Append(Line.CreateBound(first, second));
                    //profile.Append(Line.CreateBound(second, third));
                    //profile.Append(Line.CreateBound(third, fourth));
                    //profile.Append(Line.CreateBound(fourth, first));



#if R2019 || R2020 || R2021 || R2022
                    var nf = _doc.Create.NewFloor(temp, floorType, level, false);
#else

                    Floor nf = Floor.Create(_doc, new List<CurveLoop> { profile }, floorTypeId, level.Id);
#endif
                    var tp = nf.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM).AsDouble();

                    temp.Clear();
                    nf.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(elevation + ofsetStair + tp);
                    nf.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(idStairs);

                }

                //создаем стены

                WallType wallType;
                IList<Element> rooms;
                wallType = GetWallTypeInProject("0");

                XYZ mainXYZ = new XYZ();
                XYZ mainXYZ2 = new XYZ();
                XYZ mainXYZ3 = new XYZ();
                XYZ mainXYZ4 = new XYZ();


                foreach (var staitRun in runs)
                {
                    StairsRun stairsRun = _doc.GetElement(staitRun) as StairsRun;



                    //BaseElevation

                    var baseElevation = stairsRun.BaseElevation;

                    //высота подступенка
                    var hightStar = stairsRun.get_Parameter(BuiltInParameter.STAIRS_RUN_ACTUAL_RISER_HEIGHT).AsDouble();

                    //ширина лестницы
                    var wightStar = stairsRun.get_Parameter(BuiltInParameter.STAIRS_RUN_ACTUAL_RUN_WIDTH).AsDouble();

                    //ширина протсупни глубина
                    var wightStarProst = stairsRun.get_Parameter(BuiltInParameter.STAIRS_RUN_ACTUAL_TREAD_DEPTH).AsDouble();

                    //число проступней
                    var numberTheads = stairsRun.get_Parameter(BuiltInParameter.STAIRS_RUN_ACTUAL_NUMBER_OF_TREADS).AsInteger();

                    //направление надо найти для правильного копирования

                    var stirPath = stairsRun.GetStairsPath();

                    var cl2 = stirPath.FirstOrDefault() as Line;

                    var dirLine = cl2.Direction;

                    var dixX = dirLine.X;
                    var dixY = dirLine.Y;

                    Options options = new Options();

                    GeometryElement geom = stairsRun.get_Geometry(options);

                    //попробуем зайти черех Boinding box

                    var bbMax = stairsRun.get_BoundingBox(null).Max;
                    var bbMin = stairsRun.get_BoundingBox(null).Min;

                    //рамка марша
                    CurveLoop cl = stairsRun.GetFootprintBoundary();

                    //IList<Curve> curves = new List<Curve>();

                    foreach (var item in cl)
                    {
                        if (item.ApproximateLength == wightStar)

                        {
                            //создаем стены
                            var wall = Wall.Create(_doc, item, lv, false);

                            //создание стены на ступенке
                            wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).Set(hightStar); //установка высоты
                            wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).Set(baseElevation + ofsetStair);

                            wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(idStairs);

                            _doc.Regenerate();

                            //граница помещения

                            wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING).Set(0);

                            for (int i = 1; i <= numberTheads; i++)
                            {
                                XYZ vector = new XYZ(wightStarProst * dixX * i, wightStarProst * i * dixY, hightStar * i + ofsetStair);

                                //var vector = bbMax - bbMin;

                                var newWall = ElementTransformUtils.CopyElement(_doc, wall.Id, vector);

                                //_doc.Regenerate();

                                foreach (var el in newWall)
                                {
                                    _doc.GetElement(el).get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).Set(hightStar * i + baseElevation + ofsetStair);
                                    _doc.GetElement(el).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(idStairs);
                                }

                                //_doc.GetElement((ElementId)newWall).get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).Set(hightStar * i);
                                //_doc.GetElement((ElementId)newWall).get_Parameter(BuiltInParameter.WALL_TOP_OFFSET).Set(hightStar * i+ hightStar);
                            }

                            //создаем перекрытия

                            //количество ступенек numberTheads
                            //ширина лестницы wightStar
                            //глубина ступеньки wightStarProst

                            List<Line> lineForFinishDegree = new List<Line>();

                            foreach (Curve item3 in cl)

                            {
                                Line line = item3 as Line;

                                //длина длинии рисующую лестницу

                                double lineLighte = line.Length;

                                lineForFinishDegree.Add(line);

                            }

                            List<XYZ> newlistXYZ = new List<XYZ>();

                            IList<XYZ> l1 = lineForFinishDegree[0].Tessellate();

                            XYZ x1 = l1.First();
                            XYZ x2 = l1.Last();

                            int x1XInt = (int)x1.X;
                            int x2XInt = (int)x2.X;

                            int y1XInt = (int)x1.Y;
                            int y2XInt = (int)x2.Y;


                            //var l2 = lineForFinishDegree[1].Tessellate();
                            //нашли главную точку для постраения

                            //TaskDialog.Show("Отделка лестницы", $"{x1XInt} {x2XInt} {y1XInt} {y2XInt}"); ;

                            int k = 0;

                            if (x1XInt > x2XInt && y1XInt == y2XInt)
                            {

                                mainXYZ = x2;
                                k = 1;
                            }

                            if (x1XInt == x2XInt && y1XInt < y2XInt)
                            {

                                mainXYZ = l1[1];
                                k = 2;
                            }

                            if (x1XInt == x2XInt && y1XInt > y2XInt) //сделано
                            {

                                mainXYZ = l1[1];
                                k = 3;
                            }

                            if (x1XInt < x2XInt && y1XInt == y2XInt)
                            {

                                mainXYZ = l1[1];
                                k = 4;
                            }


                            //TaskDialog.Show("Отделка лестницы", $"{k}"); 

                            if (k == 1) //все в одном
                            {
                                XYZ xYZ = new XYZ(mainXYZ.X + wightStarProst, mainXYZ.Y, mainXYZ.Z);
                                mainXYZ2 = xYZ;
                                XYZ xYZ1 = new XYZ(mainXYZ.X, mainXYZ.Y - wightStar, mainXYZ.Z);
                                mainXYZ3 = xYZ1;
                                XYZ xYZ2 = new XYZ(mainXYZ.X + wightStarProst, mainXYZ.Y - wightStar, mainXYZ.Z);
                                mainXYZ4 = xYZ2;

                            }

                            if (k == 2) //все в одном
                            {
                                XYZ xYZ = new XYZ(mainXYZ.X, mainXYZ.Y - wightStarProst, mainXYZ.Z);
                                mainXYZ2 = xYZ;
                                XYZ xYZ1 = new XYZ(mainXYZ.X - wightStar, mainXYZ.Y, mainXYZ.Z);
                                mainXYZ3 = xYZ1;
                                XYZ xYZ2 = new XYZ(mainXYZ.X - wightStar, mainXYZ.Y - wightStarProst, mainXYZ.Z);
                                mainXYZ4 = xYZ2;

                            }

                            if (k == 3) //норм
                            {
                                XYZ xYZ = new XYZ(mainXYZ.X, mainXYZ.Y + wightStarProst, mainXYZ.Z);
                                mainXYZ2 = xYZ;
                                XYZ xYZ1 = new XYZ(mainXYZ.X + wightStar, mainXYZ.Y, mainXYZ.Z);
                                mainXYZ3 = xYZ1;
                                XYZ xYZ2 = new XYZ(mainXYZ.X + wightStar, mainXYZ.Y + wightStarProst, mainXYZ.Z);
                                mainXYZ4 = xYZ2;

                            }

                            if (k == 4) //все в одном
                            {
                                XYZ xYZ = new XYZ(mainXYZ.X, mainXYZ.Y + wightStar, mainXYZ.Z);
                                mainXYZ2 = xYZ;

                                XYZ xYZ1 = new XYZ(mainXYZ.X - wightStarProst, mainXYZ.Y, mainXYZ.Z);
                                mainXYZ3 = xYZ1;

                                XYZ xYZ2 = new XYZ(mainXYZ.X - wightStarProst, mainXYZ.Y + wightStar, mainXYZ.Z);
                                mainXYZ4 = xYZ2;

                            }

                            if (k == 0) //все в одном
                            {
                                TaskDialog.Show("Не сработало", "Пока такая лестница в работе у программиста https://pposinrevit.blogspot.com/");

                            }



                            Line line1 = Line.CreateBound(mainXYZ, mainXYZ2);
                            Line line2 = Line.CreateBound(mainXYZ2, mainXYZ4);
                            Line line3 = Line.CreateBound(mainXYZ4, mainXYZ3);
                            Line line4 = Line.CreateBound(mainXYZ3, mainXYZ);


                            //TaskDialog.Show("12", $"{lineForFinishDegree.Count()}");

                            //foreach (Curve item3 in cl)

                            //{
                            //    tempStair.Append(item3);

                            //}

                            tempStair.Append(line1);
                            tempStair.Append(line2);
                            tempStair.Append(line3);
                            tempStair.Append(line4);

                            CurveLoop profile = new CurveLoop();
                            profile.Append(line1);
                            profile.Append(line2);
                            profile.Append(line3);
                            profile.Append(line4);

#if R2019 || R2020 || R2021 || R2022
                            var nf = _doc.Create.NewFloor(tempStair, floorType, level, false);
#else

                            var nf = Floor.Create(_doc, new List<CurveLoop> { profile }, floorTypeId, level.Id);
#endif
                            //толщина перекрытия
                            var tp = nf.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM).AsDouble();

                            nf.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(baseElevation + hightStar + tp + ofsetStair);
                            nf.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(idStairs);


                            tempStair.Clear();

                            for (int i = 1; i < numberTheads; i++)
                            {
                                XYZ vector = new XYZ(wightStarProst * dixX * i, wightStarProst * i * dixY, hightStar * i + ofsetStair);

                                //var vector = bbMax - bbMin;

                                var newFloor = ElementTransformUtils.CopyElement(_doc, nf.Id, vector);

                                //_doc.Regenerate();

                                foreach (var el in newFloor)
                                {
                                    _doc.GetElement(el).get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(hightStar * i + baseElevation + hightStar + ofsetStair + tp);
                                    _doc.GetElement(el).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(idStairs);
                                }

                                //_doc.GetElement((ElementId)newWall).get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).Set(hightStar * i);
                                //_doc.GetElement((ElementId)newWall).get_Parameter(BuiltInParameter.WALL_TOP_OFFSET).Set(hightStar * i+ hightStar);
                            }

                            //копируем на каждую степеньку


                            break;
                        }

                    }

                }

                TaskDialog.Show("Отделка лестницы", "Отделка сделана на лестнице");
                t.Commit();

            }

            return Result.Succeeded;

        }


        // an array of planar lines and arcs that represent the horizontal profile of the floor
        public CurveArray Profile
        {
            get
            {
                return profile;
            }
            set
            {
                profile = value;
            }
        }

        // determine wether the floor is structural
        public bool Structural
        {
            get
            {
                return structural;
            }
            set
            {
                structural = value;
            }
        }

        // a list of all Floor Types Name that could be used by the new Floor
        public List<string> FloorTypesName
        {
            get
            {
                return floorTypesName;
            }
            set
            {
                floorTypesName = value;
            }
        }

        public void CreateFloorInRooms(Element elem)
        {

            FloorType floorType = GetFloorTypeInProject("Отделка");

            CurveArray roomsCurves = new CurveArray();
            Room r1 = (Room)elem;

            SpatialElementBoundaryOptions bo = new SpatialElementBoundaryOptions();
            IList<IList<BoundarySegment>> segments2 = r1.GetBoundarySegments(bo);

            CurveArray temp = new CurveArray();

            if (null != segments2)
            {
                IList<BoundarySegment> segmentList = segments2[0];

                foreach (BoundarySegment boundarySegment in segmentList)
                {
                    Curve c = boundarySegment.GetCurve();
                    temp.Append(c);
                }
                SortCurves(temp);

                CurveLoop profile = new CurveLoop();

                foreach (var g in temp)
                {
                    profile.Append(g);
                }


#if R2019 || R2020 || R2021 || R2022

                _ = _doc.Create.NewFloor(temp, floorType, r1.Level, false);
#else

                var nf = Floor.Create(_doc, new List<CurveLoop> { profile }, floorType.Id, level.Id);
#endif

            }


        }

        public FloorType GetFloorTypeInProject(string nameTypeWall)
        {
            FloorType wallType;
            //// Get a floor type for floor creation
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            collector.OfClass(typeof(FloorType));
            wallType = collector.Where(x => x.Name == nameTypeWall) as FloorType;
            //as WallType;

            FilteredElementCollector a = new FilteredElementCollector(_doc).OfClass(typeof(FloorType));
            foreach (FloorType wallType2 in a)
            {
                if (wallType2.Name == nameTypeWall)
                {
                    return wallType2;
                }
            }

            return wallType;

        }

        public void SortCurves(CurveArray lines)
        {
            XYZ pointCurves = lines.get_Item(0).GetEndPoint(1);
            Curve temCurve = lines.get_Item(0);


            Profile = creApp.NewCurveArray();

            Profile.Append(temCurve);

            while (Profile.Size != lines.Size)
            {

                temCurve = GetNext(lines, pointCurves, temCurve);

                if (Math.Abs(pointCurves.X - temCurve.GetEndPoint(0).X) < PRECISION
                 && Math.Abs(pointCurves.Y - temCurve.GetEndPoint(0).Y) < PRECISION)
                {
                    pointCurves = temCurve.GetEndPoint(1);
                }
                else
                {
                    pointCurves = temCurve.GetEndPoint(0);
                }

                Profile.Append(temCurve);
            }

        }
        public Curve GetNext(CurveArray profile, XYZ connected, Curve line)
        {
            foreach (Curve c in profile)
            {
                if (c.Equals(line))
                {
                    continue;
                }
                if ((Math.Abs(c.GetEndPoint(0).X - line.GetEndPoint(1).X) < PRECISION &&
                    Math.Abs(c.GetEndPoint(0).Y - line.GetEndPoint(1).Y) < PRECISION &&
                    Math.Abs(c.GetEndPoint(0).Z - line.GetEndPoint(1).Z) < PRECISION) &&
                    (Math.Abs(c.GetEndPoint(1).X - line.GetEndPoint(0).X) < PRECISION &&
                    Math.Abs(c.GetEndPoint(1).Y - line.GetEndPoint(0).Y) < PRECISION &&
                    Math.Abs(c.GetEndPoint(1).Z - line.GetEndPoint(0).Z) < PRECISION) &&
                    2 != profile.Size)
                {
                    continue;
                }

                if (Math.Abs(c.GetEndPoint(0).X - connected.X) < PRECISION &&
                    Math.Abs(c.GetEndPoint(0).Y - connected.Y) < PRECISION &&
                    Math.Abs(c.GetEndPoint(0).Z - connected.Z) < PRECISION)
                {
                    return c;
                }
                else if (Math.Abs(c.GetEndPoint(1).X - connected.X) < PRECISION &&
                         Math.Abs(c.GetEndPoint(1).Y - connected.Y) < PRECISION &&
                         Math.Abs(c.GetEndPoint(1).Z - connected.Z) < PRECISION)
                {
                    if (c.GetType().Name.Equals("Line"))
                    {
                        XYZ start = c.GetEndPoint(1);
                        XYZ end = c.GetEndPoint(0);
                        return Line.CreateBound(start, end);
                    }
                    else if (c.GetType().Name.Equals("Arc"))
                    {
                        int size = c.Tessellate().Count;
                        XYZ start = c.Tessellate()[0];
                        XYZ middle = c.Tessellate()[size / 2];
                        XYZ end = c.Tessellate()[size];

                        return Arc.Create(start, end, middle);
                    }
                }
            }
            throw new InvalidOperationException("The Room Boundary should be closed.");
        }

        public Stairs SelectedElement(Document sDocument)
        {

            try
            {

                var uidoc = new UIDocument(_doc);


                var r = uidoc.Selection.PickObject(ObjectType.Element,
                    "Выбрать лестницу");
                var elem = uidoc.Document.GetElement(r);


                return (Stairs)elem;

            }

            catch (Exception ex)
            {
                TaskDialog.Show("Revit", "Exception" + ex.Message);

                return null;
            }


        }

        private Stairs GetStairInfo(Document document)
        {
            Stairs stairs = null;

            FilteredElementCollector collector = new FilteredElementCollector(document);
            ICollection<ElementId> stairsIds = collector.WhereElementIsNotElementType().OfCategory(BuiltInCategory.OST_Stairs).ToElementIds();



            foreach (ElementId stairId in stairsIds)
            {
                if (Stairs.IsByComponent(document, stairId) == true)
                {
                    stairs = document.GetElement(stairId) as Stairs;

                    // Format the information
                    String info = "\nNumber of stories:  " + stairs.NumberOfStories;
                    info += "\nHeight of stairs:  " + stairs.Height;
                    info += "\nNumber of treads:  " + stairs.ActualTreadsNumber;
                    info += "\nTread depth:  " + stairs.ActualTreadDepth;

                    // Show the information to the user.
                    //TaskDialog.Show("Revit", info);
                }
            }

            return stairs;
        }

        public WallType GetWallTypeInProject(string nameTypeWall)
        {
            WallType wallType;
            //// Get a floor type for floor creation
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            collector.OfClass(typeof(WallType));
            wallType = collector.Where(x => x.Name == nameTypeWall) as WallType;
            //as WallType;

            FilteredElementCollector a = new FilteredElementCollector(_doc).OfClass(typeof(WallType));
            foreach (WallType wallType2 in a)
            {
                if (wallType2.Name == nameTypeWall)
                {
                    return wallType2;
                }
            }

            return wallType;
        }


    }


}

