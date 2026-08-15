using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Services;
using ModbusForge.Helpers;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// MainViewModel - Project partial (split for navigability; behavior unchanged).
    /// </summary>
    public partial class MainViewModel
    {
        public IAsyncRelayCommand SaveProjectCommand { get; }

        public IAsyncRelayCommand LoadProjectCommand { get; }

        public IAsyncRelayCommand ImportUnitIdsCommand { get; }

        public IAsyncRelayCommand ExportUnitIdsCommand { get; }

        public IAsyncRelayCommand ImportUnitIdAsCommand { get; }

        public IAsyncRelayCommand ExportUnitIdCommand { get; }

        public IAsyncRelayCommand SaveAllConfigCommand { get; }


        private bool CanSaveProject() => _fileDialogService != null && !IsBusy;


        private bool CanLoadProject() => _fileDialogService != null && !IsBusy;


        private bool CanExportUnitId() => _fileDialogService != null && IsServerMode && !IsBusy;


        private bool CanImportUnitIdAs() => _fileDialogService != null && _inputDialogService != null && IsServerMode && !IsBusy;




        private async Task ExportUnitIdsAsync()
        {
            if (_fileDialogService == null) return;

            try
            {
                var path = await _fileDialogService.ShowSaveFileDialogAsync(
                    "Export Unit ID Configurations",
                    "ModbusForge Unit IDs (*.mfp;*.mui)|*.mfp;*.mui|JSON files (*.json)|*.json|All files (*.*)|*.*",
                    "unit-id-configurations.mfp");

                if (path == null) return;

                var snapshot = BuildWorkspaceSnapshot();
                var project = CreateProjectConfiguration(snapshot, "Exported Unit ID Configurations");
                project.VisualNodes = new List<VisualNode>();
                project.VisualConnections = new List<NodeConnection>();
                await _fileSystem.WriteAllTextAsync(path, JsonSerializer.Serialize(project, PersistenceJsonOptions));
                StatusMessage = $"Exported {snapshot.UnitConfigurations.Count} Unit ID configuration(s) to {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error exporting Unit ID configurations");
                StatusMessage = $"Unit ID export error: {ex.Message}";
            }
        }


        private async Task ExportUnitIdAsync()
        {
            if (_fileDialogService == null || !IsServerMode) return;

            try
            {
                var selectedUnitId = SelectedUnitId;
                var path = await _fileDialogService.ShowSaveFileDialogAsync(
                    $"Export Unit ID {selectedUnitId}",
                    "ModbusForge Unit ID (*.mui)|*.mui|ModbusForge Project (*.mfp)|*.mfp|All files (*.*)|*.*",
                    $"unit-id-{selectedUnitId}.mui");

                if (path == null) return;

                var snapshot = BuildWorkspaceSnapshot();
                var project = CreateProjectConfiguration(snapshot, $"Unit ID {selectedUnitId}");
                project.UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>
                {
                    [selectedUnitId] = snapshot.UnitConfigurations.TryGetValue(selectedUnitId, out var configuration)
                        ? configuration.Clone()
                        : new UnitIdConfiguration(selectedUnitId)
                };
                project.VisualNodes = new List<VisualNode>();
                project.VisualConnections = new List<NodeConnection>();
                await _fileSystem.WriteAllTextAsync(path, JsonSerializer.Serialize(project, PersistenceJsonOptions));
                StatusMessage = $"Unit ID {selectedUnitId} exported to {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error exporting Unit ID {UnitId}", SelectedUnitId);
                StatusMessage = $"Unit ID export error: {ex.Message}";
            }
        }


        private async Task ImportUnitIdsAsync()
        {
            if (_fileDialogService == null) return;

            try
            {
                var path = await _fileDialogService.ShowOpenFileDialogAsync(
                    "Import Unit ID Configurations",
                    "ModbusForge Unit IDs (*.mfp;*.mui)|*.mfp;*.mui|JSON files (*.json)|*.json|All files (*.*)|*.*");

                if (path == null) return;

                var json = await _fileSystem.ReadAllTextAsync(path);
                if (TryDeserializeUnitConfigurations(json, out var configurations))
                {
                    var imported = 0;
                    foreach (var pair in configurations)
                    {
                        if (pair.Key is < 1 or > 247 || _unitConfigurationStore.TryGetConfiguration(pair.Key, out _))
                        {
                            continue;
                        }

                        _unitConfigurationStore.SetConfiguration(pair.Key, pair.Value);
                        imported++;
                    }

                    var availableIds = _unitConfigurationStore.UnitConfigurations.Keys
                        .Where(id => id is >= 1 and <= 247)
                        .OrderBy(id => id)
                        .ToList();
                    _unitConfigurationStore.PopulateAvailableUnitIds(availableIds);
                    if (ActiveProfile != null && IsServerMode)
                    {
                        ActiveProfile.ServerUnitIds = string.Join(",", availableIds);
                    }

                    StatusMessage = $"Imported {imported} new Unit ID configuration(s) from {Path.GetFileName(path)}.";
                    return;
                }

                // Backward compatibility with the original [1, 2, 5] JSON format.
                var ids = TryDeserializeUnitIdList(json);
                if (ids.Count == 0)
                {
                    StatusMessage = "No valid Unit ID configurations were found in the selected file.";
                    return;
                }

                foreach (var id in ids)
                {
                    _unitConfigurationStore.GetOrCreateConfiguration(id);
                }

                _unitConfigurationStore.PopulateAvailableUnitIds(ids);
                if (ActiveProfile != null && IsServerMode)
                {
                    ActiveProfile.ServerUnitIds = string.Join(",", ids);
                }

                SelectedUnitId = ids[0];
                OnPropertyChanged(nameof(ServerUnitIds));
                StatusMessage = $"Imported {ids.Count} Unit ID(s) from {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error importing Unit ID configurations");
                StatusMessage = $"Unit ID import error: {ex.Message}";
            }
        }


        private async Task ImportUnitIdAsAsync()
        {
            if (_fileDialogService == null || _inputDialogService == null || !IsServerMode)
            {
                return;
            }

            try
            {
                var path = await _fileDialogService.ShowOpenFileDialogAsync(
                    "Import Unit ID Configuration",
                    "ModbusForge Unit ID (*.mui)|*.mui|ModbusForge Project (*.mfp)|*.mfp|JSON files (*.json)|*.json|All files (*.*)|*.*");
                if (path == null) return;

                var json = await _fileSystem.ReadAllTextAsync(path);
                if (!TryDeserializeUnitConfigurations(json, out var configurations) || configurations.Count == 0)
                {
                    StatusMessage = "No Unit ID configurations were found in the selected file.";
                    return;
                }

                var source = configurations.First();
                if (!_inputDialogService.TryGetInput(
                        "Import Unit ID As",
                        $"Enter target Unit ID (1-247) to import Unit ID {source.Key} as:",
                        source.Key.ToString(CultureInfo.InvariantCulture),
                        out var input))
                {
                    return;
                }

                if (!byte.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target)
                    || target is < 1 or > 247)
                {
                    StatusMessage = "Invalid target Unit ID. Enter a value between 1 and 247.";
                    return;
                }

                var imported = source.Value.Clone();
                imported.UnitId = target;
                _unitConfigurationStore.SetConfiguration(target, imported);
                var ids = _unitConfigurationStore.UnitConfigurations.Keys
                    .Where(id => id is >= 1 and <= 247)
                    .OrderBy(id => id)
                    .ToList();
                _unitConfigurationStore.PopulateAvailableUnitIds(ids);
                if (ActiveProfile != null)
                {
                    ActiveProfile.ServerUnitIds = string.Join(",", ids);
                }

                SelectedUnitId = target;
                StatusMessage = $"Imported Unit ID {source.Key} as Unit ID {target}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error importing Unit ID configuration");
                StatusMessage = $"Unit ID import error: {ex.Message}";
            }
        }


        private async Task SaveProjectAsync()
        {
            if (_fileDialogService == null) return;

            IsBusy = true;
            try
            {
                var path = await _fileDialogService.ShowSaveFileDialogAsync(
                    "Save ModbusForge Project",
                    "ModbusForge Project (*.mfp)|*.mfp|JSON files (*.json)|*.json|All files (*.*)|*.*",
                    "project.mfp");

                if (path == null) return;

                var snapshot = BuildWorkspaceSnapshot();
                var project = CreateProjectConfiguration(snapshot, Path.GetFileNameWithoutExtension(path));
                project.Profiles = _connectionManager.Profiles.ToList();
                project.ActiveProfileId = ActiveProfile?.Id;
                project.SelectedUnitId = snapshot.SelectedUnitId;
                await _fileSystem.WriteAllTextAsync(path, JsonSerializer.Serialize(project, PersistenceJsonOptions));

                StatusMessage = $"Saved project to {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error saving project");
                StatusMessage = $"Save error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task LoadProjectAsync()
        {
            if (_fileDialogService == null) return;

            IsBusy = true;
            try
            {
                var path = await _fileDialogService.ShowOpenFileDialogAsync(
                    "Load ModbusForge Project",
                    "ModbusForge Project (*.mfp)|*.mfp|JSON files (*.json)|*.json|All files (*.*)|*.*");

                if (path == null) return;

                var json = await _fileSystem.ReadAllTextAsync(path);
                if (!TryDeserializeProject(json, out var snapshot, out var profiles, out var activeProfileId))
                {
                    StatusMessage = "The selected project file is empty or not a supported ModbusForge project.";
                    return;
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    ApplyLoadedProfiles(profiles, activeProfileId);
                    ApplyWorkspaceSnapshot(snapshot);
                    StatusMessage = $"Loaded project from {Path.GetFileName(path)}.";
                });
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error loading project");
                StatusMessage = $"Load error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }


        private ProjectWorkspaceSnapshot BuildWorkspaceSnapshot()
        {
            SyncCurrentUnitConfiguration();

            var snapshot = new ProjectWorkspaceSnapshot
            {
                Mode = Mode,
                ServerAddress = ActiveProfile?.IpAddress ?? "127.0.0.1",
                Port = ActiveProfile?.Port ?? 502,
                ServerUnitId = ServerUnitIds,
                ClientUnitId = (byte)Math.Clamp(UnitId, 1, 247),
                SelectedUnitId = SelectedUnitId,
                IsServerMode = IsServerMode,
                VisibleTabs = GetVisibleTabs(),
                VisualNodes = VisualNodeEditorViewModel?.Nodes.ToList() ?? new List<VisualNode>(),
                VisualConnections = VisualNodeEditorViewModel?.Connections.ToList() ?? new List<NodeConnection>()
            };

            foreach (var pair in _unitConfigurationStore.UnitConfigurations)
            {
                if (pair.Key is >= 1 and <= 247 && pair.Value != null)
                {
                    snapshot.UnitConfigurations[pair.Key] = pair.Value.Clone();
                }
            }

            if (snapshot.UnitConfigurations.Count == 0)
            {
                snapshot.UnitConfigurations[snapshot.IsServerMode ? snapshot.SelectedUnitId : snapshot.ClientUnitId]
                    = new UnitIdConfiguration(snapshot.IsServerMode ? snapshot.SelectedUnitId : snapshot.ClientUnitId);
            }

            return snapshot;
        }


        private void ApplyWorkspaceSnapshot(ProjectWorkspaceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            _isApplyingUnitConfiguration = true;
            try
            {
                if (ActiveProfile == null)
                {
                    var profile = new ConnectionProfile("Default", snapshot.ServerAddress, snapshot.Port, snapshot.ClientUnitId)
                    {
                        Mode = string.IsNullOrWhiteSpace(snapshot.Mode) ? "Client" : snapshot.Mode,
                        ServerUnitIds = snapshot.ServerUnitId
                    };
                    _connectionManager.AddProfile(profile);
                    _connectionManager.SetActiveProfile(profile);
                }
                else
                {
                    ActiveProfile.Mode = string.IsNullOrWhiteSpace(snapshot.Mode) ? ActiveProfile.Mode : snapshot.Mode;
                    ActiveProfile.IpAddress = string.IsNullOrWhiteSpace(snapshot.ServerAddress)
                        ? ActiveProfile.IpAddress
                        : snapshot.ServerAddress;
                    if (snapshot.Port > 0)
                    {
                        ActiveProfile.Port = snapshot.Port;
                    }

                    ActiveProfile.ServerUnitIds = string.IsNullOrWhiteSpace(snapshot.ServerUnitId)
                        ? ActiveProfile.ServerUnitIds
                        : snapshot.ServerUnitId;
                    ActiveProfile.UnitId = snapshot.ClientUnitId is >= 1 and <= 247
                        ? snapshot.ClientUnitId
                        : ActiveProfile.UnitId;
                }

                _unitConfigurationStore.Clear();
                var configurations = snapshot.UnitConfigurations ?? new Dictionary<byte, UnitIdConfiguration>();
                foreach (var pair in configurations)
                {
                    if (pair.Key is >= 1 and <= 247 && pair.Value != null)
                    {
                        var configuration = pair.Value.Clone();
                        configuration.UnitId = pair.Key;
                        _unitConfigurationStore.SetConfiguration(pair.Key, configuration);
                    }
                }

                var ids = _unitConfigurationStore.UnitConfigurations.Keys
                    .Where(id => id is >= 1 and <= 247)
                    .OrderBy(id => id)
                    .ToList();
                var requested = snapshot.SelectedUnitId is >= 1 and <= 247
                    ? snapshot.SelectedUnitId
                    : snapshot.ClientUnitId;
                if (ids.Count == 0)
                {
                    requested = requested is >= 1 and <= 247 ? requested : (byte)1;
                    _unitConfigurationStore.GetOrCreateConfiguration(requested);
                    ids.Add(requested);
                }

                _unitConfigurationStore.PopulateAvailableUnitIds(ids);
                _unitConfigurationStore.SelectedUnitId = ids.Contains(requested) ? requested : ids[0];
                ApplyCurrentUnitConfiguration();

                SetVisibleTabs(snapshot.VisibleTabs);

                if (VisualNodeEditorViewModel != null)
                {
                    VisualNodeEditorViewModel.Nodes.Clear();
                    VisualNodeEditorViewModel.Connections.Clear();
                    foreach (var node in snapshot.VisualNodes ?? new List<VisualNode>())
                    {
                        VisualNodeEditorViewModel.Nodes.Add(node);
                    }

                    foreach (var connection in snapshot.VisualConnections ?? new List<NodeConnection>())
                    {
                        VisualNodeEditorViewModel.Connections.Add(connection);
                    }
                }
            }
            finally
            {
                _isApplyingUnitConfiguration = false;
            }

            OnPropertyChanged(nameof(ActiveProfile));
            OnPropertyChanged(nameof(UnitId));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(ServerUnitIds));
            OnPropertyChanged(nameof(EffectiveUnitId));
            OnPropertyChanged(nameof(CurrentConfig));
            OnPropertyChanged(nameof(CustomEntries));
            OnPropertyChanged(nameof(AvailableUnitIds));
            ReadAllCustomNowCommand.NotifyCanExecuteChanged();
            ExportUnitIdCommand.NotifyCanExecuteChanged();
            ImportUnitIdAsCommand.NotifyCanExecuteChanged();
        }


        private void ApplyLoadedProfiles(
            IReadOnlyList<ConnectionProfile>? profiles,
            string? activeProfileId)
        {
            if (profiles == null || profiles.Count == 0)
            {
                return;
            }

            // Loading a project replaces the whole profile set - stop any pending reconnects.
            // (The per-profile "still in the manager" check in HandleProfileDisconnected
            // prevents auto-reconnect for the old profiles.)
            StopAutoReconnect();
            foreach (var connectedProfile in _connectionManager.Profiles.Where(profile => profile.IsConnected).ToList())
            {
                _ = _connectionManager.DisconnectProfileAsync(connectedProfile);
            }

            _connectionManager.Profiles.Clear();
            foreach (var source in profiles)
            {
                source.IsConnected = false;
                source.IsActive = false;
                source.Status = "Disconnected";
                _connectionManager.AddProfile(source);
            }

            var active = _connectionManager.Profiles.FirstOrDefault(profile => profile.Id == activeProfileId)
                         ?? _connectionManager.Profiles.FirstOrDefault();
            if (active != null)
            {
                _connectionManager.SetActiveProfile(active);
            }

            _connectionManager.SaveProfiles();
        }


        private static AvaloniaProjectConfiguration CreateProjectConfiguration(
            ProjectWorkspaceSnapshot snapshot,
            string projectName)
        {
            var project = new AvaloniaProjectConfiguration
            {
                ProjectInfo = new ProjectInfo
                {
                    Name = string.IsNullOrWhiteSpace(projectName) ? "ModbusForge Project" : projectName,
                    Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty,
                    Modified = DateTime.Now
                },
                GlobalSettings = new GlobalSettings
                {
                    Mode = snapshot.Mode,
                    ServerAddress = snapshot.ServerAddress,
                    Port = snapshot.Port,
                    ServerUnitId = snapshot.ServerUnitId,
                    ClientUnitId = snapshot.ClientUnitId,
                    VisibleTabs = snapshot.VisibleTabs?.ToList() ?? new List<string>()
                },
                UnitConfigurations = snapshot.UnitConfigurations
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
                VisualNodes = snapshot.VisualNodes?.ToList() ?? new List<VisualNode>(),
                VisualConnections = snapshot.VisualConnections?.ToList() ?? new List<NodeConnection>()
            };

            return project;
        }


        private static bool TryDeserializeProject(
            string json,
            out ProjectWorkspaceSnapshot snapshot,
            out IReadOnlyList<ConnectionProfile>? profiles,
            out string? activeProfileId)
        {
            snapshot = new ProjectWorkspaceSnapshot();
            profiles = null;
            activeProfileId = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (TryGetProperty(root, "globalSettings", out _))
                {
                    var project = JsonSerializer.Deserialize<AvaloniaProjectConfiguration>(json, PersistenceJsonOptions);
                    if (project == null || project.UnitConfigurations == null)
                    {
                        return false;
                    }

                    profiles = project.Profiles;
                    activeProfileId = project.ActiveProfileId;
                    snapshot = SnapshotFromProjectConfiguration(project, project.SelectedUnitId, null);
                    return snapshot.UnitConfigurations.Count > 0 || snapshot.VisualNodes.Count > 0 || profiles.Count > 0;
                }

                if (TryGetProperty(root, "unitConfigurations", out _)
                    && TryGetProperty(root, "mode", out _))
                {
                    snapshot = JsonSerializer.Deserialize<ProjectWorkspaceSnapshot>(json, PersistenceJsonOptions)
                               ?? new ProjectWorkspaceSnapshot();
                    return snapshot.UnitConfigurations.Count > 0;
                }

                // Prior Avalonia builds wrote AppConfiguration directly. Keep those
                // files loadable while all new files use ProjectConfiguration plus
                // the profile extension below.
                var legacy = JsonSerializer.Deserialize<AppConfiguration>(json, PersistenceJsonOptions);
                if (legacy == null)
                {
                    return false;
                }

                profiles = legacy.Profiles;
                activeProfileId = legacy.ActiveProfileId;
                var active = legacy.Profiles?.FirstOrDefault(profile => profile.Id == legacy.ActiveProfileId)
                             ?? legacy.Profiles?.FirstOrDefault();
                var clientId = legacy.UnitId is >= 1 and <= 247
                    ? legacy.UnitId
                    : (active?.UnitId is >= 1 and <= 247 ? active.UnitId : (byte)1);
                var configuration = new UnitIdConfiguration(clientId);
                foreach (var entry in legacy.CustomEntries ?? new List<CustomEntry>())
                {
                    configuration.CustomEntries.Add(entry);
                }

                configuration.RegisterSettings.RegisterStart = legacy.StartAddress;
                configuration.RegisterSettings.RegisterCount = legacy.RegisterCount;
                configuration.RegisterSettings.RegistersGlobalType = legacy.GlobalType ?? "int";
                configuration.RegisterSettings.RegistersSwapBytes = legacy.SwapBytes;
                configuration.RegisterSettings.RegistersSwapWords = legacy.SwapWords;
                snapshot = new ProjectWorkspaceSnapshot
                {
                    Mode = legacy.Mode ?? active?.Mode ?? "Client",
                    ServerAddress = legacy.ServerAddress ?? active?.IpAddress ?? "127.0.0.1",
                    Port = legacy.Port > 0 ? legacy.Port : active?.Port ?? 502,
                    ServerUnitId = active?.ServerUnitIds ?? "1",
                    ClientUnitId = clientId,
                    SelectedUnitId = clientId,
                    IsServerMode = string.Equals(legacy.Mode ?? active?.Mode, "Server", StringComparison.OrdinalIgnoreCase),
                    UnitConfigurations = new Dictionary<byte, UnitIdConfiguration> { [clientId] = configuration },
                    VisualNodes = legacy.VisualNodes ?? new List<VisualNode>(),
                    VisualConnections = legacy.VisualConnections ?? new List<NodeConnection>()
                };
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }


        private static ProjectWorkspaceSnapshot SnapshotFromProjectConfiguration(
            ProjectConfiguration project,
            byte? selectedUnitId,
            IReadOnlyList<string>? visibleTabsOverride)
        {
            var global = project.GlobalSettings ?? new GlobalSettings();
            var configurations = new Dictionary<byte, UnitIdConfiguration>();
            foreach (var pair in project.UnitConfigurations ?? new Dictionary<byte, UnitIdConfiguration>())
            {
                if (pair.Key is >= 1 and <= 247 && pair.Value != null)
                {
                    var configuration = pair.Value.Clone();
                    configuration.UnitId = pair.Key;
                    configurations[pair.Key] = configuration;
                }
            }

            var clientId = global.ClientUnitId is >= 1 and <= 247 ? global.ClientUnitId : (byte)1;
            var selected = selectedUnitId is >= 1 and <= 247
                ? selectedUnitId.Value
                : (configurations.ContainsKey(clientId) ? clientId : configurations.Keys.FirstOrDefault((byte)1));
            return new ProjectWorkspaceSnapshot
            {
                Mode = global.Mode ?? "Client",
                ServerAddress = global.ServerAddress ?? "127.0.0.1",
                Port = global.Port > 0 ? global.Port : 502,
                ServerUnitId = global.ServerUnitId ?? "1",
                ClientUnitId = clientId,
                SelectedUnitId = selected,
                IsServerMode = string.Equals(global.Mode, "Server", StringComparison.OrdinalIgnoreCase),
                UnitConfigurations = configurations,
                VisibleTabs = visibleTabsOverride?.ToList() ?? global.VisibleTabs?.ToList() ?? new List<string>(),
                VisualNodes = project.VisualNodes ?? new List<VisualNode>(),
                VisualConnections = project.VisualConnections ?? new List<NodeConnection>()
            };
        }


        private static bool TryDeserializeUnitConfigurations(
            string json,
            out Dictionary<byte, UnitIdConfiguration> configurations)
        {
            configurations = new Dictionary<byte, UnitIdConfiguration>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!TryGetProperty(root, "unitConfigurations", out _))
                {
                    return false;
                }

                var project = JsonSerializer.Deserialize<ProjectConfiguration>(json, PersistenceJsonOptions);
                if (project?.UnitConfigurations == null)
                {
                    return false;
                }

                foreach (var pair in project.UnitConfigurations)
                {
                    if (pair.Key is >= 1 and <= 247 && pair.Value != null)
                    {
                        var configuration = pair.Value.Clone();
                        configuration.UnitId = pair.Key;
                        configurations[pair.Key] = configuration;
                    }
                }

                return configurations.Count > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }


        private static List<byte> TryDeserializeUnitIdList(string json)
        {
            try
            {
                return (JsonSerializer.Deserialize<List<byte>>(json, PersistenceJsonOptions) ?? new List<byte>())
                    .Where(id => id is >= 1 and <= 247)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
            }
            catch (JsonException)
            {
                return new List<byte>();
            }
        }


        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }


        private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };


        private sealed class AvaloniaProjectConfiguration : ProjectConfiguration
        {
            public List<ConnectionProfile> Profiles { get; set; } = new();
            public string? ActiveProfileId { get; set; }
            public byte SelectedUnitId { get; set; } = 1;
        }

    }
}
