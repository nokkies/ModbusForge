using System;
using System.Windows;
using ModbusForge.Services;
using ModbusForge.ViewModels;

namespace ModbusForge;

public partial class ConnectionManagerWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ConnectionManagerViewModel _viewModel;

    public ConnectionManagerWindow(ConnectionManagerViewModel viewModel)
    {
        if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += OnRequestClose;
        Closed += OnClosed;
    }

    private void OnRequestClose(object? sender, bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.Dispose();
    }
}
