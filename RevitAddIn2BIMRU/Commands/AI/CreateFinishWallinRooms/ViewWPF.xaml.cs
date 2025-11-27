using System.Windows;
using System.Windows.Controls;

namespace RevitAddIn2BIMRU.Commands.AI
{
    /// <summary>
    /// Логика взаимодействия для UserControl1.xaml
    /// </summary>
    public partial class ViewWPF : Window
    {
        public List<String> checkRooms = new List<String>();
        public List<String> roomCollection;

        //public RoutedEventHandler routEvent;
        public string whatsButton;
        public bool roomBoording;


        public WallType typeWallCollectionSelected;
        public FloorType typeFloorSelected;
        public CeilingType typeCeilingSelected;

        public Level selectedLevel;
        public string heighWallUser;

        public string ofsetWallS;

        public ViewWPF(List<String> rooms, IList<WallType> typeWallCollection, List<Level> levels, IList<FloorType> typeFloorCollection, IList<CeilingType> typeCeilingCollection)
        {
            InitializeComponent();
            roomCollection = rooms;

            ListBox.ItemsSource = rooms;
            ListBox.DisplayMemberPath = "";

            ComboBox.ItemsSource = typeWallCollection;
            ComboBox.SelectedIndex = 5;
            ComboBox.DisplayMemberPath = "Name";

            ComboBoxLevel.ItemsSource = levels;
            ComboBoxLevel.DisplayMemberPath = "Name";
            ComboBoxLevel.SelectedIndex = 0;

            ComboBoxFloor.ItemsSource = typeFloorCollection;
            ComboBoxFloor.SelectedIndex = 1;
            ComboBoxFloor.DisplayMemberPath = "Name";


            ComboBoxCeiling.ItemsSource = typeCeilingCollection;
            ComboBoxCeiling.SelectedIndex = 1;
            ComboBoxCeiling.DisplayMemberPath = "Name";

        }

        private void cansel_Click(object sender, RoutedEventArgs e)
        {
            selectedLevel = ComboBoxLevel.SelectedItem as Level;
            typeWallCollectionSelected = ComboBox.SelectedValue as WallType;
            heighWallUser = heightWallNotRooms.Text;
            ofsetWallS = ofsetWall.Text;
            //typeWallCollectionSelected = ComboBox.SelectedValue as WallType;
            //MessageBox.Show("Выбрано " + typeWallCollectionSelected.ToString());

            //var nameWallType = typeWallCollectionSelected.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME).AsString();
            //MessageBox.Show("Выбрано " + nameWallType);

            Close();
        }



        public void finish1_Click(object sender, RoutedEventArgs e)
        {
            selectedLevel = ComboBoxLevel.SelectedItem as Level;
            typeWallCollectionSelected = ComboBox.SelectedValue as WallType;
            heighWallUser = heightWallNotRooms.Text;
            ofsetWallS = ofsetWall.Text;

            whatsButton = "отделкаВыборомПомещения";

            if (chekRoomBoarding.IsChecked.GetValueOrDefault())
            {
                roomBoording = true;
            }
            else
            {
                roomBoording = false;
            }

            Close();
        }

        private void finishAll_Click(object sender, RoutedEventArgs e)
        {
            //высота стен
            heighWallUser = heightWallNotRooms.Text;
            //смещение
            ofsetWallS = ofsetWall.Text;

            selectedLevel = ComboBoxLevel.SelectedItem as Level;

            typeWallCollectionSelected = ComboBox.SelectedValue as WallType;
            whatsButton = "отделкаПоСпеке";

            if (chekRoomBoarding.IsChecked.GetValueOrDefault())
            {
                roomBoording = true;
            }
            else
            {
                roomBoording = false;
            }

            foreach (var room in roomCollection)
            {
                foreach (var variable in ListBox.SelectedItems)
                {
                    if (room.GetHashCode() == variable.GetHashCode())
                    {
                        checkRooms.Add(room);
                        //MessageBox.Show("Выбрано" + element.Name);

                    }
                }
            }
            //MessageBox.Show("Выбрано " + checkRooms.Count + " комнат для отделки");
            Close();
        }

        private void finishDel_Click(object sender, RoutedEventArgs e)
        {
            if (ListBox.SelectedIndex == -1)
            {
                ListBox.SelectAll();

            }
            else
            {
                ListBox.UnselectAll();
            }

        }

        private void floorAll_Click(object sender, RoutedEventArgs e)
        {
            heighWallUser = heightWallNotRooms.Text;
            ofsetWallS = ofsetWall.Text;
            selectedLevel = ComboBoxLevel.SelectedItem as Level;


            typeFloorSelected = ComboBoxFloor.SelectedValue as FloorType;

            whatsButton = "отделкаПоСпекеПолы";

            if (chekRoomBoarding.IsChecked.GetValueOrDefault())
            {
                roomBoording = true;
            }
            else
            {
                roomBoording = false;
            }

            foreach (var room in roomCollection)
            {
                foreach (var variable in ListBox.SelectedItems)
                {
                    if (room.GetHashCode() == variable.GetHashCode())
                    {
                        checkRooms.Add(room);
                        //MessageBox.Show("Выбрано" + element.Name);

                    }
                }
            }
            //MessageBox.Show("Выбрано " + checkRooms.Count + " комнат для отделки");
            Close();

        }

        private void finishFloorInOneRoom_Click(object sender, RoutedEventArgs e)
        {
            selectedLevel = ComboBoxLevel.SelectedItem as Level;
            typeWallCollectionSelected = ComboBox.SelectedValue as WallType;
            typeFloorSelected = ComboBoxFloor.SelectedValue as FloorType;

            whatsButton = "полыВыборомПомещения";

            Close();

        }
        private void finishAllCeiling_Click(object sender, RoutedEventArgs e)
        {
            heighWallUser = heightWallNotRooms.Text;
            ofsetWallS = ofsetWall.Text;
            selectedLevel = ComboBoxLevel.SelectedItem as Level;

            typeFloorSelected = ComboBoxFloor.SelectedValue as FloorType;
            typeCeilingSelected = ComboBoxCeiling.SelectedValue as CeilingType;

            whatsButton = "отделкаПоСпекеПотолки";

            if (chekRoomBoarding.IsChecked.GetValueOrDefault())
            {
                roomBoording = true;
            }
            else
            {
                roomBoording = false;
            }

            foreach (var room in roomCollection)
            {
                foreach (var variable in ListBox.SelectedItems)
                {
                    if (room.GetHashCode() == variable.GetHashCode())
                    {
                        checkRooms.Add(room);
                        //MessageBox.Show("Выбрано" + element.Name);

                    }
                }
            }
            //MessageBox.Show("Выбрано " + checkRooms.Count + " комнат для отделки");
            Close();
        }
        private void finishCeiling_Click(object sender, RoutedEventArgs e)
        {
            selectedLevel = ComboBoxLevel.SelectedItem as Level;
            typeWallCollectionSelected = ComboBox.SelectedValue as WallType;
            typeCeilingSelected = ComboBoxCeiling.SelectedValue as CeilingType;

            //получить высоту если высота берется не по помещению
            heighWallUser = heightWallNotRooms.Text;


            whatsButton = "отделкаВыборомПомещенияПотолки";

            if (chekRoomBoarding.IsChecked.GetValueOrDefault())
            {
                roomBoording = true;
            }
            else
            {
                roomBoording = false;
            }

            Close();
        }


    }
}
