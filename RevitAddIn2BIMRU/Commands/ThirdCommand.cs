using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

namespace RevitAddIn2BIMRU.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class ThirdCommand : ExternalCommand
    {
        public override void Execute()
        {
            // Реализация третьей команды
            TaskDialog.Show("Третья команда", "Выполняется третья команда!");
        }
    }
}