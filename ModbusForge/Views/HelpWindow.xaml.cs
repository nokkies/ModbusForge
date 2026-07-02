using System.Windows;
using ModbusForge.ViewModels;

namespace ModbusForge.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow(HelpViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
