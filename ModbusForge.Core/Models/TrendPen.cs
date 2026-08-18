using System;
using System.Text.Json.Serialization;

namespace ModbusForge.Models
{
    /// <summary>
    /// One trend series source: a named register (or coil) that the app polls
    /// and publishes to the trend logger. Pens are first-class citizens of the
    /// unit configuration (<see cref="UnitIdConfiguration.TrendPens"/>); they
    /// are intentionally NOT custom watch entries, so the Trends view has its
    /// own data path and persistence.
    /// </summary>
    public sealed class TrendPen
    {
        /// <summary>
        /// Stable series key and display name. Unique per unit - the key the
        /// samples are published to the trend logger with.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Modbus area (see <see cref="CustomEntry.AvailableAreas"/>).</summary>
        public string Area { get; set; } = "HoldingRegister";

        /// <summary>Register (or coil) address.</summary>
        public int Address { get; set; }

        /// <summary>Value type used when reading/parsing (int, uint, real, string).</summary>
        public string Type { get; set; } = "int";

        /// <summary>Poll period in milliseconds.</summary>
        public int ReadPeriodMs { get; set; } = 1000;

        /// <summary>
        /// Runtime throttle stamp for the polling loop. Persisted like
        /// <see cref="CustomEntry.LastReadUtc"/> so a reload does not burst
        /// every pen at once.
        /// </summary>
        public DateTime LastReadUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// True while the polling loop's reads for this pen are failing.
        /// Runtime-only: never persisted, and not set by headless consumers
        /// that publish samples without the desktop watch loop.
        /// </summary>
        [JsonIgnore]
        public bool IsFailing { get; set; }

        /// <summary>
        /// Message of the most recent failed read, for the pen list tooltip.
        /// Cleared on the next successful read. Runtime-only.
        /// </summary>
        [JsonIgnore]
        public string? LastError { get; set; }
    }
}
