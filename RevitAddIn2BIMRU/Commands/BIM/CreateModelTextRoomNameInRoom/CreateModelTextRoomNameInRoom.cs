#region Namespace

using System.Text;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

#endregion

namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    public class CreateModelTextRoomNameInRoom : IExternalCommand
    {
        
        private Document _doc;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

            
            _doc = uiDoc.Document;

            try
            {
                int createdCount = 0;
                int errorCount = 0;
                string sourceModelName = "";

                using (var t = new Transaction(_doc))
                {
                    t.Start("Create Room Labels from Link");

                    // Выбор связанной модели
                    Document linkedDoc = SelectLinkedDocument(uiApp);
                    if (linkedDoc == null)
                    {
                        TaskDialog.Show("Отмена", "Операция отменена пользователем");
                        return Result.Cancelled;
                    }

                    // Сохраняем имя модели для использования после транзакции
                    sourceModelName = linkedDoc.Title;

                    // Создаем подписи и получаем статистику
                    (createdCount, errorCount) = CreateModelTextMethod(uiDoc, linkedDoc);

                    t.Commit();
                }

                // Формируем сообщение с результатами
                string resultMessage = CreateResultMessage(createdCount, errorCount, sourceModelName);
                TaskDialog.Show("Готово", resultMessage);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Ошибка", $"Произошла ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        public (int createdCount, int errorCount) CreateModelTextMethod(UIDocument uiDocument, Document linkedDocument)
        {
            // Получаем помещения из связанной модели
            var rooms = GetRoomsFromLinkedDocument(linkedDocument);

            FamilySymbol symbol = GetSymbol(_doc, "Для подписи помещений", "Для подписи помещений");

            if (symbol == null)
            {
                throw new Exception("Не найдено семейство 'Для подписи помещений'");
            }

            // Активируем символ семейства, если он не активен
            if (!symbol.IsActive)
                symbol.Activate();

            // Получаем трансформацию связанного файла
            RevitLinkInstance linkInstance = GetRevitLinkInstance(linkedDocument);
            Transform linkTransform = linkInstance?.GetTotalTransform() ?? Transform.Identity;

            int createdCount = 0;
            int errorCount = 0;

            // Создаем model text в помещениях
            foreach (var room in rooms)
            {
                try
                {
                    // Получаем имя и номер помещения
                    var roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Без имени";
                    var roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "Без номера";

                    // Получаем уровень помещения из связанной модели
                    var levelId = room.LevelId;
                    Level linkedLevel = linkedDocument.GetElement(levelId) as Level;

                    if (linkedLevel == null)
                    {
                        errorCount++;
                        continue;
                    }

                    // Находим соответствующий уровень в текущем документе
                    Level currentLevel = FindCorrespondingLevel(linkedLevel);
                    if (currentLevel == null)
                    {
                        errorCount++;
                        continue;
                    }

                    // Получаем точку расположения помещения
                    LocationPoint lpPoint = room.Location as LocationPoint;
                    if (lpPoint == null)
                    {
                        errorCount++;
                        continue;
                    }

                    XYZ pointRoom = lpPoint.Point;

                    // Преобразуем координаты из связанного файла в координаты основного файла
                    XYZ transformedPoint = linkTransform.OfPoint(pointRoom);

                    // Устанавливаем высоту на основе уровня текущего документа + 100 мм
                    double elevation = currentLevel.Elevation + UnitUtils.ConvertToInternalUnits(0.1, UnitTypeId.Meters);
                    XYZ placeXyzPoint = new XYZ(transformedPoint.X, transformedPoint.Y, elevation);

                    // Создаем экземпляр семейства
                    FamilyInstance familyInstance = _doc.Create.NewFamilyInstance(
                        placeXyzPoint, symbol, currentLevel, StructuralType.NonStructural);

                    // Устанавливаем параметры номера и имени помещения
                    SetParameterIfExists(familyInstance, new Guid("e6e0f5cd-3e26-485b-9342-23882b20eb43"), roomNumber);
                    SetParameterIfExists(familyInstance, new Guid("9c98831b-9450-412d-b072-7d69b39f4029"), roomName);

                    // Устанавливаем отметку от уровня в 100 мм
                    SetOffsetFromLevel(familyInstance, 100); // 100 мм

                    createdCount++;

                }
                catch (Exception ex)
                {
                    errorCount++;
                    // Логируем ошибку, но не прерываем выполнение
                    System.Diagnostics.Debug.WriteLine($"Ошибка при обработке помещения {room.Id}: {ex.Message}");
                }
            }

            return (createdCount, errorCount);
        }

        /// <summary>
        /// Устанавливает отметку от уровня для семейства
        /// </summary>
        /// <param name="familyInstance">Экземпляр семейства</param>
        /// <param name="offsetInMillimeters">Смещение в миллиметрах</param>
        private void SetOffsetFromLevel(FamilyInstance familyInstance, double offsetInMillimeters)
        {
            try
            {
                // Конвертируем миллиметры во внутренние единицы Revit (футы)
                double offsetInFeet = UnitUtils.ConvertToInternalUnits(offsetInMillimeters, UnitTypeId.Millimeters);

                // Параметр "Отметка от уровня" или "Offset"
                Parameter offsetParam = familyInstance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);

                if (offsetParam != null && !offsetParam.IsReadOnly)
                {
                    offsetParam.Set(offsetInFeet);
                    return;
                }

                // Альтернативные варианты параметров смещения
                Parameter[] possibleOffsetParams = new[]
                {
                    familyInstance.LookupParameter("Отметка от уровня"),
                    familyInstance.LookupParameter("Смещение от уровня"),
                    familyInstance.LookupParameter("Offset"),
                    familyInstance.LookupParameter("Высота"),
                    familyInstance.LookupParameter("Elevation"),
                    familyInstance.LookupParameter("Высота от уровня")
                };

                foreach (Parameter param in possibleOffsetParams)
                {
                    if (param != null && !param.IsReadOnly && param.StorageType == StorageType.Double)
                    {
                        param.Set(offsetInFeet);
                        return;
                    }
                }

                // Если параметр смещения не найден, устанавливаем высоту через Location
                LocationPoint location = familyInstance.Location as LocationPoint;
                if (location != null)
                {
                    XYZ currentPoint = location.Point;
                    Level hostLevel = familyInstance.Host as Level;

                    if (hostLevel != null)
                    {
                        double newZ = hostLevel.Elevation + offsetInFeet;
                        XYZ newPoint = new XYZ(currentPoint.X, currentPoint.Y, newZ);
                        location.Point = newPoint;
                    }
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибку, но не прерываем выполнение
                System.Diagnostics.Debug.WriteLine($"Не удалось установить смещение от уровня: {ex.Message}");
            }
        }

        /// <summary>
        /// Создает сообщение с результатами работы
        /// </summary>
        private string CreateResultMessage(int createdCount, int errorCount, string sourceModelName)
        {
            StringBuilder message = new StringBuilder();

            message.AppendLine("✅ Операция завершена успешно!");
            message.AppendLine();
            message.AppendLine($"📊 Статистика выполнения:");
            message.AppendLine($"────────────────────────");
            message.AppendLine($"📝 Подписано помещений: {createdCount}");

            if (errorCount > 0)
            {
                message.AppendLine($"⚠️  Пропущено помещений: {errorCount}");
            }

            message.AppendLine($"📁 Источник: {sourceModelName}");
            message.AppendLine($"📏 Отметка от уровня: 100 мм");
            message.AppendLine();

            if (createdCount == 0 && errorCount == 0)
            {
                message.AppendLine("ℹ️  Помещения не найдены в выбранной модели");
            }
            else if (createdCount > 0 && errorCount == 0)
            {
                message.AppendLine("🎉 Все помещения успешно подписаны!");
            }
            else if (createdCount > 0 && errorCount > 0)
            {
                message.AppendLine($"💡 Успешно обработано {createdCount} из {createdCount + errorCount} помещений");
            }
            else
            {
                message.AppendLine("❌ Не удалось подписать ни одного помещения");
            }

            return message.ToString();
        }

        /// <summary>
        /// Находит соответствующий уровень в текущем документе по уровню из связанного
        /// </summary>
        private Level FindCorrespondingLevel(Level linkedLevel)
        {
            // Пытаемся найти уровень с таким же именем
            Level sameNameLevel = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(linkedLevel.Name));

            if (sameNameLevel != null)
                return sameNameLevel;

            // Если уровень с таким именем не найден, ищем уровень с максимально близкой высотой
            double linkedElevation = linkedLevel.Elevation;

            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - linkedElevation))
                .FirstOrDefault();
        }

        /// <summary>
        /// Диалог выбора связанной модели
        /// </summary>
        private Document SelectLinkedDocument(UIApplication uiApp)
        {
            // Получаем все связанные документы
            var linkInstances = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(x => x.GetLinkDocument() != null)
                .ToList();

            if (!linkInstances.Any())
            {
                TaskDialog.Show("Ошибка", "В проекте нет связанных моделей");
                return null;
            }

            // Создаем диалог выбора
            var options = linkInstances.Select(x =>
            {
                var doc = x.GetLinkDocument();
                return $"{x.Name} ({doc.Title})";
            }).ToList();

            options.Insert(0, "Текущая модель");

            var dialog = new TaskDialog("Выбор модели");
            dialog.MainInstruction = "Выберите модель для использования помещений:";
            dialog.CommonButtons = TaskDialogCommonButtons.Cancel;

            // Добавляем радиокнопки для каждой связанной модели
            for (int i = 0; i < options.Count; i++)
            {
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1 + i, options[i]);
            }

            var result = dialog.Show();

            if (result == TaskDialogResult.Cancel)
                return null;

            int selectedIndex = (int)result - (int)TaskDialogCommandLinkId.CommandLink1;

            if (selectedIndex == 0)
            {
                return _doc; // Текущая модель
            }
            else
            {
                return linkInstances[selectedIndex - 1].GetLinkDocument();
            }
        }

        /// <summary>
        /// Получаем экземпляр связи для связанного документа
        /// </summary>
        private RevitLinkInstance GetRevitLinkInstance(Document linkedDoc)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .FirstOrDefault(x => x.GetLinkDocument()?.Title == linkedDoc.Title);
        }

        /// <summary>
        /// Получаем помещения из связанного документа
        /// </summary>
        private IList<Element> GetRoomsFromLinkedDocument(Document linkedDoc)
        {
            var roomCollector = new FilteredElementCollector(linkedDoc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            return roomCollector.ToElements();
        }

        /// <summary>
        /// Безопасная установка параметра
        /// </summary>
        private void SetParameterIfExists(FamilyInstance instance, Guid paramGuid, string value)
        {
            var param = instance.get_Parameter(paramGuid);
            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
            {
                param.Set(value);
            }
        }

        public int GetId(ICollection<ElementId> newElement)
        {
#if REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
            return newElement.FirstOrDefault()?.IntegerValue ?? 0;
#else
            return (int)(newElement.FirstOrDefault()?.Value ?? 0);
#endif
        }

        public IList<Element> GetRoomsOnCurrentProject(Document doc)
        {
            var roomCollector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            return roomCollector.ToElements();
        }

        public FamilySymbol GetSymbol(Document document, string familyName, string symbolName)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName))
                ?.GetFamilySymbolIds()
                .Select(id => document.GetElement(id))
                .OfType<FamilySymbol>()
                .FirstOrDefault(symbol => symbol.Name.Equals(symbolName));
        }

        public Level GetLevelFromRoomById(ElementId elementId)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Id == elementId);
        }
    }
}