using System;

namespace RevitAddIn2BIMRU.Commands
{
    // Класс для хранения всех GUID параметров
    public static class ParameterGuids
    {
        // GUID
        public static readonly Guid RoomNumber = new Guid("d7624888-6b1d-4398-9ea3-ed7cffc064ad"); //2BIMRU_НомерПомещения
        public static readonly Guid WallFloorCellingFinishArea = new Guid("bbea7cff-9dfd-40e5-9ab6-7c99d2f76791"); // 2BIMRU_ОбТипаПлощадьСтПерПотол
        public static readonly Guid WallFloorCellingFinishFromType = new Guid("258f4ef6-9e04-4c9f-9ed8-b690e2cc16a2"); // 2BIMRU_ИзТипаВЭкземплярСтПерПотол
        
        //Wall
        public static readonly Guid WallFinishCombined = new Guid("28bff0bf-8833-47dc-b17b-d3fb196a6dcd"); // 2BIMRU_ОбОтдСтПом
        public static readonly Guid WallAreaCombined = new Guid("423d5454-3c52-4075-89ec-d1577d069059"); // 2BIMRU_ОбПлощадьСт

        //floor
        public static readonly Guid FloorFinishCombined = new Guid("c8ba05cd-49c8-49d5-8eaa-3c2318d70123"); // 2BIMRU_ОбОтдПерПом
        public static readonly Guid FloorAreaCombined = new Guid("2bdea5ed-2995-4b5d-b92e-95aec33ce304"); // 2BIMRU_ОбПлощадьПер

        //Celling
        public static readonly Guid CellingFinishCombined = new Guid("c123fc4b-d331-4b30-b268-c0f134647a57"); // 2BIMRU_ОбОтдПотПом
        public static readonly Guid CellingAreaCombined = new Guid("48dc9837-ceb2-450b-94ea-49e10f77d4c2"); // 2BIMRU_ОбПлощадьПер




    }
}