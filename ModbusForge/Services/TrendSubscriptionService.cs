using System;
using System.Collections.Generic;
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

        public IReadOnlyCollection<TrendPen> Pens => _configStore.CurrentConfig.TrendPens;

        public TrendPen AddPen(string area, int address, string? requestedName, int readPeriodMs, string? type = null)
        {
            if (string.IsNullOrWhiteSpace(area)) throw new ArgumentException("An area is required.", nameof(area));
            if (address < 0) throw new ArgumentOutOfRangeException(nameof(address), "Address cannot be negative.");

            var pens = _configStore.CurrentConfig.TrendPens;

            var existing = pens.FirstOrDefault(p =>
                p.Address == address &&
                string.Equals(p.Area, area, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                // Reuse: the pen's key is the stable series key, so the
                // caller keeps trending the same series. Keep the existing
                // type; a new read period wins when given.
                if (readPeriodMs > 0) existing.ReadPeriodMs = readPeriodMs;
                return existing;
            }

            var name = UnitIdConfiguration.MakeUniquePenName(
                pens,
                string.IsNullOrWhiteSpace(requestedName) ? DefaultName(area, address) : requestedName.Trim());
            var pen = new TrendPen
            {
                // The key is born with the (unique) name and never changes
                // again; renames only touch the display Name.
                Key = name,
                Name = name,
                Area = area,
                Address = address,
                Type = string.IsNullOrWhiteSpace(type) ? "int" : type,
                ReadPeriodMs = readPeriodMs <= 0 ? 1000 : readPeriodMs
            };
            pens.Add(pen);
            return pen;
        }

        public bool RemovePen(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            var pens = _configStore.CurrentConfig.TrendPens;
            var pen = pens.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
            if (pen == null) return false;

            return pens.Remove(pen);
        }

        public bool RenamePen(string key, string? newName)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            var name = newName?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return false;

            var pens = _configStore.CurrentConfig.TrendPens;
            var pen = pens.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
            if (pen is null) return false;

            // Pen names stay unique per unit (the creation flow guarantees
            // it); a rename must not break that invariant either.
            if (pens.Any(p => !ReferenceEquals(p, pen) && string.Equals(p.Name, name, StringComparison.Ordinal)))
            {
                return false;
            }

            pen.Name = name;
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
