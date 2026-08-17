using System;

namespace ModbusForge.Services
{
    public interface ITrendLogger
    {
        int RetentionMinutes { get; }
        string ExportFolder { get; }
        bool IsRunning { get; }

        // Sampling is driven externally (each monitored entry is published at
        // its own read period), so there is no sample rate to configure here.
        void UpdateSettings(int retentionMinutes, string? exportFolder = null);
        void Start();
        void Stop();

        // Series management for UI
        void Add(string key, string displayName);
        void Remove(string key);

        // Push a sample for an existing key
        void Publish(string key, double value, DateTime timestampUtc);

        // Events for the TrendViewModel to maintain series
        event Action<string, string>? Added;          // key, displayName
        event Action<string>? Removed;                // key
        event Action<string, double, DateTime>? Sampled; // key, value, timestampUtc

        /// <summary>
        /// Raised on the calling thread whenever the running state changes.
        /// Both controllers of the running flag (connection lifecycle and the
        /// Trend view's Start/Stop) stay in sync through this event.
        /// </summary>
        event Action<bool>? StateChanged;

        System.Collections.Generic.IReadOnlyDictionary<string, string> ActiveKeys { get; }
    }
}
