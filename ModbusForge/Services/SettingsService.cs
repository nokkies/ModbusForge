using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILogger<SettingsService>? _logger;

    private SettingsData _settings = new();

    public bool AutoReconnect
    {
        get => _settings.AutoReconnect;
        set { _settings.AutoReconnect = value; OnSettingsChanged(); }
    }

    public int AutoReconnectIntervalMs
    {
        get => _settings.AutoReconnectIntervalMs;
        set { _settings.AutoReconnectIntervalMs = value; OnSettingsChanged(); }
    }

    public bool ShowConnectionDiagnosticsOnError
    {
        get => _settings.ShowConnectionDiagnosticsOnError;
        set { _settings.ShowConnectionDiagnosticsOnError = value; OnSettingsChanged(); }
    }

    public bool ConfirmOnExit
    {
        get => _settings.ConfirmOnExit;
        set { _settings.ConfirmOnExit = value; OnSettingsChanged(); }
    }

    public bool EnableConsoleLogging
    {
        get => _settings.EnableConsoleLogging;
        set { _settings.EnableConsoleLogging = value; OnSettingsChanged(); }
    }

    public int MaxConsoleMessages
    {
        get => _settings.MaxConsoleMessages;
        set { _settings.MaxConsoleMessages = value; OnSettingsChanged(); }
    }

    public bool EnableApi
    {
        get => _settings.EnableApi;
        set { _settings.EnableApi = value; OnSettingsChanged(); }
    }

    public int ApiPort
    {
        get => _settings.ApiPort;
        set { _settings.ApiPort = value; OnSettingsChanged(); }
    }

    public bool EnableApiDocumentation
    {
        get => _settings.EnableApiDocumentation;
        set { _settings.EnableApiDocumentation = value; OnSettingsChanged(); }
    }

    public bool EnableApiAuthentication
    {
        get => _settings.EnableApiAuthentication;
        set { _settings.EnableApiAuthentication = value; OnSettingsChanged(); }
    }

    public string ApiKey
    {
        get => _settings.ApiKey;
        set { _settings.ApiKey = value; OnSettingsChanged(); }
    }

    public int MaxConcurrentTrendRequests
    {
        get => _settings.MaxConcurrentTrendRequests;
        set { _settings.MaxConcurrentTrendRequests = value; OnSettingsChanged(); }
    }

    public event EventHandler? SettingsChanged;

    public SettingsService(ILogger<SettingsService>? logger = null)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModbusForge", "settings.json"), logger)
    {
    }

    internal SettingsService(string settingsFilePath, ILogger<SettingsService>? logger = null)
    {
        _settingsFilePath = settingsFilePath;
        _logger = logger;
        Load();
    }

    public bool Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
            return true;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            _logger?.LogError(ex, "Failed to save settings to {FilePath}", _settingsFilePath);
            return false;
        }
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<SettingsData>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                }
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            _logger?.LogError(ex, "Failed to load settings from {FilePath}", _settingsFilePath);
            // Use defaults if we can't load
            _settings = new SettingsData();
        }
    }

    private void OnSettingsChanged()
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private class SettingsData
    {
        public bool AutoReconnect { get; set; } = false;
        public int AutoReconnectIntervalMs { get; set; } = 5000;
        public bool ShowConnectionDiagnosticsOnError { get; set; } = true;
        public bool ConfirmOnExit { get; set; } = false;
        public bool EnableConsoleLogging { get; set; } = true;
        public int MaxConsoleMessages { get; set; } = 1000;
        public bool EnableApi { get; set; } = false;
        public int ApiPort { get; set; } = 5000;
        public bool EnableApiDocumentation { get; set; } = false;
        public bool EnableApiAuthentication { get; set; } = false;
        public string ApiKey { get; set; } = string.Empty;
        public int MaxConcurrentTrendRequests { get; set; } = 8;
    }
}
