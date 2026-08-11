using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class SignalGeneratorConfigWindow : Window
    {
        private SignalGeneratorConfigViewModel? _viewModel;

        public SignalGeneratorConfigWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
            }

            _viewModel = DataContext as SignalGeneratorConfigViewModel;

            if (_viewModel != null)
            {
                _viewModel.RequestClose += ViewModel_RequestClose;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
            }

            base.OnClosed(e);
        }

        private void ViewModel_RequestClose(object? sender, EventArgs e)
        {
            Close(_viewModel?.DialogResult);
        }
    }
}
