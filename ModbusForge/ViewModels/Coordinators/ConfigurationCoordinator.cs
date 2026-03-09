using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ModbusForge.Models;

namespace ModbusForge.ViewModels.Coordinators
{
    /// <summary>
    /// Coordinates application configuration save and load operations.
    /// Handles full configuration export/import including connection settings and custom entries.
    /// </summary>
    public class ConfigurationCoordinator
    {
        private readonly ILogger<ConfigurationCoordinator> _logger;

        public ConfigurationCoordinator(ILogger<ConfigurationCoordinator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private const long MaxFileSize = 5 * 1024 * 1024; // 5MB limit for full configuration

        /// <summary>
        /// Saves the complete application configuration to a JSON file.
        /// </summary>
        public async Task SaveAllConfigAsync(
            string mode,
            string serverAddress,
            int port,
            byte unitId,
            ObservableCollection<CustomEntry> customEntries,
            ObservableCollection<PlcElement> plcElements,
            Action<string> setStatusMessage)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = "modbusforge-config.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var config = new AppConfiguration
                    {
                        Mode = mode,
                        ServerAddress = serverAddress,
                        Port = port,
                        UnitId = unitId,
                        CustomEntries = customEntries.ToList(),
                        PlcElements = plcElements.ToList()
                    };

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    using var stream = File.Create(dialog.FileName);
                    await JsonSerializer.SerializeAsync(stream, config, options);
                    setStatusMessage($"Saved configuration to {Path.GetFileName(dialog.FileName)}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving configuration");
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Loads the complete application configuration from a JSON file.
        /// </summary>
        public async Task<AppConfiguration?> LoadAllConfigAsync(Action<string> setStatusMessage)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var fileInfo = new FileInfo(dialog.FileName);
                    if (fileInfo.Length > MaxFileSize)
                    {
                        throw new InvalidDataException($"The selected file is too large (max {MaxFileSize / 1024 / 1024}MB).");
                    }

                    using var stream = File.OpenRead(dialog.FileName);
                    var config = await JsonSerializer.DeserializeAsync<AppConfiguration>(stream);

                    if (config != null)
                    {
                        setStatusMessage($"Loaded configuration from {Path.GetFileName(dialog.FileName)}");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration");
                MessageBox.Show($"Failed to load configuration: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return null;
        }

        /// <summary>
        /// Applies loaded configuration to the application state.
        /// </summary>
        public void ApplyConfiguration(
            AppConfiguration config,
            Action<string> setMode,
            Action<string> setServerAddress,
            Action<int> setPort,
            Action<byte> setUnitId,
            ObservableCollection<CustomEntry> customEntries,
            ObservableCollection<PlcElement> plcElements,
            Action subscribeCustomEntries)
        {
            if (config == null) return;

            if (!string.IsNullOrWhiteSpace(config.Mode))
                setMode(config.Mode);
            
            if (!string.IsNullOrWhiteSpace(config.ServerAddress))
                setServerAddress(config.ServerAddress);
            
            if (config.Port > 0)
                setPort(config.Port);
            
            if (config.UnitId > 0)
                setUnitId(config.UnitId);

            if (config.CustomEntries != null && config.CustomEntries.Any())
            {
                customEntries.Clear();
                foreach (var ce in config.CustomEntries)
                    customEntries.Add(ce);
                subscribeCustomEntries();
            }

            if (config.PlcElements != null && config.PlcElements.Any())
            {
                plcElements.Clear();
                foreach (var pe in config.PlcElements)
                    plcElements.Add(pe);
            }
        }
    }
}
