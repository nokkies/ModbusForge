using System;
using System.Linq;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <inheritdoc />
    public sealed class TrendSubscriptionService : ITrendSubscriptionService
    {
        private readonly IUnitConfigurationStore _configStore;

        public TrendSubscriptionService(IUnitConfigurationStore configStore)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        }

        public string AddPen(string area, int address, string? requestedName, int readPeriodMs,
            string? type = null, string? initialValue = null)
        {
            if (string.IsNullOrWhiteSpace(area)) throw new ArgumentException("An area is required.", nameof(area));
            if (address < 0) throw new ArgumentOutOfRangeException(nameof(address), "Address cannot be negative.");

            var entries = _configStore.CurrentConfig.CustomEntries;

            var existing = entries.FirstOrDefault(e => e.Area == area && e.Address == address);
            if (existing != null)
            {
                // Reuse: the entry's name is the stable series key, and its
                // type/value are whatever the user already configured.
                existing.Trend = true;
                existing.Monitor = true;
                if (readPeriodMs > 0) existing.ReadPeriodMs = readPeriodMs;
                return existing.Name;
            }

            var entry = new CustomEntry
            {
                Name = string.IsNullOrWhiteSpace(requestedName) ? DefaultName(area, address) : requestedName.Trim(),
                Address = address,
                Area = area,
                Type = string.IsNullOrWhiteSpace(type) ? "int" : type,
                Value = string.IsNullOrWhiteSpace(initialValue) ? "0" : initialValue,
                WriteValue = string.IsNullOrWhiteSpace(initialValue) ? "0" : initialValue,
                Continuous = false,
                PeriodMs = 1000,
                Monitor = true,
                ReadPeriodMs = readPeriodMs <= 0 ? 1000 : readPeriodMs,
                Trend = true
            };
            entries.Add(entry);
            return entry.Name;
        }

        public bool RemovePen(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            var entries = _configStore.CurrentConfig.CustomEntries;
            var entry = entries.FirstOrDefault(e => e.Name == key);
            if (entry == null) return false;

            entry.Trend = false;
            return true;
        }

        public string DefaultName(string area, int address)
        {
            // Historical context-menu naming: register areas abbreviate to
            // "HR"/"IR", coil areas keep their full name.
            return area switch
            {
                "HoldingRegister" => $"HR Trend {address}",
                "InputRegister" => $"IR Trend {address}",
                _ => $"{area} Trend {address}"
            };
        }
    }
}
