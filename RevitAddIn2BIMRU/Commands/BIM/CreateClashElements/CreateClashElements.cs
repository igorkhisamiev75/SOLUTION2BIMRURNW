#region Namespace
using System.Xml;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System.Xml.Linq;
using Autodesk.Revit.DB.Structure;
using System.Globalization;
using System.Windows.Forms;

#endregion

namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateClashElements : IExternalCommand

    {
        private Document _doc;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)

        {

            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

            _doc = uiDoc.Document;

            Document doc = commandData.Application.ActiveUIDocument.Document;
            //List<int> ints = new List<int>();

            List<pointAndNameClash> nameClashes = new List<pointAndNameClash>();

            string strfilename; //имя файла
            string fullPath = "";

            FamilySymbol symbol = GetSymbol(_doc, "Для подписи пересечений", "Для подписи пересечений");

            // get a ViewFamilyType for a 3D View
            ViewFamilyType viewFamilyType =
                (from v in new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                 where v.ViewFamily == ViewFamily.ThreeDimensional
                 select v).First();

            using (var t = new Transaction(doc))
            {

                t.Start("Create view for collisions");

                OpenFileDialog fbd = new OpenFileDialog();

                fbd.Title = "Загрузи XML с коллизиями";
                fbd.Filter = "XML files|*.xml";

                if (fbd.ShowDialog() == DialogResult.OK)
                {

                    strfilename = fbd.FileName;

                    //получение пути
                    fullPath = strfilename;

                }



                XmlDocument xDoc = new XmlDocument();
                xDoc.Load($@"{fullPath}");
                // получим корневой элемент
                XmlElement xRoot = xDoc.DocumentElement;

                XDocument doci = XDocument.Load($@"{fullPath}");

                IFormatProvider formatter = new NumberFormatInfo { NumberDecimalSeparator = "." };

                // Then parse like this:
                var clashTests = doci.Descendants("clashtest")
                    .Select(t1 => new ClashTest
                    {
                        Name = t1.Attribute("name")?.Value,
                        TestType = t1.Attribute("test_type")?.Value,
                        Status = t1.Attribute("status")?.Value,
                        Results = t1.Descendants("clashresult")
                            .Select(r => new ClashResult
                            {
                                Name = r.Attribute("name")?.Value,
                                Guid = r.Attribute("guid")?.Value,
                                X = double.Parse(r.Descendants("pos3f").FirstOrDefault()?.Attribute("x")?.Value, formatter),
                                Y = double.Parse(r.Descendants("pos3f").FirstOrDefault()?.Attribute("y")?.Value, formatter),
                                Z = double.Parse(r.Descendants("pos3f").FirstOrDefault()?.Attribute("z")?.Value, formatter)
                            })
                            .ToList()
                    })
                    .ToList();


                TaskDialog.Show("Колличество коллизий = ", $"{clashTests[0].Results.Count}");

                foreach (var clash in clashTests[0].Results)
                {
                    //clash.X = UnitUtils.ConvertFromInternalUnits(clash.X,
                    double feetX = UnitUtils.Convert(clash.X, UnitTypeId.Meters, UnitTypeId.Feet);
                    double feetY = UnitUtils.Convert(clash.Y, UnitTypeId.Meters, UnitTypeId.Feet);
                    double feetZ = UnitUtils.Convert(clash.Z, UnitTypeId.Meters, UnitTypeId.Feet);


                    XYZ pointRoom = new XYZ(feetX, feetY, feetZ);
                    string name = clash.Name;

                    FamilyInstance familyInstance = _doc.Create.NewFamilyInstance(
                       pointRoom, symbol, StructuralType.NonStructural);

                    familyInstance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set(name);

                }


                t.Commit();

                return Result.Succeeded;


            }

        }

        public FamilySymbol GetSymbol(Document document, string familyName, string symbolName)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Family)).OfType<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName))?.GetFamilySymbolIds()
                .Select(id => document.GetElement(id)).OfType<FamilySymbol>().FirstOrDefault(symbol => symbol.Name.Equals(symbolName));
        }


    }

    class pointAndNameClash
    {
        public string clashName { get; set; }
        public double xPoint { get; set; }
        public double yPoint { get; set; }
        public double zPoint { get; set; }


    }


    public class ClashTest
    {
        public string Name { get; set; }
        public string TestType { get; set; }
        public string Status { get; set; }
        public List<ClashResult> Results { get; set; } = new List<ClashResult>();
    }

    public class ClashResult
    {
        public string Name { get; set; }
        public string Guid { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}