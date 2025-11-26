using Autodesk.Revit.UI;

using Nice3point.Revit.Extensions;
using Nice3point.Revit.Toolkit.External;

using RevitAddIn2BIMRU.Commands;

using System;
using System.Collections.Generic;
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
            string panelNameElectric = "MEP-Надстройки";
            string panelNameAnnotation = "AN-Аннотации";
            string panelNameBim = "BIM-Менеджер";
            string panelNameInfo = "Info";


            // Создаем основную панель Rooms (как в вашем исходном коде)
            RibbonPanel roomsPanel = Application.CreatePanel(panelNameRooms, tabName);
            RibbonPanel arPanel = Application.CreatePanel(panelNameAR, tabName);
            RibbonPanel aiPanel = Application.CreatePanel(panelNameAI, tabName);
            RibbonPanel mepPanel = Application.CreatePanel(panelNameElectric, tabName);
            RibbonPanel anPanel = Application.CreatePanel(panelNameAnnotation, tabName);
            RibbonPanel bimPanel = Application.CreatePanel(panelNameBim, tabName);
            RibbonPanel infoPanel = Application.CreatePanel(panelNameInfo, tabName);



            infoPanel.AddPushButton<StartupCommand>("Ссылка на сайт")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png");

            //roomsPanel.AddSeparator();

            roomsPanel.AddPushButton<CreateNameSet>("Создать имена для помещений")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")
                .SetToolTip("Описание второй команды");

            roomsPanel.AddPushButton<ThirdCommand>("Третья команда")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")
                .SetToolTip("Описание третьей команды");

            roomsPanel.AddPushButton<FourthCommand>("Четвертая команда")
                .SetImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/RevitAddIn2BIMRU;component/Resources/Icons/RibbonIcon32.png")
                .SetToolTip("Описание четвертой команды");

            // Интегрируем кнопки из старого кода
            //AddBIMv2Buttons();
        }

        //private void AddBIMv2Buttons()
        //{
        //    // Получаем путь к сборке
        //    string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

        //    // Создаем вкладку 2BIM.RU
      

        //    // Загружаем изображения
        //    BitmapImage TImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-t-16.png");
        //    BitmapImage NImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-circled-n-16.png");
        //    BitmapImage binImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-bin-16.png");

        //    // Панель Rooms
        //    AddRoomsPanel(ribbonPanelArRoom, thisAssemblyPath, TImage, NImage, binImage);

        //    // Панель AR-Архитектура
        //    AddArchitecturePanel(ribbonPanelAR, thisAssemblyPath);

        //    // Панель AI-Дизайн
        //    AddDesignPanel(ribbonPanelAi, thisAssemblyPath);

        //    // Панель MEP-Надстройки
        //    AddElectricPanel(ribbonPanelElectric, thisAssemblyPath);

        //    // Панель AN-Аннотации
        //    AddAnnotationPanel(ribbonPanelAnnotation, thisAssemblyPath);

        //    // Панель BIM
        //    AddBIMPanel(ribbonPanelBIM, thisAssemblyPath, TImage, NImage, binImage);

        //    // Панель Info
        //    AddInfoPanel(ribbonPanelInfo, thisAssemblyPath);
        //}

        //private void AddRoomsPanel(RibbonPanel panel, string assemblyPath, BitmapImage TImage, BitmapImage NImage, BitmapImage binImage)
        //{
        //    // Создаем отдельные кнопки вместо стека с настройкой текста
        //    var button1 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Имена помещений",
        //            "Имена помещений",
        //            assemblyPath,
        //            "BIMv2.CreateNameSet")
        //        {
        //            Image = NImage,
        //            LargeImage = NImage,
        //            ToolTip = "Создать имена помещений по списку с разделителем точка-запятая ;",
        //            ToolTipImage = NImage
        //        });

        private void CreateRibbonTab(string tabNameB2)
        {
            throw new NotImplementedException();
        }

        private RibbonPanel CreatePanel(string tabNameB2, string panelNameRooms)
        {
            throw new NotImplementedException();
        }

        private void AddRoomsPanel(RibbonPanel panel, string assemblyPath, BitmapImage TImage, BitmapImage NImage, BitmapImage binImage)
        {
            string buttonName3 = "Имена помещений";
            string buttonName4 = "Удалить неразмещенные";
            string buttonName5 = "Прописать тип квартиры";

        //    var button3 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Прописать тип квартиры",
        //            "Прописать тип квартиры",
        //            assemblyPath,
        //            "BIMv2.WriteTypeRooms")
        //        {
        //            Image = TImage,
        //            LargeImage = TImage,
        //            ToolTip = "Записать тип квартиры",
        //            ToolTipImage = TImage
        //        });
        //}

        //private void AddArchitecturePanel(RibbonPanel panel, string assemblyPath)
        //{
        //    // Загружаем изображения
        //    BitmapImage SImageAR1 = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-bd-16.png");
        //    BitmapImage SImageAR2 = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-microsoft-excel-16.png");
        //    BitmapImage SImageAR3 = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8.png");

        //    // Создаем отдельные кнопки вместо стека
        //    var button1 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Перемычкер",
        //            "Перемычки",
        //            assemblyPath,
        //            "LintelsOnTheFloor2.LintelsOnTheFloor2")
        //        {
        //            Image = SImageAR1,
        //            LargeImage = SImageAR1,
        //            ToolTip = "Создать перемычки и кайфовать",
        //            ToolTipImage = SImageAR1
        //        });

        //    var button2 = panel.AddPushButton(
        //        new PushButtonData(
        //            "ВыгрузкаВExcel",
        //            "Выгрузка в Excel",
        //            assemblyPath,
        //            "BIMv2.ExportScheduleToExcel")
        //        {
        //            Image = SImageAR2,
        //            LargeImage = SImageAR2,
        //            ToolTip = "Открыть спецификацию с картинками для выгрузки",
        //            ToolTipImage = SImageAR2
        //        });

        //    var button3 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Копирование помещений из АР в свою модель",
        //            "Копирование помещений из АР в свою модель",
        //            assemblyPath,
        //            "BIMv2.CopyRoomsFromLinkCommand")
        //        {
        //            Image = SImageAR3,
        //            LargeImage = SImageAR3,
        //            ToolTip = "Копирование помещений из АР в свою модель",
        //            ToolTipImage = SImageAR3
        //        });

        //    // Split button для отверстий
        //    var splitBtnHl = panel.AddSplitButton("SplitButtonHl", "Отверстия");

        //    splitBtnHl.AddPushButton(new PushButtonData(
        //        "cmdMakingHolesForWindows.cs",
        //        "Отверстия для окон",
        //        assemblyPath,
        //        "BIMv2.MakingHolesForWindows")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-doors-32.png")
        //    });

        //    splitBtnHl.AddPushButton(new PushButtonData(
        //        "cmdMakingHolesForFacadeWindows.cs",
        //        "Отверстия для фасада",
        //        assemblyPath,
        //        "BIMv2.MakingHolesForFacadeWindows")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-doorsArm-32.png")
        //    });

        //    splitBtnHl.AddPushButton(new PushButtonData(
        //        "cmdMakingHolesForFacadeWindowsDoors.cs",
        //        "Отверстия окна, двери, фасад",
        //        assemblyPath,
        //        "BIMv2.MakingHolesForFacadeWindowsDoors")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-backet-32.png")
        //    });
        //}

        //private void AddDesignPanel(RibbonPanel panel, string assemblyPath)
        //{
        //    // Кнопка создания отделки
        //    panel.AddPushButton<CreteFinishWallsCommand>("Отделка")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-paint-bucket-32.png"))
        //        .SetToolTip("Создать отделочные стены, потолки и полы в помещении");

        //    // Кнопка отделки лестницы
        //    panel.AddPushButton<FinishStairsCommand>("Отделка лестницы")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-stairs-32.png"))
        //        .SetToolTip("Сделать отделку на лестнице");

        //    // Split button для подсчета отделки
        //    var splitBtnCount = panel.AddSplitButton("SplitButtonCount", "Подсчет отделки");

        //    splitBtnCount.AddPushButton(new PushButtonData(
        //        "cmdCountFinishWall.cs",
        //        "Посчитать отделку - стены",
        //        assemblyPath,
        //        "BIMv2.CountFinishWall")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-floor-32.png")
        //    });

        //    splitBtnCount.AddPushButton(new PushButtonData(
        //        "cmdCountFinishFloor.cs",
        //        "Посчитать отделку - полы",
        //        assemblyPath,
        //        "BIMv2.CountFinishFloor")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-floor-32.png")
        //    });

        //    splitBtnCount.AddPushButton(new PushButtonData(
        //        "cmdCountFinishCeiling.cs",
        //        "Посчитать отделку - потолки",
        //        assemblyPath,
        //        "BIMv2.CountFinishCeiling")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-floor-32.png")
        //    });

        //    // Split button для группировки
        //    var splitBtnGroup = panel.AddSplitButton("SplitButtonGR", "Группировка");

        //    splitBtnGroup.AddPushButton(new PushButtonData(
        //        "cmdGroupTypeFloorByNote.cs",
        //        "Сгруппировать полы",
        //        assemblyPath,
        //        "BIMv2Buttons.GroupTypeFloorByNote")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-floor-32.png")
        //    });

        //    splitBtnGroup.AddPushButton(new PushButtonData(
        //        "cmdGroupTypeCeilingByNote.cs",
        //        "Сгруппировать потолки",
        //        assemblyPath,
        //        "BIMv2Buttons.GroupTypeCeilingByNote")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-floor-32.png")
        //    });

        //    splitBtnGroup.AddPushButton(new PushButtonData(
        //        "cmdGroupTypeWallsByNote.cs",
        //        "Сгруппировать стены",
        //        assemblyPath,
        //        "BIMv2Buttons.GroupTypeWallsByNote")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-floor-32.png")
        //    });
        //}

        //private void AddElectricPanel(RibbonPanel panel, string assemblyPath)
        //{
        //    // Split button для электрики
        //    var splitBtnES = panel.AddSplitButton("SplitButtonES", "Электрика");

        //    splitBtnES.AddPushButton(new PushButtonData(
        //        "cmdCreatePipeSectionView",
        //        "Разрез линейного элемента",
        //        assemblyPath,
        //        "BIMv2.CreatePipeSectionView")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-double-arrows32.png")
        //    });

        //    splitBtnES.AddPushButton(new PushButtonData(
        //        "cmdNormalIlluminationForLightFix.cs",
        //        "Записать освещенности",
        //        assemblyPath,
        //        "BIMv2.NormalIlluminationForLightFix")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-nl-32.png")
        //    });

        //    splitBtnES.AddPushButton(new PushButtonData(
        //        "cmdCreateLightingInRoom.cs",
        //        "Расставить светильники",
        //        assemblyPath,
        //        "BIMv2.CreateLightingInRoom")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-cl-32.png")
        //    });

        //    splitBtnES.AddPushButton(new PushButtonData(
        //        "cmdSumAllLightingFixtureInRoom.cs",
        //        "Посчитать светильники",
        //        assemblyPath,
        //        "BIMv2.SumAllLightingFixtureInRoom")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-sl-32.png")
        //    });
        //}

        //private void AddAnnotationPanel(RibbonPanel panel, string assemblyPath)
        //{
        //    // Кнопка создания разверток
        //    panel.AddPushButton<CreateRoomUnfoldsCommand>("Создание разверток")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-r-32.png"))
        //        .SetToolTip("Создать разрезы в помещении и разместить их на новом листе");

        //    // Кнопка разреза по стене
        //    panel.AddPushButton<WallSectionCommand>("Разрез по стене")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-double-arrows32.png"))
        //        .SetToolTip("Создаем разрез по стене");

        //    // Кнопка копии листа
        //    panel.AddPushButton<DuplicateSheetsCommand>("Копия листа")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-sheets-32.png"))
        //        .SetToolTip("Скопировать лист с видами размещенным на нем");

        //    // Кнопка осей 2D/3D
        //    panel.AddPushButton<ConvertGrids3Dto2DCommand>("Оси 2D/3D поменять")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-2d-16.png"))
        //        .SetToolTip("Перевести оси в 2D либо 3D");
        //}

        //private void AddBIMPanel(RibbonPanel panel, string assemblyPath, BitmapImage TImage, BitmapImage NImage, BitmapImage binImage)
        //{
        //    // Создаем отдельные кнопки вместо стека
        //    var button1 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Рабочие наборы",
        //            "Рабочие наборы",
        //            assemblyPath,
        //            "BIMv2.CreateWorksets")
        //        {
        //            Image = NImage,
        //            LargeImage = NImage,
        //            ToolTip = "Создать рабочие наборы в файле",
        //            ToolTipImage = NImage
        //        });

        //    var button2 = panel.AddPushButton(
        //        new PushButtonData(
        //            "3D связей",
        //            "3D связей",
        //            assemblyPath,
        //            "BIMv2.CreateViewByLinkedFiles")
        //        {
        //            Image = binImage,
        //            LargeImage = binImage,
        //            ToolTip = "Создать 3D виды для связанных файлов",
        //            ToolTipImage = binImage
        //        });

        //    var button3 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Создать коллизии",
        //            "Создать коллизии",
        //            assemblyPath,
        //            "BIMv2.CreateViewForCollisions")
        //        {
        //            Image = TImage,
        //            LargeImage = TImage,
        //            ToolTip = "Создает 3D виды коллизий по отчету из Navisworks в формате.xml",
        //            ToolTipImage = TImage
        //        });

        //    // Вторая группа кнопок
        //    BitmapImage NImage2 = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-chain-16.png");

        //    var button4 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Групповая загрузка файлов",
        //            "Групповая загрузка файлов",
        //            assemblyPath,
        //            "BIMv2.LoadFilesAutoSharedCoordinatesCommand")
        //        {
        //            Image = NImage2,
        //            LargeImage = NImage2,
        //            ToolTip = "Групповая загрузка Revit файлов и создание рабочих наборов",
        //            ToolTipImage = NImage2
        //        });

        //    var button5 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Создание 3D видов поэтажно",
        //            "Создание 3D видов поэтажно",
        //            assemblyPath,
        //            "BIMv2.CreateLevel3DViews")
        //        {
        //            Image = binImage,
        //            LargeImage = binImage,
        //            ToolTip = "Создание 3D видов поэтажно",
        //            ToolTipImage = binImage
        //        });

        //    var button6 = panel.AddPushButton(
        //        new PushButtonData(
        //            "Создать коллизии2",
        //            "Создать коллизии2",
        //            assemblyPath,
        //            "BIMv2.CreateViewForCollisions")
        //        {
        //            Image = TImage,
        //            LargeImage = TImage,
        //            ToolTip = "Создает 3D виды коллизий по отчету из Navisworks в формате.xml",
        //            ToolTipImage = TImage
        //        });

        //    // Кнопка удаления фильтров
        //    panel.AddPushButton<DeletedFilterCommand>("Удалить фильтры")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-clean-32.png"))
        //        .SetToolTip("Удалить фильтры");

        //    // Кнопка очистки проекта
        //    panel.AddPushButton<ClearProjectBrowserCommand>("Удалить всё")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-clean3-32.png"))
        //        .SetToolTip("Удалить всё кроме активного вида");

        //    // Split button для BIM операций
        //    var splitBtnBIM = panel.AddSplitButton("SplitButtonBIM", "BIM операции");

        //    splitBtnBIM.AddPushButton(new PushButtonData(
        //        "cmdCreateModelTextRoomNameInRoom.cs",
        //        "Помещения в 3D",
        //        assemblyPath,
        //        "BIMv2.CreateModelTextRoomNameInRoom")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-sign-32.png")
        //    });

        //    splitBtnBIM.AddPushButton(new PushButtonData(
        //        "cmdBIMv2.CreateFromDWG.cs",
        //        "Элементы по DWG",
        //        assemblyPath,
        //        "BIMv2.CreateFromDWG")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-sign-32.png")
        //    });

        //    splitBtnBIM.AddPushButton(new PushButtonData(
        //        "cmdCreateFromDWGPipe.cs",
        //        "Трубы по DWG",
        //        assemblyPath,
        //        "BIMv2.CreateFromDWGPipe")
        //    {
        //        LargeImage = LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/icons8-sign-32.png")
        //    });
        //}

        //private void AddInfoPanel(RibbonPanel panel, string assemblyPath)
        //{
        //    // Кнопка блога
        //    panel.AddPushButton<HelpBIMBlogCommand>("Blog 2bim.ru")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/blog2bim.png"))
        //        .SetToolTip("Подписываемся, ставим лайки, комментируем");

        //    // Кнопка поддержки
        //    panel.AddPushButton<HelpBIMTelegramCommand>("На кофе программисту")
        //        .SetLargeImage(LoadBitmapImage("pack://application:,,,/RevitAddIn2BIMRU;component/Resources/blog2bim.png"))
        //        .SetToolTip("Подписываемся, ставим лайки, комментируем");
        //}

        //private BitmapImage LoadBitmapImage(string uriString)
        //{
        //    try
        //    {
        //        return new BitmapImage(new Uri(uriString, UriKind.RelativeOrAbsolute));
        //    }
        //    catch
        //    {
        //        // Возвращаем заглушку если изображение не найдено
        //        return new BitmapImage();
        //    }
        //}
    }
}
