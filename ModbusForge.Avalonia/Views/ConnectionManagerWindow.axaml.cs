using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class ConnectionManagerWindow : global::Avalonia.Controls.Window
    {
        private ConnectionManagerViewModel? _viewModel;

        public ConnectionManagerWindow()
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

            _viewModel = DataContext as ConnectionManagerViewModel;

            if (_viewModel != null)
            {
                _viewModel.RequestClose += ViewModel_RequestClose;
            }
        }

        private void ViewModel_RequestClose(object? sender, bool e)
        {
            Close();
        }
    }
}
