using global::Avalonia.Controls;
using global::Avalonia.Markup.Xaml;

namespace ModbusForge.Avalonia.Views
{
    public partial class MainView : global::Avalonia.Controls.UserControl
    {
        public MainView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }
    }
}
