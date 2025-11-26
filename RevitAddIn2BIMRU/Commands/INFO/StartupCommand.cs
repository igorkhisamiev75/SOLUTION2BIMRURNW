using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

using System.Diagnostics;

namespace RevitAddIn2BIMRU.Commands.INFO
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : ExternalCommand
    {
        public override void Execute()
        {
            // Открываем сайт при запуске команды
            OpenWebsite("https://2bim.ru/");

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
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Не удалось открыть сайт: {ex.Message}");
            }
        }
    }
}