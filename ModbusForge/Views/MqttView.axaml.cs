using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModbusForge.Avalonia.Views
{
    public partial class MqttView : UserControl
    {
        public MqttView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
