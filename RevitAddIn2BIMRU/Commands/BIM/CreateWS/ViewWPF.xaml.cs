using System.Collections.Generic;
using System.Windows;

namespace RevitAddIn2BIMRU.Commands.BIM.CreateWS
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

            string[] subStrings = textWorkSets.Split(' ');

            foreach (var VARIABLE in subStrings)
            {
               listWs.Add(VARIABLE);  
            }

            MessageBox.Show("Будет создано " + listWs.Count + " рабочих наборов");
            Close();

        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("А нахера открывали???");
            Close();
        }
    }
}
