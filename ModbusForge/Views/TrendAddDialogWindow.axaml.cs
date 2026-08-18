using System;
using global::Avalonia.Controls;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class TrendAddDialogWindow : Window
    {
        private TrendAddDialogViewModel? _viewModel;

        public TrendAddDialogWindow()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
            }

            _viewModel = DataContext as TrendAddDialogViewModel;

            if (_viewModel != null)
            {
                _viewModel.RequestClose += ViewModel_RequestClose;
            }
        }

        private void ViewModel_RequestClose(object? sender, EventArgs e)
        {
            Close();
        }
    }
}
