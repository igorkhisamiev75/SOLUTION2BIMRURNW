using Autodesk.Revit.DB;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAddIn2BIMRU.Commands.AI
{
    /// <summary>
    /// Логика взаимодействия для UserControl1.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Document _doc;
        public WallType SelectedWallType { get; private set; }
        public FloorType SelectedFloorType { get; private set; }
        public List<Element> SelectedStairs { get; private set; } = new List<Element>();
        public bool ProcessAllStairs { get; private set; } = false;

        public MainWindow(Document doc)
        {
            _doc = doc;
            InitializeComponent();
            // Заполняем комбобоксы типами стен и перекрытий
            var wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .OrderBy(wt => wt.Name);

            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(ft => ft.Name);

            WallTypeComboBox.ItemsSource = wallTypes;
            FloorTypeComboBox.ItemsSource = floorTypes;

            // Выбираем первые элементы по умолчанию
            if (wallTypes.Any()) WallTypeComboBox.SelectedIndex = 0;
            if (floorTypes.Any()) FloorTypeComboBox.SelectedIndex = 0;
        }

        private void SelectStairsButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedWallType = WallTypeComboBox.SelectedItem as WallType;
            SelectedFloorType = FloorTypeComboBox.SelectedItem as FloorType;
            //ProcessAllStairs = false;
            MessageBox.Show("Выбери лестницу");

            DialogResult = true;
            Close();
        }

        private void ProcessAllStairsButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedWallType = WallTypeComboBox.SelectedItem as WallType;
            SelectedFloorType = FloorTypeComboBox.SelectedItem as FloorType;
            ProcessAllStairs = true;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
