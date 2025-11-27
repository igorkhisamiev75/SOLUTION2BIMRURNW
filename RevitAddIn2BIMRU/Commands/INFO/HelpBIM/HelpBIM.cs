using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

using System.Diagnostics;

namespace RevitAddIn2BIMRU.Commands.INFO.HelpBIM
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class HelpBIM : ExternalCommand
    {
        public override void Execute()
        {
            // Открываем сайт при запуске команды
            OpenWebsite("https://docs.google.com/document/d/1o17Ta1qpHE-wKsILrAd5hp5gool2WnB0tnfcIPxxyso/edit?usp=sharing");

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
