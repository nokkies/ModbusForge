using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<CustomEntryCoordinator> _logger;

        public CustomEntryCoordinator(
            RegisterCoordinator registerCoordinator,
            ICustomEntryService customEntryService,
            ILogger<CustomEntryCoordinator> logger)
        {
            _registerCoordinator = registerCoordinator ?? throw new ArgumentNullException(nameof(registerCoordinator));
            _customEntryService = customEntryService ?? throw new ArgumentNullException(nameof(customEntryService));
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
