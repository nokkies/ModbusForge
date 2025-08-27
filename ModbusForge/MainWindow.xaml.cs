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
using ModbusForge.Models;
using MahApps.Metro.Controls;

namespace ModbusForge
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
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

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
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

        private async void Trend_ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var trendView = this.TrendViewControl;
                if (trendView?.DataContext is not TrendViewModel vm)
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
                    await vm.ExportCsvAsync(dlg.FileName, vm.SelectedSeriesItem);
                    _viewModel.StatusMessage = $"Exported CSV: {Path.GetFileName(dlg.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export CSV failed: {ex.Message}", "Trend Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Trend_ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var trendView = this.TrendViewControl;
                if (trendView?.DataContext is not TrendViewModel vm)
                {
                    MessageBox.Show("Trend view is not available.", "Trend", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    Multiselect = false
                };
                if (dlg.ShowDialog(this) == true)
                {
                    await vm.ImportCsvAsync(dlg.FileName);
                    _viewModel.StatusMessage = $"Imported CSV: {Path.GetFileName(dlg.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import CSV failed: {ex.Message}", "Trend Import", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void HoldingRegistersGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Placeholder to satisfy XAML event; value processing handled by bindings/VM.
            if (e.EditAction != DataGridEditAction.Commit) return;
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