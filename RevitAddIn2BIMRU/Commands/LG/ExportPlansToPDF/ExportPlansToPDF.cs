using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RevitAddIn2BIMRU.Commands.LG
{
    [Transaction(TransactionMode.Manual)]
    public class ExportPlansToPDF : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Пожалуйста, выберите один или несколько планов этажей в дереве проекта.");
                    return Result.Failed;
                }

                List<ViewPlan> viewPlans = new List<ViewPlan>();
                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem is ViewPlan viewPlan)
                        viewPlans.Add(viewPlan);
                }

                if (viewPlans.Count == 0)
                {
                    TaskDialog.Show("Ошибка", "Среди выбранных элементов нет ни одного плана этажа (ViewPlan).");
                    return Result.Failed;
                }

                string confirmMsg = $"Будет экспортировано {viewPlans.Count} планов в отдельные PDF-файлы. Продолжить?";
                if (TaskDialog.Show("Подтверждение", confirmMsg, TaskDialogCommonButtons.Yes) != TaskDialogResult.Yes)
                    return Result.Cancelled;

                using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.Description = "Выберите папку для сохранения PDF-файлов";
                    folderDialog.ShowNewFolderButton = true;
                    if (folderDialog.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;

                    string outputPath = folderDialog.SelectedPath;
                    int exportedCount = 0;

#if REVIT2022_OR_GREATER
                    // ---------- Revit 2022+ : нативный PDF-экспорт ----------
                    foreach (ViewPlan viewPlan in viewPlans)
                    {
                        try
                        {
                            string cleanName = string.Join("_", viewPlan.Name.Split(Path.GetInvalidFileNameChars()));
                            if (string.IsNullOrWhiteSpace(cleanName))
                                cleanName = "UnnamedView";

                            PDFExportOptions options = new PDFExportOptions
                            {
                                FileName = cleanName,
                                Combine = false,
                                ExportQuality = PDFExportQualityType.DPI300
                            };

                            List<ElementId> viewsToExport = new List<ElementId> { viewPlan.Id };
                            doc.Export(outputPath, viewsToExport, options);
                            exportedCount++;
                        }
                        catch (Exception ex)
                        {
                            TaskDialog.Show("Ошибка", $"Не удалось экспортировать вид '{viewPlan.Name}': {ex.Message}");
                        }
                    }
#else
                    // ---------- Revit 2021 и старше : печать через PrintManager ----------
                    PrintManager printManager = doc.PrintManager;

                    using (PrinterSelectionForm form = new PrinterSelectionForm())
                    {
                        if (form.ShowDialog() != DialogResult.OK)
                            return Result.Cancelled;

                        string selectedPrinter = form.SelectedPrinterName;
                        if (string.IsNullOrEmpty(selectedPrinter))
                        {
                            TaskDialog.Show("Ошибка", "Принтер не выбран.");
                            return Result.Failed;
                        }

                        printManager.SelectNewPrintDriver(selectedPrinter);
                        printManager.PrintToFile = true;
                        printManager.PrintRange = Autodesk.Revit.DB.PrintRange.Select;
                        printManager.Apply();
                    }

                    foreach (ViewPlan viewPlan in viewPlans)
                    {
                        try
                        {
                            if (!viewPlan.CanBePrinted)
                            {
                                TaskDialog.Show("Предупреждение", $"Вид '{viewPlan.Name}' не может быть напечатан. Пропускаем.");
                                continue;
                            }

                            string cleanName = string.Join("_", viewPlan.Name.Split(Path.GetInvalidFileNameChars()));
                            if (string.IsNullOrWhiteSpace(cleanName))
                                cleanName = "UnnamedView";

                            string fullPath = Path.Combine(outputPath, cleanName + ".pdf");
                            printManager.PrintToFileName = fullPath;
                            printManager.Apply();

                            printManager.SubmitPrint(viewPlan);
                            exportedCount++;
                        }
                        catch (Exception ex)
                        {
                            TaskDialog.Show("Ошибка", $"Не удалось экспортировать вид '{viewPlan.Name}': {ex.Message}");
                        }
                    }
#endif

                    TaskDialog.Show("Успешно", $"Экспортировано {exportedCount} из {viewPlans.Count} планов в папку:\n{outputPath}");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка экспорта", $"Не удалось выполнить экспорт: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    // ---------- Форма выбора принтера (исправленный конфликт имён) ----------
    public class PrinterSelectionForm : System.Windows.Forms.Form   // <-- явное указание
    {
        private System.Windows.Forms.ComboBox comboBoxPrinters;
        private Button buttonOK;
        private Button buttonCancel;

        public string SelectedPrinterName { get; private set; }

        public PrinterSelectionForm()
        {
            InitializeComponent();
            LoadPrinters();
        }

        private void InitializeComponent()
        {
            this.comboBoxPrinters = new System.Windows.Forms.ComboBox();
            this.buttonOK = new Button();
            this.buttonCancel = new Button();
            this.SuspendLayout();

            this.comboBoxPrinters.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBoxPrinters.FormattingEnabled = true;
            this.comboBoxPrinters.Location = new System.Drawing.Point(12, 12);
            this.comboBoxPrinters.Name = "comboBoxPrinters";
            this.comboBoxPrinters.Size = new System.Drawing.Size(360, 21);
            this.comboBoxPrinters.TabIndex = 0;

            this.buttonOK.Location = new System.Drawing.Point(216, 50);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(75, 23);
            this.buttonOK.TabIndex = 1;
            this.buttonOK.Text = "OK";
            this.buttonOK.UseVisualStyleBackColor = true;
            this.buttonOK.Click += new EventHandler(this.ButtonOK_Click);

            this.buttonCancel.Location = new System.Drawing.Point(297, 50);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(75, 23);
            this.buttonCancel.TabIndex = 2;
            this.buttonCancel.Text = "Отмена";
            this.buttonCancel.UseVisualStyleBackColor = true;
            this.buttonCancel.Click += new EventHandler(this.ButtonCancel_Click);

            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(384, 85);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOK);
            this.Controls.Add(this.comboBoxPrinters);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "PrinterSelectionForm";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "Выбор PDF-принтера";
            this.ResumeLayout(false);
        }

        private void LoadPrinters()
        {
            comboBoxPrinters.Items.Clear();

            try
            {
                var printerNames = PrinterSettings.InstalledPrinters;

                if (printerNames == null || printerNames.Count == 0)
                {
                    comboBoxPrinters.Items.Add("Нет доступных принтеров");
                    comboBoxPrinters.SelectedIndex = 0;
                    buttonOK.Enabled = false;
                    return;
                }

                foreach (string name in printerNames)
                    comboBoxPrinters.Items.Add(name);

                comboBoxPrinters.SelectedIndex = 0;
                buttonOK.Enabled = true;
            }
            catch (Exception ex)
            {
                comboBoxPrinters.Items.Add($"Ошибка: {ex.Message}");
                comboBoxPrinters.SelectedIndex = 0;
                buttonOK.Enabled = false;
            }
        }

        private void ButtonOK_Click(object sender, EventArgs e)
        {
            if (comboBoxPrinters.SelectedItem == null ||
                comboBoxPrinters.SelectedItem.ToString() == "Нет доступных принтеров" ||
                comboBoxPrinters.SelectedItem.ToString().StartsWith("Ошибка"))
            {
                DialogResult = DialogResult.None;
                return;
            }
            SelectedPrinterName = comboBoxPrinters.SelectedItem.ToString();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}