using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class PreferencesWindow : Window
    {
        public PreferencesWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public PreferencesWindow(PreferencesViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.RequestClose += (s, saved) =>
            {
                if (saved)
                {
                    Close(true);
                }
                else
                {
                    Close(false);
                }
            };
        }
    }
}
