using global::Avalonia.Controls;
using global::Avalonia.Markup.Xaml;

namespace ModbusForge.Avalonia.Views
{
    public partial class MainWindow : global::Avalonia.Controls.Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }
    }
}
