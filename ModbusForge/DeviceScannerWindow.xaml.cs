using System;
using ModbusForge.ViewModels;

namespace ModbusForge;

public partial class DeviceScannerWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly DeviceScannerViewModel _viewModel;

    public DeviceScannerWindow(DeviceScannerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += OnRequestClose;
        Closed += OnClosed;
    }

    private void OnRequestClose(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.Dispose();
    }
}
