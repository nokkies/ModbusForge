using System.Windows;

namespace ModbusForge
{
    public partial class KeyboardShortcutsWindow : Window
    {
        public KeyboardShortcutsWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
