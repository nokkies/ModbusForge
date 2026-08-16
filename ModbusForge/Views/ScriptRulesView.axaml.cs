using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModbusForge.Avalonia.Views
{
    public partial class ScriptRulesView : UserControl
    {
        public ScriptRulesView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
