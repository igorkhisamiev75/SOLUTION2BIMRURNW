#region Namespaces
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;

#endregion

namespace RevitAddIn2BIMRU.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
 
    public class CreateNameSet : Window, IExternalCommand
    {

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            ViewWPF viewWpf = new ViewWPF();
            viewWpf.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            viewWpf.ShowDialog();

            //get list 
            List<string> listWS=viewWpf.listWs; //0 1 2 3 4

            //Document doc = commandData.Application.ActiveUIDocument.Document;
            Phase newConstructionPhase = doc.Phases.get_Item(1);

            using (var t = new Transaction(doc))
            {
                t.Start("Create Rooms");


                try
                {
                    // create room using Phase

                    int k = 0;

                    foreach (var roomName in listWS)
                    {
                        Room newScheduleRoom = doc.Create.NewRoom(newConstructionPhase);

                        // set the Room Number and Name
                        string newRoomNumber = "удалить";
                        string newRoomName = roomName;
                        newScheduleRoom.Name = newRoomName;
                        newScheduleRoom.Number = newRoomNumber;

                        //doc.Regenerate();
                        k++;
                    }

                    t.Commit();
                    return Result.Succeeded;

                }



                catch (Exception ex)
                {
                    TaskDialog.Show("Revit", ex.Message);
                    return Result.Failed;
                }

            }
        }
    }
}