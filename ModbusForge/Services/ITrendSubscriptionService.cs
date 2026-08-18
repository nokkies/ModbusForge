using System;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Bridges trend "pens" to the custom watch entries that actually poll
    /// registers and publish trend samples. A pen is the Trends view's view of
    /// a watch entry with Trend=true: samples flow through the existing custom
    /// watch loop, so there is exactly one polling mechanism in the app.
    ///
    /// The series key published by the watch loop is the entry's Name, so
    /// methods here take and return entry names.
    /// </summary>
    public interface ITrendSubscriptionService
    {
        /// <summary>
        /// Creates or reuses the watch entry that feeds the trend logger for
        /// the given area + address, and enables trend + read monitoring on
        /// it. Reusing keeps an existing watch entry's name (the stable series
        /// key) and type/value.
        /// </summary>
        /// <returns>The series key (entry name) samples will be published with.</returns>
        string AddPen(string area, int address, string? requestedName, int readPeriodMs,
            string? type = null, string? initialValue = null);

        /// <summary>
        /// Stops trend feeding for the series key by clearing Trend on the
        /// matching watch entry. The watch entry itself is kept (it may still
        /// be a live watch item); future samples stop, so a removed pen stays
        /// removed instead of re-appearing on the next read.
        /// </summary>
        /// <returns>True when a matching watch entry existed and was updated.</returns>
        bool RemovePen(string key);

        /// <summary>
        /// Default pen name matching the historical context-menu names
        /// ("HR Trend 1", "Coil Trend 5").
        /// </summary>
        string DefaultName(string area, int address);
    }
}
