using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;
using ModbusForge.ViewModels;

namespace ModbusForge
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            
            // Handle window closing to properly dispose resources
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel?.Dispose();
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private async void HoldingRegistersGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            // We don't rely on the binding committing for typed edits (e.g., float or negative int).
            // Instead, read the editing TextBox content and branch by row Type.
            if (e.Row?.Item is RegisterEntry entry)
            {
                try
                {
                    string? editedText = (e.EditingElement as TextBox)?.Text;
                    string type = entry.Type?.ToLowerInvariant() ?? "uint";

                    if (!string.IsNullOrWhiteSpace(editedText))
                    {
                        switch (type)
                        {
                            case "real":
                                {
                                    if (float.TryParse(editedText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                                    {
                                        // Write two registers for 32-bit float starting at this address
                                        await _viewModel.WriteFloatAtAsync(entry.Address, f);
                                        // Cancel commit to avoid binding errors (Value is ushort) then refresh
                                        e.Cancel = true;
                                        _viewModel.ReadRegistersCommand.Execute(null);
                                    }
                                    else
                                    {
                                        MessageBox.Show($"Invalid float value: '{editedText}'", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                        e.Cancel = true;
                                    }
                                    break;
                                }
                            case "int":
                                {
                                    if (int.TryParse(editedText, out int iv))
                                    {
                                        ushort raw = unchecked((ushort)iv);
                                        await _viewModel.WriteRegisterAtAsync(entry.Address, raw);
                                        // Cancel commit (binding to ushort would fail for negatives) then refresh
                                        e.Cancel = true;
                                        _viewModel.ReadRegistersCommand.Execute(null);
                                    }
                                    else
                                    {
                                        MessageBox.Show($"Invalid integer value: '{editedText}'", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                        e.Cancel = true;
                                    }
                                    break;
                                }
                            default:
                                {
                                    // uint/others: allow standard ushort processing via binding
                                    await Dispatcher.Yield(DispatcherPriority.Background);
                                    await _viewModel.WriteRegisterAtAsync(entry.Address, entry.Value);
                                    break;
                                }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to write register {entry.Address}: {ex.Message}", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Cancel = true;
                }
            }
        }

        private async void CoilsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            await Dispatcher.Yield(DispatcherPriority.Background);

            if (e.Row?.Item is CoilEntry entry)
            {
                try
                {
                    await _viewModel.WriteCoilAtAsync(entry.Address, entry.State);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to write coil {entry.Address}: {ex.Message}", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CoilsGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            // Ensure checkbox edits are committed while avoiding duplicate writes here.
            if (sender is DataGrid grid)
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }

        private void CustomGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            if (sender is DataGrid grid)
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }

        private void CustomGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            if (sender is DataGrid grid)
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }
    }
}