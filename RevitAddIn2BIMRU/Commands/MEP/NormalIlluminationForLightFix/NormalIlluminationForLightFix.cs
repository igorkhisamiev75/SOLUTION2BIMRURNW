#region Namespace
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
#endregion
namespace RevitAddIn2BIMRU.Commands.MEP
{
    [Transaction(TransactionMode.Manual)]
    public class NormalIlluminationForLightFix : IExternalCommand
    {
        private Application _app;
        private Document _doc;
        public ElementId linkDocId = new ElementId(BuiltInCategory.OST_RvtLinks);
        public List<ElementId> roomids = new List<ElementId>();
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
           // _app = uiApp.Application;
            _doc = uiDoc.Document;
            using (var t = new Transaction(_doc))
            {
                t.Start("Light fixtures");

                WriteLightingFixturesillumination(uiApp);

                var countLigh = GetAllLightFix().Count;

                TaskDialog.Show("У тебя получилось", $"Готово для {countLigh} светильников");
                t.Commit();
               
            }
            return Result.Succeeded;
        }
        public void WriteLightingFixturesillumination(UIApplication uiApp)
        {
            IList<Element> allLight = GetAllLightFix();
            IList<Element> allSpace = GetAllSpace(_doc);

            foreach (var element in allLight)
            {
                //ADSK_Зона
                string nameRoomInLightFix = element.get_Parameter(new Guid("c78f0a7d-b68b-4d21-a247-1c8c6ced8bc5")).AsString();

                foreach (var space in allSpace)
                {
                    var numberSpace = space.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString();
                    var nameSpace = space.get_Parameter(BuiltInParameter.ROOM_NAME).AsString();

                    var normaIlluminationSpace=space.get_Parameter(new Guid("732b6d7a-fb49-47ea-a4fc-bf0558c9808c")).AsString();
                    if (normaIlluminationSpace == null)
                    {
                        normaIlluminationSpace =
                            "Пропиши в пространство норм.освещенность, либо давай запрогаем автоматическое заполнение, по списку";
                    }

                    var numNameSpace = numberSpace +" "+ nameSpace; 

                    if (nameRoomInLightFix == numNameSpace)
                    {
                        element.get_Parameter(new Guid("732b6d7a-fb49-47ea-a4fc-bf0558c9808c"))
                            .Set(normaIlluminationSpace);
                        break;
                    }
                    
                }

                
            }
        }

        public IList<Element> GetAllSpace(Document doc)
        {
            // List all Space's
            var space_collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType();
            IList<Element> allSpaces = space_collector.ToElements();

            return allSpaces;

        }

        public IList<Element> GetAllLightFix()
        {
            // List all LightingFixtures
            var LightingFixturesCollector = new FilteredElementCollector(_doc).OfClass(typeof(FamilyInstance));
            LightingFixturesCollector.OfCategory(BuiltInCategory.OST_LightingFixtures);
            var plFixList = LightingFixturesCollector.ToElements();

            return plFixList;
        }

        
    }
}