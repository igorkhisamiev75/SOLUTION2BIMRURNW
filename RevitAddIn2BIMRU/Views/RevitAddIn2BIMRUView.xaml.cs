using RevitAddIn2BIMRU.ViewModels;

namespace RevitAddIn2BIMRU.Views
{
    public sealed partial class RevitAddIn2BIMRUView
    {
        public RevitAddIn2BIMRUView(RevitAddIn2BIMRUViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}