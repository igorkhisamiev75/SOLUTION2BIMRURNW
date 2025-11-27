#region Namespace

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

#endregion

namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    public class CreateFromDWGPipe : IExternalCommand
    {
        
        private Document _doc;
        public ElementId linkDocId = new ElementId(BuiltInCategory.OST_RvtLinks);
        public List<ElementId> roomids = new List<ElementId>();
        public List<Room> projectRooms = new List<Room>();


        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            
            _doc = uiDoc.Document;


            using (var t = new Transaction(_doc))
            {
                t.Start("Create");

                TaskDialog.Show("Complite!", "Выбрать DWG элементы!!!");

                IList<Reference> R2 = uiDoc.Selection.PickObjects(ObjectType.Element, "SELECT ELEMENTS");

                //
                //float number = Convert.ToSingle(sq);

                List<Element> elements1 = new List<Element>();

                if (R2 != null)
                {

                    foreach (var e in R2)
                    {
                        Element elem = uiDoc.Document.GetElement(e);

                        elements1.Add(elem);
                    }


                }

                List<myPipeFromLine> myPipeFromLines = new List<myPipeFromLine>();

                foreach (Element item in elements1)
                {
                    myPipeFromLine myPipeFromLine2 = new myPipeFromLine();

                    DetailLine detaiLine;
                    DetailArc detaiLine2;

                    detaiLine = item as DetailLine;
                    detaiLine2 = item as DetailArc;

                    string nameLine = "";

                    if (detaiLine != null)
                    {

                        nameLine = detaiLine.LineStyle.Name;
                        string[] separators = new string[] { "_", " " };

                        string[] strings = nameLine.Split(separators, StringSplitOptions.RemoveEmptyEntries);

                        var d = Convert.ToSingle(strings.LastOrDefault());

                        if (d > 0)
                        {
                            myPipeFromLine2.dPipe = d;
                        }
                        else
                        {
                            myPipeFromLine2.dPipe = 25;
                        }

                        var gg = detaiLine.GeometryCurve;

                        var f = gg.Tessellate();

                        myPipeFromLine2.firstPoint = f.FirstOrDefault();
                        myPipeFromLine2.lastPoint = f.LastOrDefault();


                        myPipeFromLines.Add(myPipeFromLine2);

                    }


                }

                //Get PipingSystemType
                var systemType = new FilteredElementCollector(_doc).OfClass(typeof(PipingSystemType)).FirstElementId();

                Level level = uiDoc.ActiveView.GenLevel;

                foreach (var item in myPipeFromLines)
                {
                    try
                    {
                        var pipe = CreateNewPipe(_doc, systemType, level.Id, item);
                        double d1 = item.dPipe;

#if R2019 || R2020
 double d2 = UnitUtils.Convert(d1, DisplayUnitType.DUT_MILLIMETERS,
                                             DisplayUnitType.DUT_DECIMAL_FEET);
                        pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(d2);
                        
#else
                        double d2 = UnitUtils.ConvertToInternalUnits(d1, UnitTypeId.Millimeters);
#endif
                    }

                    catch (Exception ex) { }

                }





                //TaskDialog.Show("Complite!", "Выбрать элемент размещения");
                //Reference r = uiDoc.Selection.PickObject(ObjectType.Element, "Выбрать сплинклер");
                //Element elem3 = uiDoc.Document.GetElement(r);


                //CreateElement(elements1, elem3);
                //CreateAnyElementInLinkedFiles(elem);


                TaskDialog.Show("Complite!", $"Размещено {elements1.Count} элементов!");
                t.Commit();


                return Result.Succeeded;
            }
        }

        private void CreateElement(List<Element> elemList, Element elementSplinkler)
        {


            FamilyInstance familyInstance = elementSplinkler as FamilyInstance;
            FamilySymbol familySymbol = familyInstance.Symbol;

            var level2 = familyInstance.LevelId;

            Level level1 = GetLevelFromRoom(level2);

            foreach (var element in elemList)
            {

                ImportInstance importInstance = element as ImportInstance;

                if (importInstance != null)
                {

                    //var transf = importInstance.GetTransform().Origin;
                    XYZ transf = (element.get_BoundingBox(_doc.ActiveView).Min + element.get_BoundingBox(_doc.ActiveView).Max) / 2;



                    FamilyInstance instLight = _doc.Create.NewFamilyInstance(
                              transf, familySymbol, level1, StructuralType.NonStructural);

                }

            }



        }


        private Level GetLevelFromRoom(ElementId elementId)
        {
            FilteredElementCollector lvlCollector = new FilteredElementCollector(_doc);
            ICollection<Element> lvlCollection = lvlCollector.OfClass(typeof(Level)).ToElements();

            foreach (Element l in lvlCollection)
            {
                if (l.Id == elementId)
                {
                    return (Level)l;
                }

            }

            return null;

        }

        public static Pipe CreateNewPipe(Document document, ElementId systemTypeId, ElementId levelId, myPipeFromLine lineList)
        {
            // find a pipe type

            FilteredElementCollector collector = new FilteredElementCollector(document);
            collector.OfClass(typeof(PipeType));
            PipeType pipeType = collector.FirstElement() as PipeType;

            Pipe pipe = null;

            if (null != pipeType)
            {
                // create pipe between 2 points
                //XYZ p1 = new XYZ(0, 0, 0);

                //XYZ p2 = new XYZ(10, 0, 0);

                XYZ p1 = lineList.firstPoint;

                XYZ p2 = lineList.lastPoint;

                pipe = Pipe.Create(document, systemTypeId, pipeType.Id, levelId, p1, p2);
            }

            return pipe;
        }

    }

    public class myPipeFromLine
    {
        public double dPipe { get; set; }
        public XYZ firstPoint { get; set; }
        public XYZ lastPoint { get; set; }


    }
}