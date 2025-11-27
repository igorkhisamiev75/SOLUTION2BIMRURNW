using System.Windows;

namespace RevitAddIn2BIMRU.Commands.AR
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Level selectedLevel;
        public string selectedElement;
        public string selectedElement2;
        public bool doorCheck;
        public bool windowCheck;
        public string whatsButton;

        public MainWindow(List<Level> levelCollection, ICollection<Element> lintelsCollection)
        {
            InitializeComponent();

            cmbLevels.ItemsSource = levelCollection;
            cmbLevels.SelectedIndex = 0;
            cmbLevels.DisplayMemberPath = "Name";
            
        }

        private void createLintels_Click(object sender, RoutedEventArgs e)
        {
            selectedLevel = cmbLevels.SelectedItem as Level;
            selectedElement= txtName.Text;
            selectedElement2= txtName2.Text;

            whatsButton = "все";

            //check box door
            if (cbDoors.IsChecked.GetValueOrDefault())
            {
                doorCheck = true;
            }
            else
            {
                doorCheck = false;
            }

            //check box window
            if (cbWindows.IsChecked.GetValueOrDefault())
            {
                windowCheck = true;
            }
            else
            {
                windowCheck = false;
            }



            Close();
        }

        private void createLintel_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Не жми пока сюда ");

            Close();
        }

        private void btnDelAll_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("И сюда тоже");

            Close();

        }
    }
}
