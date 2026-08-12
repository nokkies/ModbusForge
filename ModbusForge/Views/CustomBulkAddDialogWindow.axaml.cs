using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class CustomBulkAddDialogWindow : Window
    {
        private CustomBulkAddDialogViewModel? _viewModel;

        public CustomBulkAddDialogWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
            }

            _viewModel = DataContext as CustomBulkAddDialogViewModel;

            if (_viewModel != null)
            {
                _viewModel.RequestClose += ViewModel_RequestClose;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel?.Close();
            base.OnClosed(e);
        }

        private void ViewModel_RequestClose(object? sender, EventArgs e)
        {
            Close();
        }
    }
}
