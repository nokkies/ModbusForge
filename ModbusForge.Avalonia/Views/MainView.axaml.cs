using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Services;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using System.Globalization;
using System.Threading.Tasks;

namespace ModbusForge.Avalonia.Views
{
    public partial class MainView : global::Avalonia.Controls.UserControl
    {
        private DataGrid? _holdingRegistersGrid;
        private DataGrid? _coilsGrid;
        private DataGrid? _inputRegistersGrid;
        private DataGrid? _discreteInputsGrid;

        public MainView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

            _holdingRegistersGrid = this.FindControl<DataGrid>("HoldingRegistersGrid");
            _coilsGrid = this.FindControl<DataGrid>("CoilsGrid");
            _inputRegistersGrid = this.FindControl<DataGrid>("InputRegistersGrid");
            _discreteInputsGrid = this.FindControl<DataGrid>("DiscreteInputsGrid");
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (global::Avalonia.Application.Current is App app)
            {
                var host = app.Services?.GetService<AvaloniaDockingHost>();
                host?.SetMainView(this);
            }
        }

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        private void ScriptEditor_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.SelectedTabIndex = 4;
            }
        }

        private void HoldingRegistersGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row?.DataContext is not RegisterEntry entry) return;
            if (e.Column?.Header?.ToString() != "Value") return;

            ViewModel?.WriteHoldingRegisterFromEditAsync(entry);
        }

        private void CoilsGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row?.DataContext is not CoilEntry entry) return;
            if (e.Column?.Header?.ToString() != "State") return;

            ViewModel?.WriteCoilFromEditAsync(entry);
        }

        private static T? GetSelectedItem<T>(DataGrid? grid) where T : class
        {
            return grid?.SelectedItem as T;
        }

        private void CopyToClipboard(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is not { } clipboard) return;

            _ = clipboard.SetTextAsync(text);
        }

        private void AddRegisterToCustom(RegisterEntry entry, string area)
        {
            var vm = ViewModel;
            if (vm == null) return;

            foreach (var existing in vm.CustomEntries)
            {
                if (existing.Address == entry.Address && existing.Area == area)
                {
                    vm.StatusMessage = $"Address {entry.Address} already in watch list.";
                    return;
                }
            }

            var initialValue = string.IsNullOrEmpty(entry.ValueText) ? entry.Value.ToString(CultureInfo.InvariantCulture) : entry.ValueText;
            vm.CustomEntries.Add(new CustomEntry
            {
                Name = $"{area[0]}R {entry.Address}",
                Address = entry.Address,
                Area = area,
                Type = entry.Type ?? "uint",
                Value = initialValue,
                WriteValue = initialValue,
                Continuous = false,
                PeriodMs = 1000,
                Monitor = false,
                ReadPeriodMs = 1000,
                Trend = false
            });

            vm.StatusMessage = $"Added {area} {entry.Address} to watch list.";
        }

        private void AddCoilToCustom(CoilEntry entry, string area)
        {
            var vm = ViewModel;
            if (vm == null) return;

            foreach (var existing in vm.CustomEntries)
            {
                if (existing.Address == entry.Address && existing.Area == area)
                {
                    vm.StatusMessage = $"Address {entry.Address} already in watch list.";
                    return;
                }
            }

            var value = entry.State ? "1" : "0";
            vm.CustomEntries.Add(new CustomEntry
            {
                Name = $"{area} {entry.Address}",
                Address = entry.Address,
                Area = area,
                Type = "uint",
                Value = value,
                WriteValue = value,
                Continuous = false,
                PeriodMs = 1000,
                Monitor = false,
                ReadPeriodMs = 1000,
                Trend = false
            });

            vm.StatusMessage = $"Added {area} {entry.Address} to watch list.";
        }

        private void AddRegisterToTrend(RegisterEntry entry, string area)
        {
            var vm = ViewModel;
            if (vm == null) return;

            CustomEntry? trendEntry = null;
            foreach (var existing in vm.CustomEntries)
            {
                if (existing.Address == entry.Address && existing.Area == area)
                {
                    trendEntry = existing;
                    break;
                }
            }

            if (trendEntry == null)
            {
                var initialValue = string.IsNullOrEmpty(entry.ValueText) ? entry.Value.ToString(CultureInfo.InvariantCulture) : entry.ValueText;
                trendEntry = new CustomEntry
                {
                    Name = $"{area[0]}R Trend {entry.Address}",
                    Address = entry.Address,
                    Area = area,
                    Type = entry.Type ?? "uint",
                    Value = initialValue,
                    WriteValue = initialValue,
                    Continuous = false,
                    PeriodMs = 1000,
                    Monitor = true,
                    ReadPeriodMs = 1000,
                    Trend = true
                };
                vm.CustomEntries.Add(trendEntry);
            }
            else
            {
                trendEntry.Trend = true;
                trendEntry.Monitor = true;
            }

            if (area == "HoldingRegister")
                vm.HoldingMonitorEnabled = true;
            else if (area == "InputRegister")
                vm.InputRegistersMonitorEnabled = true;

            vm.StatusMessage = $"Added {area} {entry.Address} to trend logger.";
        }

        private void AddCoilToTrend(CoilEntry entry, string area)
        {
            var vm = ViewModel;
            if (vm == null) return;

            CustomEntry? trendEntry = null;
            foreach (var existing in vm.CustomEntries)
            {
                if (existing.Address == entry.Address && existing.Area == area)
                {
                    trendEntry = existing;
                    break;
                }
            }

            var value = entry.State ? "1" : "0";
            if (trendEntry == null)
            {
                trendEntry = new CustomEntry
                {
                    Name = $"{area} Trend {entry.Address}",
                    Address = entry.Address,
                    Area = area,
                    Type = "uint",
                    Value = value,
                    WriteValue = value,
                    Continuous = false,
                    PeriodMs = 1000,
                    Monitor = true,
                    ReadPeriodMs = 1000,
                    Trend = true
                };
                vm.CustomEntries.Add(trendEntry);
            }
            else
            {
                trendEntry.Trend = true;
                trendEntry.Monitor = true;
            }

            if (area == "Coil")
                vm.CoilsMonitorEnabled = true;
            else if (area == "DiscreteInput")
                vm.DiscreteInputsMonitorEnabled = true;

            vm.StatusMessage = $"Added {area} {entry.Address} to trend logger.";
        }

        #region Holding Registers Context Menu

        private void HoldingRegisters_QuickWrite_Click(object? sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            if (GetSelectedItem<RegisterEntry>(_holdingRegistersGrid) is not { } entry) return;

            vm.HoldingRegisterStart = entry.Address;
            vm.WriteHoldingRegisterCommand.Execute(null);
        }

        private void HoldingRegisters_AddToCustom_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<RegisterEntry>(_holdingRegistersGrid) is { } entry)
                AddRegisterToCustom(entry, "HoldingRegister");
        }

        private void HoldingRegisters_AddToTrend_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<RegisterEntry>(_holdingRegistersGrid) is { } entry)
                AddRegisterToTrend(entry, "HoldingRegister");
        }

        private void HoldingRegisters_CopyAddress_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<RegisterEntry>(_holdingRegistersGrid) is { } entry)
                CopyToClipboard(entry.Address.ToString(CultureInfo.InvariantCulture));
        }

        private void HoldingRegisters_CopyValue_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<RegisterEntry>(_holdingRegistersGrid) is { } entry)
                CopyToClipboard(string.IsNullOrEmpty(entry.ValueText) ? entry.Value.ToString(CultureInfo.InvariantCulture) : entry.ValueText);
        }

        #endregion

        #region Coils Context Menu

        private void Coils_QuickWrite_Click(object? sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            if (GetSelectedItem<CoilEntry>(_coilsGrid) is not { } entry) return;

            vm.CoilStart = entry.Address;
            vm.WriteCoilCommand.Execute(null);
        }

        private void Coils_AddToCustom_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<CoilEntry>(_coilsGrid) is { } entry)
                AddCoilToCustom(entry, "Coil");
        }

        private void Coils_AddToTrend_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<CoilEntry>(_coilsGrid) is { } entry)
                AddCoilToTrend(entry, "Coil");
        }

        private void Coils_CopyAddress_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<CoilEntry>(_coilsGrid) is { } entry)
                CopyToClipboard(entry.Address.ToString(CultureInfo.InvariantCulture));
        }

        private void Coils_CopyState_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<CoilEntry>(_coilsGrid) is { } entry)
                CopyToClipboard(entry.State ? "1" : "0");
        }

        #endregion

        #region Input Registers Context Menu

        private void InputRegisters_AddToCustom_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<RegisterEntry>(_inputRegistersGrid) is { } entry)
                AddRegisterToCustom(entry, "InputRegister");
        }

        private void InputRegisters_AddToTrend_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<RegisterEntry>(_inputRegistersGrid) is { } entry)
                AddRegisterToTrend(entry, "InputRegister");
        }

        private void InputRegisters_CopyAddress_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<RegisterEntry>(_inputRegistersGrid) is { } entry)
                CopyToClipboard(entry.Address.ToString(CultureInfo.InvariantCulture));
        }

        private void InputRegisters_CopyValue_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<RegisterEntry>(_inputRegistersGrid) is { } entry)
                CopyToClipboard(string.IsNullOrEmpty(entry.ValueText) ? entry.Value.ToString(CultureInfo.InvariantCulture) : entry.ValueText);
        }

        #endregion

        #region Discrete Inputs Context Menu

        private void DiscreteInputs_AddToCustom_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<CoilEntry>(_discreteInputsGrid) is { } entry)
                AddCoilToCustom(entry, "DiscreteInput");
        }

        private void DiscreteInputs_AddToTrend_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<CoilEntry>(_discreteInputsGrid) is { } entry)
                AddCoilToTrend(entry, "DiscreteInput");
        }

        private void DiscreteInputs_CopyAddress_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<CoilEntry>(_discreteInputsGrid) is { } entry)
                CopyToClipboard(entry.Address.ToString(CultureInfo.InvariantCulture));
        }

        private void DiscreteInputs_CopyState_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSelectedItem<CoilEntry>(_discreteInputsGrid) is { } entry)
                CopyToClipboard(entry.State ? "1" : "0");
        }

        #endregion

        private void AdvancedFunctions_Click(object? sender, RoutedEventArgs e)
        {
            var app = global::Avalonia.Application.Current as App;
            var vm = ViewModel;
            if (app?.Services == null || vm?.ActiveService == null)
            {
                if (vm != null) vm.StatusMessage = "Connect a profile before opening Advanced Functions.";
                return;
            }

            var window = new AdvancedFunctionsWindow(
                new AdvancedFunctionsViewModel(
                    vm.ActiveService,
                    vm.EffectiveUnitId,
                    app.Services.GetRequiredService<ILogger<AdvancedFunctionsViewModel>>()));

            var topLevel = global::Avalonia.Controls.TopLevel.GetTopLevel(this);
            if (topLevel is global::Avalonia.Controls.Window owner)
                _ = window.ShowDialog(owner);
            else
                window.Show();
        }

        private void DeviceScanner_Click(object? sender, RoutedEventArgs e)
        {
            var app = global::Avalonia.Application.Current as App;
            if (app?.Services == null) return;

            var scanner = app.Services.GetRequiredService<IDeviceScannerService>();
            var connectionManager = app.Services.GetRequiredService<IConnectionManager>();
            var dispatcher = app.Services.GetRequiredService<ModbusForge.Services.IDispatcher>();
            var fileDialogService = app.Services.GetRequiredService<IFileDialogService>();
            var messageBoxService = app.Services.GetRequiredService<IMessageBoxService>();
            var fileSystem = app.Services.GetRequiredService<IFileSystem>();

            var window = new DeviceScannerWindow
            {
                DataContext = new DeviceScannerViewModel(
                    scanner,
                    connectionManager,
                    dispatcher,
                    fileDialogService,
                    messageBoxService,
                    fileSystem,
                    app.Services.GetRequiredService<ILogger<DeviceScannerViewModel>>())
            };

            var topLevel = global::Avalonia.Controls.TopLevel.GetTopLevel(this);
            if (topLevel is global::Avalonia.Controls.Window owner)
            {
                _ = window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }
        }
    }
}