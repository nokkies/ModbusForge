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
using ModbusForge.Services;

namespace ModbusForge.ViewModels.Coordinators
{
    /// <summary>
    /// Coordinates application configuration save and load operations.
    /// Handles full configuration export/import including connection settings and custom entries,
    /// as well as project persistence (save/load project and import/export Unit IDs).
    /// </summary>
    public class ConfigurationCoordinator
    {
        private readonly ILogger<ConfigurationCoordinator> _logger;
        private readonly IFileDialogService _fileDialogService;
        private readonly IFileSystem _fileSystem;
        private readonly IInputDialogService _inputDialogService;
        private readonly IDialogService _dialogService;

        public ConfigurationCoordinator(
            ILogger<ConfigurationCoordinator> logger,
            IFileDialogService? fileDialogService = null,
            IFileSystem? fileSystem = null,
            IInputDialogService? inputDialogService = null,
            IDialogService? dialogService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileDialogService = fileDialogService ?? new FileDialogService();
            _fileSystem = fileSystem ?? new FileSystem();
            _inputDialogService = inputDialogService ?? new NullInputDialogService();
            _dialogService = dialogService ?? new NullDialogService();
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
            ObservableCollection<VisualNode> visualNodes,
            ObservableCollection<NodeConnection> visualConnections,
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
                        VisualNodes = visualNodes.ToList(),
                        VisualConnections = visualConnections.ToList()
                    };

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    using var stream = File.Create(dialog.FileName);
                    await JsonSerializer.SerializeAsync(stream, config, options);
                    setStatusMessage($"Saved configuration to {Path.GetFileName(dialog.FileName)}");
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error saving configuration");
                _dialogService.Show($"Failed to save configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error loading configuration");
                _dialogService.Show($"Failed to load configuration: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            ObservableCollection<VisualNode> visualNodes,
            ObservableCollection<NodeConnection> visualConnections,
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

            if (config.VisualNodes != null && config.VisualNodes.Any())
            {
                visualNodes.Clear();
                foreach (var vn in config.VisualNodes)
                    visualNodes.Add(vn);
            }

            if (config.VisualConnections != null && config.VisualConnections.Any())
            {
                visualConnections.Clear();
                foreach (var vc in config.VisualConnections)
                    visualConnections.Add(vc);
            }
        }

        #region Project Persistence

        /// <summary>
        /// Saves a project workspace snapshot to a .mfp file.
        /// </summary>
        public async Task<ProjectPersistenceResult> SaveProjectAsync(ProjectWorkspaceSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            try
            {
                var defaultFileName = GenerateAutoFileName(snapshot);
                var filePath = _fileDialogService.ShowSaveFileDialog(
                    snapshot.IsServerMode ? "Save Server Project" : "Save Client Project",
                    "ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*",
                    defaultFileName);

                if (string.IsNullOrEmpty(filePath))
                    return new ProjectPersistenceResult { Success = false, Message = "Save cancelled" };

                var projectConfig = new ProjectConfiguration
                {
                    ProjectInfo = new ProjectInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(filePath),
                        Modified = DateTime.Now
                    },
                    GlobalSettings = new GlobalSettings
                    {
                        Mode = snapshot.Mode,
                        ServerAddress = snapshot.ServerAddress,
                        Port = snapshot.Port,
                        ServerUnitId = snapshot.ServerUnitId,
                        ClientUnitId = snapshot.ClientUnitId,
                        VisibleTabs = snapshot.VisibleTabs ?? new List<string>()
                    }
                };

                if (snapshot.IsServerMode)
                {
                    foreach (var kvp in snapshot.UnitConfigurations)
                        projectConfig.UnitConfigurations[kvp.Key] = kvp.Value.Clone();
                }
                else
                {
                    var clientConfig = snapshot.UnitConfigurations.ContainsKey(snapshot.ClientUnitId)
                        ? snapshot.UnitConfigurations[snapshot.ClientUnitId].Clone()
                        : new UnitIdConfiguration(snapshot.ClientUnitId);
                    projectConfig.UnitConfigurations[snapshot.ClientUnitId] = clientConfig;
                }

                projectConfig.VisualNodes = snapshot.VisualNodes?.ToList() ?? new List<VisualNode>();
                projectConfig.VisualConnections = snapshot.VisualConnections?.ToList() ?? new List<NodeConnection>();

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                await _fileSystem.WriteAllTextAsync(filePath, JsonSerializer.Serialize(projectConfig, options));

                return new ProjectPersistenceResult
                {
                    Success = true,
                    FilePath = filePath,
                    Message = $"{(snapshot.IsServerMode ? "Server" : "Client")} project saved to {Path.GetFileName(filePath)}"
                };
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error saving project");
                return new ProjectPersistenceResult { Success = false, Message = $"Error saving project: {ex.Message}" };
            }
        }

        /// <summary>
        /// Loads a project workspace snapshot from a .mfp file.
        /// </summary>
        public async Task<ProjectPersistenceResult> LoadProjectAsync()
        {
            try
            {
                var filePath = _fileDialogService.ShowOpenFileDialog(
                    "Load ModbusForge Project",
                    "ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*");

                if (string.IsNullOrEmpty(filePath))
                    return new ProjectPersistenceResult { Success = false, Message = "Load cancelled" };

                var json = await _fileSystem.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var projectConfig = JsonSerializer.Deserialize<ProjectConfiguration>(json, options);
                if (projectConfig == null)
                    return new ProjectPersistenceResult { Success = false, Message = "Failed to deserialize project file" };

                var snapshot = new ProjectWorkspaceSnapshot
                {
                    Mode = projectConfig.GlobalSettings.Mode,
                    ServerAddress = projectConfig.GlobalSettings.ServerAddress,
                    Port = projectConfig.GlobalSettings.Port,
                    ServerUnitId = projectConfig.GlobalSettings.ServerUnitId,
                    ClientUnitId = projectConfig.GlobalSettings.ClientUnitId,
                    SelectedUnitId = projectConfig.GlobalSettings.ClientUnitId,
                    IsServerMode = string.Equals(projectConfig.GlobalSettings.Mode, "Server", StringComparison.OrdinalIgnoreCase),
                    VisibleTabs = projectConfig.GlobalSettings.VisibleTabs ?? new List<string>(),
                    VisualNodes = projectConfig.VisualNodes ?? new List<VisualNode>(),
                    VisualConnections = projectConfig.VisualConnections ?? new List<NodeConnection>(),
                    UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>()
                };

                foreach (var kvp in projectConfig.UnitConfigurations)
                    snapshot.UnitConfigurations[kvp.Key] = kvp.Value.Clone();

                return new ProjectPersistenceResult
                {
                    Success = true,
                    Snapshot = snapshot,
                    FilePath = filePath,
                    Message = $"Project loaded: {Path.GetFileName(filePath)}"
                };
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error loading project");
                return new ProjectPersistenceResult { Success = false, Message = $"Error loading project: {ex.Message}" };
            }
        }

        /// <summary>
        /// Imports Unit ID configurations from a project file without overwriting existing ones.
        /// </summary>
        public async Task<ProjectPersistenceResult> ImportUnitIdsAsync()
        {
            try
            {
                var filePath = _fileDialogService.ShowOpenFileDialog(
                    "Import Unit ID Configurations",
                    "ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*");

                if (string.IsNullOrEmpty(filePath))
                    return new ProjectPersistenceResult { Success = false, Message = "Import cancelled" };

                var json = await _fileSystem.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var projectConfig = JsonSerializer.Deserialize<ProjectConfiguration>(json, options);
                if (projectConfig?.UnitConfigurations == null)
                    return new ProjectPersistenceResult { Success = false, Message = "No Unit ID configurations found in the selected file." };

                var snapshot = new ProjectWorkspaceSnapshot
                {
                    UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>()
                };

                foreach (var kvp in projectConfig.UnitConfigurations)
                    snapshot.UnitConfigurations[kvp.Key] = kvp.Value.Clone();

                return new ProjectPersistenceResult
                {
                    Success = true,
                    Snapshot = snapshot,
                    Message = $"Import ready: {snapshot.UnitConfigurations.Count} Unit ID configuration(s) found."
                };
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error importing Unit IDs");
                return new ProjectPersistenceResult { Success = false, Message = $"Error importing Unit IDs: {ex.Message}" };
            }
        }

        /// <summary>
        /// Exports all Unit ID configurations to a project file.
        /// </summary>
        public async Task<ProjectPersistenceResult> ExportUnitIdsAsync(ProjectWorkspaceSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            try
            {
                var defaultFileName = GenerateAutoFileName(snapshot) + "_AllUnitIDs";
                var filePath = _fileDialogService.ShowSaveFileDialog(
                    "Export Unit ID Configurations",
                    "ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*",
                    defaultFileName);

                if (string.IsNullOrEmpty(filePath))
                    return new ProjectPersistenceResult { Success = false, Message = "Export cancelled" };

                var projectConfig = new ProjectConfiguration
                {
                    ProjectInfo = new ProjectInfo
                    {
                        Name = $"Exported Unit IDs - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        Modified = DateTime.Now
                    },
                    GlobalSettings = new GlobalSettings
                    {
                        Mode = snapshot.Mode,
                        ServerAddress = snapshot.ServerAddress,
                        Port = snapshot.Port,
                        ServerUnitId = snapshot.ServerUnitId,
                        ClientUnitId = snapshot.ClientUnitId
                    }
                };

                foreach (var kvp in snapshot.UnitConfigurations)
                    projectConfig.UnitConfigurations[kvp.Key] = kvp.Value.Clone();

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                await _fileSystem.WriteAllTextAsync(filePath, JsonSerializer.Serialize(projectConfig, options));

                return new ProjectPersistenceResult
                {
                    Success = true,
                    FilePath = filePath,
                    Message = $"Exported {snapshot.UnitConfigurations.Count} Unit ID configurations"
                };
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error exporting Unit IDs");
                return new ProjectPersistenceResult { Success = false, Message = $"Error exporting Unit IDs: {ex.Message}" };
            }
        }

        /// <summary>
        /// Exports the configuration for a single Unit ID.
        /// </summary>
        public async Task<ProjectPersistenceResult> ExportUnitIdAsync(ProjectWorkspaceSnapshot snapshot, byte selectedUnitId)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            try
            {
                if (!snapshot.IsServerMode)
                {
                    _dialogService.Show("Export Unit ID is only available in Server mode.", "Export Unit ID", MessageBoxButton.OK, MessageBoxImage.Information);
                    return new ProjectPersistenceResult { Success = false, Message = "Export Unit ID is only available in Server mode." };
                }

                var defaultFileName = GenerateAutoFileName(snapshot) + $"_ID{selectedUnitId}";
                var filePath = _fileDialogService.ShowSaveFileDialog(
                    $"Export Unit ID {selectedUnitId}",
                    "ModbusForge Unit ID (*.mui)|*.mui|All Files (*.*)|*.*",
                    defaultFileName);

                if (string.IsNullOrEmpty(filePath))
                    return new ProjectPersistenceResult { Success = false, Message = "Export cancelled" };

                var unitConfig = snapshot.UnitConfigurations.ContainsKey(selectedUnitId)
                    ? snapshot.UnitConfigurations[selectedUnitId].Clone()
                    : new UnitIdConfiguration(selectedUnitId);

                var projectConfig = new ProjectConfiguration
                {
                    ProjectInfo = new ProjectInfo
                    {
                        Name = $"Unit ID {selectedUnitId} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        Modified = DateTime.Now
                    },
                    GlobalSettings = new GlobalSettings
                    {
                        Mode = snapshot.Mode,
                        ServerAddress = snapshot.ServerAddress,
                        Port = snapshot.Port,
                        ServerUnitId = snapshot.ServerUnitId,
                        ClientUnitId = snapshot.ClientUnitId
                    },
                    UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>
                    {
                        [selectedUnitId] = unitConfig
                    }
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                await _fileSystem.WriteAllTextAsync(filePath, JsonSerializer.Serialize(projectConfig, options));

                return new ProjectPersistenceResult
                {
                    Success = true,
                    FilePath = filePath,
                    Message = $"Unit ID {selectedUnitId} exported to {Path.GetFileName(filePath)}"
                };
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error exporting Unit ID");
                return new ProjectPersistenceResult { Success = false, Message = $"Error exporting Unit ID: {ex.Message}" };
            }
        }

        /// <summary>
        /// Imports a single Unit ID configuration under a new Unit ID.
        /// </summary>
        public async Task<ProjectPersistenceResult> ImportUnitIdAsAsync()
        {
            try
            {
                var filePath = _fileDialogService.ShowOpenFileDialog(
                    "Import Unit ID Configuration",
                    "ModbusForge Unit ID (*.mui)|*.mui|ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*");

                if (string.IsNullOrEmpty(filePath))
                    return new ProjectPersistenceResult { Success = false, Message = "Import cancelled" };

                var json = await _fileSystem.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var projectConfig = JsonSerializer.Deserialize<ProjectConfiguration>(json, options);
                if (projectConfig?.UnitConfigurations == null || projectConfig.UnitConfigurations.Count == 0)
                    return new ProjectPersistenceResult { Success = false, Message = "No Unit ID configurations found in the selected file." };

                var importedUnitId = projectConfig.UnitConfigurations.Keys.First();
                var importedConfig = projectConfig.UnitConfigurations[importedUnitId].Clone();

                if (!_inputDialogService.TryGetInput(
                    "Import Unit ID As",
                    $"Enter target Unit ID (1-247) to import Unit ID {importedUnitId} as:",
                    "1",
                    out var inputText))
                {
                    return new ProjectPersistenceResult { Success = false, Message = "Import cancelled" };
                }

                if (!byte.TryParse(inputText, out byte targetUnitId) || targetUnitId < 1 || targetUnitId > 247)
                {
                    _dialogService.Show("Invalid Unit ID. Please enter a value between 1 and 247.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return new ProjectPersistenceResult { Success = false, Message = "Invalid target Unit ID" };
                }

                return new ProjectPersistenceResult
                {
                    Success = true,
                    ImportedUnitId = targetUnitId,
                    ImportedConfiguration = importedConfig,
                    Message = $"Unit ID {importedUnitId} ready to import as Unit ID {targetUnitId}"
                };
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error importing Unit ID");
                return new ProjectPersistenceResult { Success = false, Message = $"Error importing Unit ID: {ex.Message}" };
            }
        }

        private string GenerateAutoFileName(ProjectWorkspaceSnapshot snapshot)
        {
            try
            {
                var ipAddress = snapshot.IsServerMode ? "Server" : SanitizeIpAddress(snapshot.ServerAddress);
                var unitId = snapshot.IsServerMode ? snapshot.SelectedUnitId : snapshot.ClientUnitId;
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                return $"MBIP{ipAddress}_ID{unitId}_{timestamp}";
            }
            catch
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var unitId = snapshot.IsServerMode ? snapshot.SelectedUnitId : snapshot.ClientUnitId;
                return $"ModbusForge_ID{unitId}_{timestamp}";
            }
        }

        private string SanitizeIpAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return "Unknown";

            var sanitized = ipAddress.Replace(".", "000");

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            if (char.IsDigit(sanitized[0]))
            {
                sanitized = "IP" + sanitized;
            }

            return sanitized;
        }

        #endregion
    }

    /// <summary>
    /// No-op input dialog service for unit tests or when no UI prompt is available.
    /// </summary>
    public class NullInputDialogService : IInputDialogService
    {
        public bool TryGetInput(string title, string prompt, string defaultValue, out string input)
        {
            input = string.Empty;
            return false;
        }
    }
}
