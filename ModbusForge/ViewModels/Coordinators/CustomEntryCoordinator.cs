using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Reads multiple custom entries using a batched approach for better performance.
        /// </summary>
        public async Task ReadCustomEntriesAsync(IEnumerable<CustomEntry> entries, byte unitId, Action<string> setStatusMessage, bool isServerMode)
        {
            var snapshot = entries.ToList();
            if (snapshot.Count == 0) return;

            try
            {
                var groups = snapshot.GroupBy(ce => (ce.Area ?? "HoldingRegister").ToLowerInvariant());

                foreach (var group in groups)
                {
                    await ProcessAreaBatchAsync(group.Key, group, unitId, isServerMode, setStatusMessage);
                }
                setStatusMessage($"Read {snapshot.Count} custom entries");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading batched custom entries");
                setStatusMessage($"Batch read error: {ex.Message}");
            }
        }

        private async Task ProcessAreaBatchAsync(string area, IEnumerable<CustomEntry> entries, byte unitId, bool isServerMode, Action<string> setStatusMessage)
        {
            var service = GetService(isServerMode);
            var sorted = entries.OrderBy(e => e.Address).ToList();
            var chunks = CreateChunks(sorted);

            foreach (var chunk in chunks)
            {
                try
                {
                    await ReadChunkAsync(area, chunk, service, unitId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Batch read failed for chunk in {Area}, falling back to individual reads", area);
                    foreach (var entry in chunk)
                    {
                        try { await ReadCustomNowAsync(entry, unitId, setStatusMessage, isServerMode); }
                        catch { /* suppress individual fallback errors to continue with others */ }
                    }
                }
            }
        }

        private List<List<CustomEntry>> CreateChunks(List<CustomEntry> sortedEntries)
        {
            var chunks = new List<List<CustomEntry>>();
            if (sortedEntries.Count == 0) return chunks;

            var currentChunk = new List<CustomEntry> { sortedEntries[0] };

            int chunkStart = sortedEntries[0].Address;
            int chunkEnd = chunkStart + GetSize(sortedEntries[0]);

            for (int i = 1; i < sortedEntries.Count; i++)
            {
                var entry = sortedEntries[i];
                int entryStart = entry.Address;
                int entryEnd = entryStart + GetSize(entry);

                int potentialNewEnd = Math.Max(chunkEnd, entryEnd);
                int potentialCount = potentialNewEnd - chunkStart;

                // Allow gap of up to 10 registers/coils.
                bool gapOk = (entryStart - chunkEnd) <= 10;
                // Max count: 120 (safe margin below 125/253 limits)
                bool countOk = potentialCount <= 120;

                if (gapOk && countOk)
                {
                    currentChunk.Add(entry);
                    chunkEnd = potentialNewEnd;
                }
                else
                {
                    chunks.Add(currentChunk);
                    currentChunk = new List<CustomEntry> { entry };
                    chunkStart = entryStart;
                    chunkEnd = entryEnd;
                }
            }
            chunks.Add(currentChunk);
            return chunks;
        }

        private int GetSize(CustomEntry entry)
        {
            var type = (entry.Type ?? "uint").ToLowerInvariant();
            if (type == "real" || type == "float") return 2;
            return 1;
        }

        private async Task ReadChunkAsync(string area, List<CustomEntry> chunk, IModbusService service, byte unitId)
        {
            if (chunk.Count == 0) return;

            int startAddress = chunk[0].Address;
            int endAddress = chunk.Max(e => e.Address + GetSize(e));
            int count = endAddress - startAddress;

            if (area == "holdingregister")
            {
                var data = await service.ReadHoldingRegistersAsync(unitId, startAddress, count);
                if (data == null) throw new Exception("Read returned null");
                foreach (var entry in chunk)
                {
                    int offset = entry.Address - startAddress;
                    var val = ExtractValue(entry, data, offset);
                    if (val != null) entry.Value = val;
                }
            }
            else if (area == "inputregister")
            {
                var data = await service.ReadInputRegistersAsync(unitId, startAddress, count);
                if (data == null) throw new Exception("Read returned null");
                foreach (var entry in chunk)
                {
                    int offset = entry.Address - startAddress;
                    var val = ExtractValue(entry, data, offset);
                    if (val != null) entry.Value = val;
                }
            }
            else if (area == "coil")
            {
                var data = await service.ReadCoilsAsync(unitId, startAddress, count);
                if (data == null) throw new Exception("Read returned null");
                foreach (var entry in chunk)
                {
                    int offset = entry.Address - startAddress;
                    if (offset < data.Length)
                        entry.Value = data[offset] ? "1" : "0";
                }
            }
            else if (area == "discreteinput")
            {
                var data = await service.ReadDiscreteInputsAsync(unitId, startAddress, count);
                if (data == null) throw new Exception("Read returned null");
                foreach (var entry in chunk)
                {
                    int offset = entry.Address - startAddress;
                    if (offset < data.Length)
                        entry.Value = data[offset] ? "1" : "0";
                }
            }
        }

        private string? ExtractValue(CustomEntry entry, ushort[] data, int offset)
        {
            if (offset < 0 || offset >= data.Length) return null;

            var type = (entry.Type ?? "uint").ToLowerInvariant();

            if (type == "real")
            {
                if (offset + 1 >= data.Length) return null;
                return DataTypeConverter.ToSingle(data[offset], data[offset + 1]).ToString(CultureInfo.InvariantCulture);
            }
            else if (type == "int")
            {
                return unchecked((short)data[offset]).ToString(CultureInfo.InvariantCulture);
            }
            else if (type == "string")
            {
                return DataTypeConverter.ToString(data[offset]);
            }
            else // uint
            {
                return data[offset].ToString(CultureInfo.InvariantCulture);
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