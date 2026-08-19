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
        private string _exportFolder;
        private bool _isRunning;
        private readonly Dictionary<string, string> _keys = new(); // key -> displayName

        public TrendLoggingService(IOptions<LoggingSettings> options)
        {
            var s = options?.Value ?? new LoggingSettings();
            s.Clamp();
            _retentionMinutes = s.RetentionMinutes;
            _exportFolder = string.IsNullOrWhiteSpace(s.ExportFolder) ? "Exports" : s.ExportFolder;
        }

        public int RetentionMinutes { get { lock (_sync) return _retentionMinutes; } }
        public string ExportFolder { get { lock (_sync) return _exportFolder; } }
        public bool IsRunning { get { lock (_sync) return _isRunning; } }

        public void UpdateSettings(int retentionMinutes, string? exportFolder = null)
        {
            lock (_sync)
            {
                if (retentionMinutes < 1) retentionMinutes = 1;
                if (retentionMinutes > 60) retentionMinutes = 60;
                _retentionMinutes = retentionMinutes;
                if (!string.IsNullOrWhiteSpace(exportFolder)) _exportFolder = exportFolder!;
            }
        }

        public void Start()
        {
            bool changed;
            lock (_sync)
            {
                changed = !_isRunning;
                _isRunning = true;
            }
            // Sampling is driven externally via Publish; no internal timer here.
            if (changed) StateChanged?.Invoke(true);
        }

        public void Stop()
        {
            bool changed;
            lock (_sync)
            {
                changed = _isRunning;
                _isRunning = false;
            }
            if (changed) StateChanged?.Invoke(false);
        }

        public event Action<string, string>? Added;
        public event Action<string>? Removed;
        public event Action<string, double, DateTime>? Sampled;
        public event Action<bool>? StateChanged;

        public void Add(string key, string displayName)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_sync)
            {
                if (_keys.ContainsKey(key)) return;
                displayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
                _keys[key] = displayName;
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

        public void SetDisplayName(string key, string displayName)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (string.IsNullOrWhiteSpace(displayName)) return;
            lock (_sync)
            {
                if (!_keys.TryGetValue(key, out var current) || current == displayName) return;
                _keys[key] = displayName;
            }
        }

        public void Publish(string key, double value, DateTime timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!_isRunning) return; // ignore when not running
            Sampled?.Invoke(key, value, timestampUtc);
        }

        public IReadOnlyDictionary<string, string> ActiveKeys
        {
            get
            {
                lock (_sync)
                {
                    return new Dictionary<string, string>(_keys);
                }
            }
        }
    }
}
