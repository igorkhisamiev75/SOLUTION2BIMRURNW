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

            panel.AddPushButton<StartupCommand>("Ссылка на сайт ")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png");

            // Добавляем разделитель
            panel.AddSeparator();

            // Вторая кнопка
            panel.AddPushButton<SecondCommand>("Вторая команда")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")
                .SetToolTip("Описание второй команды");

            // Третья кнопка
            panel.AddPushButton<ThirdCommand>("Третья команда")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")
                .SetToolTip("Описание третьей команды");

            // Четвертая кнопка
            panel.AddPushButton<FourthCommand>("Четвертая команда")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")
                .SetToolTip("Описание четвертой команды");
        }
    }
}