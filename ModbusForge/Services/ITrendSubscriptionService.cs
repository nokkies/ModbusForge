namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Manages the trend pens of the current unit. A pen is a first-class
    /// member of <c>UnitIdConfiguration.TrendPens</c>: the watch loop polls it
    /// and publishes samples to the trend logger under the pen's name, so the
    /// Trends view has its own data path that no longer rides on custom watch
    /// entries.
    ///
    /// The pen's Name is the stable series key, so methods here take and
    /// return pen names.
    /// </summary>
    public interface ITrendSubscriptionService
    {
        /// <summary>
        /// Creates the trend pen for the given area + address, or reuses the
        /// pen that already covers it (its name is the stable series key).
        /// </summary>
        /// <param name="area">Modbus area of the address.</param>
        /// <param name="address">Register/coil address.</param>
        /// <param name="requestedName">Desired pen name; made unique within the unit when it collides.</param>
        /// <param name="readPeriodMs">Poll period in milliseconds.</param>
        /// <param name="type">Value type used when reading (int, uint, real, string).</param>
        /// <returns>The series key (pen name) samples will be published with.</returns>
        string AddPen(string area, int address, string? requestedName, int readPeriodMs, string? type = null);

        /// <summary>
        /// Removes the pen for the series key from the unit configuration.
        /// Future samples stop, so a removed pen stays removed.
        /// </summary>
        /// <returns>True when a matching pen existed and was removed.</returns>
        bool RemovePen(string key);

        /// <summary>
        /// Default pen name matching the historical context-menu names
        /// ("HR Trend 1", "Coil Trend 5").
        /// </summary>
        string DefaultName(string area, int address);
    }
}
