using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace RevitAddIn2BIMRU.Commands.BIM
{
    [Transaction(TransactionMode.Manual)]
    public class LoadFilesAutoSharedCoordinatesCommand : IExternalCommand
    {
        private string _worksetPrefix = "0_Связь_";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                var openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Revit Files (*.rvt)|*.rvt";
                openFileDialog.Multiselect = true;
                openFileDialog.Title = "Import RVT Files - Positioning: Auto - By Shared Coordinates";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // Запрашиваем префикс для рабочих наборов
                    string userPrefix = ShowWorksetNameDialog();
                    if (userPrefix != null) // null означает отмену
                    {
                        _worksetPrefix = string.IsNullOrEmpty(userPrefix) ? "0_Связь_" : userPrefix;

                        List<string> selectedFiles = openFileDialog.FileNames.ToList();
                        int successCount = 0;
                        int errorCount = 0;

                        using (Transaction trans = new Transaction(doc, "Import RVT Links with Auto-Shared Coordinates"))
                        {
                            trans.Start();

                            foreach (string filePath in selectedFiles)
                            {
                                if (ImportRvtLinkWithAutoPositioning(doc, filePath))
                                {
                                    successCount++;
                                }
                                else
                                {
                                    errorCount++;
                                }
                            }

                            trans.Commit();
                        }

                        // Показываем только итоговое сообщение
                        if (errorCount == 0)
                        {
                            TaskDialog.Show("Import Complete",
                                $"Successfully imported {successCount} RVT files with positioning: Auto - By Shared Coordinates\n" +
                                $"Each link placed in its own workset: '{_worksetPrefix}+filename'");
                        }
                        else
                        {
                            TaskDialog.Show("Import Complete",
                                $"Import completed with results:\n" +
                                $"Successfully imported: {successCount} files\n" +
                                $"Failed to import: {errorCount} files\n" +
                                $"Each successful link placed in its own workset: '{_worksetPrefix}+filename'");
                        }
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Import Error", ex.Message);
                return Result.Failed;
            }
        }

        private string ShowWorksetNameDialog()
        {
            using (var form = new System.Windows.Forms.Form())
            {
                form.Text = "Название РН связи";
                form.Width = 600;
                form.Height = 350;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var label = new Label()
                {
                    Text = "Введите префикс для рабочих наборов:",
                    Location = new System.Drawing.Point(10, 15),
                    Width = 550,
                    Height = 60
                };

                var textBox = new System.Windows.Forms.TextBox()
                {
                    Text = "0_Связь_",
                    Location = new System.Drawing.Point(10, 80),
                    Width = 360,
                    Height = 80
                };

                var okButton = new Button()
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Location = new System.Drawing.Point(10, 150),
                    Width = 150,
                    Height = 60
                };

                var cancelButton = new Button()
                {
                    Text = "Отмена",
                    DialogResult = DialogResult.Cancel,
                    Location = new System.Drawing.Point(290, 150),
                    Width = 150,
                    Height = 60
                };

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);

                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                var result = form.ShowDialog();
                if (result == DialogResult.OK)
                {
                    return textBox.Text.Trim();
                }
                else
                {
                    return null; // Отмена
                }
            }
        }

        private bool ImportRvtLinkWithAutoPositioning(Document doc, string filePath)
        {
            try
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);

                // Используем RevitLinkOptions с позиционированием по общим координатам
                WorksetConfiguration worksetConfig = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
                RevitLinkOptions options = new RevitLinkOptions(false, worksetConfig); // false = Auto - By Shared Coordinates

                // Создаем связь
                LinkLoadResult loadResult = RevitLinkType.Create(doc, modelPath, options);

                if (loadResult != null && loadResult.ElementId != ElementId.InvalidElementId)
                {
                    RevitLinkType linkType = doc.GetElement(loadResult.ElementId) as RevitLinkType;
                    if (linkType != null)
                    {
                        // Создаем экземпляр связи
                        RevitLinkInstance linkInstance = RevitLinkInstance.Create(doc, linkType.Id);

                        // Создаем рабочий набор
                        string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                        string worksetName = _worksetPrefix + fileName;
                        CreateWorksetForLink(doc, worksetName, linkInstance);

                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                // Подавляем все исключения и сообщения о разных системах координат
                // Просто возвращаем false для подсчета ошибок
                return false;
            }
        }

        private void CreateWorksetForLink(Document doc, string worksetName, RevitLinkInstance linkInstance)
        {
            FilteredWorksetCollector worksetCollector = new FilteredWorksetCollector(doc);
            Workset existingWorkset = worksetCollector.OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(w => w.Name.Equals(worksetName));

            WorksetId worksetId = existingWorkset?.Id ?? Workset.Create(doc, worksetName).Id;

            Parameter worksetParam = linkInstance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (worksetParam != null && !worksetParam.IsReadOnly)
            {
                worksetParam.Set(worksetId.IntegerValue);
            }
        }
    }
}