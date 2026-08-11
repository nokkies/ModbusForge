using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.Services;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class ConnectionManagerWindow : global::Avalonia.Controls.Window, IDockableTool
    {
        private ConnectionManagerViewModel? _viewModel;
        private global::Avalonia.Controls.Control? _content;
        private global::Avalonia.Controls.Button? _dockToggleButton;
        private Action? _toggleDockCallback;

        public ConnectionManagerWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            _content = this.Content as global::Avalonia.Controls.Control;
            _dockToggleButton = this.FindControl<global::Avalonia.Controls.Button>("DockToggleButton");
        }

        public Action? ToggleDockCallback
        {
            get => _toggleDockCallback;
            set => _toggleDockCallback = value;
        }

        public void SetDocked(bool isDocked)
        {
            if (_dockToggleButton != null)
            {
                _dockToggleButton.Content = isDocked ? "Float" : "Dock";
            }
        }

        private void DockToggleButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            _toggleDockCallback?.Invoke();
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

        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= ViewModel_RequestClose;
                _viewModel.Dispose();
            }

            base.OnClosed(e);
        }

        private void ViewModel_RequestClose(object? sender, bool e)
        {
            Close();
        }
    }
}
