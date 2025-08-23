using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Navigation;
using ModbusForge.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;

namespace ModbusForge
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private bool _isCommittingCustom;

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

        private void MenuItem_About_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow
            {
                Owner = this
            };
            about.ShowDialog();
        }

        private void MenuItem_Donate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = "https://www.paypal.com/donate/?hosted_button_id=ELTVNJEYLZE3W";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}", "Donate", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Trend_ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var trendView = this.TrendViewControl; // from XAML name
                if (trendView?.DataContext is not TrendViewModel tvm)
                {
                    MessageBox.Show("Trend view is not available.", "Trend", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var dlg = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = "trend-export.csv"
                };
                if (dlg.ShowDialog(this) == true)
                {
                    await tvm.ExportCsvAsync(dlg.FileName, tvm.SelectedSeriesItem);
                    _viewModel.StatusMessage = $"Exported CSV: {Path.GetFileName(dlg.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export CSV failed: {ex.Message}", "Trend Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Trend_ExportPng_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var trendView = this.TrendViewControl;
                if (trendView == null)
                {
                    MessageBox.Show("Trend view is not available.", "Trend", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG files (*.png)|*.png|All files (*.*)|*.*",
                    FileName = "trend-export.png"
                };
                if (dlg.ShowDialog(this) == true)
                {
                    trendView.SaveChartAsPng(dlg.FileName);
                    _viewModel.StatusMessage = $"Exported PNG: {Path.GetFileName(dlg.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export PNG failed: {ex.Message}", "Trend Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Trend_ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var trendView = this.TrendViewControl;
                if (trendView?.DataContext is not TrendViewModel tvm)
                {
                    MessageBox.Show("Trend view is not available.", "Trend", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var dlg = new OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog(this) == true)
                {
                    await tvm.ImportCsvAsync(dlg.FileName);
                    _viewModel.StatusMessage = $"Imported CSV: {Path.GetFileName(dlg.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import CSV failed: {ex.Message}", "Trend Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                        // Normalize decimal separator and trim
                        var text = editedText.Trim().Replace(',', '.');

                        // Heuristic: if input looks like a float (contains '.' or exponent)
                        // prefer the 'real' path even if the Type binding hasn't committed yet
                        bool looksLikeFloat = text.Contains('.') || text.Contains("e", StringComparison.OrdinalIgnoreCase);

                        switch (type)
                        {
                            case "real":
                            {
                                if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
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
                            case "string":
                            {
                                await _viewModel.WriteStringAtAsync(entry.Address, editedText);
                                // Cancel commit and refresh to display resulting register bytes
                                e.Cancel = true;
                                _viewModel.ReadRegistersCommand.Execute(null);
                                break;
                            }
                            case "int":
                            {
                                if (int.TryParse(text, out int iv))
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
                                // If it looks like a float and parses, treat as real (covers cases where Type binding hasn't committed yet)
                                if (looksLikeFloat && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                                {
                                    await _viewModel.WriteFloatAtAsync(entry.Address, f);
                                    e.Cancel = true;
                                    _viewModel.ReadRegistersCommand.Execute(null);
                                    break;
                                }
                                // uint: parse and write, update entry.Value to keep ValueText in sync
                                if (uint.TryParse(text, out uint uv) && uv <= ushort.MaxValue)
                                {
                                    ushort val = (ushort)uv;
                                    await _viewModel.WriteRegisterAtAsync(entry.Address, val);
                                    entry.Value = val; // updates ValueText via setter sync
                                    e.Cancel = true;   // we took care of updating the model
                                }
                                else
                                {
                                    MessageBox.Show($"Invalid unsigned value: '{editedText}' (0..65535)", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    e.Cancel = true;
                                }
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
            // Do not force commits here to avoid re-entrancy.
            if (e.EditAction != DataGridEditAction.Commit)
                return;
        }

        private void CustomGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            if (_isCommittingCustom) return;
            if (sender is DataGrid grid)
            {
                try
                {
                    _isCommittingCustom = true;
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                }
                finally
                {
                    _isCommittingCustom = false;
                }
            }
        }
    }
}