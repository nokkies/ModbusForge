using System;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Tests.Fakes
{
    public sealed class FakeSettingsService : ISettingsService
    {
        public bool AutoReconnect { get; set; }
        public int AutoReconnectIntervalMs { get; set; } = 1000;
        public bool ShowConnectionDiagnosticsOnError { get; set; }
        public bool ConfirmOnExit { get; set; }
        public bool EnableConsoleLogging { get; set; }
        public int MaxConsoleMessages { get; set; } = 100;
        public bool EnableApi { get; set; }
        public int ApiPort { get; set; } = 5000;
        public bool EnableApiDocumentation { get; set; }
        public bool EnableApiAuthentication { get; set; }
        public string ApiKey { get; set; } = string.Empty;
        public int MaxConcurrentTrendRequests { get; set; } = 4;
        public bool CheckForUpdatesOnStartup { get; set; }
        public MqttSettings MqttSettings { get; set; } = new MqttSettings();
        public bool SaveWasCalled { get; private set; }
        public bool LoadWasCalled { get; private set; }

        public bool Save()
        {
            SaveWasCalled = true;
            return true;
        }

        public void Load()
        {
            LoadWasCalled = true;
        }

        public event EventHandler? SettingsChanged;

        public void RaiseSettingsChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
