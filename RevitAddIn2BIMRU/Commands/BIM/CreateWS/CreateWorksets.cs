#region Namespaces
using System.Windows;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

#endregion

namespace RevitAddIn2BIMRU.Commands.BIM.CreateWS

{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]

    public class CreateWorksets : Window, IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            ViewWPF viewWpf = new ViewWPF();

            viewWpf.ShowDialog();

            //get list workSet
            List<string> listWS=viewWpf.listWs; 

            if (listWS.Count>=2)
            {

                int countWorkset = listWS.Count; //5

                //last workset with 2 chair
                string lastWS = listWS[listWS.Count - 1]; //4\r\n

                int intLast = lastWS.Length; //3


                lastWS = lastWS.Remove(lastWS.Length - 2);


                listWS[listWS.Count - 1] = lastWS;

                doc.EnableWorksharing($"{listWS[0]}", $"{listWS[1]}");

                //Workset newWorkset = null;
                // Worksets can only be created in a document with worksharing enabled
                if (doc.IsWorkshared)
                {
                    if (countWorkset > 2)
                    {
                        for (int j = 1; j < countWorkset - 1; j++)
                        {
                            if (listWS[j + 1] != null)
                            {
                                string worksetName = listWS[j + 1];

                                // Workset name must not be in use by another workset
                                if (WorksetTable.IsWorksetNameUnique(doc, worksetName))
                                {
                                    using (Transaction worksetTransaction = new Transaction(doc, "Set preview view id"))
                                    {
                                        worksetTransaction.Start();

                                        Workset newWorkset = Workset.Create(doc, worksetName);
                                        WorksetTable daf = doc.GetWorksetTable();

                                        daf.SetActiveWorksetId(newWorkset.Id);
                                        worksetTransaction.Commit();
                                    }
                                }
                            }

                        }

                    }

                }
            }

            return Result.Succeeded;
        }
    }
}