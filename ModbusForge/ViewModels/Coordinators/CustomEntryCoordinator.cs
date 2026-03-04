using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using ModbusForge.Helpers;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.ViewModels.Coordinators
{
    /// <summary>
    /// Handles custom entry operations including read, write, save, and load.
    /// </summary>
    public class CustomEntryCoordinator
    {
        private readonly RegisterCoordinator _registerCoordinator;
        private readonly ICustomEntryService _customEntryService;
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
        private readonly ILogger<CustomEntryCoordinator> _logger;

        public CustomEntryCoordinator(
            RegisterCoordinator registerCoordinator,
            ICustomEntryService customEntryService,
            ModbusTcpService clientService,
            ModbusServerService serverService,
            ILogger<CustomEntryCoordinator> logger)
        {
            _registerCoordinator = registerCoordinator ?? throw new ArgumentNullException(nameof(registerCoordinator));
            _customEntryService = customEntryService ?? throw new ArgumentNullException(nameof(customEntryService));
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Adds a new custom entry to the collection.
        /// </summary>
        public void AddCustomEntry(ObservableCollection<CustomEntry> customEntries)
        {
            int nextAddress = 1;
            if (customEntries.Count > 0)
            {
                nextAddress = customEntries[^1].Address + 1;
            }
            customEntries.Add(new CustomEntry 
            { 
                Address = nextAddress, 
                Area = "HoldingRegister", 
                Type = "uint", 
                Value = "0", 
                Continuous = false, 
                PeriodMs = 1000, 
                Monitor = false, 
                ReadPeriodMs = 1000 
            });
        }

        /// <summary>
        /// Writes a custom entry value.
        /// </summary>
        public async Task WriteCustomNowAsync(CustomEntry entry, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            if (entry is null) return;
            
            try
            {
                var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
                switch (area)
                {
                    case "holdingregister":
                        await WriteHoldingRegisterByTypeAsync(entry, unitId, setStatusMessage, isServerMode);
                        break;
                    case "coil":
                        await WriteCoilAsync(entry, unitId, setStatusMessage, isServerMode);
                        break;
                    case "inputregister":
                    case "discreteinput":
                        setStatusMessage($"{entry.Area} is read-only. Select HoldingRegister or Coil to write.");
                        break;
                    default:
                        setStatusMessage($"Unknown area: {entry.Area}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing custom entry");
                setStatusMessage($"Custom write error: {ex.Message}");
            }
        }

        private async Task WriteHoldingRegisterByTypeAsync(CustomEntry entry, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            var type = (entry.Type ?? "uint").ToLowerInvariant();
            switch (type)
            {
                case "real":
                    if (float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ||
                        float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                    {
                        await _registerCoordinator.WriteFloatAtAsync(unitId, entry.Address, f, isServerMode);
                        setStatusMessage($"Wrote REAL {f} at {entry.Address}");
                    }
                    else
                    {
                        setStatusMessage($"Invalid float: {entry.Value}");
                    }
                    break;
                case "string":
                    await _registerCoordinator.WriteStringAtAsync(unitId, entry.Address, entry.Value ?? string.Empty, isServerMode);
                    setStatusMessage($"Wrote STRING '{entry.Value}' at {entry.Address}");
                    break;
                case "int":
                    if (int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                    {
                        await _registerCoordinator.WriteRegisterAtAsync(unitId, entry.Address, unchecked((ushort)iv), isServerMode);
                        setStatusMessage($"Wrote INT {iv} at {entry.Address}");
                    }
                    else
                    {
                        setStatusMessage($"Invalid int: {entry.Value}");
                    }
                    break;
                default: // uint
                    if (uint.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uv))
                    {
                        if (uv > 0xFFFF) uv = 0xFFFF;
                        await _registerCoordinator.WriteRegisterAtAsync(unitId, entry.Address, (ushort)uv, isServerMode);
                        setStatusMessage($"Wrote UINT {uv} at {entry.Address}");
                    }
                    else
                    {
                        setStatusMessage($"Invalid uint: {entry.Value}");
                    }
                    break;
            }
        }

        private async Task WriteCoilAsync(CustomEntry entry, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            if (TryParseBool(entry.Value, out bool b))
            {
                await _registerCoordinator.WriteCoilAtAsync(unitId, entry.Address, b, isServerMode);
                setStatusMessage($"Wrote COIL {(b ? 1 : 0)} at {entry.Address}");
            }
            else
            {
                setStatusMessage($"Invalid coil value: {entry.Value}. Use true/false or 1/0.");
            }
        }

        /// <summary>
        /// Reads a custom entry value.
        /// </summary>
        public async Task ReadCustomNowAsync(CustomEntry entry, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            if (entry is null) return;
            try
            {
                var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
                switch (area)
                {
                    case "holdingregister":
                        await ReadHoldingRegisterByTypeAsync(entry, unitId, setStatusMessage, isServerMode);
                        break;
                    case "inputregister":
                        await ReadInputRegisterByTypeAsync(entry, unitId, setStatusMessage, isServerMode);
                        break;
                    case "coil":
                        await ReadCoilAsync(entry, unitId, setStatusMessage, isServerMode);
                        break;
                    case "discreteinput":
                        await ReadDiscreteInputAsync(entry, unitId, setStatusMessage, isServerMode);
                        break;
                    default:
                        setStatusMessage($"Unknown area: {entry.Area}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading custom entry");
                setStatusMessage($"Custom read error: {ex.Message}");
            }
        }

        private IModbusService GetService(bool isServerMode) => isServerMode ? _serverService : _clientService;

        /// <summary>
        /// Reads a holding register value based on the entry's data type.
        /// </summary>
        private async Task ReadHoldingRegisterByTypeAsync(CustomEntry entry, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            var type = (entry.Type ?? "uint").ToLowerInvariant();
            var address = entry.Address;
            var service = GetService(isServerMode);

            switch (type)
            {
                case "real":
                    var regsReal = await service.ReadHoldingRegistersAsync(unitId, address, 2);
                    if (regsReal is null) return;
                    entry.Value = DataTypeConverter.ToSingle(regsReal[0], regsReal[1]).ToString(CultureInfo.InvariantCulture);
                    setStatusMessage($"Read REAL {entry.Value} from HR {address}");
                    break;
                case "int":
                    var regsInt = await service.ReadHoldingRegistersAsync(unitId, address, 1);
                    if (regsInt is null) return;
                    entry.Value = unchecked((short)regsInt[0]).ToString(CultureInfo.InvariantCulture);
                    setStatusMessage($"Read INT {entry.Value} from HR {address}");
                    break;
                case "string":
                    var regsString = await service.ReadHoldingRegistersAsync(unitId, address, 1);
                    if (regsString is null) return;
                    entry.Value = DataTypeConverter.ToString(regsString[0]);
                    setStatusMessage($"Read STRING '{entry.Value}' from HR {address}");
                    break;
                default: // uint
                    var regsUInt = await service.ReadHoldingRegistersAsync(unitId, address, 1);
                    if (regsUInt is null) return;
                    entry.Value = regsUInt[0].ToString(CultureInfo.InvariantCulture);
                    setStatusMessage($"Read UINT {entry.Value} from HR {address}");
                    break;
            }
        }

        /// <summary>
        /// Reads an input register value based on the entry's data type.
        /// </summary>
        private async Task ReadInputRegisterByTypeAsync(CustomEntry entry, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            var type = (entry.Type ?? "uint").ToLowerInvariant();
            var address = entry.Address;
            var service = GetService(isServerMode);

            switch (type)
            {
                case "real":
                    var regsReal = await service.ReadInputRegistersAsync(unitId, address, 2);
                    if (regsReal is null) return;
                    entry.Value = DataTypeConverter.ToSingle(regsReal[0], regsReal[1]).ToString(CultureInfo.InvariantCulture);
                    setStatusMessage($"Read REAL {entry.Value} from IR {address}");
                    break;
                case "int":
                    var regsInt = await service.ReadInputRegistersAsync(unitId, address, 1);
                    if (regsInt is null) return;
                    entry.Value = unchecked((short)regsInt[0]).ToString(CultureInfo.InvariantCulture);
                    setStatusMessage($"Read INT {entry.Value} from IR {address}");
                    break;
                case "string":
                    var regsString = await service.ReadInputRegistersAsync(unitId, address, 1);
                    if (regsString is null) return;
                    entry.Value = DataTypeConverter.ToString(regsString[0]);
                    setStatusMessage($"Read STRING '{entry.Value}' from IR {address}");
                    break;
                default: // uint
                    var regsUInt = await service.ReadInputRegistersAsync(unitId, address, 1);
                    if (regsUInt is null) return;
                    entry.Value = regsUInt[0].ToString(CultureInfo.InvariantCulture);
                    setStatusMessage($"Read UINT {entry.Value} from IR {address}");
                    break;
            }
        }

        /// <summary>
        /// Reads a coil (boolean) value.
        /// </summary>
        private async Task ReadCoilAsync(CustomEntry entry, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            var service = GetService(isServerMode);
            var states = await service.ReadCoilsAsync(unitId, entry.Address, 1);
            if (states is null) return;
            entry.Value = states[0] ? "1" : "0";
            setStatusMessage($"Read COIL {entry.Value} from {entry.Address}");
        }

        /// <summary>
        /// Reads a discrete input (boolean) value.
        /// </summary>
        private async Task ReadDiscreteInputAsync(CustomEntry entry, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            var service = GetService(isServerMode);
            var states = await service.ReadDiscreteInputsAsync(unitId, entry.Address, 1);
            if (states is null) return;
            entry.Value = states[0] ? "1" : "0";
            setStatusMessage($"Read DI {entry.Value} from {entry.Address}");
        }

        private static bool TryParseBool(string? value, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value)) return false;
            
            var v = value.Trim().ToLowerInvariant();
            if (v == "true" || v == "1" || v == "on" || v == "yes")
            {
                result = true;
                return true;
            }
            if (v == "false" || v == "0" || v == "off" || v == "no")
            {
                result = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Saves custom entries to a file.
        /// </summary>
        public async Task SaveCustomAsync(ObservableCollection<CustomEntry> customEntries, Action<string> setStatusMessage)
        {
            try
            {
                await _customEntryService.SaveCustomAsync(customEntries);
                setStatusMessage($"Saved {customEntries.Count} custom entries");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving custom entries");
                setStatusMessage($"Error saving: {ex.Message}");
                MessageBox.Show($"Failed to save custom entries: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Loads custom entries from a file.
        /// </summary>
        public async Task LoadCustomAsync(ObservableCollection<CustomEntry> customEntries, Action<string> setStatusMessage)
        {
            try
            {
                var loadedEntries = await _customEntryService.LoadCustomAsync();
                if (loadedEntries != null)
                {
                    customEntries.Clear();
                    foreach (var entry in loadedEntries)
                    {
                        customEntries.Add(entry);
                    }
                    setStatusMessage($"Loaded {loadedEntries.Count} custom entries");
                }
                else
                {
                    setStatusMessage($"Load cancelled");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom entries");
                setStatusMessage($"Error loading: {ex.Message}");
                MessageBox.Show($"Failed to load custom entries: {ex.Message}", "Load Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}