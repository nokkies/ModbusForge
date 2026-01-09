using System;

namespace ModbusForge.Services;

public interface ISettingsService
{
    // Connection settings
    bool AutoReconnect { get; set; }
    int AutoReconnectIntervalMs { get; set; }
    
    // UI settings
    bool ShowConnectionDiagnosticsOnError { get; set; }
    bool ConfirmOnExit { get; set; }
    
    // Logging settings
    bool EnableConsoleLogging { get; set; }
    int MaxConsoleMessages { get; set; }
    
    // Save/Load
    void Save();
    void Load();
    
    // Event for settings changed
    event EventHandler? SettingsChanged;
}
