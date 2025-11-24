using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

using RevitAddIn2BIMRU.Commands;

namespace RevitAddIn2BIMRU
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        public override void OnStartup()
        {
            CreateRibbon();
        }

        private void CreateRibbon()
        {
            RibbonPanel panel = Application.CreatePanel("INFO", "RevitAddIn2BIMRU");

            panel.AddPushButton<StartupCommand>("Execute")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png");
        }
    }
}