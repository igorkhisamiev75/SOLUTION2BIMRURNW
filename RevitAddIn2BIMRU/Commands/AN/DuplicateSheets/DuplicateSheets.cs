using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace RevitAddIn2BIMRU.Commands.AN
{
    [TransactionAttribute(TransactionMode.Manual)]
    class DuplicateSheets : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Check the current active view
            View selView = doc.ActiveView;
            ViewSheet vSheet = doc.ActiveView as ViewSheet;

            //Retrieve titleblock from current sheet and all elements in view
            FamilyInstance titleblock = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                            .OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilyInstance>()
                            .FirstOrDefault(q => q.OwnerViewId == vSheet.Id);


            //FamilyInstance titleblock = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
            //               .OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilyInstance>()
            //               .FirstOrDefault();

            //var titleblock = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
            //               .OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilyInstance>()
            //               . GetEnumerator(q => q.Name == "нет");

            var elementsInViewId = new FilteredElementCollector(doc, selView.Id).ToElementIds();

            // Retrieve viewports in view
            FilteredElementCollector viewPorts = new FilteredElementCollector(doc, selView.Id)
                .OfClass(typeof(Viewport));

            // Retrieve schedules in project
            FilteredElementCollector schedules = new FilteredElementCollector(doc)
                .OwnedByView(selView.Id).OfClass(typeof(ScheduleSheetInstance));

            // Retrieve viewSchedules все спецухи
            FilteredElementCollector viewSchedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule));

            // Store copied elements and annotation elements
            var copiedElementIds = new List<ElementId>();
            var annotationElementsId = new List<ElementId>();

            using (Transaction t = new Transaction(doc, "Duplicate Sheet"))
            {
                // Start transaction to duplicate sheet
                t.Start();

                // Duplicate sheet
                ViewSheet newsheet = ViewSheet.Create(doc, titleblock.GetTypeId());

                newsheet.SheetNumber = vSheet.SheetNumber + "-COPY2";
                newsheet.Name = vSheet.Name;

                // Get origin of the titleblock
                XYZ originTitle = titleblock.GetTransform().Origin;

                // Check titleblock position
                Element copyTitleBlock = new FilteredElementCollector(doc)
                    .OwnedByView(newsheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();

                LocationPoint titleLoc = copyTitleBlock.Location as LocationPoint;
                XYZ titleLocPoint = titleLoc.Point;

                // Check if title block is in the same position as original
                if (titleLocPoint.DistanceTo(originTitle) != 0)
                {
                    // Move it in case it is not
                    titleLoc.Move(originTitle);
                }

                // Retrieve all views placed on sheet except schedules
                foreach (ElementId eId in vSheet.GetAllPlacedViews())
                {
                    View origView = doc.GetElement(eId) as View;
                    View newView = null;

                    // Legends
                    if (origView.ViewType == ViewType.Legend)
                    {
                        newView = origView;
                    }
                    // Rest of view types
                    else
                    {
                        if (origView.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
                        {
                            ElementId newViewId = origView.Duplicate(ViewDuplicateOption.WithDetailing);
                            newView = doc.GetElement(newViewId) as View;
                            newView.Name = origView.Name + "-COPY";
                        }
                    }


                    // Loop through viewports
                    foreach (Viewport vp in viewPorts)
                    {
                        if (vp.SheetId == vSheet.Id && vp.ViewId == origView.Id)
                        {
                            // Retrieve centerpoint of original viewport
                            XYZ center = vp.GetBoxCenter();
                            // Create viewport in the original spot
                            Viewport newVp = Viewport.Create(doc, newsheet.Id, newView.Id, center);
                        }
                        // Add element in copied list
                        copiedElementIds.Add(vp.Id);
                    }
                    // Add element in copied list
                    copiedElementIds.Add(eId);
                }

                // Retrieve and copy schedules
                foreach (ScheduleSheetInstance sch in schedules) //те что на виде
                {
                    // Check schedule is not a revision inside titleblock
                    if (!sch.IsTitleblockRevisionSchedule)
                    {
                        foreach (ViewSchedule vsc in viewSchedules)
                        {
                            if (sch.ScheduleId == vsc.Id)
                            {
                                // Retrieve center of schedule
                                XYZ schCenter = sch.Point;

                                // Create schedule in the same position
                                ScheduleSheetInstance newSch = ScheduleSheetInstance
                                    .Create(doc, newsheet.Id, vsc.Id, schCenter);

                                copiedElementIds.Add(sch.Id);
                            }
                            //copiedElementIds.Add(vsc.Id);
                            //doc.Regenerate();
                        }
                    }
                }


                // Duplicate annotation elements
                foreach (ElementId eId in elementsInViewId)
                {
                    if (!copiedElementIds.Contains(eId))
                    {
                        annotationElementsId.Add(eId);
                    }
                }

                // Copy annotation elements
                ElementTransformUtils.CopyElements(selView, annotationElementsId, newsheet, null, null);


                // Use ElementClassFilter to find family instances whose name is 60" x 30" Student 
                ElementClassFilter filter = new ElementClassFilter(typeof(FamilyInstance));


                var n = newsheet.GetDependentElements(filter);

                //doc.Regenerate();

                doc.Delete(n[0]);

                TaskDialog.Show("2BIM.RU", $"Лист {newsheet.Name} с видами, спецификациями и легендами  скопирован");

                // Commit transaction
                t.Commit();

                return Result.Succeeded;
            }
        }
    }
}