using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

using RevitAddIn2BIMRU.ViewModels;
using RevitAddIn2BIMRU.Views;

using System.Diagnostics;

namespace RevitAddIn2BIMRU.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : ExternalCommand
    {
        public override void Execute()
        {
            // Открываем сайт при запуске команды
            OpenWebsite("https://2bim.ru/");

            //var viewModel = new RevitAddIn2BIMRUViewModel();
            //var view = new RevitAddIn2BIMRUView(viewModel);
            //TaskDialog.Show("1", "11");
            //view.ShowDialog();
        }

        private void OpenWebsite(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Не удалось открыть сайт: {ex.Message}");
            }
        }
    }
}