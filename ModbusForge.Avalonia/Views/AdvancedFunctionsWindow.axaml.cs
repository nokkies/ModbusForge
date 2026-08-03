using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class AdvancedFunctionsWindow : Window
    {
        public AdvancedFunctionsWindow()
        {
            InitializeComponent();
        }

        public AdvancedFunctionsWindow(AdvancedFunctionsViewModel viewModel) : this()
        {
            ArgumentNullException.ThrowIfNull(viewModel);
            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
