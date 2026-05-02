using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModbusForge",
        "settings.json");

    private readonly ILogger<SettingsService> _logger;
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

    public event EventHandler? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        Load();
    }

    public bool Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {SettingsFilePath}", SettingsFilePath);
            return false;
        }
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<SettingsData>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings from {SettingsFilePath}. Using defaults.", SettingsFilePath);
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
    }
}
