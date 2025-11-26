using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

using RevitAddIn2BIMRU.Commands;
using RevitAddIn2BIMRU.Commands.INFO;

using System.Reflection;
using System.Windows.Media.Imaging;

namespace RevitAddIn2BIMRU
{
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        public override void OnStartup()
        {
            CreateRibbon();
        }

        private void CreateRibbon()
        {
            // Создаем вкладку 2BIM.RU
            string tabName = "2BIM.RUv2.0";
            //CreateRibbonTab(tabNameB2);

            // Определяем названия панелей
            string panelNameRooms = "Rooms";
            string panelNameAR = "AR-Архитектура";
            string panelNameAI = "AI-Дизайн";
            string panelNameMEP = "MEP-Надстройки";
            string panelNameAnnotation = "AN-Аннотации";
            string panelNameBim = "BIM-Менеджер";
            string panelNameInfo = "Info";

            // Создаем основную панели
            RibbonPanel roomsPanel = Application.CreatePanel(panelNameRooms, tabName);
            RibbonPanel arPanel = Application.CreatePanel(panelNameAR, tabName);
            RibbonPanel aiPanel = Application.CreatePanel(panelNameAI, tabName);
            RibbonPanel mepPanel = Application.CreatePanel(panelNameMEP, tabName);
            RibbonPanel anPanel = Application.CreatePanel(panelNameAnnotation, tabName);
            RibbonPanel bimPanel = Application.CreatePanel(panelNameBim, tabName);
            RibbonPanel infoPanel = Application.CreatePanel(panelNameInfo, tabName);

            // Создаем SplitButton для панели Rooms
            SplitButtonData splitButtonData = new SplitButtonData("RoomsSplitButton", "Операции с помещениями");
            SplitButton roomsSplitButton = roomsPanel.AddItem(splitButtonData) as SplitButton;

            // Добавляем кнопки в SplitButton
            PushButtonData createNameSetButtonData = new PushButtonData(
                "CreateNameSet",
                "Создать имена помещений",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateNameSet).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Создаются неразмещенные помещения по списку"
            };

            PushButtonData delUnplacedCommandButtonData = new PushButtonData(
                "DelUnplacedRoom",
                "Удалить неразмещенные помещения",
                Assembly.GetExecutingAssembly().Location,
                typeof(DelUnplacedRoom).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Удалить неразмещенные помещения"
            };

            PushButtonData writyTypeRoomCommandButtonData = new PushButtonData(
                "WriteTypeRooms",
                "Прописать тип квартиры",
                Assembly.GetExecutingAssembly().Location,
                typeof(WriteTypeRooms).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Описание четвертой команды"
            };

            // Добавляем кнопки в SplitButton
            roomsSplitButton.AddPushButton(createNameSetButtonData);
            roomsSplitButton.AddPushButton(delUnplacedCommandButtonData);
            roomsSplitButton.AddPushButton(writyTypeRoomCommandButtonData);

            //сайт разработчика
            infoPanel.AddPushButton<StartupCommand>("Сайт разработчика")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")
                .SetToolTip("Открыть сайт и написать разработчику");
        }

       
    }
}
