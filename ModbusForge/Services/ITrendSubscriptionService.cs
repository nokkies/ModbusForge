using System.Collections.Generic;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Manages the trend pens of the current unit. A pen is a first-class
    /// member of <c>UnitIdConfiguration.TrendPens</c>: the watch loop polls it
    /// and publishes samples to the trend logger under the pen's stable
    /// <c>Key</c>, so the Trends view has its own data path that no longer
    /// rides on custom watch entries.
    ///
    /// The pen's <c>Key</c> is the stable series key; methods here take and
    /// return keys. The display <c>Name</c> is freely renamable without
    /// touching the key (or the chart's history).
    /// </summary>
    public interface ITrendSubscriptionService
    {
        /// <summary>
        /// The pens of the current unit, in configuration order. The Trends
        /// view lists a row per pen, so a pen that has not sampled yet (or
        /// whose reads are failing) is still visible and manageable.
        /// </summary>
        IReadOnlyCollection<TrendPen> Pens { get; }

        /// <summary>
        /// Creates the trend pen for the given area + address, or reuses the
        /// pen that already covers it.
        /// </summary>
        /// <param name="area">Modbus area of the address.</param>
        /// <param name="address">Register/coil address.</param>
        /// <param name="requestedName">Desired pen name; made unique within the unit when it collides.</param>
        /// <param name="readPeriodMs">Poll period in milliseconds.</param>
        /// <param name="type">Value type used when reading (int, uint, real, string).</param>
        /// <returns>The created or reused pen; samples are published under its <c>Key</c>.</returns>
        TrendPen AddPen(string area, int address, string? requestedName, int readPeriodMs, string? type = null);

        /// <summary>
        /// Removes the pen for the series key from the unit configuration.
        /// Future samples stop, so a removed pen stays removed.
        /// </summary>
        /// <returns>True when a matching pen existed and was removed.</returns>
        bool RemovePen(string key);

        /// <summary>
        /// Renames the pen for the series key. The key (and the chart history
        /// behind it) is untouched; the new name persists with the unit
        /// configuration. A blank (or null) name is rejected (returns false)
        /// so a pen can never be left anonymous.
        /// </summary>
        /// <returns>True when a matching pen existed and was renamed.</returns>
        bool RenamePen(string key, string? newName);

        /// <summary>
        /// Default pen name matching the historical context-menu names
        /// ("HR Trend 1", "Coil Trend 5").
        /// </summary>
        string DefaultName(string area, int address);
    }
}
