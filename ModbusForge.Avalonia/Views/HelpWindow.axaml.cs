using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ModbusForge.Avalonia.Views
{
    public partial class HelpWindow : Window
    {
        public string HelpText { get; set; } = "Help content not available.";

        public HelpWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public HelpWindow(string helpText) : this()
        {
            HelpText = helpText;
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
