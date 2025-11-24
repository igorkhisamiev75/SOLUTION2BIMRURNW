using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

namespace RevitAddIn2BIMRU.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class FourthCommand : ExternalCommand
    {
        public override void Execute()
        {
            // Реализация четвертой команды
            TaskDialog.Show("Четвертая команда", "Выполняется четвертая команда!");
        }
    }
}