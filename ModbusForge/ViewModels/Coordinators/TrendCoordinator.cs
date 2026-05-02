using System;
using System.Collections.Generic;
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
    /// Coordinates trend logging operations for custom entries.
    /// Handles reading values for trending and publishing to the trend logger.
    /// </summary>
    public class TrendCoordinator
    {
        private readonly IModbusService _clientService;
        private readonly IModbusService _serverService;
        private readonly ITrendLogger _trendLogger;
        private readonly ILogger<TrendCoordinator> _logger;
        private bool _isTrending;

        public TrendCoordinator(
            IModbusService clientService,
            IModbusService serverService,
            ITrendLogger trendLogger,
            ILogger<TrendCoordinator> logger)
        {
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes trend sampling for custom entries that have trending enabled.
        /// </summary>
        public async Task ProcessTrendSamplingAsync(
            IEnumerable<CustomEntry> trendEntries,
            byte unitId,
            bool isServerMode,
            Action<bool> setGlobalMonitorEnabled)
        {
            if (_isTrending) return;

            var snapshot = trendEntries.Where(ce => !string.Equals(ce.Type, "string", StringComparison.OrdinalIgnoreCase)).ToList();
            if (snapshot.Count == 0) return;

            _isTrending = true;
            try
            {
                var now = DateTime.UtcNow;
                int totalEntries = snapshot.Count;
                int successCount = 0;
                int errorCount = 0;

                var groups = snapshot.GroupBy(ce => ce.Area ?? "HoldingRegister", StringComparer.OrdinalIgnoreCase);

                foreach (var group in groups)
                {
                    await ProcessAreaAsync(group.Key.ToLowerInvariant(), group, unitId, isServerMode, now,
                        onSuccess: () => successCount++,
                        onError: () => errorCount++);
                }

                // If all trend reads failed, disable monitoring and show error
                if (errorCount > 0 && successCount == 0)
                {
                    setGlobalMonitorEnabled(false);
                    MessageBox.Show(
                        "All trend reads failed. Continuous monitoring has been paused. Fix the issue and re-enable monitoring.",
                        "Trend Read Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                _isTrending = false;
            }
        }

        private async Task ProcessAreaAsync(
            string area,
            IEnumerable<CustomEntry> entries,
            byte unitId,
            bool isServerMode,
            DateTime now,
            Action onSuccess,
            Action onError)
        {
            var service = isServerMode ? _serverService : _clientService;
            var sorted = entries.OrderBy(e => e.Address).ToList();
            var chunks = CreateChunks(sorted);

            foreach (var chunk in chunks)
            {
                try
                {
                    await ReadChunkAsync(area, chunk, service, unitId, now);
                    foreach (var _ in chunk) onSuccess();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Trend read failed for chunk in {Area}", area);
                    // If a chunk fails, try to fallback to individual reads?
                    // For now, assume chunk failure means connection issue or invalid range,
                    // which likely affects individual reads too.
                    // But to be safe and consistent with previous behavior (where one bad entry didn't stop others),
                    // we could try falling back. However, existing code didn't group, so one bad entry just failed that one.
                    // If we group, one bad entry (e.g. illegal address) will fail the whole chunk.
                    // This is a trade-off. To be robust, we should try individual read on failure.

                    // Fallback to individual reads
                    foreach (var entry in chunk)
                    {
                        try
                        {
                            var val = await ReadValueForTrendAsync(entry, unitId, isServerMode);
                            if (val.HasValue)
                            {
                                UpdateEntry(entry, val.Value, now);
                                onSuccess();
                            }
                            else
                            {
                                // Should be rare to get null without exception if connected
                                onError();
                            }
                        }
                        catch (Exception innerEx)
                        {
                            _logger.LogDebug(innerEx, "Individual fallback failed for {Area} {Address}", entry.Area, entry.Address);
                            onError();
                        }
                    }
                }
            }
        }

        private List<List<CustomEntry>> CreateChunks(List<CustomEntry> sortedEntries)
        {
            var chunks = new List<List<CustomEntry>>();
            if (sortedEntries.Count == 0) return chunks;

            var currentChunk = new List<CustomEntry>();
            currentChunk.Add(sortedEntries[0]);

            int chunkStart = sortedEntries[0].Address;
            int chunkEnd = chunkStart + GetSize(sortedEntries[0]);

            for (int i = 1; i < sortedEntries.Count; i++)
            {
                var entry = sortedEntries[i];
                int entryStart = entry.Address;
                int entryEnd = entryStart + GetSize(entry);

                int potentialNewEnd = Math.Max(chunkEnd, entryEnd);
                int potentialCount = potentialNewEnd - chunkStart;

                // Allow gap of up to 10 registers/coils. Reading 10 extra is cheap compared to new request.
                // Modbus frame overhead ~10-20 bytes + RTT.
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
                    currentChunk = new List<CustomEntry>();
                    currentChunk.Add(entry);
                    chunkStart = entryStart;
                    chunkEnd = entryEnd;
                }
            }
            chunks.Add(currentChunk);
            return chunks;
        }

        private int GetSize(CustomEntry entry)
        {
            var type = entry.Type ?? "uint";
            if (string.Equals(type, "real", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "float", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private async Task ReadChunkAsync(
            string area,
            List<CustomEntry> chunk,
            IModbusService service,
            byte unitId,
            DateTime now)
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
                    double? val = ExtractRegisterValue(entry, data, offset);
                    if (val.HasValue) UpdateEntry(entry, val.Value, now);
                }
            }
            else if (area == "inputregister")
            {
                var data = await service.ReadInputRegistersAsync(unitId, startAddress, count);
                if (data == null) throw new Exception("Read returned null");
                foreach (var entry in chunk)
                {
                    int offset = entry.Address - startAddress;
                    double? val = ExtractRegisterValue(entry, data, offset);
                    if (val.HasValue) UpdateEntry(entry, val.Value, now);
                }
            }
            else if (area == "coil")
            {
                var data = await service.ReadCoilsAsync(unitId, startAddress, count);
                if (data == null) throw new Exception("Read returned null");
                foreach (var entry in chunk)
                {
                    int offset = entry.Address - startAddress;
                    double? val = (offset < data.Length) ? (data[offset] ? 1.0 : 0.0) : null;
                    if (val.HasValue) UpdateEntry(entry, val.Value, now);
                }
            }
            else if (area == "discreteinput")
            {
                var data = await service.ReadDiscreteInputsAsync(unitId, startAddress, count);
                if (data == null) throw new Exception("Read returned null");
                foreach (var entry in chunk)
                {
                    int offset = entry.Address - startAddress;
                    double? val = (offset < data.Length) ? (data[offset] ? 1.0 : 0.0) : null;
                    if (val.HasValue) UpdateEntry(entry, val.Value, now);
                }
            }
        }

        private double? ExtractRegisterValue(CustomEntry entry, ushort[] data, int offset)
        {
            if (offset < 0 || offset >= data.Length) return null;

            var type = entry.Type ?? "uint";

            if (string.Equals(type, "real", StringComparison.OrdinalIgnoreCase))
            {
                if (offset + 1 >= data.Length) return null;
                return DataTypeConverter.ToSingle(data[offset], data[offset + 1]);
            }
            else if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
            {
                return (double)unchecked((short)data[offset]);
            }
            else // uint
            {
                return (double)data[offset];
            }
        }

        private void UpdateEntry(CustomEntry ce, double val, DateTime now)
        {
             _trendLogger.Publish(GetTrendKey(ce), val, now);

            var area = ce.Area ?? "HoldingRegister";
            var type = ce.Type ?? "uint";
            string display;

            if (string.Equals(area, "coil", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(area, "discreteinput", StringComparison.OrdinalIgnoreCase))
            {
                display = val != 0.0 ? "1" : "0";
            }
            else if (string.Equals(type, "real", StringComparison.OrdinalIgnoreCase))
            {
                display = val.ToString("G9", CultureInfo.InvariantCulture);
            }
            else if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
            {
                display = ((short)val).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                display = ((ushort)val).ToString(CultureInfo.InvariantCulture);
            }

            ce.Value = display;
        }

        /// <summary>
        /// Reads a value from a custom entry for trend logging.
        /// (Kept for fallback logic)
        /// </summary>
        private async Task<double?> ReadValueForTrendAsync(CustomEntry entry, byte unitId, bool isServerMode)
        {
            var service = isServerMode ? _serverService : _clientService;
            var area = entry.Area ?? "HoldingRegister";

            if (string.Equals(area, "holdingregister", StringComparison.OrdinalIgnoreCase))
            {
                return await ReadHoldingRegisterForTrendAsync(service, entry, unitId);
            }
            else if (string.Equals(area, "inputregister", StringComparison.OrdinalIgnoreCase))
            {
                return await ReadInputRegisterForTrendAsync(service, entry, unitId);
            }
            else if (string.Equals(area, "coil", StringComparison.OrdinalIgnoreCase))
            {
                var states = await service.ReadCoilsAsync(unitId, entry.Address, 1);
                if (states is null) return null;
                return states[0] ? 1.0 : 0.0;
            }
            else if (string.Equals(area, "discreteinput", StringComparison.OrdinalIgnoreCase))
            {
                var states = await service.ReadDiscreteInputsAsync(unitId, entry.Address, 1);
                if (states is null) return null;
                return states[0] ? 1.0 : 0.0;
            }

            return null;
        }

        private async Task<double?> ReadHoldingRegisterForTrendAsync(IModbusService service, CustomEntry entry, byte unitId)
        {
            var type = entry.Type ?? "uint";

            if (string.Equals(type, "real", StringComparison.OrdinalIgnoreCase))
            {
                var regs = await service.ReadHoldingRegistersAsync(unitId, entry.Address, 2);
                if (regs is null) return null;
                return DataTypeConverter.ToSingle(regs[0], regs[1]);
            }
            else if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
            {
                var regs = await service.ReadHoldingRegistersAsync(unitId, entry.Address, 1);
                if (regs is null) return null;
                short sv = unchecked((short)regs[0]);
                return (double)sv;
            }
            else if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            else // uint
            {
                var regs = await service.ReadHoldingRegistersAsync(unitId, entry.Address, 1);
                if (regs is null) return null;
                return (double)regs[0];
            }
        }

        private async Task<double?> ReadInputRegisterForTrendAsync(IModbusService service, CustomEntry entry, byte unitId)
        {
            var type = entry.Type ?? "uint";

            if (string.Equals(type, "real", StringComparison.OrdinalIgnoreCase))
            {
                var regs = await service.ReadInputRegistersAsync(unitId, entry.Address, 2);
                if (regs is null) return null;
                return DataTypeConverter.ToSingle(regs[0], regs[1]);
            }
            else if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
            {
                var regs = await service.ReadInputRegistersAsync(unitId, entry.Address, 1);
                if (regs is null) return null;
                short sv = unchecked((short)regs[0]);
                return (double)sv;
            }
            else if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            else // uint
            {
                var regs = await service.ReadInputRegistersAsync(unitId, entry.Address, 1);
                if (regs is null) return null;
                return (double)regs[0];
            }
        }

        /// <summary>
        /// Gets the trend key for a custom entry.
        /// </summary>
        public static string GetTrendKey(CustomEntry ce) => $"{(ce.Area ?? "HoldingRegister")}:{ce.Address}";

        /// <summary>
        /// Gets the display name for a trend entry.
        /// </summary>
        public static string GetTrendDisplayName(CustomEntry ce)
        {
            var name = (ce.Name ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(name)) return name;
            return $"{(ce.Area ?? "HR")} {ce.Address} ({ce.Type})";
        }
    }
}
