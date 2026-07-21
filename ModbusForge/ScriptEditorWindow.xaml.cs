using System;
using System.Windows;
using ModbusForge.Services;
using ModbusForge.ViewModels;

namespace ModbusForge;

public partial class ScriptEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ScriptEditorViewModel _viewModel;

    public ScriptEditorWindow(ScriptEditorViewModel viewModel)
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
