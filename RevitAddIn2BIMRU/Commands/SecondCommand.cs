using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

namespace RevitAddIn2BIMRU.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class SecondCommand : ExternalCommand
    {
        public override void Execute()
        {
            // Реализация второй команды
            TaskDialog.Show("Вторая команда", "Выполняется вторая команда!");
        }
    }
}