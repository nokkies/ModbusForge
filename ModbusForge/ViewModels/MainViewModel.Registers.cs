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
    /// MainViewModel - Registers partial (split for navigability; behavior unchanged).
    /// </summary>
    public partial class MainViewModel
    {
        private DateTime _lastHoldingReadUtc;

        private DateTime _lastInputRegReadUtc;

        private DateTime _lastCoilsReadUtc;

        private DateTime _lastDiscreteReadUtc;

        private const int MultiRegisterTypeIncrement = 2;

        private const int SingleRegisterTypeIncrement = 1;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCommand))]
        private PlcArea _selectedArea = PlcArea.HoldingRegister;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadHoldingRegistersCommand))]
        private int _holdingRegisterStart = 1;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadHoldingRegistersCommand))]
        private int _holdingRegisterCount = 20;


        [ObservableProperty]
        private string _registersGlobalType = "int";


        [ObservableProperty]
        private bool _registersSwapBytes;


        [ObservableProperty]
        private bool _registersSwapWords;


        [ObservableProperty]
        private int _holdingMonitorPeriodMs = 1000;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadInputRegistersCommand))]
        private int _inputRegisterStart = 1;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadInputRegistersCommand))]
        private int _inputRegisterCount = 20;


        [ObservableProperty]
        private string _inputRegistersGlobalType = "int";


        [ObservableProperty]
        private bool _inputRegistersSwapBytes;


        [ObservableProperty]
        private bool _inputRegistersSwapWords;


        [ObservableProperty]
        private int _inputRegistersMonitorPeriodMs = 1000;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCoilsCommand))]
        private int _coilStart = 1;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCoilsCommand))]
        private int _coilCount = 20;


        [ObservableProperty]
        private int _coilsMonitorPeriodMs = 1000;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadDiscreteInputsCommand))]
        private int _discreteInputStart = 1;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadDiscreteInputsCommand))]
        private int _discreteInputCount = 20;


        [ObservableProperty]
        private int _discreteInputsMonitorPeriodMs = 1000;


        [ObservableProperty]
        private ObservableCollection<RegisterEntry> _holdingRegisters = new();


        [ObservableProperty]
        private ObservableCollection<RegisterEntry> _inputRegisters = new();


        [ObservableProperty]
        private ObservableCollection<CoilEntry> _coils = new();


        [ObservableProperty]
        private ObservableCollection<CoilEntry> _discreteInputs = new();


        public ObservableCollection<RegisterEntry> Registers => HoldingRegisters;


        public bool IsRegisterArea => SelectedArea is PlcArea.HoldingRegister or PlcArea.InputRegister;

        public IAsyncRelayCommand ReadCommand { get; }

        public IAsyncRelayCommand WriteCommand { get; }

        public IAsyncRelayCommand ReadHoldingRegistersCommand { get; }

        public IAsyncRelayCommand ReadInputRegistersCommand { get; }

        public IAsyncRelayCommand ReadCoilsCommand { get; }

        public IAsyncRelayCommand ReadDiscreteInputsCommand { get; }

        public IAsyncRelayCommand WriteHoldingRegisterCommand { get; }

        public IAsyncRelayCommand WriteCoilCommand { get; }

        public ICommand ReadShortcutCommand { get; }


        [ObservableProperty]
        private bool _isRegisterGridEditing;


        partial void OnSelectedAreaChanged(PlcArea value)
        {
            IsRegisterGridEditing = false;
            OnPropertyChanged(nameof(IsRegisterArea));
            OnPropertyChanged(nameof(CanWrite));
            WriteCommand.NotifyCanExecuteChanged();
        }


        private bool CanRead() => CanRead(SelectedArea);


        private bool CanRead(PlcArea area)
        {
            if (ActiveProfile is not { IsConnected: true } || IsBusy)
                return false;

            var (start, count) = GetAreaStartCount(area);
            var validator = new ModbusAddressValidator();
            return area is PlcArea.HoldingRegister or PlcArea.InputRegister
                ? validator.IsValidAddressRange(start, count)
                : validator.IsValidRange(start, count, area);
        }


        private bool CanWrite() => CanWrite(SelectedArea);


        private bool CanWrite(PlcArea area) => ActiveProfile is { IsConnected: true } && !IsBusy &&
                                               (area is PlcArea.HoldingRegister or PlcArea.Coil);


        private async Task ReadAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            await ReadAreaWithBusyAsync(SelectedArea);
        }


        private async Task ReadHoldingRegistersAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            SelectedArea = PlcArea.HoldingRegister;
            await ReadAreaWithBusyAsync(PlcArea.HoldingRegister);
        }


        private async Task ReadInputRegistersAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            SelectedArea = PlcArea.InputRegister;
            await ReadAreaWithBusyAsync(PlcArea.InputRegister);
        }


        private async Task ReadCoilsAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            SelectedArea = PlcArea.Coil;
            await ReadAreaWithBusyAsync(PlcArea.Coil);
        }


        private async Task ReadDiscreteInputsAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            SelectedArea = PlcArea.DiscreteInput;
            await ReadAreaWithBusyAsync(PlcArea.DiscreteInput);
        }


        private async Task ReadAreaWithBusyAsync(PlcArea area)
        {
            IsBusy = true;
            try
            {
                await Task.Run(() => ReadAreaAsync(area, CancellationToken.None));
            }
            catch (Exception ex)
            {
                _dispatcher.Invoke(() => StatusMessage = $"Read error: {ex.Message}");
                _logger.LogError(ex, "Manual read failed");
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task WriteAsync() => await PromptAndWriteAsync(SelectedArea);


        private async Task WriteHoldingRegisterAsync() => await PromptAndWriteAsync(PlcArea.HoldingRegister);


        private async Task WriteCoilAsync() => await PromptAndWriteAsync(PlcArea.Coil);


        private async Task PromptAndWriteAsync(PlcArea area)
        {
            if (ActiveProfile == null || ActiveService == null || _inputDialogService == null) return;
            if (area is not (PlcArea.HoldingRegister or PlcArea.Coil)) return;

            var address = PromptAddress($"Write {area}", GetAreaStart(area));
            if (!address.HasValue) return;

            try
            {
                IsBusy = true;
                await WriteValueToAreaAsync(area, address.Value);
                await ReadAreaAsync(area, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Write failed");
                _dispatcher.Invoke(() => StatusMessage = $"Write error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task WriteValueToAreaAsync(PlcArea area, int address)
        {
            if (_inputDialogService == null || ActiveService == null)
                return;

            var service = ActiveService;
            if (service == null) return;

            await _modbusIoGate.WaitAsync(CancellationToken.None);
            try
            {
                var unitId = EffectiveUnitId;

                if (area == PlcArea.HoldingRegister)
                {
                    var valueText = _inputDialogService.TryGetInput("Write Value", "Value:", "0", out var input) ? input : null;
                    if (string.IsNullOrWhiteSpace(valueText) || !ushort.TryParse(valueText, out var value))
                    {
                        _dispatcher.Invoke(() => StatusMessage = "Invalid register value.");
                        return;
                    }

                    await service.WriteSingleRegisterAsync(unitId, address, value);
                    _dispatcher.Invoke(() => StatusMessage = $"Wrote {value} to holding register {address}.");
                }
                else if (area == PlcArea.Coil)
                {
                    var valueText = _inputDialogService.TryGetInput("Write Coil", "Value (true/false):", "false", out var input) ? input : null;
                    if (string.IsNullOrWhiteSpace(valueText) || !TryParseBool(valueText, out var value))
                    {
                        _dispatcher.Invoke(() => StatusMessage = "Invalid coil value. Use true/false, 1/0, on/off.");
                        return;
                    }

                    await service.WriteSingleCoilAsync(unitId, address, value);
                    _dispatcher.Invoke(() => StatusMessage = $"Wrote {value} to coil {address}.");
                }
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }


        public async Task WriteHoldingRegisterFromEditAsync(RegisterEntry? entry)
        {
            if (entry == null || ActiveProfile == null || ActiveService == null) return;

            IsBusy = true;
            try
            {
                var unitId = EffectiveUnitId;
                var type = (entry.Type ?? "int").ToLowerInvariant();
                var text = (entry.ValueText ?? string.Empty).Trim().Replace(',', '.');

                await _modbusIoGate.WaitAsync(CancellationToken.None);
                try
                {
                    switch (type)
                    {
                        case "real":
                            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                            {
                                var words = DataTypeConverter.ToUInt16(f, RegistersSwapBytes, RegistersSwapWords);
                                await ActiveService.WriteRegistersAsync(unitId, entry.Address, words);
                            }
                            else
                            {
                                _dispatcher.Invoke(() => StatusMessage = $"Invalid float value: {entry.ValueText}");
                                return;
                            }
                            break;

                        case "string":
                            // A string write expands to one register per two characters. Without a cap a
                            // long string would silently clobber every register after the target address
                            // (or fail deep in NModbus past the FC16 limit with a cryptic error).
                            var stringWords = DataTypeConverter.ToUInt16(text);
                            if (stringWords.Length > ModbusAddressValidator.MaxWriteRegisters)
                            {
                                _dispatcher.Invoke(() => StatusMessage =
                                    $"String write too large: {stringWords.Length} registers (max {ModbusAddressValidator.MaxWriteRegisters}).");
                                return;
                            }
                            await ActiveService.WriteRegistersAsync(unitId, entry.Address, stringWords);
                            break;

                        case "int":
                            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                            {
                                await ActiveService.WriteSingleRegisterAsync(unitId, entry.Address, unchecked((ushort)iv));
                            }
                            else
                            {
                                _dispatcher.Invoke(() => StatusMessage = $"Invalid integer value: {entry.ValueText}");
                                return;
                            }
                            break;

                        default:
                            if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uv) && uv <= ushort.MaxValue)
                            {
                                await ActiveService.WriteSingleRegisterAsync(unitId, entry.Address, (ushort)uv);
                            }
                            else
                            {
                                _dispatcher.Invoke(() => StatusMessage = $"Invalid unsigned value: {entry.ValueText}");
                                return;
                            }
                            break;
                    }
                }
                finally
                {
                    _modbusIoGate.Release();
                }

                await ReadAreaAsync(PlcArea.HoldingRegister, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Holding register write failed");
                _dispatcher.Invoke(() => StatusMessage = $"Write error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }


        public async Task WriteCoilFromEditAsync(CoilEntry? entry)
        {
            if (entry == null || ActiveProfile == null || ActiveService == null) return;

            IsBusy = true;
            try
            {
                await _modbusIoGate.WaitAsync(CancellationToken.None);
                try
                {
                    await ActiveService.WriteSingleCoilAsync(EffectiveUnitId, entry.Address, entry.State);
                }
                finally
                {
                    _modbusIoGate.Release();
                }

                await ReadAreaAsync(PlcArea.Coil, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Coil write failed");
                _dispatcher.Invoke(() => StatusMessage = $"Write error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }


        private int? PromptAddress(string title, int defaultAddress)
        {
            if (_inputDialogService == null) return null;

            var defaultValue = defaultAddress.ToString(CultureInfo.InvariantCulture);
            if (!_inputDialogService.TryGetInput(title, "Address:", defaultValue, out var input) ||
                !int.TryParse(input, out var address))
            {
                _dispatcher.Invoke(() => StatusMessage = "Invalid address.");
                return null;
            }

            if (address is < ModbusAddressValidator.MinStartAddress or > ModbusAddressValidator.MaxStartAddress)
            {
                _dispatcher.Invoke(() => StatusMessage =
                    $"Address must be between {ModbusAddressValidator.MinStartAddress} and {ModbusAddressValidator.MaxStartAddress}.");
                return null;
            }

            return address;
        }


        private ObservableCollection<RegisterEntry> ApplyRegisterValues(
            ObservableCollection<RegisterEntry>? target,
            int start,
            ushort[] values,
            string globalType,
            bool swapBytes,
            bool swapWords,
            IEnumerable<RegisterMetadata>? metadata = null,
            bool isPartialRead = false)
        {
            target ??= new ObservableCollection<RegisterEntry>();
            var metadataByAddress = metadata?.GroupBy(item => item.Address)
                                    .ToDictionary(g => g.Key, g => g.First())
                                    ?? new Dictionary<int, RegisterMetadata>();

            // Duplicate addresses in the grid (user editing) must not crash the read path;
            // the first entry wins and is the one updated below.
            var entriesByAddress = new Dictionary<int, RegisterEntry>();
            foreach (var existing in target)
            {
                entriesByAddress.TryAdd(existing.Address, existing);
            }
            var usedAddresses = new HashSet<int>();

            int idx = 0;
            while (idx < values.Length)
            {
                var address = start + idx;
                var savedMetadata = metadataByAddress.GetValueOrDefault(address);
                var type = (savedMetadata?.Type ?? globalType).ToLowerInvariant();
                usedAddresses.Add(address);

                if (!entriesByAddress.TryGetValue(address, out var entry))
                {
                    entry = new RegisterEntry();
                    target.Add(entry);
                    entriesByAddress[address] = entry;
                }

                entry.Address = address;
                entry.Value = values[idx];
                entry.Type = type;
                entry.SwapBytes = savedMetadata?.SwapBytes ?? swapBytes;
                entry.SwapWords = savedMetadata?.SwapWords ?? swapWords;
                entry.IsReadError = false;
                entry.ReadErrorMessage = null;

                switch (type)
                {
                    case "int":
                        entry.ValueText = unchecked((short)values[idx]).ToString(CultureInfo.InvariantCulture);
                        idx += 1;
                        break;

                    case "real":
                        if (idx + 1 < values.Length)
                        {
                            entry.ValueText = DataTypeConverter.ToSingle(values[idx], values[idx + 1], entry.SwapBytes, entry.SwapWords).ToString(CultureInfo.InvariantCulture);

                            var nextAddress = address + 1;
                            usedAddresses.Add(nextAddress);
                            if (!entriesByAddress.TryGetValue(nextAddress, out var next))
                            {
                                next = new RegisterEntry();
                                target.Add(next);
                                entriesByAddress[nextAddress] = next;
                            }

                            next.Address = nextAddress;
                            next.Value = values[idx + 1];
                            next.Type = type;
                            next.SwapBytes = swapBytes;
                            next.SwapWords = swapWords;
                            next.ValueText = string.Empty;
                            next.IsReadError = false;
                            next.ReadErrorMessage = null;

                            idx += 2;
                        }
                        else
                        {
                            entry.ValueText = values[idx].ToString(CultureInfo.InvariantCulture);
                            idx += 1;
                        }
                        break;

                    case "string":
                        entry.ValueText = DataTypeConverter.ToString(values[idx]);
                        idx += 1;
                        break;

                    default:
                        entry.ValueText = values[idx].ToString(CultureInfo.InvariantCulture);
                        idx += 1;
                        break;
                }
            }

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!usedAddresses.Contains(target[i].Address))
                {
                    target.RemoveAt(i);
                }
            }

            if (isPartialRead)
            {
                var lastVisible = target
                    .Where(e => !string.IsNullOrEmpty(e.ValueText))
                    .OrderByDescending(e => e.Address)
                    .FirstOrDefault();
                if (lastVisible != null)
                {
                    lastVisible.IsReadError = true;
                    lastVisible.ReadErrorMessage = "Read stopped here. The remaining registers could not be read.";
                }
            }

            EnsureSortedByAddress(target);
            return target;
        }


        private static void EnsureSortedByAddress(ObservableCollection<RegisterEntry> collection)
        {
            if (collection.Count < 2) return;

            bool sorted = true;
            for (int i = 1; i < collection.Count; i++)
            {
                if (collection[i].Address < collection[i - 1].Address)
                {
                    sorted = false;
                    break;
                }
            }

            if (sorted) return;

            var entries = collection.OrderBy(e => e.Address).ToList();
            collection.Clear();
            foreach (var entry in entries)
            {
                collection.Add(entry);
            }
        }


        private ObservableCollection<CoilEntry> ApplyCoilValues(ObservableCollection<CoilEntry>? target, int start, bool[] values, bool isPartialRead = false)
        {
            target ??= new ObservableCollection<CoilEntry>();
            var entriesByAddress = target.ToDictionary(e => e.Address);
            var usedAddresses = new HashSet<int>();

            for (int i = 0; i < values.Length; i++)
            {
                var address = start + i;
                usedAddresses.Add(address);
                if (!entriesByAddress.TryGetValue(address, out var entry))
                {
                    entry = new CoilEntry { Address = address };
                    target.Add(entry);
                    entriesByAddress[address] = entry;
                }

                entry.State = values[i];
                entry.IsReadError = false;
                entry.ReadErrorMessage = null;
            }

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!usedAddresses.Contains(target[i].Address))
                {
                    target.RemoveAt(i);
                }
            }

            if (isPartialRead && target.Count > 0)
            {
                var lastCoil = target.OrderByDescending(e => e.Address).First();
                lastCoil.IsReadError = true;
                lastCoil.ReadErrorMessage = "Read stopped here. The remaining coils could not be read.";
            }

            EnsureSortedByAddress(target);
            return target;
        }


        private static void EnsureSortedByAddress(ObservableCollection<CoilEntry> collection)
        {
            if (collection.Count < 2) return;

            bool sorted = true;
            for (int i = 1; i < collection.Count; i++)
            {
                if (collection[i].Address < collection[i - 1].Address)
                {
                    sorted = false;
                    break;
                }
            }

            if (sorted) return;

            var entries = collection.OrderBy(e => e.Address).ToList();
            collection.Clear();
            foreach (var entry in entries)
            {
                collection.Add(entry);
            }
        }

    }
}
