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

namespace RevitAddIn2BIMRU.Commands
{
    /// <summary>
    /// Логика взаимодействия для ViewWPF.xaml
    /// </summary>
    
    public partial class ViewWPF : Window
    {
        public List<string> listWs = new List<string>();
        public ViewWPF()
        {
            InitializeComponent();
            
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

            string textWorkSets = new System.Windows.Documents.TextRange(RichTextBox.Document.ContentStart, 
                RichTextBox.Document.ContentEnd).Text;

            string[] subStrings = textWorkSets.Split(';');

            foreach (var VARIABLE in subStrings)
            {
               listWs.Add(VARIABLE);  
            }

            MessageBox.Show("Будет создано " + listWs.Count + " имен помещений");
            Close();

        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("А зачем открывали???");
            Close();
        }
    }
}
