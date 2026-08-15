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
    /// MainViewModel - Connection partial (split for navigability; behavior unchanged).
    /// </summary>
    public partial class MainViewModel
    {
        private readonly object _monitorFailureLock = new();

        private readonly Dictionary<PlcArea, int> _monitorFailureCounts = new();

        private readonly Dictionary<PlcArea, DateTime> _lastMonitorFailureUtc = new();

        private CancellationTokenSource? _autoReconnectCts;

        private readonly object _autoReconnectLock = new();

        private volatile bool _userInitiatedDisconnect;


        /// <summary>
        /// Pause before restarting the poll loop after it died from an error, so a
        /// persistently failing service cannot spin an error → immediate-restart loop.
        /// </summary>
        private const int PollRestartBackoffMs = 1000;


        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UnitId))]
        private ConnectionProfile? _activeProfile;


        public ObservableCollection<ConnectionProfile> ConnectionProfiles => _connectionManager.Profiles;


        public ConnectionProfile? DashboardSelectedProfile
        {
            get => ActiveProfile;
            set
            {
                if (value != null && !ReferenceEquals(_connectionManager.ActiveProfile, value))
                {
                    _connectionManager.SetActiveProfile(value);
                }

                OnPropertyChanged();
            }
        }


        public bool IsServerMode => string.Equals(Mode, "Server", StringComparison.OrdinalIgnoreCase);


        public bool ShowClientFields => !IsServerMode;


        public bool ShowServerFields => IsServerMode;


        public string ConnectButtonText => IsServerMode ? "Start Server" : "Connect";


        public string ToggleConnectionButtonText => IsConnected ? "Disconnect" : ConnectButtonText;


        public string ConnectionHeader => IsServerMode ? "Modbus Connection (Server)" : "Modbus Connection (Client)";


        public string AddressLabel => IsServerMode ? "Interface:" : "Server:";


        public string ServerUnitIds
        {
            get => ActiveProfile?.ServerUnitIds ?? "1";
            set
            {
                if (ActiveProfile != null && ActiveProfile.ServerUnitIds != value)
                {
                    ActiveProfile.ServerUnitIds = value;
                    OnPropertyChanged();
                }
            }
        }


        public IAsyncRelayCommand ConnectCommand { get; }

        public IAsyncRelayCommand DisconnectCommand { get; }

        public IAsyncRelayCommand ToggleConnectionCommand { get; }


        [ObservableProperty]
        private bool _hasConnectionError;


        public bool IsConnected => ActiveProfile?.IsConnected == true;


        public bool IsDisconnected => !IsConnected && !HasConnectionError;


        public bool IsConnectionErrorVisible => HasConnectionError && !IsConnected;


        public string ServerIpAddress
        {
            get
            {
                if (!IsServerMode)
                    return string.Empty;

                var port = ActiveProfile?.Port ?? 502;
                if (ActiveService is ModbusServerService server && IsConnected)
                {
                    return server.BoundEndpoint;
                }

                var ips = IpRangeHelper.GetAllLocalIPv4();
                if (ips.Count == 0)
                    return $"127.0.0.1:{port}";

                var withPort = ips.Select(ip => $"{ip}:{port}");
                return string.Join(", ", withPort);
            }
        }


        public string ActiveProfileDisplayName
        {
            get
            {
                if (ActiveProfile == null)
                    return "None";

                if (IsServerMode)
                    return $"{ActiveProfile.Name} ({ServerIpAddress})";

                return ActiveProfile.DisplayName;
            }
        }


        public DateTime LastErrorTime { get; private set; } = DateTime.MinValue;


        public int HoldingMonitorFailureCount => GetMonitorFailureCount(PlcArea.HoldingRegister);


        public int InputRegistersMonitorFailureCount => GetMonitorFailureCount(PlcArea.InputRegister);


        public int CoilsMonitorFailureCount => GetMonitorFailureCount(PlcArea.Coil);


        public int DiscreteInputsMonitorFailureCount => GetMonitorFailureCount(PlcArea.DiscreteInput);


        public int GetMonitorFailureCount(PlcArea area)
        {
            lock (_monitorFailureLock)
            {
                return _monitorFailureCounts.TryGetValue(area, out var count) ? count : 0;
            }
        }


        public DateTime GetLastMonitorFailureUtc(PlcArea area)
        {
            lock (_monitorFailureLock)
            {
                return _lastMonitorFailureUtc.TryGetValue(area, out var timestamp)
                    ? timestamp
                    : DateTime.MinValue;
            }
        }


        public string ConnectionStatusText => IsConnected
            ? (ActiveProfile?.Status ?? "Connected")
            : HasConnectionError ? "Connection error" : "Not connected";


        public string DebugSummary => $"Profile: {ActiveProfile?.DisplayName ?? "None"} | " +
                                      $"Mode: {ActiveProfile?.Mode ?? "(null)"} | IsServer: {ActiveProfile?.IsServerMode} | " +
                                      $"Connected: {IsConnected} | Busy: {IsBusy} | " +
                                      $"Holding: {HoldingRegisters.Count} | Input: {InputRegisters.Count} | " +
                                      $"Coils: {Coils.Count} | Discrete: {DiscreteInputs.Count}";


        partial void OnActiveProfileChanged(ConnectionProfile? value)
        {
            RefreshConnectionDependentProperties();
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            ToggleConnectionCommand.NotifyCanExecuteChanged();
        }


        private void RefreshConnectionDependentProperties()
        {
            if (ActiveProfile is null) return;

            RefreshAvailableUnitIds();
            SyncCurrentUnitConfiguration();

            OnPropertyChanged(nameof(ActiveService));
            OnPropertyChanged(nameof(ServerIpAddress));
            OnPropertyChanged(nameof(ActiveProfileDisplayName));
            OnPropertyChanged(nameof(UnitId));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(ModeIndex));
            OnPropertyChanged(nameof(IsServerMode));
            OnPropertyChanged(nameof(ShowClientFields));
            OnPropertyChanged(nameof(ShowServerFields));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(ToggleConnectionButtonText));
            OnPropertyChanged(nameof(ConnectionHeader));
            OnPropertyChanged(nameof(AddressLabel));
            OnPropertyChanged(nameof(ServerUnitIds));
            OnPropertyChanged(nameof(AvailableUnitIds));
            OnPropertyChanged(nameof(SelectedUnitId));
            OnPropertyChanged(nameof(EffectiveUnitId));
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
            OnPropertyChanged(nameof(CanRead));
            OnPropertyChanged(nameof(CanWrite));
            OnPropertyChanged(nameof(CanReadCustomEntry));
            OnPropertyChanged(nameof(CanWriteCustomEntry));
        }


        private bool CanConnect() => ActiveProfile is { IsConnected: false } && !IsBusy;


        private bool CanDisconnect() => ActiveProfile is { IsConnected: true } && !IsBusy;


        private void HandleActiveProfilePropertyChanged(PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConnectionProfile.IsConnected))
            {
                if (ActiveProfile?.IsConnected == true)
                {
                    HasConnectionError = false;
                }

                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsDisconnected));
                OnPropertyChanged(nameof(IsConnectionErrorVisible));
                OnPropertyChanged(nameof(ConnectionStatusText));
                OnPropertyChanged(nameof(ServerIpAddress));
            OnPropertyChanged(nameof(ActiveProfileDisplayName));
                OnPropertyChanged(nameof(DebugSummary));
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(CanDisconnect));
                OnPropertyChanged(nameof(ToggleConnectionButtonText));
                OnPropertyChanged(nameof(CanRead));
                OnPropertyChanged(nameof(CanWrite));
                OnPropertyChanged(nameof(CanReadCustomEntry));
                OnPropertyChanged(nameof(CanWriteCustomEntry));
                ConnectCommand.NotifyCanExecuteChanged();
                ToggleConnectionCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
                ReadCommand.NotifyCanExecuteChanged();
                WriteCommand.NotifyCanExecuteChanged();
                ReadHoldingRegistersCommand.NotifyCanExecuteChanged();
                ReadInputRegistersCommand.NotifyCanExecuteChanged();
                ReadCoilsCommand.NotifyCanExecuteChanged();
                ReadDiscreteInputsCommand.NotifyCanExecuteChanged();
                WriteHoldingRegisterCommand.NotifyCanExecuteChanged();
                WriteCoilCommand.NotifyCanExecuteChanged();
                ReadCustomEntryCommand.NotifyCanExecuteChanged();
                WriteCustomEntryCommand.NotifyCanExecuteChanged();
                ReadCustomNowCommand.NotifyCanExecuteChanged();
                WriteCustomNowCommand.NotifyCanExecuteChanged();
                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
            }

            if (e.PropertyName == nameof(ConnectionProfile.Status))
            {
                StatusMessage = ActiveProfile?.Status ?? "Ready";
                OnPropertyChanged(nameof(ConnectionStatusText));
            }

            if (e.PropertyName == nameof(ConnectionProfile.Port))
            {
                OnPropertyChanged(nameof(ServerIpAddress));
            OnPropertyChanged(nameof(ActiveProfileDisplayName));
            }

            if (e.PropertyName == nameof(ConnectionProfile.UnitId))
            {
                if (!IsServerMode && ActiveProfile != null)
                {
                    _unitConfigurationStore.GetOrCreateConfiguration(ActiveProfile.UnitId);
                    if (_unitConfigurationStore.SelectedUnitId != ActiveProfile.UnitId)
                    {
                        _unitConfigurationStore.SelectedUnitId = ActiveProfile.UnitId;
                    }
                }

                OnPropertyChanged(nameof(UnitId));
                OnPropertyChanged(nameof(EffectiveUnitId));
            }

            if (e.PropertyName == nameof(ConnectionProfile.Mode))
            {
                OnPropertyChanged(nameof(Mode));
                OnPropertyChanged(nameof(IsServerMode));
                OnPropertyChanged(nameof(ShowClientFields));
                OnPropertyChanged(nameof(ShowServerFields));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(ToggleConnectionButtonText));
                OnPropertyChanged(nameof(ConnectionHeader));
                OnPropertyChanged(nameof(AddressLabel));
                OnPropertyChanged(nameof(EffectiveUnitId));
                OnPropertyChanged(nameof(DebugSummary));
                RefreshAvailableUnitIds();
                OnPropertyChanged(nameof(AvailableUnitIds));
                OnPropertyChanged(nameof(SelectedUnitId));
                ExportUnitIdCommand.NotifyCanExecuteChanged();
                ImportUnitIdAsCommand.NotifyCanExecuteChanged();
            }

            if (e.PropertyName == nameof(ConnectionProfile.ServerUnitIds))
            {
                OnPropertyChanged(nameof(ServerUnitIds));
                RefreshAvailableUnitIds();
            }
        }


        private void ConnectionManager_ActiveProfileChanged(object? sender, ConnectionProfile? e)
        {
            // Raised on the caller's thread (UI in most cases, but ConnectionManager can be
            // driven from other threads) - marshal to keep VM state on the UI thread.
            _ = _dispatcher.InvokeAsync(() => HandleActiveProfileChanged(e));
        }


        private void HandleActiveProfileChanged(ConnectionProfile? e)
        {
            // The auto-reconnect loop targets a specific profile; a profile switch voids it.
            StopAutoReconnect();

            SyncCurrentUnitConfiguration();

            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged -= ActiveProfile_PropertyChanged;
            }

            ActiveProfile = e;

            if (e != null)
            {
                e.PropertyChanged += ActiveProfile_PropertyChanged;
                if (!e.IsServerMode)
                {
                    SelectedUnitId = e.UnitId;
                }
            }

            OnPropertyChanged(nameof(ActiveProfile));
            OnPropertyChanged(nameof(DashboardSelectedProfile));
            RefreshConnectionDependentProperties();
            ReadCommand.NotifyCanExecuteChanged();
            WriteCommand.NotifyCanExecuteChanged();
            ReadCustomEntryCommand.NotifyCanExecuteChanged();
            WriteCustomEntryCommand.NotifyCanExecuteChanged();
            ReadCustomNowCommand.NotifyCanExecuteChanged();
            WriteCustomNowCommand.NotifyCanExecuteChanged();
            ReadAllCustomNowCommand.NotifyCanExecuteChanged();

            RefreshAvailableUnitIds();

            ResetMonitorFailures();
            HasConnectionError = false;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(IsConnectionErrorVisible));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(DebugSummary));
            StatusMessage = e != null ? $"Active profile: {e.DisplayName}" : "No active connection profile";
        }


        private void ConnectionManager_ProfileConnected(object? sender, ConnectionProfile e)
        {
            // Raised from ConnectProfileAsync on a thread-pool thread.
            _ = _dispatcher.InvokeAsync(() => HandleProfileConnected(e));
        }


        private void HandleProfileConnected(ConnectionProfile e)
        {
            // A successful connection (manual or auto) ends any pending auto-reconnect.
            StopAutoReconnect();

            _logger.LogInformation("Profile connected: {Name}", e.Name);
            ResetMonitorFailures();
            HasConnectionError = false;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(IsConnectionErrorVisible));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(ServerIpAddress));
            OnPropertyChanged(nameof(ActiveProfileDisplayName));
            OnPropertyChanged(nameof(DebugSummary));

            if (e.IsServerMode && _connectionManager.ActiveService is ModbusServerService server)
            {
                var unitIds = server.GetUnitIds()
                    .Where(id => id is >= 1 and <= 247)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
                _unitConfigurationStore.PopulateAvailableUnitIds(unitIds);

                foreach (var id in unitIds)
                {
                    _unitConfigurationStore.GetOrCreateConfiguration(id);
                }

                if (unitIds.Count > 0)
                {
                    SelectedUnitId = unitIds[0];
                }

                StatusMessage = $"Server started on {server.BoundEndpoint}";

                OnPropertyChanged(nameof(ShowServerFields));
                ExportUnitIdCommand.NotifyCanExecuteChanged();
                ImportUnitIdAsCommand.NotifyCanExecuteChanged();
            }

            _trendLogger?.Start();
            StartPolling();
            StartCustomWatchMonitoring();
        }


        private void ConnectionManager_ProfileDisconnected(object? sender, ConnectionProfile e)
        {
            // Raised from DisconnectProfileAsync on a thread-pool thread.
            _ = _dispatcher.InvokeAsync(() => HandleProfileDisconnected(e));
        }


        private void HandleProfileDisconnected(ConnectionProfile e)
        {
            _logger.LogInformation("Profile disconnected: {Name}", e.Name);
            OnPropertyChanged(nameof(ServerIpAddress));
            OnPropertyChanged(nameof(ActiveProfileDisplayName));
            _trendLogger?.Stop();
            StopPolling();
            StopCustomWatchMonitoring();

            var userInitiated = _userInitiatedDisconnect;
            _userInitiatedDisconnect = false;

            // Profiles removed from the manager (profile deleted, project reloaded) must
            // never be reconnected either.
            if (userInitiated || !_connectionManager.Profiles.Contains(e))
            {
                StopAutoReconnect();
            }
            else
            {
                // Unexpected loss: restart the link if the user opted in.
                StartAutoReconnectIfEnabled("unexpected connection loss");
            }
        }


        private void RefreshAvailableUnitIds()
        {
            if (ActiveProfile is null || !IsServerMode)
            {
                _unitConfigurationStore.PopulateAvailableUnitIds(Array.Empty<byte>());
                return;
            }

            var ids = ParseUnitIdString(ActiveProfile.ServerUnitIds);
            _unitConfigurationStore.PopulateAvailableUnitIds(ids);

            foreach (var id in ids)
            {
                _unitConfigurationStore.GetOrCreateConfiguration(id);
            }

            if (ids.Count > 0 && (SelectedUnitId == 0 || !ids.Contains(SelectedUnitId)))
            {
                SelectedUnitId = ids[0];
            }
        }


        private static List<byte> ParseUnitIdString(string? input)
        {
            var result = new List<byte>();
            if (string.IsNullOrWhiteSpace(input)) return result;

            var parts = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains('-'))
                {
                    var range = trimmed.Split('-');
                    if (range.Length == 2 && byte.TryParse(range[0].Trim(), out byte start) && byte.TryParse(range[1].Trim(), out byte end))
                    {
                        for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                        {
                            if (i >= 1 && i <= 247 && !result.Contains((byte)i))
                                result.Add((byte)i);
                        }
                    }
                }
                else if (byte.TryParse(trimmed, out byte id))
                {
                    if (id >= 1 && id <= 247 && !result.Contains(id))
                        result.Add(id);
                }
            }

            return result;
        }


        private async Task ConnectAsync()
        {
            if (ActiveProfile == null) return;

            IsBusy = true;
            try
            {
                await _connectionManager.ConnectProfileAsync(ActiveProfile);
            }
            catch (Exception ex)
            {
                HasConnectionError = true;
                StatusMessage = $"Connection error: {ex.Message}";
                _logger.LogError(ex, "Error connecting profile {Name}", ActiveProfile.Name);
            }
            finally
            {
                IsBusy = false;
            }
        }


        private async Task DisconnectAsync()
        {
            if (ActiveProfile == null) return;

            IsBusy = true;
            // Distinguishes a deliberate user disconnect (no auto-reconnect) from an
            // unexpected service-side loss (auto-reconnect if enabled).
            _userInitiatedDisconnect = true;
            try
            {
                await _connectionManager.DisconnectProfileAsync(ActiveProfile);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Disconnect error: {ex.Message}";
                _logger.LogError(ex, "Error disconnecting profile {Name}", ActiveProfile.Name);
            }
            finally
            {
                IsBusy = false;
            }
        }


        /// <summary>
        /// Starts the auto-reconnect loop if the AutoReconnect preference is enabled.
        /// Retries ConnectProfileAsync on the configured interval until it succeeds, the
        /// profile changes, the user disconnects, or the view model is disposed.
        /// </summary>
        private void StartAutoReconnectIfEnabled(string reason)
        {
            if (_disposed)
                return;

            if (_settingsService is not { AutoReconnect: true })
                return;

            lock (_autoReconnectLock)
            {
                if (_autoReconnectCts != null)
                    return; // a loop is already running

                _autoReconnectCts = new CancellationTokenSource();
            }

            _logger.LogInformation("Auto-reconnect started ({Reason})", reason);
            _ = AutoReconnectLoopAsync(_autoReconnectCts.Token);
        }


        private void StopAutoReconnect()
        {
            lock (_autoReconnectLock)
            {
                _autoReconnectCts?.Cancel();
                // Disposal happens in the loop's finally.
            }
        }


        private async Task AutoReconnectLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !_disposed)
                {
                    // Read the interval each iteration so preference changes apply without a restart.
                    var intervalMs = Math.Max(100, _settingsService?.AutoReconnectIntervalMs ?? 5000);
                    try
                    {
                        await Task.Delay(intervalMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    var profile = ActiveProfile;
                    if (profile is null || profile.IsConnected)
                        return;

                    await _dispatcher.InvokeAsync(() => StatusMessage = $"Connection lost. Retrying in {intervalMs / 1000.0:0.#} s...");

                    bool success;
                    try
                    {
                        success = await _connectionManager.ConnectProfileAsync(profile);
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        _logger.LogWarning(ex, "Auto-reconnect attempt for profile {Name} threw", profile.Name);
                        success = false;
                    }

                    if (token.IsCancellationRequested || _disposed)
                        return;

                    if (success)
                    {
                        // ProfileConnected (marshaled) stops the loop; also break here in case it raced.
                        _logger.LogInformation("Auto-reconnect succeeded for profile {Name}", profile.Name);
                        return;
                    }

                    _logger.LogWarning("Auto-reconnect attempt failed for profile {Name}; retrying in {Interval} ms", profile.Name, intervalMs);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Auto-reconnect loop failed");
            }
            finally
            {
                lock (_autoReconnectLock)
                {
                    if (_autoReconnectCts != null && ReferenceEquals(_autoReconnectCts.Token, token))
                    {
                        _autoReconnectCts.Dispose();
                        _autoReconnectCts = null;
                    }
                }
            }
        }


        private async Task ToggleConnectionAsync()
        {
            _logger.LogInformation("ToggleConnectionAsync invoked");
            if (IsConnected)
                await DisconnectAsync();
            else
                await ConnectAsync();
        }


        private int GetMonitorFailureCountUnsafe(PlcArea area) =>
            _monitorFailureCounts.TryGetValue(area, out var count) ? count : 0;


        private void ClearMonitorFailure(PlcArea area)
        {
            var allClear = false;
            lock (_monitorFailureLock)
            {
                _monitorFailureCounts[area] = 0;
                _lastMonitorFailureUtc.Remove(area);
                allClear = _monitorFailureCounts.Values.All(count => count == 0);
            }

            if (allClear)
            {
                // May run on the poll thread - marshal the observable property change to the UI thread.
                _dispatcher.Invoke(() => HasConnectionError = false);
            }
        }


        private void ResetMonitorFailures()
        {
            lock (_monitorFailureLock)
            {
                _monitorFailureCounts.Clear();
                _lastMonitorFailureUtc.Clear();
            }

            LastErrorTime = DateTime.MinValue;
        }

    }
}
