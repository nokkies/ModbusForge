using System;

namespace ModbusForge.Services
{
    public interface ITrendLogger
    {
        int RetentionMinutes { get; }
        int SampleRateMs { get; }
        string ExportFolder { get; }
        bool IsRunning { get; }

        void UpdateSettings(int retentionMinutes, int sampleRateMs, string? exportFolder = null);
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
    }
}
