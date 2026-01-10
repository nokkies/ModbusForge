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
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
        private readonly ITrendLogger _trendLogger;
        private readonly ILogger<TrendCoordinator> _logger;
        private bool _isTrending;

        public TrendCoordinator(
            ModbusTcpService clientService,
            ModbusServerService serverService,
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

            var snapshot = trendEntries.ToList();
            if (snapshot.Count == 0) return;

            _isTrending = true;
            try
            {
                var now = DateTime.UtcNow;
                int errorCount = 0;

                foreach (var ce in snapshot)
                {
                    try
                    {
                        var val = await ReadValueForTrendAsync(ce, unitId, isServerMode);
                        if (val.HasValue)
                        {
                            _trendLogger.Publish(GetTrendKey(ce), val.Value, now);
                            
                            // Update display value
                            var area = (ce.Area ?? "HoldingRegister").ToLowerInvariant();
                            var type = (ce.Type ?? "uint").ToLowerInvariant();
                            string display;
                            
                            if (area == "coil" || area == "discreteinput")
                            {
                                display = val.Value != 0.0 ? "1" : "0";
                            }
                            else if (type == "real")
                            {
                                display = val.Value.ToString("G9", CultureInfo.InvariantCulture);
                            }
                            else if (type == "int")
                            {
                                display = ((short)val.Value).ToString(CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                display = ((ushort)val.Value).ToString(CultureInfo.InvariantCulture);
                            }
                            
                            ce.Value = display;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Trend read failed for {Area} {Address}", ce.Area, ce.Address);
                        errorCount++;
                    }
                }

                // If all trend reads failed, disable monitoring and show error
                if (errorCount > 0 && errorCount == snapshot.Count)
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

        /// <summary>
        /// Reads a value from a custom entry for trend logging.
        /// </summary>
        private async Task<double?> ReadValueForTrendAsync(CustomEntry entry, byte unitId, bool isServerMode)
        {
            var service = isServerMode ? (IModbusService)_serverService : _clientService;
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                    return await ReadHoldingRegisterForTrendAsync(service, entry, unitId);

                case "inputregister":
                    return await ReadInputRegisterForTrendAsync(service, entry, unitId);

                case "coil":
                    {
                        var states = await service.ReadCoilsAsync(unitId, entry.Address, 1);
                        if (states is null) return null;
                        return states[0] ? 1.0 : 0.0;
                    }

                case "discreteinput":
                    {
                        var states = await service.ReadDiscreteInputsAsync(unitId, entry.Address, 1);
                        if (states is null) return null;
                        return states[0] ? 1.0 : 0.0;
                    }

                default:
                    return null;
            }
        }

        private async Task<double?> ReadHoldingRegisterForTrendAsync(IModbusService service, CustomEntry entry, byte unitId)
        {
            var type = (entry.Type ?? "uint").ToLowerInvariant();

            if (type == "real")
            {
                var regs = await service.ReadHoldingRegistersAsync(unitId, entry.Address, 2);
                if (regs is null) return null;
                return DataTypeConverter.ToSingle(regs[0], regs[1]);
            }
            else if (type == "int")
            {
                var regs = await service.ReadHoldingRegistersAsync(unitId, entry.Address, 1);
                if (regs is null) return null;
                short sv = unchecked((short)regs[0]);
                return (double)sv;
            }
            else if (type == "string")
            {
                // Not a numeric trend; skip
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
            var type = (entry.Type ?? "uint").ToLowerInvariant();

            if (type == "real")
            {
                var regs = await service.ReadInputRegistersAsync(unitId, entry.Address, 2);
                if (regs is null) return null;
                return DataTypeConverter.ToSingle(regs[0], regs[1]);
            }
            else if (type == "int")
            {
                var regs = await service.ReadInputRegistersAsync(unitId, entry.Address, 1);
                if (regs is null) return null;
                short sv = unchecked((short)regs[0]);
                return (double)sv;
            }
            else if (type == "string")
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
