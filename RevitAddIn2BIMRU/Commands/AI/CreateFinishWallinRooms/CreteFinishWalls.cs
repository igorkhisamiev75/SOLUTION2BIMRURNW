using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI.Selection;
using System.Diagnostics;
using Floor = Autodesk.Revit.DB.Floor;


namespace RevitAddIn2BIMRU.Commands.AI
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    //[Journaling(JournalingMode.NoCommandData)]
    public class CreteFinishWalls : Window, IExternalCommand
    {
        // Private Members
        IList<WallType> m_wallTypeCollection;         // Store all the wall types in current document
        IList<CeilingType> m_ceilingTypeCollection;         // Store all the wall types in current document
        IList<FloorType> m_FloorTypeCollection;
        public const double PRECISION = 0.00000001;
        public CurveArray profile;
        public Autodesk.Revit.Creation.Application creApp;
        public FloorType floorType;
        public CeilingType ceilingType;
        private Application _app;
        private Document _doc;
        public Floor createdFloor;
        public Ceiling createdCeiling;
        private Level level;
        public ICollection<Element> roomsCollection = new List<Element>();

        public List<Level> levelCollection2 = new List<Level>();
        public List<Room> roomsCollection2 = new List<Room>();
        public List<String> roomsNameInProject = new List<String>();

        //public ICollection<Element> filtersCollectionCopy = new List<Element>();

        //public ICollection<Element> unusedFiltersCollection = new List<Element>();

        public IList<WallType> typeCollectionWall = new List<WallType>();

        public int roomBoardingWall; //для границы помещений
        Level selectedLevelInForms; //выбор уровня
        double wallHeight = 0;
        double wallOffset = 0;

        // Properties
        /// <summary>
        /// Inform all the wall types can be created in current document
        /// </summary>
        public IList<WallType> WallTypes
        {
            get
            {
                return m_wallTypeCollection;
            }
        }

        public IList<CeilingType> CeilingTypes
        {
            get
            {
                return m_ceilingTypeCollection;
            }
        }
        // the Level on which the floors are to be placed
        public Level Level
        {
            get
            {
                return level;
            }
            set
            {
                level = value;
            }
        }
        // Properties
        /// <summary>
        /// Inform all the floor types can be created in current document
        /// </summary>
        public IList<FloorType> FloorTypes
        {
            get
            {
                return m_FloorTypeCollection;
            }
        }

        // an array of planar lines and arcs that represent the horizontal profile of the floor

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {

            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

            //_app = uiApp.Application;
            _doc = uiDoc.Document;

            //m_wallTypeCollection = new IList<WallType>();

            // Search all level
            FilteredElementCollector filteredLevelCollector = new FilteredElementCollector(_doc);
            filteredLevelCollector.OfClass(typeof(Level));
            List<Level> levelCollection = filteredLevelCollector.Cast<Level>().ToList<Level>();

            levelCollection2 = levelCollection;

            //TaskDialog.Show("2bim.ru", $"{levelCollection.Count()}");

            // Search all the wall types in the Revit
            FilteredElementCollector filteredElementCollector = new FilteredElementCollector(_doc);
            filteredElementCollector.OfClass(typeof(WallType));
            m_wallTypeCollection = filteredElementCollector.Cast<WallType>().ToList<WallType>();

            // Search all the floor types in the Revit
            FilteredElementCollector filteredElementCollectorFloor = new FilteredElementCollector(_doc);
            filteredElementCollectorFloor.OfClass(typeof(FloorType));
            m_FloorTypeCollection = filteredElementCollectorFloor.Cast<FloorType>().ToList<FloorType>();

            // Search all the Ceiling types in the Revit
            FilteredElementCollector filteredElementCollectorCeiling = new FilteredElementCollector(_doc);
            filteredElementCollectorCeiling.OfClass(typeof(CeilingType));
            m_ceilingTypeCollection = filteredElementCollectorCeiling.Cast<CeilingType>().ToList<CeilingType>();

            roomsCollection = GetAllRooms(_doc);

            foreach (var rooms in roomsCollection)
            {
                roomsCollection2.Add((Room)rooms);
            }

            //удаление дубликатов, групирруем по имени помещения
            List<Room> roomGroup = roomsCollection2.GroupBy(x =>
            x.get_Parameter(BuiltInParameter.ROOM_NAME).AsString()).Select(x => x.First()).ToList();

            foreach (Room room in roomGroup)
            {
                roomsNameInProject.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
            }

            //create form 
            ViewWPF vpViewWpf = new ViewWPF(roomsNameInProject, WallTypes, levelCollection2, FloorTypes, CeilingTypes);

            //show form
            vpViewWpf.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            vpViewWpf.ShowDialog();


            //смещение от уровня
            wallOffset = Convert.ToDouble(vpViewWpf.ofsetWallS);

            //TaskDialog.Show("1Смещение от уровня", $"{vpViewWpf.ofsetWallS}");
            //TaskDialog.Show("2Смещение от уровня", $"{wallOffset}");
#if R2019 || R2020 || R2021 

           wallOffset = UnitUtils.Convert(wallOffset, DisplayUnitType.DUT_MILLIMETERS, DisplayUnitType.DUT_DECIMAL_FEET);

            wallOffset = UnitUtils.Convert(wallOffset, DisplayUnitType.DUT_MILLIMETERS, DisplayUnitType.DUT_DECIMAL_FEET);
#else
            wallOffset = UnitUtils.ConvertFromInternalUnits(wallOffset, UnitTypeId.Millimeters);
#endif

            //TaskDialog.Show("3Смещение от уровня", $"{wallOffset}");

            if (vpViewWpf.heighWallUser != "")
            {
                wallHeight = Convert.ToDouble(vpViewWpf.heighWallUser);

#if R2019 || R2020 || R2021 
                wallHeight = UnitUtils.Convert(wallHeight, DisplayUnitType.DUT_MILLIMETERS, DisplayUnitType.DUT_DECIMAL_FEET);
#else
                wallHeight = UnitUtils.ConvertFromInternalUnits(wallHeight, UnitTypeId.Millimeters);

#endif
                //TaskDialog.Show("2bim.ru", $"Высота стен {wallHeight.ToString()}");
            }


            //room boarding cheack box

            if (vpViewWpf.roomBoording == true)
            { roomBoardingWall = 1; }

            else { roomBoardingWall = 0; }

            //get selected walltype
            WallType selectedWallTpe = vpViewWpf.typeWallCollectionSelected;

            //get selected floortype
            FloorType selectedFloorType = vpViewWpf.typeFloorSelected;

            //get selected ceilingtype
            CeilingType selectedCeilingType = vpViewWpf.typeCeilingSelected;

            //TaskDialog.Show("2bim.ru", $" selected {selectedCeilingType.Name}");

            //get rooms checked
            List<String> checkRoomsString = vpViewWpf.checkRooms;

            //get selected level

            selectedLevelInForms = vpViewWpf.selectedLevel;
            //if(selectedLevelInForms != null)
            //{
            //    TaskDialog.Show("2bim.ru", $"Level name selected {selectedLevelInForms.Name}");
            //}


            List<Room> checkRooms = new List<Room>();

            foreach (Room room in roomsCollection2)
            {
                string nameRoome = room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString();
                var levelRoom = room.get_Parameter(BuiltInParameter.ROOM_LEVEL_ID).AsValueString();
                var levelSelect = selectedLevelInForms.Name;

                if (checkRoomsString.Contains(nameRoome) && levelSelect == levelRoom)
                {
                    checkRooms.Add(room);
                }
            }

            //whats button
            string whatsButton = vpViewWpf.whatsButton;

            //граница помещения

            bool rb = vpViewWpf.roomBoording;

            using (Transaction t = new Transaction(_doc))
            {


                t.Start("Create a finish");

                FailureHandlingOptions options = t.GetFailureHandlingOptions();
                MyPreProcessor preproccessor = new MyPreProcessor();
                options.SetClearAfterRollback(true);
                options.SetFailuresPreprocessor(preproccessor);
                t.SetFailureHandlingOptions(options);

                uiApp = commandData.Application;
                uiDoc = uiApp.ActiveUIDocument;

                if (whatsButton == "отделкаВыборомПомещения")
                {

                    TaskDialog.Show("2bim.ru", "Выберите помещения на плане");

                    IList<Reference> R2 = uiDoc.Selection.PickObjects(ObjectType.Element, "Выбрать помещения");

                    List<Element> elements1 = new List<Element>();

                    if (R2 != null)
                    {

                        foreach (var e in R2)
                        {
                            Element elem2 = uiDoc.Document.GetElement(e);
                            elements1.Add(elem2);
                        }


                    }

                    //Element elem = SelectedElement(_doc); //select room
                    try
                    {
                        foreach (var elem in elements1)
                        {
                            CreateWallInRoom(elem, uiDoc, roomBoardingWall, selectedWallTpe);
                        }

                        TaskDialog.Show("2bim.ru", "Отделка стен создана ☺☺☺");
                    }

                    catch
                    {
                        //TaskDialog.Show("2bim.ru", "Отделка стен создана ☺☺☺");
                        //
                    }
                    t.Commit(options);
                }

                if (whatsButton == "отделкаПоСпеке")
                {
                    foreach (Room room in checkRooms)
                    {

                        try
                        {
                            Element element = room;
                            CreateWallInRoom(element, uiDoc, roomBoardingWall, selectedWallTpe);
                            _doc.Regenerate();

                            //TaskDialog.Show("2bim.ru", "Отделка стен создана ☺☺☺");
                        }

                        catch
                        {
                            //TaskDialog.Show("2bim.ru", "Ошибка");
                            //TaskDialog.Show("2bim.ru", "Не сработало");
                        }
                    }
                    t.Commit(options);
                }

                //floor in selected room

                if (whatsButton == "полыВыборомПомещения")
                {
                    TaskDialog.Show("2bim.ru", "Выберите помещения на плане");
                    //TaskDialog.Show("2bim.ru", "Выбери помещение на плане");
                    IList<Reference> R2 = uiDoc.Selection.PickObjects(ObjectType.Element, "Выбрать помещения");

                    List<Element> elements1 = new List<Element>();

                    if (R2 != null)
                    {

                        foreach (var e in R2)
                        {
                            Element elem2 = uiDoc.Document.GetElement(e);
                            elements1.Add(elem2);
                        }


                    }
                    //Element elem = SelectedElement(_doc); //select room

                    try
                    {
                        foreach (var elem in elements1)
                        {
                            CreateFloorInRooms(elem, selectedFloorType);
                            CreateFloorOpenings(elem);
                        }
                        TaskDialog.Show("2bim.ru", "Полы созданы ☺☺☺");

                    }

                    catch (Exception ex)
                    {
                        TaskDialog.Show("2bim.ru", $"{ex} ");

                    }

                    t.Commit(options);
                    //_doc.Regenerate();

                    //try
                    //{

                    //    t.Start();
                    //    foreach (var elem in elements1)
                    //    {
                    //        CreateFloorOpenings(elem);
                    //    }
                    //    TransactionStatus res = t.Commit(options);
                    //}

                    //catch (Exception ex)
                    //{
                    //    TaskDialog.Show("2bim.ru", $"{ex} ");

                    //}




                }

                //floors with schedule

                if (whatsButton == "отделкаПоСпекеПолы")
                {
                    foreach (Room room in checkRooms)
                    {
                        Element elem = (Element)room; //select room

                        try
                        {
                            CreateFloorInRooms(elem, selectedFloorType);

                            //TaskDialog.Show("2bim.ru", "Пол создан ☺☺☺");
                            //_doc.Regenerate();

                        }

                        catch (Exception ex)
                        {
                            TaskDialog.Show("2bim.ru", $"{ex} ");
                        }

                        t.Commit(options);

                        //if (failureHandler.ErrorMessage != "")
                        //{
                        //    MessageBox.Show(failureHandler.ErrorSeverity + " || "
                        //      + failureHandler.ErrorMessage);
                        //}

                        try
                        {

                            t.Start();
                            CreateFloorOpenings(elem);

                        }



                        catch (Exception ex)
                        {
                            TaskDialog.Show("2bim.ru", $"{ex} ");

                        }

                    }

                }

                //ceiling in selected room

                if (whatsButton == "отделкаВыборомПомещенияПотолки")
                {

                    TaskDialog.Show("2bim.ru", "Выберите помещения на плане");

                    IList<Reference> R2 = uiDoc.Selection.PickObjects(ObjectType.Element, "Выбрать помещения");

                    List<Element> elements1 = new List<Element>();

                    if (R2 != null)
                    {

                        foreach (var e in R2)
                        {
                            Element elem2 = uiDoc.Document.GetElement(e);
                            elements1.Add(elem2);
                        }


                    }

                    //Element elem = SelectedElement(_doc); //select room

                    try
                    {
                        foreach (var elem in elements1)
                        {
                            CreateCeilingInRooms(elem, selectedCeilingType);
                            //CreateCeilingOpenings(elem);
                        }


                        TaskDialog.Show("2bim.ru", "Потолки созданы ☺☺☺");

                    }

                    catch (Exception ex)
                    {
                        TaskDialog.Show("2bim.ru", $"{ex} ");

                    }

                    t.Commit(options);
                    //_doc.Regenerate();


                }

                //floors with schedule

                if (whatsButton == "отделкаПоСпекеПотолки")
                {
                    foreach (Room room in checkRooms)
                    {
                        Element elem = (Element)room; //select room

                        try
                        {
                            CreateCeilingInRooms(elem, selectedCeilingType);

                        }

                        catch (Exception ex)
                        {
                            TaskDialog.Show("2bim.ru", $"{ex} ");
                        }


                    }
                    t.Commit(options);
                    TaskDialog.Show("2bim.ru", "Потолки созданы ☺☺☺");

                }

            }

            return Result.Succeeded;
        }

        public void CreateFloorOpenings(Element elem)
        {
            //CurveArray roomsCurves = new CurveArray();
            var r1 = (Room)elem;

            SpatialElementBoundaryOptions bo = new SpatialElementBoundaryOptions();
            IList<IList<BoundarySegment>> segments2 = r1.GetBoundarySegments(bo);

            CurveArray temp2 = new CurveArray();

            if (null != segments2)
            {
                for (int j = 1; j < segments2.Count; j++)
                {
                    IList<BoundarySegment> segmentList = segments2[j];

                    foreach (BoundarySegment boundarySegment in segmentList)
                    {
                        Curve c = boundarySegment.GetCurve();
                        temp2.Append(c);
                    }
                    //SortCurves(temp2);

                    //Opening opening = _doc.Create.NewOpening(createdFloor, temp2, true); //create opening

                    _doc.Create.NewOpening(createdFloor, temp2, true); //create opening

                    //_doc.Regenerate();
                }

            }



        }

        public void CreateFloorInRooms(Element elem, FloorType floorType3)
        {


            //FloorType floorType2 = GetFloorTypeInProject(_doc);

            //TaskDialog.Show("У тебя получилось", $"{floorType.GetType()} Пол создан ☺☺☺");
            Floor floor;

            //IList<Element> rooms;


            CurveArray roomsCurves = new CurveArray();
            Room r1 = (Room)elem;

            level = r1.Level;

            string ADSK_Primechanie = "a85b7661-26b0-412f-979c-66af80b4b2c3";
            string numberRoom = r1.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();
            string nameRoom = r1.get_Parameter(BuiltInParameter.ROOM_NAME).AsString();


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
                var newSortCurve = SortCurves(temp);

                CurveLoop profile = new CurveLoop();

                foreach (var g in temp)
                {
                    profile.Append((Line)g);
                }

#if R2019 || R2020 || R2021 || R2022
                floor = _doc.Create.NewFloor(newSortCurve, floorType3, r1.Level, false);
                floor.get_Parameter(new Guid(ADSK_Primechanie)).Set(numberRoom);
                floor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(nameRoom);
#else

                floor = Floor.Create(_doc, new List<CurveLoop> { profile }, floorType3.Id, level.Id);
#endif
                //set room name and number
                floor.get_Parameter(new Guid(ADSK_Primechanie)).Set(numberRoom);
                floor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(nameRoom);

                createdFloor = floor;
                _doc.Regenerate();

            }


        }

        public CurveArray SortCurves(CurveArray lines)
        {
            //TaskDialog.Show("2bim.ru", "ЗАШЕЛ в метод сортировки курве эррей");

            XYZ pointCurves = lines.get_Item(0).GetEndPoint(1);
            Curve temCurve = lines.get_Item(0);

            //Profile = creApp.NewCurveArray();

            var ggg = _doc.Application.Create.NewCurveArray();


            CurveArray profNew = _doc.Application.Create.NewCurveArray();

            profNew.Append(temCurve);

            //rofile.Append(temCurve);

            while (profNew.Size != lines.Size)
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

                profNew.Append(temCurve);
            }

            return profNew;

            //TaskDialog.Show("2bim.ru", "Вышел из метода");

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

        public void CreateWallInRoom(Element elem, UIDocument uiDoc, int _roomBoardingWall, WallType wallType)
        {
            string ADSK_Primechanie = "a85b7661-26b0-412f-979c-66af80b4b2c3";

            IList<Wall> walls = new List<Wall>();

            ElementId wallTypeId = wallType.GetTypeId();

            var elementIdWall = wallType.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME).Element.Id;

            CurveArray roomsCurves = new CurveArray();

            Room room = (Room)elem;

            ElementId levelId = room.LevelId;

            double heightRoom = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT).AsDouble();

            double offsetWallInRoom = wallOffset;


            if (wallHeight != 0)
            {
                heightRoom = wallHeight;
            }

            if (heightRoom < 0)
            {
                heightRoom = 1;
            }

            string numberRoom = room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();
            string nameRoom = room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString();

            IList<Curve> curves = new List<Curve>();

            SpatialElementBoundaryOptions bo = new SpatialElementBoundaryOptions();
            IList<IList<BoundarySegment>> segments2 = room.GetBoundarySegments(bo);


            //CurveArray temp = new CurveArray();

            if (null != segments2)
            {

                foreach (IList<BoundarySegment> segment in segments2)
                {
                    IList<BoundarySegment> segmentList = segment;


                    foreach (BoundarySegment boundarySegment in segmentList)
                    {
                        Curve c = boundarySegment.GetCurve();
                        roomsCurves.Append(c);
                    }
                }


            }

            foreach (Curve g in roomsCurves)
            {
                if (g == null) continue;
                if (g.IsCyclic)
                {
                    //Curve prog=g.
                    var wall = Wall.Create(_doc, g, levelId, false);

                    //set bounding граница помещения
                    wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING).Set(_roomBoardingWall);

                    //set room name and number
                    wall.get_Parameter(new Guid(ADSK_Primechanie)).Set(numberRoom);
                    wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(nameRoom);

                    //set height
                    wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).Set(heightRoom);

                    //set offset
                    wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).Set(offsetWallInRoom);

                    //set type 
                    wall.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).Set(elementIdWall);


                    _doc.Regenerate();

                    //IList<Reference> sideFaces = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);

                    //// MOVE WALL
                    //Face face = uiDoc.Document.GetElement(sideFaces[0])
                    //    .GetGeometryObjectFromReference(sideFaces[0]) as Face;
                    //PlanarFace pf = face as PlanarFace;
                    //XYZ normal_reverted = pf.FaceNormal;
                    //wall.Location.Move(-normal_reverted * (wall.WallType.Width / 2.0));

                    walls.Add(wall);
                    continue;
                }
                if (!g.IsCyclic)
                {

                    var wall = Wall.Create(_doc, g, levelId, false);
                    //Wall wall = Wall.Create(_doc, curves, wallTypeId, levelId, false);
                    wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING).Set(_roomBoardingWall);
                    //wall.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM).Set(wallTypeId);
                    wall.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).Set(elementIdWall);

                    wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).Set(heightRoom);
                    //set offset
                    wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).Set(offsetWallInRoom);

                    wall.get_Parameter(new Guid(ADSK_Primechanie)).Set(numberRoom);
                    wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(nameRoom);

                    _doc.Regenerate();

                    IList<Reference> sideFaces = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
                    // MOVE WALL
                    Face face = uiDoc.Document.GetElement(sideFaces[0])
                        .GetGeometryObjectFromReference(sideFaces[0]) as Face;
                    PlanarFace pf = face as PlanarFace;
                    XYZ normal_reverted = pf.FaceNormal;
                    wall.Location.Move(-normal_reverted * (wall.WallType.Width / 2.0));

                    walls.Add(wall);

                }
            }

            //join walls
            foreach (var wall1 in walls)
            {
                foreach (var wall2 in walls)
                {
                    if (wall1 != wall2)
                    {
                        BoundingBoxXYZ boundingBoxXYZ = wall1.get_BoundingBox(null);
                        BoundingBoxXYZ boundingBoxXYZ2 = wall2.get_BoundingBox(null);

                        if (bbIntersect(boundingBoxXYZ, boundingBoxXYZ2))
                        {
                            if (!JoinGeometryUtils.AreElementsJoined(_doc, wall1, wall2))
                            {
                                try
                                {
                                    JoinGeometryUtils.JoinGeometry(_doc, wall1, wall2);
                                }
                                catch (Autodesk.Revit.Exceptions.ArgumentException a)
                                {
                                    Debug.Print("Ошибка" + a.ToString());
                                }
                            }

                        }

                    }




                }
            }

            _doc.Regenerate();



        }

        public bool bbIntersect(BoundingBoxXYZ BoxXYZ, BoundingBoxXYZ BoxXYZ2)
        {
            if (BoxXYZ != null && BoxXYZ2 != null)
            {
                Outline outline1 = new Outline(BoxXYZ.Min, BoxXYZ.Max);
                Outline outline2 = new Outline(BoxXYZ2.Min, BoxXYZ2.Max);

                if (outline1.Intersects(outline2, 0))
                {
                    return true;
                }
            }

            return false;

        }

        public Element SelectedElement(Document sDocument)
        {

            try
            {
                var uidoc = new UIDocument(_doc);

                //sDocument.Regenerate();

                var r = uidoc.Selection.PickObject(ObjectType.Element,
                    "Выбрать помещение");
                var elem = uidoc.Document.GetElement(r);
                return elem;

            }

            catch (Exception ex)
            {
                TaskDialog.Show("Revit", "Exception" + ex.Message);
                //subTransaction.RollBack();
                return null;
            }

        }


        //get all rooms
        public IList<Element> GetAllRooms(Document doc)
        {
            // List all Room's
            var room_collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();
            IList<ElementId> room_eids = room_collector.ToElementIds() as IList<ElementId>;
            var allRooms = room_collector.ToElements();

            return allRooms;
        }

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

        public FloorType GetFloorTypeInProject(Document _doc)
        {
            // Search all the floor types in the Revit
            FilteredElementCollector filteredElementCollectorFloor = new FilteredElementCollector(_doc);
            filteredElementCollectorFloor.OfClass(typeof(FloorType));
            var m_FloorTypeCollection = filteredElementCollectorFloor.Cast<FloorType>().ToList<FloorType>();
            FloorType g = m_FloorTypeCollection[3];
            return g;

        }

        public class MyPreProcessor : IFailuresPreprocessor
        {
            FailureProcessingResult IFailuresPreprocessor.PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                String transactionName = failuresAccessor.GetTransactionName();

                IList<FailureMessageAccessor> fmas = failuresAccessor.GetFailureMessages();


                if (fmas.Count == 0)
                    return FailureProcessingResult.Continue;


                if (transactionName.Equals("EXEMPLE"))
                {
                    foreach (FailureMessageAccessor fma in fmas)
                    {
                        if (fma.GetSeverity() == FailureSeverity.Error)
                        {
                            failuresAccessor.DeleteAllWarnings();
                            return FailureProcessingResult.ProceedWithRollBack;
                        }
                        else
                        {
                            failuresAccessor.DeleteWarning(fma);
                        }

                    }
                }
                else
                {
                    foreach (FailureMessageAccessor fma in fmas)
                    {
                        failuresAccessor.DeleteAllWarnings();
                    }
                }
                return FailureProcessingResult.Continue;
            }
        }

        //создание потолка

        public void CreateCeilingInRooms(Element elem, CeilingType ceilingType)
        {


            //FloorType floorType2 = GetFloorTypeInProject(_doc);

            //TaskDialog.Show("У тебя получилось", $"{ceilingType.GetType()} Пол создан ☺☺☺");
            Ceiling ceiling;

            //IList<Element> rooms;

            //CurveArray roomsCurves = new CurveArray();
            Room room = (Room)elem;

            level = room.Level;

            string ADSK_Primechanie = "a85b7661-26b0-412f-979c-66af80b4b2c3";
            string numberRoom = room.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();
            string nameRoom = room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString();

            double heightRoom = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT).AsDouble();

            double offsetWallInRoom = wallOffset;


            if (wallHeight != 0)
            {
                heightRoom = wallHeight;
            }

            if (heightRoom < 0)
            {
                heightRoom = 1;
            }


            SpatialElementBoundaryOptions bo = new SpatialElementBoundaryOptions();
            IList<IList<BoundarySegment>> segments2 = room.GetBoundarySegments(bo);

            CurveArray tempCurveArray = new CurveArray();
            IList<CurveLoop> looplist = new List<CurveLoop>();
            CurveLoop profile = new CurveLoop();

            //TaskDialog.Show("Всего бондарей", $"{segments2.Count}"); //3

            if (null != segments2)
            {
                foreach (IList<BoundarySegment> segment in segments2)
                {
                    //TaskDialog.Show("Зашли в BondarySegments", $"{segment.Count}"); //1 -4 2 -2

                    //IList<BoundarySegment> segmentList = segments2[0];
                    profile = new CurveLoop();
                    tempCurveArray = new CurveArray();
                    //if( segment != null )

                    foreach (BoundarySegment boundarySegment in segment)
                    {
                        Curve c = boundarySegment.GetCurve();
                        tempCurveArray.Append(c);
                    }
                    CurveArray newSortCurve = SortCurves(tempCurveArray);

                    //TaskDialog.Show("newSortCurve", $"{newSortCurve.Size}"); // 1 - 4 2 - 2

                    foreach (var c in newSortCurve)
                    {

                        try
                        {
                            Line line = c as Line;
                            //TaskDialog.Show("Line", $"{c.ToString()}");//1
                            profile.Append(line);
                        }
                        catch
                        {
                            Arc line = c as Arc;
                            //TaskDialog.Show("Arc", $"{c.ToString()}");
                            profile.Append(line);
                        }

                    }

                    //TaskDialog.Show("В профиль добавили", $"{profile.Count()}");//4 2


                    looplist.Add(profile);
                    //TaskDialog.Show("looplist", $"{looplist.Count()}");//1 2

                }




            }

            string v = looplist.Count.ToString();

            //TaskDialog.Show("looplist", $"{v}");

#if R2019 || R2020 || R2021 || R2022
            //floor = _doc.Create.NewFloor(newSortCurve, floorType3, r1.Level, false);
            //floor.get_Parameter(new Guid(ADSK_Primechanie)).Set(numberRoom);
            //floor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(nameRoom);
#else
            //TaskDialog.Show("У тебя получилось", $"{profile.GetType()} Потолок создается ☺☺☺");

            ceiling = Ceiling.Create(_doc, looplist, ceilingType.Id, level.Id);

            ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM).Set(heightRoom);


#endif
            //set room name and number
            //ceiling.get_Parameter(new Guid(ADSK_Primechanie)).Set(numberRoom);
            //ceiling.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(nameRoom);

            // createdFloor = floor;
            _doc.Regenerate();




        }



    }
}
