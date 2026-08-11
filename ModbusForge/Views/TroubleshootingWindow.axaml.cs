using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ModbusForge.Avalonia.Views
{
    public partial class TroubleshootingWindow : Window
    {
        public TroubleshootingWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
