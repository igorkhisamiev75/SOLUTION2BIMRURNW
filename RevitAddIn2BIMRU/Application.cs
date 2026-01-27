using Autodesk.Revit.UI;

using Nice3point.Revit.Toolkit.External;

using RevitAddIn2BIMRU.Commands;
using RevitAddIn2BIMRU.Commands.AI;
using RevitAddIn2BIMRU.Commands.AN;
using RevitAddIn2BIMRU.Commands.AR;
using RevitAddIn2BIMRU.Commands.BIM;
using RevitAddIn2BIMRU.Commands.BIM.CreateWS;
using RevitAddIn2BIMRU.Commands.INFO;
using RevitAddIn2BIMRU.Commands.INFO.HelpBIM;
using RevitAddIn2BIMRU.Commands.MEP;
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
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/ukaz16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/ukaz32.png")),
                ToolTip = "Создаются неразмещенные помещения по списку"
            };

            PushButtonData delUnplacedCommandButtonData = new PushButtonData(
                "DelUnplacedRoom",
                "Удалить неразмещенные помещения",
                Assembly.GetExecutingAssembly().Location,
                typeof(DelUnplacedRoom).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/del16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/del32.png")),
                ToolTip = "Удалить неразмещенные помещения"
            };

            PushButtonData delUnplacedCommandButtonData2 = new PushButtonData(
               "DelUnplacedAreas",
               "Удалить неразмещенные зоны",
               Assembly.GetExecutingAssembly().Location,
               typeof(DelUnplacedAreas).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/del16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/del32.png")),
                ToolTip = "Удалить неразмещенные зоны"
            };

            PushButtonData writyTypeRoomCommandButtonData = new PushButtonData(
                "WriteTypeRooms",
                "Прописать тип квартиры",
                Assembly.GetExecutingAssembly().Location,
                typeof(WriteTypeRooms).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/ukaz16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/ukaz32.png")),
                ToolTip = "Прописать тип квартиры для шаблона Б2"
            };

            PushButtonData writyTypeRoomCommandButtonData2 = new PushButtonData(
             "CalculateDoorWindowAreas",
             "Расчет S проемов в помещении(окна, двери, витражи, разделители)",
             Assembly.GetExecutingAssembly().Location,
             typeof(CalculateDoorWindowAreas).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/ukaz16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/ukaz32.png")),
                ToolTip = "Расчет S проемов в помещении(окна, двери, витражи, разделители)"
            };

            // Добавляем кнопки в SplitButton
            roomsSplitButton.AddPushButton(createNameSetButtonData);
            roomsSplitButton.AddPushButton(delUnplacedCommandButtonData);
            roomsSplitButton.AddPushButton(delUnplacedCommandButtonData2);
            roomsSplitButton.AddPushButton(writyTypeRoomCommandButtonData);
            roomsSplitButton.AddPushButton(writyTypeRoomCommandButtonData2);


            //Архитектра

            // Создаем SplitButton для панели AR
            SplitButtonData splitButtonDataAR = new SplitButtonData("arSplitButton", "Операции для AR");
            SplitButton arSplitButton = arPanel.AddItem(splitButtonDataAR) as SplitButton;

            // Добавляем 4 кнопки в SplitButton AR
            PushButtonData ar1SetButtonData = new PushButtonData(
                "LintelsOnTheFloor",
                "Перемычки",
                Assembly.GetExecutingAssembly().Location,
                typeof(LintelsOnTheFloor).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Перемычки"
            };

            PushButtonData ar2SetButtonData = new PushButtonData(
                "DisconnectWallsOptimized",
                "Отмена примыканий для стен",
                Assembly.GetExecutingAssembly().Location,
                typeof(DisconnectWallsOptimized).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Отмена примыканий для стен"
            };

            PushButtonData ar3SetButtonData = new PushButtonData(
                "WallExtensionCommand",
                "Создание сеток - Проект LSR",
                Assembly.GetExecutingAssembly().Location,
                typeof(WallExtensionCommand).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Создаем стены поверх других стен"
            };

            //PushButtonData ar4SetButtonData = new PushButtonData(
            //    "AROperation4",
            //    "AR операция 4",
            //    Assembly.GetExecutingAssembly().Location,
            //    typeof(YourARCommand4).FullName)
            //{
            //    Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
            //    LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
            //    ToolTip = "Описание AR функции 4"
            //};

            // Добавляем все 4 кнопки в SplitButton AR
            arSplitButton.AddPushButton(ar1SetButtonData);
            arSplitButton.AddPushButton(ar2SetButtonData);
            arSplitButton.AddPushButton(ar3SetButtonData);
            //arSplitButton.AddPushButton(ar4SetButtonData);

            // Создаем SplitButton для панели AI
            SplitButtonData splitButtonDataAI = new SplitButtonData("aiSplitButton", "Операции для AI");
            SplitButton aiSplitButton = aiPanel.AddItem(splitButtonDataAI) as SplitButton;

            // Добавляем 4 кнопки в SplitButton AI
            PushButtonData ai1SetButtonData = new PushButtonData(
                "CreteFinishWalls",
                "Отделка помещений",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreteFinishWalls).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/kist16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/kist32.png")),
                ToolTip = "Создать отделочные стены, потолки и полы в помещении, *потолоки с 23 ревита"
            };

            PushButtonData ai2SetButtonData = new PushButtonData(
                "FinishingOnTheStairs",
                "Отделка лестницы",
                Assembly.GetExecutingAssembly().Location,
                typeof(FinishingOnTheStairs).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/kist16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/kist32.png")),
                ToolTip = "Сделать отделку на лестнице"
            };

            //PushButtonData ai3SetButtonData = new PushButtonData(
            //    "AIOperation3",
            //    "AI операция 3",
            //    Assembly.GetExecutingAssembly().Location,
            //    typeof(YourAICommand3).FullName)
            //{
            //    Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
            //    LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
            //    ToolTip = "Описание AI функции 3"
            //};

            //PushButtonData ai4SetButtonData = new PushButtonData(
            //    "AIOperation4",
            //    "AI операция 4",
            //    Assembly.GetExecutingAssembly().Location,
            //    typeof(YourAICommand4).FullName)
            //{
            //    Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
            //    LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
            //    ToolTip = "Описание AI функции 4"
            //};

            // Добавляем все 4 кнопки в SplitButton AI
            aiSplitButton.AddPushButton(ai1SetButtonData);
            aiSplitButton.AddPushButton(ai2SetButtonData);
            //aiSplitButton.AddPushButton(ai3SetButtonData);
            //aiSplitButton.AddPushButton(ai4SetButtonData);

            //MEP настройки

            // Создаем SplitButton для панели Rooms
            SplitButtonData splitButtonDataMEP = new SplitButtonData("mepSplitButton", "Операции для МЕР");
            SplitButton mepSplitButton = mepPanel.AddItem(splitButtonDataMEP) as SplitButton;

            // Добавляем кнопки в SplitButton
            PushButtonData mep1SetButtonData = new PushButtonData(
                "SumAllLightingFixtureInRoom",
                "Посчитать светильники",
                Assembly.GetExecutingAssembly().Location,
                typeof(SumAllLightingFixtureInRoom).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/lamp16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/lamp32.png")),
                ToolTip = "Подсчет количества светильников в помещении и запись в марку ADSK_Количество светильников"
            };

            PushButtonData mep2SetButtonData = new PushButtonData(
                "NormalIlluminationForLightFix",
                "Записать освещенность",
                Assembly.GetExecutingAssembly().Location,
                typeof(NormalIlluminationForLightFix).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/lamp16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/lamp32.png")),
                ToolTip = "Записать освещенность в пространства"
            };

            PushButtonData mep3SetButtonData = new PushButtonData(
                "CreateLightingInRoom",
                "Создание светильников в помещениях",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateLightingInRoom).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/lamp16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/lamp32.png")),
                ToolTip = "Создание светильников в помещениях"
            };

            PushButtonData mep4SetButtonData = new PushButtonData(
               "CreatePipeSectionView",
               "Разрер по элементу",
               Assembly.GetExecutingAssembly().Location,
               typeof(CreatePipeSectionView).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sect16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sect32.png")),
                ToolTip = "Построить разрез по лотку-трубе-воздуховоду"
            };

            // Добавляем кнопки в SplitButton
            mepSplitButton.AddPushButton(mep1SetButtonData);
            mepSplitButton.AddPushButton(mep2SetButtonData);
            mepSplitButton.AddPushButton(mep3SetButtonData);
            mepSplitButton.AddPushButton(mep4SetButtonData);


            // Создаем SplitButton для панели AN
            SplitButtonData splitButtonDataAN = new SplitButtonData("anSplitButton", "Операции для AN");
            SplitButton anSplitButton = anPanel.AddItem(splitButtonDataAN) as SplitButton;

            // Добавляем кнопки в SplitButton AN
            PushButtonData an1SetButtonData = new PushButtonData(
                "CreateRoomUnfolds",
                "Создание разверток",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateRoomUnfolds).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol32.png")),
                ToolTip = "Создать разрезы в помещении и разместить их на новом листе"
            };

            PushButtonData an2SetButtonData = new PushButtonData(
                "WallSection",
                "Разрез по стене",
                Assembly.GetExecutingAssembly().Location,
                typeof(WallSection).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol32.png")),
                ToolTip = "Создает разрез по стене"
            };

            PushButtonData an3SetButtonData = new PushButtonData(
                "DuplicateSheets",
                "Копия листа",
                Assembly.GetExecutingAssembly().Location,
                typeof(DuplicateSheets).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol32.png")),
                ToolTip = "Скопировать лист с видами размещенным на нем"
            };

            PushButtonData an4SetButtonData = new PushButtonData(
                "ConvertGrids3Dto2D",
                "Оси 2D/3D",
                Assembly.GetExecutingAssembly().Location,
                typeof(ConvertGrids3Dto2D).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol32.png")),
                ToolTip = "Перевести оси в 2D либо 3D"
            };

            PushButtonData an5SetButtonData = new PushButtonData(
                "MarkElementsByPosition",
                "Маркировка элементов",
                Assembly.GetExecutingAssembly().Location,
                typeof(MarkElementsByGrid).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol32.png")),
                ToolTip = "Маркировка элементов по своим правилам"
            };

            PushButtonData an6SetButtonData = new PushButtonData(
               "MoveAllRoomTagsSmart",
               "Перенос марок помещений",
               Assembly.GetExecutingAssembly().Location,
               typeof(MoveAllRoomTagsSmart).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol32.png")),
                ToolTip = "Перенос марок помещений, с указанием своего допуска"
            };

            // Добавляем кнопки в SplitButton AN
            anSplitButton.AddPushButton(an1SetButtonData);
            anSplitButton.AddPushButton(an2SetButtonData);
            anSplitButton.AddPushButton(an3SetButtonData);
            anSplitButton.AddPushButton(an4SetButtonData);
            anSplitButton.AddPushButton(an5SetButtonData);
            anSplitButton.AddPushButton(an6SetButtonData);

            // Создаем SplitButton для панели BIM
            SplitButtonData splitButtonDataBIM = new SplitButtonData("bimSplitButton", "Операции для BIM");
            SplitButton bimSplitButton = bimPanel.AddItem(splitButtonDataBIM) as SplitButton;

            // Добавляем 10 кнопок в SplitButton BIM
            PushButtonData bim1SetButtonData = new PushButtonData(
                "CreateWorksets",
                "Рабочие наборы",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateWorksets).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Создать рабочие наборы в файле"
            };

            PushButtonData bim2SetButtonData = new PushButtonData(
                "CreateViewByLinkedFiles",
                "Создание 3D связей",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateViewByLinkedFiles).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Создать 3D виды для связанных файлов"
            };

            PushButtonData bim3SetButtonData = new PushButtonData(
                "CreateViewForCollisions",
                "Создать коллизии",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateViewForCollisions).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/sol32.png")),
                ToolTip = "Создает 3D виды коллизий по отчету из Navisworks в формате.xml"
            };

            PushButtonData bim4SetButtonData = new PushButtonData(
                "LoadFilesAutoSharedCoordinatesCommand",
                "Групповая загрузка файлов",
                Assembly.GetExecutingAssembly().Location,
                typeof(LoadFilesAutoSharedCoordinatesCommand).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Групповая загрузка Revit файлов и создание рабочих наборов под каждый файл"
            };

            PushButtonData bim5SetButtonData = new PushButtonData(
                "CreateLevel3DViews",
                "3D виды поэтажно",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateLevel3DViews).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Создание 3D видов поэтажно"
            };

            PushButtonData bim6SetButtonData = new PushButtonData(
                "DeletedFilter",
                "Удалить фильтры",
                Assembly.GetExecutingAssembly().Location,
                typeof(DeletedFilter).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/del16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/del32.png")),
                ToolTip = "Удалить фильтры"
            };

            PushButtonData bim7SetButtonData = new PushButtonData(
                "ClearProjectBrowser",
                "Удалить всё в проекте",
                Assembly.GetExecutingAssembly().Location,
                typeof(ClearProjectBrowser).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/del16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/del32.png")),
                ToolTip = "Удалить всё кроме активного вида"
            };

            PushButtonData bim8SetButtonData = new PushButtonData(
                "CreateFromDWG",
                "Элементы по DWG",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateFromDWG).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Элементы по DWG"
            };

            PushButtonData bim9SetButtonData = new PushButtonData(
                "CreateFromDWGPipe",
                "Трубы по DWG",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateFromDWGPipe).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Трубы по DWG"
            };

            PushButtonData bim10SetButtonData = new PushButtonData(
                "CreateClashElements",
                "Точки из Navisworks",
                Assembly.GetExecutingAssembly().Location,
                typeof(CreateClashElements).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Точки из Navisworks"
            };

            PushButtonData bim11SetButtonData = new PushButtonData(
             "CreateModelTextRoomNameInRoom",
             "Подписать помещения для 3D",
             Assembly.GetExecutingAssembly().Location,
             typeof(CreateModelTextRoomNameInRoom).FullName)
            {
                Image = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")),
                LargeImage = new BitmapImage(new Uri("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")),
                ToolTip = "Подписать помещения для 3D, семейство Для подписи помещений"
            };

            // Добавляем все 10 кнопок в SplitButton BIM
            bimSplitButton.AddPushButton(bim1SetButtonData);
            bimSplitButton.AddPushButton(bim2SetButtonData);
            bimSplitButton.AddPushButton(bim3SetButtonData);
            bimSplitButton.AddPushButton(bim4SetButtonData);
            bimSplitButton.AddPushButton(bim5SetButtonData);
            bimSplitButton.AddPushButton(bim6SetButtonData);
            bimSplitButton.AddPushButton(bim7SetButtonData);
            bimSplitButton.AddPushButton(bim8SetButtonData);
            bimSplitButton.AddPushButton(bim9SetButtonData);
            bimSplitButton.AddPushButton(bim10SetButtonData);
            bimSplitButton.AddPushButton(bim11SetButtonData);

            //сайт разработчика
            infoPanel.AddPushButton<StartupCommand>("Сайт разработчика")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")
                .SetToolTip("Открыть сайт и написать разработчику");

            infoPanel.AddPushButton<HelpBIM>("На кофе программисту")
             .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/coffee16.png")
             .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/coffee32.png")
             .SetToolTip("На кофе программисту");
        }

       
    }
}
