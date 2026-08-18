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

        public string AddPen(string area, int address, string? requestedName, int readPeriodMs, string? type = null)
        {
            if (string.IsNullOrWhiteSpace(area)) throw new ArgumentException("An area is required.", nameof(area));
            if (address < 0) throw new ArgumentOutOfRangeException(nameof(address), "Address cannot be negative.");

            var pens = _configStore.CurrentConfig.TrendPens;

            var existing = pens.FirstOrDefault(p =>
                p.Address == address &&
                string.Equals(p.Area, area, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                // Reuse: the pen's name is the stable series key. Keep the
                // existing type; a new read period wins when given.
                if (readPeriodMs > 0) existing.ReadPeriodMs = readPeriodMs;
                return existing.Name;
            }

            var pen = new TrendPen
            {
                Name = UnitIdConfiguration.MakeUniquePenName(
                    pens,
                    string.IsNullOrWhiteSpace(requestedName) ? DefaultName(area, address) : requestedName.Trim()),
                Area = area,
                Address = address,
                Type = string.IsNullOrWhiteSpace(type) ? "int" : type,
                ReadPeriodMs = readPeriodMs <= 0 ? 1000 : readPeriodMs
            };
            pens.Add(pen);
            return pen.Name;
        }

        public bool RemovePen(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            var pens = _configStore.CurrentConfig.TrendPens;
            var pen = pens.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.Ordinal));
            if (pen == null) return false;

            return pens.Remove(pen);
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
