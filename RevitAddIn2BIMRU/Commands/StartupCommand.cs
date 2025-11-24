using Autodesk.Revit.Attributes;

using Nice3point.Revit.Toolkit.External;

using RevitAddIn2BIMRU.ViewModels;
using RevitAddIn2BIMRU.Views;

namespace RevitAddIn2BIMRU.Commands
{
    /// <summary>
    ///     External command entry point
    /// </summary>
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : ExternalCommand
    {
        public override void Execute()
        {
            var viewModel = new RevitAddIn2BIMRUViewModel();
            var view = new RevitAddIn2BIMRUView(viewModel);
            view.ShowDialog();
        }
    }
}