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

    // API settings
    bool EnableApi { get; set; }
    int ApiPort { get; set; }

    // API security / documentation
    /// <summary>When true, Swagger UI is available at /swagger. Defaults to false.</summary>
    bool EnableApiDocumentation { get; set; }
    /// <summary>When true, every mutating or sensitive endpoint requires the X-ModbusForge-Api-Key header.</summary>
    bool EnableApiAuthentication { get; set; }
    /// <summary>
    /// The API key compared against the X-ModbusForge-Api-Key header.
    /// Generated on first enable; stored in user settings.
    /// The value is never written to logs.
    /// </summary>
    string ApiKey { get; set; }

    // Trend settings
    int MaxConcurrentTrendRequests { get; set; }

    // Save/Load
    bool Save();
    void Load();
    
    // Event for settings changed
    event EventHandler? SettingsChanged;
}
