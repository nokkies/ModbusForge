using System;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using System.Collections.Generic;

namespace ModbusForge.Services
{
    public class TrendLoggingService : ITrendLogger
    {
        private readonly object _sync = new();
        private int _retentionMinutes;
        private int _sampleRateMs;
        private string _exportFolder;
        private bool _isRunning;
        private readonly Dictionary<string, string> _keys = new(); // key -> displayName

        public TrendLoggingService(IOptions<LoggingSettings> options)
        {
            var s = options?.Value ?? new LoggingSettings();
            s.Clamp();
            _retentionMinutes = s.RetentionMinutes;
            _sampleRateMs = s.SampleRateMs;
            _exportFolder = string.IsNullOrWhiteSpace(s.ExportFolder) ? "Exports" : s.ExportFolder;
        }

        public int RetentionMinutes { get { lock (_sync) return _retentionMinutes; } }
        public int SampleRateMs { get { lock (_sync) return _sampleRateMs; } }
        public string ExportFolder { get { lock (_sync) return _exportFolder; } }
        public bool IsRunning { get { lock (_sync) return _isRunning; } }

        public void UpdateSettings(int retentionMinutes, int sampleRateMs, string? exportFolder = null)
        {
            lock (_sync)
            {
                if (retentionMinutes < 1) retentionMinutes = 1;
                if (retentionMinutes > 60) retentionMinutes = 60;
                if (sampleRateMs < 50) sampleRateMs = 50;
                if (sampleRateMs > 60000) sampleRateMs = 60000;
                _retentionMinutes = retentionMinutes;
                _sampleRateMs = sampleRateMs;
                if (!string.IsNullOrWhiteSpace(exportFolder)) _exportFolder = exportFolder!;
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                _isRunning = true;
            }
            // Sampling is driven externally via Publish; no internal timer here.
        }

        public void Stop()
        {
            lock (_sync)
            {
                _isRunning = false;
            }
        }

        public event Action<string, string>? Added;
        public event Action<string>? Removed;
        public event Action<string, double, DateTime>? Sampled;

        public void Add(string key, string displayName)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_sync)
            {
                if (_keys.ContainsKey(key)) return;
                _keys[key] = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
            }
            Added?.Invoke(key, displayName);
        }

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            bool existed = false;
            lock (_sync)
            {
                existed = _keys.Remove(key);
            }
            if (existed) Removed?.Invoke(key);
        }

        public void Publish(string key, double value, DateTime timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!_isRunning) return; // ignore when not running
            Sampled?.Invoke(key, value, timestampUtc);
        }
    }
}
