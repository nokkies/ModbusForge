using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Services;
using ModbusForge.Helpers;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// MainViewModel - CustomWatch partial (split for navigability; behavior unchanged).
    /// </summary>
    public partial class MainViewModel
    {
        private CancellationTokenSource? _customWatchCts;

        private readonly object _customWatchLifecycleLock = new();

        private const int DefaultCustomPeriodMs = 1000;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveCustomCommand))]
        [NotifyCanExecuteChangedFor(nameof(LoadCustomCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand))]
        [NotifyCanExecuteChangedFor(nameof(LoadProjectCommand))]
        private bool _isCustomWatchMonitoring;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCustomEntryCommand))]
        private CustomEntry? _selectedCustomEntry;


        /// <summary>
        /// Compatibility property for the custom-watch bindings. The collection now
        /// belongs to the selected Unit ID configuration instead of one global list.
        /// </summary>
        public ObservableCollection<CustomEntry> CustomEntries => _unitConfigurationStore.CurrentConfig.CustomEntries;


        public IAsyncRelayCommand AddCustomEntryCommand { get; }

        public IAsyncRelayCommand AddBulkCustomEntryCommand { get; }

        public IAsyncRelayCommand RemoveCustomEntryCommand { get; }

        public IAsyncRelayCommand ReadCustomEntryCommand { get; }

        public IAsyncRelayCommand WriteCustomEntryCommand { get; }

        public IAsyncRelayCommand<CustomEntry?> ReadCustomNowCommand { get; }

        public IAsyncRelayCommand<CustomEntry?> WriteCustomNowCommand { get; }

        public IAsyncRelayCommand<CustomEntry?> DeleteCustomEntryCommand { get; }

        public IAsyncRelayCommand ReadAllCustomNowCommand { get; }

        public IAsyncRelayCommand SaveCustomCommand { get; }

        public IAsyncRelayCommand LoadCustomCommand { get; }


        partial void OnIsCustomWatchMonitoringChanged(bool value)
        {
            if (value)
            {
                StartCustomWatchMonitoring();
            }
            else
            {
                StopCustomWatchMonitoring();
            }
        }


        private ObservableCollection<CustomEntry>? _hookedCustomEntries;


        private void HookCustomEntries()
        {
            if (_hookedCustomEntries == CustomEntries) return;

            UnhookCustomEntries();

            _hookedCustomEntries = CustomEntries;

            if (_hookedCustomEntries != null)
            {
                _hookedCustomEntries.CollectionChanged += OnCustomEntriesCollectionChanged;
                foreach (var entry in _hookedCustomEntries)
                {
                    entry.PropertyChanged += OnCustomEntryPropertyChanged;
                }
            }
        }


        private void UnhookCustomEntries()
        {
            if (_hookedCustomEntries == null)
                return;

            _hookedCustomEntries.CollectionChanged -= OnCustomEntriesCollectionChanged;
            foreach (var entry in _hookedCustomEntries)
            {
                entry.PropertyChanged -= OnCustomEntryPropertyChanged;
            }
            _hookedCustomEntries = null;
        }


        private void OnCustomEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (CustomEntry item in e.NewItems)
                {
                    item.PropertyChanged += OnCustomEntryPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (CustomEntry item in e.OldItems)
                {
                    item.PropertyChanged -= OnCustomEntryPropertyChanged;
                }
            }

            UpdateCustomWatchMonitoringState();
        }


        private void OnCustomEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CustomEntry.Monitor) or nameof(CustomEntry.Continuous))
            {
                UpdateCustomWatchMonitoringState();
            }
        }


        private void UpdateCustomWatchMonitoringState()
        {
            var shouldMonitor = CustomEntries.Any(entry => entry.Monitor || entry.Continuous);
            if (IsCustomWatchMonitoring != shouldMonitor)
            {
                IsCustomWatchMonitoring = shouldMonitor;
            }
        }


        private bool CanRemoveCustomEntry() => SelectedCustomEntry != null && !IsBusy;


        private bool CanReadCustomEntry() => SelectedCustomEntry != null && ActiveProfile is { IsConnected: true } && !IsBusy;


        private bool CanReadCustomEntry(CustomEntry? entry) => entry != null && ActiveProfile is { IsConnected: true } && !IsBusy;


        private bool CanWriteCustomEntry() => SelectedCustomEntry != null && ActiveProfile is { IsConnected: true } && !IsBusy;


        private bool CanWriteCustomEntry(CustomEntry? entry) => entry != null && ActiveProfile is { IsConnected: true } && !IsBusy;


        private bool CanReadAllCustomEntries() => CustomEntries.Count > 0 && ActiveProfile is { IsConnected: true } && !IsBusy;


        private bool CanSaveCustom() => _customEntryService != null && !IsBusy;


        private bool CanLoadCustom() => _customEntryService != null && !IsBusy;



        private async Task AddCustomEntryAsync()
        {
            await _dispatcher.InvokeAsync(() =>
            {
                int nextAddress = 1;
                string type = "int";
                string area = "HoldingRegister";
                string name = "Tag0";

                if (CustomEntries.Count > 0)
                {
                    var last = CustomEntries[^1];
                    type = last.Type ?? "int";
                    area = last.Area ?? "HoldingRegister";
                    name = GenerateNextName(last.Name);

                    int increment = IsMultiRegisterType(type)
                        ? MultiRegisterTypeIncrement
                        : SingleRegisterTypeIncrement;
                    nextAddress = Math.Max(1, last.Address + increment);
                }

                var entry = new CustomEntry
                {
                    Name = name,
                    Address = nextAddress,
                    Area = area,
                    Type = type,
                    Value = "0",
                    WriteValue = "0",
                    Continuous = false,
                    PeriodMs = DefaultCustomPeriodMs,
                    Monitor = false,
                    ReadPeriodMs = DefaultCustomPeriodMs,
                    Trend = false
                };

                CustomEntries.Add(entry);
                SelectedCustomEntry = entry;
                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                StatusMessage = $"Added custom entry {name}.";
            });
        }


        private Task AddCustomBulkEntryAsync()
        {
            if (_customBulkAddDialogService == null) return Task.CompletedTask;

            return _dispatcher.InvokeAsync(() =>
            {
                if (!_customBulkAddDialogService.TryGetBulkAdd(out var result) || result == null)
                {
                    StatusMessage = "Bulk add cancelled.";
                    return;
                }

                int increment = IsMultiRegisterType(result.Type)
                    ? MultiRegisterTypeIncrement
                    : SingleRegisterTypeIncrement;

                for (int i = 0; i < result.Count; i++)
                {
                    int address = result.StartRegister + i * increment;
                    var entry = new CustomEntry
                    {
                        Name = $"{result.NamePrefix}{i}",
                        Address = address,
                        Area = result.Area,
                        Type = result.Type,
                        Value = "0",
                        WriteValue = "0",
                        Continuous = false,
                        PeriodMs = result.WritePeriodMs,
                        Monitor = false,
                        ReadPeriodMs = result.ReadPeriodMs,
                        Trend = false
                    };

                    CustomEntries.Add(entry);
                }

                SelectedCustomEntry = CustomEntries[^1];
                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                StatusMessage = $"Added {result.Count} custom entries starting at {result.StartRegister}.";
            });
        }


        private static bool IsMultiRegisterType(string type) =>
            type.Equals("real", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("float", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("dword", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("dint", StringComparison.OrdinalIgnoreCase);


        private async Task RemoveCustomEntryAsync()
        {
            if (SelectedCustomEntry == null) return;

            await _dispatcher.InvokeAsync(() =>
            {
                CustomEntries.Remove(SelectedCustomEntry);
                SelectedCustomEntry = null;
                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                StatusMessage = "Removed custom entry.";
            });
        }


        private Task DeleteCustomEntryAsync(CustomEntry? entry)
        {
            if (entry == null) return Task.CompletedTask;

            return _dispatcher.InvokeAsync(() =>
            {
                CustomEntries.Remove(entry);
                if (SelectedCustomEntry == entry)
                {
                    SelectedCustomEntry = null;
                }

                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                StatusMessage = $"Removed custom entry {entry.Name}.";
            });
        }


        private Task ReadSelectedCustomEntryAsync() => ReadCustomEntryNowAsync(SelectedCustomEntry);


        private Task WriteSelectedCustomEntryAsync() => WriteCustomEntryNowAsync(SelectedCustomEntry);


        private async Task ReadCustomEntryNowAsync(CustomEntry? entry)
        {
            if (entry == null || ActiveService == null || ActiveProfile == null) return;

            IsBusy = true;
            try
            {
                var value = await ReadCustomValueSerializedAsync(entry, CancellationToken.None);
                var readAt = DateTime.UtcNow;
                await _dispatcher.InvokeAsync(() =>
                {
                    entry.Value = value;
                    entry.LastReadUtc = readAt;
                    StatusMessage = $"Read {entry.Name} = {value}";
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await ReportCustomOperationFailureAsync("read", entry, ex, false);
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task WriteCustomEntryNowAsync(CustomEntry? entry)
        {
            if (entry == null || ActiveService == null || ActiveProfile == null) return;

            IsBusy = true;
            try
            {
                var result = await WriteCustomValueSerializedAsync(entry, CancellationToken.None);
                var writtenAt = DateTime.UtcNow;
                await _dispatcher.InvokeAsync(() =>
                {
                    if (result)
                    {
                        entry.LastWriteUtc = writtenAt;
                    }

                    StatusMessage = result
                        ? $"Wrote {entry.Name}"
                        : $"Write completed with issues for {entry.Name}.";
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await ReportCustomOperationFailureAsync("write", entry, ex, false);
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task ReadAllCustomEntriesAsync()
        {
            if (ActiveService == null || ActiveProfile == null || CustomEntries.Count == 0) return;

            IsBusy = true;
            try
            {
                var entries = await _dispatcher.InvokeAsync(() => CustomEntries.ToList());
                var readCount = 0;
                foreach (var entry in entries)
                {
                    try
                    {
                        var value = await ReadCustomValueSerializedAsync(entry, CancellationToken.None);
                        var readAt = DateTime.UtcNow;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            entry.Value = value;
                            entry.LastReadUtc = readAt;
                        });
                        readCount++;
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        await ReportCustomOperationFailureAsync("read", entry, ex, false);
                    }
                }

                await _dispatcher.InvokeAsync(() => StatusMessage = $"Read {readCount} of {entries.Count} custom entries.");
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task<string> ReadCustomValueSerializedAsync(CustomEntry entry, CancellationToken token)
        {
            await _modbusIoGate.WaitAsync(token);
            try
            {
                return await ReadCustomValueAsync(entry);
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }


        private async Task<bool> WriteCustomValueSerializedAsync(CustomEntry entry, CancellationToken token)
        {
            await _modbusIoGate.WaitAsync(token);
            try
            {
                return await WriteCustomValueAsync(entry);
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }


        private async Task ReportCustomOperationFailureAsync(string operation, CustomEntry entry, Exception exception, bool monitoring)
        {
            _logger.LogError(exception, "Error {Operation} custom entry {Name}", operation, entry.Name);
            var suffix = monitoring ? " Continuous monitoring has been paused." : string.Empty;
            var message = $"Failed to {operation} custom entry '{entry.Name}': {exception.Message}.{suffix}";
            await _dispatcher.InvokeAsync(() => StatusMessage = message);
            if (_messageBoxService != null)
            {
                Task<DialogResult>? dialogTask = null;
                await _dispatcher.InvokeAsync(() =>
                    dialogTask = _messageBoxService.ShowAsync(message, "Custom Watch Error", DialogButton.Ok, DialogIcon.Error));
                if (dialogTask != null)
                {
                    await dialogTask;
                }
            }
        }


        private async Task<string> ReadCustomValueAsync(CustomEntry entry)
        {
            var service = ActiveService;
            if (service == null || ActiveProfile == null)
                throw new InvalidOperationException("No active service.");

            var unitId = EffectiveUnitId;
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "int").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                case "inputregister":
                    int count = type == "real" ? 2 : 1;
                    var areaEnum = area == "holdingregister" ? PlcArea.HoldingRegister : PlcArea.InputRegister;
                    var values = areaEnum == PlcArea.HoldingRegister
                        ? await service.ReadHoldingRegistersAsync(unitId, entry.Address, count)
                        : await service.ReadInputRegistersAsync(unitId, entry.Address, count);

                    if (values == null || values.Length == 0)
                        throw new InvalidOperationException("Read returned no response.");

                    if (type == "real" && values.Length < 2)
                        throw new InvalidOperationException("A REAL value requires two registers.");

                    return type switch
                    {
                        "int" => unchecked((short)values[0]).ToString(CultureInfo.InvariantCulture),
                        "real" => DataTypeConverter.ToSingle(values[0], values[1], false, false).ToString(CultureInfo.InvariantCulture),
                        "string" => DataTypeConverter.ToString(values[0]),
                        _ => values[0].ToString(CultureInfo.InvariantCulture)
                    };

                case "coil":
                case "discreteinput":
                    var coilValues = area == "coil"
                        ? await service.ReadCoilsAsync(unitId, entry.Address, 1)
                        : await service.ReadDiscreteInputsAsync(unitId, entry.Address, 1);
                    if (coilValues == null || coilValues.Length == 0) return "No response";
                    return coilValues[0] ? "1" : "0";

                default:
                    return $"Unknown area: {entry.Area}";
            }
        }


        private async Task<bool> WriteCustomValueAsync(CustomEntry entry)
        {
            var service = ActiveService;
            if (service == null || ActiveProfile == null) return false;

            var unitId = EffectiveUnitId;
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "int").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                    switch (type)
                    {
                        case "real":
                            if (float.TryParse(entry.WriteValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ||
                                float.TryParse(entry.WriteValue, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                            {
                                var words = DataTypeConverter.ToUInt16(f, false, false);
                                await service.WriteRegistersAsync(unitId, entry.Address, words);
                                return true;
                            }
                            return false;

                        case "string":
                            // Same FC16 cap as the register grid: reject oversized strings with a clear
                            // message (surfaced by the caller) instead of clobbering adjacent registers.
                            var stringWords = DataTypeConverter.ToUInt16(entry.WriteValue ?? string.Empty);
                            if (stringWords.Length > ModbusAddressValidator.MaxWriteRegisters)
                            {
                                throw new ArgumentException(
                                    $"String write too large: {stringWords.Length} registers (max {ModbusAddressValidator.MaxWriteRegisters}).");
                            }
                            await service.WriteRegistersAsync(unitId, entry.Address, stringWords);
                            return true;

                        case "int":
                            if (int.TryParse(entry.WriteValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                            {
                                await service.WriteSingleRegisterAsync(unitId, entry.Address, unchecked((ushort)iv));
                                return true;
                            }
                            return false;

                        default:
                            if (uint.TryParse(entry.WriteValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uv))
                            {
                                if (uv > 0xFFFF) uv = 0xFFFF;
                                await service.WriteSingleRegisterAsync(unitId, entry.Address, (ushort)uv);
                                return true;
                            }
                            return false;
                    }

                case "coil":
                    if (TryParseBool(entry.WriteValue, out bool b))
                    {
                        await service.WriteSingleCoilAsync(unitId, entry.Address, b);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }


        private async Task SaveCustomAsync()
        {
            if (_customEntryService == null) return;

            IsBusy = true;
            try
            {
                await _customEntryService.SaveCustomAsync(CustomEntries);
                StatusMessage = "Custom entries saved.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving custom entries");
                StatusMessage = $"Save error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task LoadCustomAsync()
        {
            if (_customEntryService == null) return;

            IsBusy = true;
            try
            {
                var entries = await _customEntryService.LoadCustomAsync();
                if (entries != null)
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        CustomEntries.Clear();
                        foreach (var e in entries)
                        {
                            CustomEntries.Add(e);
                        }
                        ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                    });
                    StatusMessage = $"Loaded {entries.Count} custom entries.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom entries");
                StatusMessage = $"Load error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }


        private void StartCustomWatchMonitoring()
        {
            if (_disposed || ActiveProfile is not { IsConnected: true } || !IsCustomWatchMonitoring) return;

            lock (_customWatchLifecycleLock)
            {
                if (_customWatchCts != null) return;

                var cts = new CancellationTokenSource();
                _customWatchCts = cts;
                _ = Task.Run(() => CustomWatchLoopAsync(cts), cts.Token);
            }
        }


        private void StopCustomWatchMonitoring()
        {
            lock (_customWatchLifecycleLock)
            {
                _customWatchCts?.Cancel();
            }
        }


        private async Task CustomWatchLoopAsync(CancellationTokenSource loopCts)
        {
            var token = loopCts.Token;
            try
            {
                while (!token.IsCancellationRequested && ActiveProfile is { IsConnected: true } && IsCustomWatchMonitoring)
                {
                    var entries = await _dispatcher.InvokeAsync(() => CustomEntries.ToList());
                    var now = DateTime.UtcNow;

                    foreach (var entry in entries)
                    {
                        token.ThrowIfCancellationRequested();

                        var readPeriod = entry.ReadPeriodMs <= 0 ? DefaultCustomPeriodMs : entry.ReadPeriodMs;
                        if (entry.Monitor && (now - entry.LastReadUtc).TotalMilliseconds >= readPeriod)
                        {
                            try
                            {
                                var value = await ReadCustomValueSerializedAsync(entry, token);
                                var readAt = DateTime.UtcNow;
                                await _dispatcher.InvokeAsync(() =>
                                {
                                    entry.Value = value;
                                    entry.LastReadUtc = readAt;
                                });

                                if (entry.Trend && _trendLogger != null && TryParseTrendValue(value, out var trendValue))
                                {
                                    _trendLogger.Publish(entry.Name, trendValue, readAt);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex) when (ex is not OutOfMemoryException)
                            {
                                await _dispatcher.InvokeAsync(() => entry.Monitor = false);
                                await ReportCustomOperationFailureAsync("read", entry, ex, true);
                            }
                        }

                        var writePeriod = entry.PeriodMs <= 0 ? DefaultCustomPeriodMs : entry.PeriodMs;
                        if (entry.Continuous && (now - entry.LastWriteUtc).TotalMilliseconds >= writePeriod)
                        {
                            try
                            {
                                var success = await WriteCustomValueSerializedAsync(entry, token);
                                if (!success)
                                {
                                    throw new InvalidOperationException("The custom value is invalid for the selected type or area.");
                                }

                                var writeAt = DateTime.UtcNow;
                                await _dispatcher.InvokeAsync(() => entry.LastWriteUtc = writeAt);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex) when (ex is not OutOfMemoryException)
                            {
                                await _dispatcher.InvokeAsync(() => entry.Continuous = false);
                                await ReportCustomOperationFailureAsync("write", entry, ex, true);
                            }
                        }
                    }

                    await Task.Delay(PollLoopIntervalMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Custom watch loop canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Custom watch loop failed");
                await _dispatcher.InvokeAsync(() => StatusMessage = $"Custom watch error: {ex.Message}");
            }
            finally
            {
                var restart = false;
                lock (_customWatchLifecycleLock)
                {
                    if (ReferenceEquals(_customWatchCts, loopCts))
                    {
                        _customWatchCts = null;
                        restart = !_disposed && ActiveProfile is { IsConnected: true } && IsCustomWatchMonitoring;
                    }
                }

                loopCts.Dispose();
                if (restart)
                {
                    StartCustomWatchMonitoring();
                }
            }
        }


        private static string GenerateNextName(string previousName)
        {
            if (string.IsNullOrWhiteSpace(previousName))
                return "Tag0";

            int i = previousName.Length - 1;
            while (i >= 0 && char.IsDigit(previousName[i]))
                i--;

            if (i < previousName.Length - 1 &&
                int.TryParse(previousName[(i + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            {
                return previousName.Substring(0, i + 1) + (num + 1).ToString(CultureInfo.InvariantCulture);
            }

            return previousName + "1";
        }


        private static bool TryParseBool(string? text, out bool result)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                result = false;
                return false;
            }

            var trimmed = text.Trim();

            if (bool.TryParse(trimmed, out result))
                return true;

            if (int.TryParse(trimmed, out var value))
            {
                result = value != 0;
                return true;
            }

            if (trimmed.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (trimmed.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }


        private static bool TryParseTrendValue(string? text, out double result)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                result = 0;
                return false;
            }

            var trimmed = text.Trim();

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            if (bool.TryParse(trimmed, out var b))
            {
                result = b ? 1 : 0;
                return true;
            }

            return false;
        }

    }
}
