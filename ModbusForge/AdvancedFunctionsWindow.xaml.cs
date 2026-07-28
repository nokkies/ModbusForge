using System;
using System.Windows;
using ModbusForge.ViewModels;

namespace ModbusForge;

public partial class AdvancedFunctionsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AdvancedFunctionsViewModel _viewModel;

    public AdvancedFunctionsWindow(AdvancedFunctionsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        _viewModel.Dispose();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
