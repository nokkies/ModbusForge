using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModbusForge.Avalonia.Views
{
    public partial class DecodeView : UserControl
    {
        public DecodeView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
