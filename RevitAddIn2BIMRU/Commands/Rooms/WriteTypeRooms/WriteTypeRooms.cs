#region Namespace

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;




#endregion

namespace RevitAddIn2BIMRU.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WriteTypeRooms : IExternalCommand

    {
        public List<string> RoomNameList = new List<string>();
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)

        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            string adsk_numberRoom = "10fb72de-237e-4b9c-915b-8849b8907695";
            string adsk_typeRoom = "78e3b89c-eb68-4600-84a7-c523de162743";

            using (var t = new Transaction(doc))
            {
                t.Start("Write Type Rooms");

                //get rooms
                var Rooms = GetAllRooms(doc);

                List<Room> rooms = new List<Room>();

                foreach (Element element in Rooms)
                {
                    try
                    {
                        var ggg = (Room)element;
                        //get square 
                        if ((ggg.get_Parameter(BuiltInParameter.ROOM_AREA)).AsDouble() > 0)
                        {
                            rooms.Add(ggg);
                        }

                    }
                    catch (Exception e)
                    {
                        continue;
                    }

                }

                //group by number apartment
                IEnumerable<IGrouping<string, Room>> groupsRoom = rooms.GroupBy(p2 =>
                    p2.get_Parameter(new Guid(adsk_numberRoom)).AsString());

                //del not placedrooms

                try
                {
                    foreach (var variable in groupsRoom)
                    {

                        //посчитаем коэфициент квартир

                        double koefRoom = 0;

                        foreach (var room in variable)
                        {
                            if (room.get_Parameter((BuiltInParameter.ROOM_NAME)).AsString() == "Жилая комната")
                            {
                                koefRoom = koefRoom + 1;
                            }

                            if (room.get_Parameter((BuiltInParameter.ROOM_NAME)).AsString() == "Кухня-гостиная" ||
                               room.get_Parameter((BuiltInParameter.ROOM_NAME)).AsString() == "Кухня")
                            {
                                koefRoom = koefRoom + 0.8;
                            }

                            if (room.get_Parameter((BuiltInParameter.ROOM_NAME)).AsString() == "Кухня-ниша")
                            {
                                koefRoom = koefRoom + 0.5;
                            }

                        }


                        //int i = variable.Count();

                        if (koefRoom == 1.5)
                        {
                            foreach (var room in variable)
                            {
                                room.get_Parameter(new Guid(adsk_typeRoom)).Set("Ст");
                            }
                            continue;

                        }

                        if (koefRoom == 2.5)
                        {
                            foreach (var room in variable)
                            {
                                room.get_Parameter(new Guid(adsk_typeRoom)).Set("2");
                            }
                            continue;

                        }

                        if (koefRoom == 1.8)
                        {
                            foreach (var room in variable)
                            {
                                room.get_Parameter(new Guid(adsk_typeRoom)).Set("1");
                            }
                            continue;

                        }

                        if (koefRoom == 2.8)
                        {
                            foreach (var room in variable)
                            {
                                room.get_Parameter(new Guid(adsk_typeRoom)).Set("2");
                            }
                            continue;

                        }

                        if (koefRoom == 3.5)
                        {
                            foreach (var room in variable)
                            {
                                room.get_Parameter(new Guid(adsk_typeRoom)).Set("3");

                            }
                            continue;

                        }

                        if (koefRoom == 3.8)
                        {
                            foreach (var room in variable)
                            {
                                room.get_Parameter(new Guid(adsk_typeRoom)).Set("3");

                            }
                            continue;

                        }

                        if (koefRoom == 4.5)
                        {
                            foreach (var room in variable)
                            {
                                room.get_Parameter(new Guid(adsk_typeRoom)).Set("4");

                            }
                            continue;

                        }

                        if (koefRoom == 4.8)
                        {
                            foreach (var room in variable)
                            {
                                room.get_Parameter(new Guid(adsk_typeRoom)).Set("4");

                            }
                            continue;
                        }

                        else
                        {

                            foreach (var room in variable)
                            {
                                if ((room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)).AsString() == "Квартиры")
                                {
                                    room.get_Parameter(new Guid(adsk_typeRoom)).Set("ERROR");

                                }
                                continue;
                            }

                        }

                    }

                    TaskDialog.Show("Revit", $"Тип квартир записан для {groupsRoom.Count()} квартир");
                    t.Commit();
                    return Result.Succeeded;

                }



                catch (Exception ex)
                {
                    TaskDialog.Show("Revit", ex.Message);
                    return Result.Failed;
                }

            }



        }

        public IList<Element> GetAllRooms(Document doc)
        {
            // List all Room's
            var room_collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();
            IList<ElementId> room_eids = room_collector.ToElementIds() as IList<ElementId>;
            var allRooms = room_collector.ToElements();

            return allRooms;
        }




    }
}