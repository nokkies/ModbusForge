using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Navigation;
using ModbusForge.ViewModels;
using ModbusForge.Views;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.Helpers;
using MahApps.Metro.Controls;
using System.Collections.ObjectModel;
using AvalonDock.Layout;

namespace ModbusForge
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
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

            // Handle keyboard shortcut navigation requests
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Handle F1 for context-sensitive help
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            this.CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (s, e) => MenuItem_Help_Click(null!, null!)));
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.RequestedViewIndex) && _viewModel.RequestedViewIndex >= 0)
            {
                NavigateToView(_viewModel.RequestedViewIndex, null!);
                _viewModel.RequestedViewIndex = -1; // Reset
            }
            else if (e.PropertyName == nameof(MainViewModel.RequestShowHelp) && _viewModel.RequestShowHelp)
            {
                MenuItem_Help_Click(null!, null!);
                _viewModel.RequestShowHelp = false; // Reset
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel?.Dispose();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // F1 for context-sensitive help
            if (e.Key == Key.F1)
            {
                e.Handled = true;
                MenuItem_Help_Click(null!, null!);
            }
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

        private void MenuItem_KeyboardShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var shortcuts = new KeyboardShortcutsWindow
            {
                Owner = this
            };
            shortcuts.ShowDialog();
        }

        private void MenuItem_Help_Click(object sender, RoutedEventArgs e)
        {
            var helpViewModel = App.ServiceProvider.GetService(typeof(ViewModels.HelpViewModel)) as ViewModels.HelpViewModel;
            if (helpViewModel != null)
            {
                var helpWindow = new Views.HelpWindow(helpViewModel)
                {
                    Owner = this
                };
                helpWindow.ShowDialog();
            }
        }

        private void MenuItem_Troubleshooting_Click(object sender, RoutedEventArgs e)
        {
            var troubleshootingWindow = new Views.TroubleshootingWindow
            {
                Owner = this
            };
            troubleshootingWindow.ShowDialog();
        }

        private void MenuItem_Donate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = "https://www.paypal.com/donate/?hosted_button_id=ELTVNJEYLZE3W";
                UrlHelper.OpenUrl(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}", "Donate", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuItem_Preferences_Click(object sender, RoutedEventArgs e)
        {
            var settingsService = App.ServiceProvider.GetService(typeof(ISettingsService)) as ISettingsService;
            if (settingsService != null)
            {
                var preferencesWindow = new PreferencesWindow(settingsService)
                {
                    Owner = this
                };
                preferencesWindow.ShowDialog();
            }
        }

        private void MenuItem_ConnectionManager_Click(object sender, RoutedEventArgs e)
        {
            var connectionManager = App.ServiceProvider.GetService(typeof(IConnectionManager)) as IConnectionManager;
            if (connectionManager != null)
            {
                var connectionManagerWindow = new ConnectionManagerWindow(connectionManager)
                {
                    Owner = this
                };
                connectionManagerWindow.ShowDialog();
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

        private void NavItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.NavigationViewItem item && item.Tag != null)
            {
                if (int.TryParse(item.Tag.ToString(), out int index))
                {
                    NavigateToView(index, item);
                    e.Handled = true;
                }
            }
        }

        private void NavigateToView(int index, Wpf.Ui.Controls.NavigationViewItem? item = null)
        {
            LayoutDocument? targetDoc = null;
            switch (index)
            {
                case 0: targetDoc = DocRegisters; break;
                case 1: targetDoc = DocInputRegisters; break;
                case 2: targetDoc = DocCoils; break;
                case 3: targetDoc = DocDiscreteInputs; break;
                case 4: targetDoc = DocCustomWatch; break;
                case 5: targetDoc = DocSimulation; break;
                case 6: targetDoc = DocDecode; break;
                case 7: targetDoc = DocTrend; break;
                case 8: targetDoc = DocConsole; break;
                case 9: targetDoc = DocDebug; break;
            }

            if (targetDoc != null)
            {
                if (targetDoc.Parent == null)
                {
                    MainDocumentPane.Children.Insert(Math.Min(index, MainDocumentPane.Children.Count), targetDoc);
                }
                targetDoc.IsActive = true;
                targetDoc.IsSelected = true;
            }

            // Update navigation item selection
            if (item != null)
            {
                foreach (var menuItemObj in RootNavigation.MenuItems)
                {
                    if (menuItemObj is Wpf.Ui.Controls.NavigationViewItem navItem)
                    {
                        navItem.SetValue(Wpf.Ui.Controls.NavigationViewItem.IsActiveProperty, navItem == item);
                    }
                }
            }
            else
            {
                // Find the navigation item corresponding to the index
                foreach (var menuItemObj in RootNavigation.MenuItems)
                {
                    if (menuItemObj is Wpf.Ui.Controls.NavigationViewItem navItem && navItem.Tag != null)
                    {
                        if (int.TryParse(navItem.Tag.ToString(), out int navIndex) && navIndex == index)
                        {
                            foreach (var menuItemObj2 in RootNavigation.MenuItems)
                            {
                                if (menuItemObj2 is Wpf.Ui.Controls.NavigationViewItem navItem2)
                                {
                                    navItem2.SetValue(Wpf.Ui.Controls.NavigationViewItem.IsActiveProperty, navItem2 == navItem);
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }

        private void MenuItem_ShowSimPalette_Click(object sender, RoutedEventArgs e)
        {
            VisualEditor?.ShowPalette();
        }

        private void MenuItem_ShowSimControls_Click(object sender, RoutedEventArgs e)
        {
            VisualEditor?.ShowControls();
        }

        private void MenuItem_ShowSimPrograms_Click(object sender, RoutedEventArgs e)
        {
            VisualEditor?.ShowPrograms();
        }

        private T? GetSelectedGridItem<T>(object sender) where T : class
        {
            if (sender is MenuItem menuItem)
            {
                if (menuItem.CommandParameter is T param) return param;
                if (menuItem.Parent is ContextMenu menu && menu.PlacementTarget is DataGrid grid)
                {
                    return grid.SelectedItem as T;
                }
            }
            return null;
        }

        private void OpenWriteDialog(object entry)
        {
            var dialog = new ModbusForge.Views.WriteDialog(_viewModel, entry)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        private void AddRegisterToCustom(RegisterEntry entry, string area)
        {
            foreach (var existing in _viewModel.CustomEntries)
            {
                if (existing.Address == entry.Address && existing.Area == area)
                {
                    _viewModel.StatusMessage = $"Address {entry.Address} already in watch list.";
                    return;
                }
            }

            var newEntry = new CustomEntry
            {
                Name = $"{area[0]}R {entry.Address}",
                Address = entry.Address,
                Area = area,
                Type = entry.Type ?? "uint",
                Value = string.IsNullOrEmpty(entry.ValueText) ? entry.Value.ToString() : entry.ValueText,
                Continuous = false,
                PeriodMs = 1000,
                Monitor = false,
                ReadPeriodMs = 1000,
                Trend = false
            };

            _viewModel.CustomEntries.Add(newEntry);
            _viewModel.StatusMessage = $"Added {area} {entry.Address} to watch list.";
        }

        private void AddCoilToCustom(CoilEntry entry, string area)
        {
            foreach (var existing in _viewModel.CustomEntries)
            {
                if (existing.Address == entry.Address && existing.Area == area)
                {
                    _viewModel.StatusMessage = $"Address {entry.Address} already in watch list.";
                    return;
                }
            }

            var newEntry = new CustomEntry
            {
                Name = $"{area} {entry.Address}",
                Address = entry.Address,
                Area = area,
                Type = "uint",
                Value = entry.State ? "1" : "0",
                Continuous = false,
                PeriodMs = 1000,
                Monitor = false,
                ReadPeriodMs = 1000,
                Trend = false
            };

            _viewModel.CustomEntries.Add(newEntry);
            _viewModel.StatusMessage = $"Added {area} {entry.Address} to watch list.";
        }

        private void AddRegisterToTrend(RegisterEntry entry, string area)
        {
            CustomEntry? trendEntry = null;
            foreach (var existing in _viewModel.CustomEntries)
            {
                if (existing.Address == entry.Address && existing.Area == area)
                {
                    trendEntry = existing;
                    break;
                }
            }

            if (trendEntry == null)
            {
                trendEntry = new CustomEntry
                {
                    Name = $"{area[0]}R {entry.Address}",
                    Address = entry.Address,
                    Area = area,
                    Type = entry.Type ?? "uint",
                    Value = string.IsNullOrEmpty(entry.ValueText) ? entry.Value.ToString() : entry.ValueText,
                    Continuous = false,
                    PeriodMs = 1000,
                    Monitor = false,
                    ReadPeriodMs = 1000,
                    Trend = true
                };
                _viewModel.CustomEntries.Add(trendEntry);
            }
            else
            {
                trendEntry.Trend = true;
            }

            _viewModel.GlobalMonitorEnabled = true;

            // Automatically enable the specific continuous read for the area
            if (area == "HoldingRegister")
            {
                _viewModel.HoldingMonitorEnabled = true;
            }
            else if (area == "InputRegister")
            {
                _viewModel.InputRegistersMonitorEnabled = true;
            }

            _viewModel.StatusMessage = $"Added {area} {entry.Address} to trend logger.";
        }

        // Holding Registers Grid Handlers
        private void HoldingRegistersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is RegisterEntry entry)
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                {
                    dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
                }
                if (dep is DataGridRow)
                {
                    OpenWriteDialog(entry);
                }
            }
        }

        private void HoldingRegisters_Write_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                OpenWriteDialog(entry);
            }
        }

        private void HoldingRegisters_AddToCustom_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                AddRegisterToCustom(entry, "HoldingRegister");
            }
        }

        private void HoldingRegisters_AddToTrend_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                AddRegisterToTrend(entry, "HoldingRegister");
            }
        }

        private void HoldingRegisters_CopyAddress_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                Clipboard.SetText(entry.Address.ToString());
            }
        }

        private void HoldingRegisters_CopyValue_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                Clipboard.SetText(string.IsNullOrEmpty(entry.ValueText) ? entry.Value.ToString() : entry.ValueText);
            }
        }

        // Input Registers Grid Handlers
        private void InputRegistersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is RegisterEntry entry)
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                {
                    dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
                }
                if (dep is DataGridRow)
                {
                    AddRegisterToCustom(entry, "InputRegister");
                }
            }
        }

        private void InputRegisters_AddToCustom_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                AddRegisterToCustom(entry, "InputRegister");
            }
        }

        private void InputRegisters_AddToTrend_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                AddRegisterToTrend(entry, "InputRegister");
            }
        }

        private void InputRegisters_CopyAddress_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                Clipboard.SetText(entry.Address.ToString());
            }
        }

        private void InputRegisters_CopyValue_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<RegisterEntry>(sender);
            if (entry != null)
            {
                Clipboard.SetText(string.IsNullOrEmpty(entry.ValueText) ? entry.Value.ToString() : entry.ValueText);
            }
        }

        // Coils Grid Handlers
        private void CoilsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is CoilEntry entry)
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                {
                    dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
                }
                if (dep is DataGridRow)
                {
                    OpenWriteDialog(entry);
                }
            }
        }

        private void Coils_Write_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<CoilEntry>(sender);
            if (entry != null)
            {
                OpenWriteDialog(entry);
            }
        }

        private void Coils_AddToCustom_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<CoilEntry>(sender);
            if (entry != null)
            {
                AddCoilToCustom(entry, "Coil");
            }
        }

        private void Coils_CopyAddress_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<CoilEntry>(sender);
            if (entry != null)
            {
                Clipboard.SetText(entry.Address.ToString());
            }
        }

        private void Coils_CopyState_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<CoilEntry>(sender);
            if (entry != null)
            {
                Clipboard.SetText(entry.State.ToString());
            }
        }

        // Discrete Inputs Grid Handlers
        private void DiscreteInputsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is CoilEntry entry)
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                {
                    dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
                }
                if (dep is DataGridRow)
                {
                    AddCoilToCustom(entry, "DiscreteInput");
                }
            }
        }

        private void DiscreteInputs_AddToCustom_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<CoilEntry>(sender);
            if (entry != null)
            {
                AddCoilToCustom(entry, "DiscreteInput");
            }
        }

        private void DiscreteInputs_CopyAddress_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<CoilEntry>(sender);
            if (entry != null)
            {
                Clipboard.SetText(entry.Address.ToString());
            }
        }

        private void DiscreteInputs_CopyState_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedGridItem<CoilEntry>(sender);
            if (entry != null)
            {
                Clipboard.SetText(entry.State.ToString());
            }
        }

        private void RootNavigation_PaneOpened(object sender, RoutedEventArgs e)
        {
            // Navigation pane is now a standalone element; no resize needed.
        }

        private void RootNavigation_PaneClosed(object sender, RoutedEventArgs e)
        {
            // Navigation pane is now a standalone element; no resize needed.
        }
    }
}
