using System;
using System.Linq;
using System.Windows;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Services;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows.Data;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Specialized;
using ModbusForge.Models;
using System.Windows.Controls;
using ModbusForge.Helpers;
using ModbusForge.ViewModels.Coordinators;
using ModbusForge.Controls;

namespace ModbusForge.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IDisposable
    {
        private const long MaxProjectFileSize = 5 * 1024 * 1024; // 5MB limit for project files

        // Partial method declarations for delegated properties (required by CommunityToolkit.Mvvm)
        partial void OnRegistersGlobalTypeChanged(string value);
        partial void OnInputRegistersGlobalTypeChanged(string value);

        private IModbusService _modbusService;
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
        private readonly IConsoleLoggerService _consoleLoggerService;
        private readonly ConnectionCoordinator _connectionCoordinator;
        private readonly RegisterCoordinator _registerCoordinator;
        private readonly CustomEntryCoordinator _customEntryCoordinator;
        private readonly TrendCoordinator _trendCoordinator;
        private readonly ConfigurationCoordinator _configurationCoordinator;
        private readonly VisualNodeEditorViewModel _visualNodeEditorViewModel;
        private readonly IVisualSimulationService _visualSimulationService;
        private bool _disposed = false;
        // Mode-aware UI helpers

        public bool IsServerMode => string.Equals(Mode, "Server", StringComparison.OrdinalIgnoreCase);
        public bool ShowClientFields => !IsServerMode; // show IP/UnitId only in client mode
        public string ConnectButtonText => IsServerMode ? "Start Server" : "Connect";
        public string ConnectionHeader => IsServerMode ? "Modbus Connection (Server)" : "Modbus Connection (Client)";
        public string AddressLabel => IsServerMode ? "Interface:" : "Server:";

        public MainViewModel() : this(
            App.ServiceProvider.GetRequiredService<ModbusTcpService>(),
            App.ServiceProvider.GetRequiredService<ModbusServerService>(),
            App.ServiceProvider.GetRequiredService<ILogger<MainViewModel>>(),
            App.ServiceProvider.GetRequiredService<IOptions<ServerSettings>>(),
            App.ServiceProvider.GetRequiredService<ITrendLogger>(),
            App.ServiceProvider.GetRequiredService<ICustomEntryService>(),
            App.ServiceProvider.GetRequiredService<IConsoleLoggerService>(),
            App.ServiceProvider.GetRequiredService<ConnectionCoordinator>(),
            App.ServiceProvider.GetRequiredService<RegisterCoordinator>(),
            App.ServiceProvider.GetRequiredService<CustomEntryCoordinator>(),
            App.ServiceProvider.GetRequiredService<TrendCoordinator>(),
            App.ServiceProvider.GetRequiredService<ConfigurationCoordinator>())
        {
        }

        private async Task ReadAllCustomNowAsync()
        {
            if (!IsConnected) return;
            var snapshot = CustomEntries.ToList();
            foreach (var ce in snapshot)
            {
                try { await _customEntryCoordinator.ReadCustomNowAsync(ce, EffectiveUnitId, msg => StatusMessage = msg, IsServerMode); }
                catch (Exception ex) { _logger.LogDebug(ex, "ReadAllCustomNow: failed for {Area} {Address}", ce.Area, ce.Address); }
            }
            StatusMessage = $"Read {snapshot.Count} custom entries";
        }

        public VisualNodeEditorViewModel VisualNodeEditorViewModel => _visualNodeEditorViewModel;

        public MainViewModel(ModbusTcpService clientService, ModbusServerService serverService, ILogger<MainViewModel> logger, IOptions<ServerSettings> options, ITrendLogger trendLogger, ICustomEntryService customEntryService, IConsoleLoggerService consoleLoggerService, ConnectionCoordinator connectionCoordinator, RegisterCoordinator registerCoordinator, CustomEntryCoordinator customEntryCoordinator, TrendCoordinator trendCoordinator, ConfigurationCoordinator configurationCoordinator)
        {
            // Store dependencies
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
            _customEntryService = customEntryService ?? throw new ArgumentNullException(nameof(customEntryService));
            _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));
            _connectionCoordinator = connectionCoordinator ?? throw new ArgumentNullException(nameof(connectionCoordinator));
            _registerCoordinator = registerCoordinator ?? throw new ArgumentNullException(nameof(registerCoordinator));
            _customEntryCoordinator = customEntryCoordinator ?? throw new ArgumentNullException(nameof(customEntryCoordinator));
            _trendCoordinator = trendCoordinator ?? throw new ArgumentNullException(nameof(trendCoordinator));
            _configurationCoordinator = configurationCoordinator ?? throw new ArgumentNullException(nameof(configurationCoordinator));
            // Initialize visual node editor
            _visualNodeEditorViewModel = new VisualNodeEditorViewModel();
            _visualSimulationService = App.ServiceProvider.GetRequiredService<IVisualSimulationService>();
            // VisualSimulationService will be started/stopped by ShowLiveValues toggle
            
            var settings = options?.Value ?? new ServerSettings();

            // Initialize in logical order
            InitializeMode(settings);
            InitializeDefaultsFromConfig(settings);
            InitializeCommands();
            InitializeServiceState();
            InitializeWindowTitle();
            InitializeTimersAndServices();

            _logger.LogInformation("MainViewModel initialized");
        }

        /// <summary>
        /// Initializes the Mode (Client/Server) from configuration.
        /// </summary>
        private void InitializeMode(ServerSettings settings)
        {
            Mode = string.Equals(settings.Mode, "Server", StringComparison.OrdinalIgnoreCase) ? "Server" : "Client";
        }

        /// <summary>
        /// Initializes default values from configuration settings.
        /// </summary>
        private void InitializeDefaultsFromConfig(ServerSettings settings)
        {
            try
            {
                if (settings.DefaultPort > 0)
                {
                    Port = settings.DefaultPort;
                }

                if (!IsServerMode)
                {
                    if (string.IsNullOrWhiteSpace(ServerAddress))
                    {
                        ServerAddress = "127.0.0.1";
                    }
                    if (settings.DefaultUnitId >= 1 && settings.DefaultUnitId <= 247)
                    {
                        UnitId = (byte)settings.DefaultUnitId;
                    }
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to load settings, using defaults"); }
        }

        /// <summary>
        /// Initializes all command objects for UI bindings.
        /// </summary>
        private void InitializeCommands()
        {
            // Modbus operation commands
            UpdateHoldingRegisterCommand = new AsyncRelayCommand<DataGridCellEditEndingEventArgs>(UpdateHoldingRegister);
            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), CanConnect);
            _disconnectCommand = new RelayCommand(async () => await DisconnectAsync(), CanDisconnect);
            DisconnectCommand = _disconnectCommand;
            RunDiagnosticsCommand = new RelayCommand(async () => await RunDiagnosticsAsync());
            
            ReadRegistersCommand = new RelayCommand(async () => await ReadRegistersAsync(), () => IsConnected);
            WriteRegisterCommand = new RelayCommand(async () => await WriteRegisterAsync(), () => IsConnected);
            ReadCoilsCommand = new RelayCommand(async () => await ReadCoilsAsync(), () => IsConnected);
            WriteCoilCommand = new RelayCommand(async () => await WriteCoilAsync(), () => IsConnected);
            ReadInputRegistersCommand = new RelayCommand(async () => await ReadInputRegistersAsync(), () => IsConnected);
            ReadDiscreteInputsCommand = new RelayCommand(async () => await ReadDiscreteInputsAsync(), () => IsConnected);

            // Custom tab commands
            AddCustomEntryCommand = new RelayCommand(AddCustomEntry);
            WriteCustomNowCommand = new AsyncRelayCommand<object?>(async param =>
            {
                if (param is CustomEntry ce)
                    await WriteCustomNowAsync(ce);
            });
            ReadCustomNowCommand = new AsyncRelayCommand<object?>(async param =>
            {
                if (param is CustomEntry ce)
                    await _customEntryCoordinator.ReadCustomNowAsync(ce, EffectiveUnitId, msg => StatusMessage = msg, IsServerMode);
            });
            ReadAllCustomNowCommand = new RelayCommand(async () => await ReadAllCustomNowAsync());
            // Project commands (replacing Custom save/load)
            SaveProjectCommand = new RelayCommand(async () => await SaveProjectAsync());
            LoadProjectCommand = new RelayCommand(async () => await LoadProjectAsync());
            ImportUnitIdsCommand = new RelayCommand(async () => await ImportUnitIdsAsync());
            ExportUnitIdsCommand = new RelayCommand(async () => await ExportUnitIdsAsync());
            ExportUnitIdCommand = new RelayCommand(async () => await ExportUnitIdAsync());
            ImportUnitIdAsCommand = new RelayCommand(async () => await ImportUnitIdAsAsync());
            
            // Legacy Custom commands (kept for compatibility but will be hidden)
            SaveCustomCommand = new RelayCommand(async () => await SaveCustomAsync());
            LoadCustomCommand = new RelayCommand(async () => await LoadCustomAsync());
            SaveAllConfigCommand = new RelayCommand(async () => await SaveAllConfigAsync());
            LoadAllConfigCommand = new RelayCommand(async () => await LoadAllConfigAsync());
        }

        /// <summary>
        /// Initializes the active Modbus service and connection state.
        /// </summary>
        private void InitializeServiceState()
        {
            _modbusService = IsServerMode ? _serverService : _clientService;
            IsConnected = _modbusService.IsConnected;
            StatusMessage = IsConnected ? "Connected" : "Disconnected";
        }

        /// <summary>
        /// Initializes the window title with application version.
        /// </summary>
        private void InitializeWindowTitle()
        {
            try
            {
                string? version = GetApplicationVersion();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    Title = $"ModbusForge v{version}";
                    Version = version;
                }
                else
                {
                    Title = "ModbusForge v3.0.3";
                    Version = "3.0.3";
                }
            }
            catch
            {
                Title = "ModbusForge v3.0.3";
                Version = "3.0.3";
            }
        }

        /// <summary>
        /// Gets the application version from assembly metadata.
        /// </summary>
        private static string? GetApplicationVersion()
        {
            // Try getting version from process path (works with single-file apps)
            var procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath))
            {
                var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(procPath)?.ProductVersion;
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }

            // Fallback to informational version or simple version
            var entryAssembly = Assembly.GetEntryAssembly();
            return entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion 
                ?? entryAssembly?.GetName().Version?.ToString() 
                ?? "3.0.3";
        }

        /// <summary>
        /// Initializes and starts all timers and background services.
        /// </summary>
        private void InitializeTimersAndServices()
        {
            // Custom writer timer
            _customTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _customTimer.Tick += CustomTimer_Tick;
            _customTimer.Start();

            // Monitor timer for continuous reads
            _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();

            // Trend sampling timer
            _trendTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, _trendLogger.SampleRateMs)) };
            _trendTimer.Tick += TrendTimer_Tick;
            _trendTimer.Start();

            // Start services
            try { _trendLogger.Start(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to start trend logger"); }
            SubscribeCustomEntries();
        }

        [ObservableProperty]
        private string _title = "ModbusForge";

        [ObservableProperty]
        private string _version = "3.0.2";

        // UI-selectable mode: "Client" or "Server"
        [ObservableProperty]
        private string _mode = "Client";

        partial void OnModeChanged(string value)
        {
            try
            {
                // If connected, disconnect current service when switching modes
                if (IsConnected)
                {
                    var serviceToDisconnect = _modbusService;
                    // Fire-and-forget disconnect to avoid blocking UI
                    Task.Run(async () =>
                    {
                        try
                        {
                            await serviceToDisconnect.DisconnectAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error disconnecting previous service during mode switch");
                        }
                    });

                    IsConnected = false;
                    StatusMessage = "Disconnected";
                }

                _modbusService = IsServerMode ? _serverService : _clientService;

                // Update dependent computed properties
                OnPropertyChanged(nameof(IsServerMode));
                OnPropertyChanged(nameof(ShowClientFields));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(ConnectionHeader));
                OnPropertyChanged(nameof(AddressLabel));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error switching mode to {Mode}", value);
            }
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadRegistersCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteRegisterCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadCoilsCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCoilCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadInputRegistersCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadDiscreteInputsCommand))]
        private bool _isConnected = false;

        [ObservableProperty]
        private string _serverAddress = "127.0.0.1";

        [ObservableProperty]
        private int _port = 502;

        [ObservableProperty]
        private string _statusMessage = "Disconnected";

        // Modbus addressing defaults (kept for client mode compatibility)
        [ObservableProperty]
        private byte _unitId = 1;

        [ObservableProperty]
        private string _serverUnitId = "1";

        // Note: Register properties are now delegated to CurrentConfig.RegisterSettings
        // See the delegated properties in the section above around line 497-511

        private IRelayCommand? _disconnectCommand;
        private readonly ILogger<MainViewModel> _logger;
        private DispatcherTimer _customTimer;
        private DispatcherTimer _monitorTimer;
        private DispatcherTimer _trendTimer;
        private readonly ITrendLogger _trendLogger;
        private readonly ICustomEntryService _customEntryService;
        private bool _isMonitoring;
        private DateTime _lastHoldingReadUtc = DateTime.MinValue;
        private DateTime _lastInputRegReadUtc = DateTime.MinValue;
        private DateTime _lastCoilsReadUtc = DateTime.MinValue;
        private DateTime _lastDiscreteReadUtc = DateTime.MinValue;
        private bool _hasConnectionError = false;
        private DateTime _lastErrorTime = DateTime.MinValue;

        public ICommand ConnectCommand { get; private set; }
        public IRelayCommand DisconnectCommand { get; private set; }
        public ICommand RunDiagnosticsCommand { get; private set; }
        public IRelayCommand ReadRegistersCommand { get; private set; }
        public IRelayCommand WriteRegisterCommand { get; private set; }
        public IRelayCommand ReadCoilsCommand { get; private set; }
        public IRelayCommand WriteCoilCommand { get; private set; }
        public IRelayCommand ReadInputRegistersCommand { get; private set; }
        public IRelayCommand ReadDiscreteInputsCommand { get; private set; }
        public ICommand AddCustomEntryCommand { get; private set; }
        public ICommand WriteCustomNowCommand { get; private set; }
        public ICommand ReadCustomNowCommand { get; private set; }
        public IRelayCommand ReadAllCustomNowCommand { get; private set; }
        public ICommand SaveProjectCommand { get; private set; }
        public ICommand LoadProjectCommand { get; private set; }
        public ICommand ImportUnitIdsCommand { get; private set; }
        public ICommand ExportUnitIdsCommand { get; private set; }
        public ICommand ExportUnitIdCommand { get; private set; }
        public ICommand ImportUnitIdAsCommand { get; private set; }
        public IRelayCommand SaveCustomCommand { get; private set; }
        public IRelayCommand LoadCustomCommand { get; private set; }
        public IRelayCommand SaveAllConfigCommand { get; private set; }
        public IRelayCommand LoadAllConfigCommand { get; private set; }

        public ObservableCollection<string> ConsoleMessages => _consoleLoggerService.LogMessages;

        // Register collections (shared across all Unit IDs for display)
        public ObservableCollection<RegisterEntry> HoldingRegisters { get; } = new();
        public ObservableCollection<CoilEntry> Coils { get; } = new();
        public ObservableCollection<RegisterEntry> InputRegisters { get; } = new();
        public ObservableCollection<CoilEntry> DiscreteInputs { get; } = new();

        public IAsyncRelayCommand<DataGridCellEditEndingEventArgs> UpdateHoldingRegisterCommand { get; private set; }

        // Unit ID configurations for complete isolation
        [ObservableProperty]
        private Dictionary<byte, UnitIdConfiguration> _unitConfigurations = new();

        [ObservableProperty]
        private byte _selectedUnitId = 1;

        [ObservableProperty]
        private ObservableCollection<byte> _availableUnitIds = new ObservableCollection<byte>();

        // Current active configuration (binds to selected Unit ID)
        public UnitIdConfiguration CurrentConfig
        {
            get
            {
                if (!UnitConfigurations.ContainsKey(SelectedUnitId))
                {
                    UnitConfigurations[SelectedUnitId] = new UnitIdConfiguration(SelectedUnitId);
                }
                return UnitConfigurations[SelectedUnitId];
            }
        }

        // Helper to get the correct Unit ID based on mode
        public byte EffectiveUnitId => IsServerMode ? SelectedUnitId : UnitId;

        // Properties that now delegate to current configuration
        public ObservableCollection<CustomEntry> CustomEntries => CurrentConfig.CustomEntries;
        public bool SimulationEnabled => CurrentConfig.SimulationSettings.SimulationEnabled;
        public int SimulationPeriodMs => CurrentConfig.SimulationSettings.SimulationPeriodMs;

        // Monitoring properties that delegate to current configuration
        public bool GlobalMonitorEnabled 
        { 
            get => CurrentConfig.MonitoringSettings.GlobalMonitorEnabled; 
            set => SetGlobalMonitorEnabled(value); 
        }
        public bool HoldingMonitorEnabled 
        { 
            get => CurrentConfig.MonitoringSettings.HoldingMonitorEnabled; 
            set => SetHoldingMonitorEnabled(value); 
        }
        public int HoldingMonitorPeriodMs 
        { 
            get => CurrentConfig.MonitoringSettings.HoldingMonitorPeriodMs; 
            set => SetHoldingMonitorPeriodMs(value); 
        }
        public bool InputRegistersMonitorEnabled 
        { 
            get => CurrentConfig.MonitoringSettings.InputRegistersMonitorEnabled; 
            set => SetInputRegistersMonitorEnabled(value); 
        }
        public int InputRegistersMonitorPeriodMs 
        { 
            get => CurrentConfig.MonitoringSettings.InputRegistersMonitorPeriodMs; 
            set => SetInputRegistersMonitorPeriodMs(value); 
        }
        public bool CoilsMonitorEnabled 
        { 
            get => CurrentConfig.MonitoringSettings.CoilsMonitorEnabled; 
            set => SetCoilsMonitorEnabled(value); 
        }
        public int CoilsMonitorPeriodMs 
        { 
            get => CurrentConfig.MonitoringSettings.CoilsMonitorPeriodMs; 
            set => SetCoilsMonitorPeriodMs(value); 
        }
        public bool DiscreteInputsMonitorEnabled 
        { 
            get => CurrentConfig.MonitoringSettings.DiscreteInputsMonitorEnabled; 
            set => SetDiscreteInputsMonitorEnabled(value); 
        }
        public int DiscreteInputsMonitorPeriodMs 
        { 
            get => CurrentConfig.MonitoringSettings.DiscreteInputsMonitorPeriodMs; 
            set => SetDiscreteInputsMonitorPeriodMs(value); 
        }
        public bool CustomMonitorEnabled 
        { 
            get => CurrentConfig.MonitoringSettings.CustomMonitorEnabled; 
            set => SetCustomMonitorEnabled(value); 
        }
        public bool CustomReadMonitorEnabled 
        { 
            get => CurrentConfig.MonitoringSettings.CustomReadMonitorEnabled; 
            set => SetCustomReadMonitorEnabled(value); 
        } 
        public int RegisterStart 
        { 
            get => CurrentConfig.RegisterSettings.RegisterStart; 
            set => SetRegisterStart(value); 
        }
        public int RegisterCount 
        { 
            get => CurrentConfig.RegisterSettings.RegisterCount; 
            set => SetRegisterCount(value); 
        }
        public int WriteRegisterAddress 
        { 
            get => CurrentConfig.RegisterSettings.WriteRegisterAddress; 
            set => SetWriteRegisterAddress(value); 
        }
        public ushort WriteRegisterValue 
        { 
            get => CurrentConfig.RegisterSettings.WriteRegisterValue; 
            set => SetWriteRegisterValue(value); 
        }
        public string RegistersGlobalType 
        { 
            get => CurrentConfig.RegisterSettings.RegistersGlobalType; 
            set => SetRegistersGlobalType(value); 
        }
        public int CoilStart 
        { 
            get => CurrentConfig.RegisterSettings.CoilStart; 
            set => SetCoilStart(value); 
        }
        public int CoilCount 
        { 
            get => CurrentConfig.RegisterSettings.CoilCount; 
            set => SetCoilCount(value); 
        }
        public int WriteCoilAddress 
        { 
            get => CurrentConfig.RegisterSettings.WriteCoilAddress; 
            set => SetWriteCoilAddress(value); 
        }
        public bool WriteCoilState 
        { 
            get => CurrentConfig.RegisterSettings.WriteCoilState; 
            set => SetWriteCoilState(value); 
        }
        public int InputRegisterStart 
        { 
            get => CurrentConfig.RegisterSettings.InputRegisterStart; 
            set => SetInputRegisterStart(value); 
        }
        public int InputRegisterCount 
        { 
            get => CurrentConfig.RegisterSettings.InputRegisterCount; 
            set => SetInputRegisterCount(value); 
        }
        public string InputRegistersGlobalType 
        { 
            get => CurrentConfig.RegisterSettings.InputRegistersGlobalType; 
            set => SetInputRegistersGlobalType(value); 
        }
        public int DiscreteInputStart 
        { 
            get => CurrentConfig.RegisterSettings.DiscreteInputStart; 
            set => SetDiscreteInputStart(value); 
        }
        public int DiscreteInputCount 
        { 
            get => CurrentConfig.RegisterSettings.DiscreteInputCount; 
            set => SetDiscreteInputCount(value); 
        }

        partial void OnSelectedUnitIdChanged(byte value)
        {
            // Ensure configuration exists for the new Unit ID
            if (!UnitConfigurations.ContainsKey(value))
            {
                UnitConfigurations[value] = new UnitIdConfiguration(value);
            }
            
            // Refresh Custom entries when Unit ID changes in server mode
            if (IsServerMode && IsConnected)
            {
                _ = Task.Run(async () => await ReadAllCustomNowAsync());
            }
            
            // Notify all delegated properties that they may have changed
            OnPropertyChanged(nameof(CustomEntries));
            OnPropertyChanged(nameof(GlobalMonitorEnabled));
            OnPropertyChanged(nameof(HoldingMonitorEnabled));
            OnPropertyChanged(nameof(InputRegistersMonitorEnabled));
            OnPropertyChanged(nameof(CoilsMonitorEnabled));
            OnPropertyChanged(nameof(DiscreteInputsMonitorEnabled));
            OnPropertyChanged(nameof(CustomMonitorEnabled));
            OnPropertyChanged(nameof(CustomReadMonitorEnabled));
        }

        // Setters for delegated properties (needed for two-way binding)
        private void SetGlobalMonitorEnabled(bool value) => CurrentConfig.MonitoringSettings.GlobalMonitorEnabled = value;
        private void SetHoldingMonitorEnabled(bool value) => CurrentConfig.MonitoringSettings.HoldingMonitorEnabled = value;
        private void SetHoldingMonitorPeriodMs(int value) => CurrentConfig.MonitoringSettings.HoldingMonitorPeriodMs = value;
        private void SetInputRegistersMonitorEnabled(bool value) => CurrentConfig.MonitoringSettings.InputRegistersMonitorEnabled = value;
        private void SetInputRegistersMonitorPeriodMs(int value) => CurrentConfig.MonitoringSettings.InputRegistersMonitorPeriodMs = value;
        private void SetCoilsMonitorEnabled(bool value) => CurrentConfig.MonitoringSettings.CoilsMonitorEnabled = value;
        private void SetCoilsMonitorPeriodMs(int value) => CurrentConfig.MonitoringSettings.CoilsMonitorPeriodMs = value;
        private void SetDiscreteInputsMonitorEnabled(bool value) => CurrentConfig.MonitoringSettings.DiscreteInputsMonitorEnabled = value;
        private void SetDiscreteInputsMonitorPeriodMs(int value) => CurrentConfig.MonitoringSettings.DiscreteInputsMonitorPeriodMs = value;
        private void SetCustomMonitorEnabled(bool value) => CurrentConfig.MonitoringSettings.CustomMonitorEnabled = value;
        private void SetCustomReadMonitorEnabled(bool value) => CurrentConfig.MonitoringSettings.CustomReadMonitorEnabled = value;
        
        private void SetRegisterStart(int value) => CurrentConfig.RegisterSettings.RegisterStart = value;
        private void SetRegisterCount(int value) => CurrentConfig.RegisterSettings.RegisterCount = value;
        private void SetWriteRegisterAddress(int value) => CurrentConfig.RegisterSettings.WriteRegisterAddress = value;
        private void SetWriteRegisterValue(ushort value) => CurrentConfig.RegisterSettings.WriteRegisterValue = value;
        private void SetRegistersGlobalType(string value) => CurrentConfig.RegisterSettings.RegistersGlobalType = value;
        private void SetCoilStart(int value) => CurrentConfig.RegisterSettings.CoilStart = value;
        private void SetCoilCount(int value) => CurrentConfig.RegisterSettings.CoilCount = value;
        private void SetWriteCoilAddress(int value) => CurrentConfig.RegisterSettings.WriteCoilAddress = value;
        private void SetWriteCoilState(bool value) => CurrentConfig.RegisterSettings.WriteCoilState = value;
        private void SetInputRegisterStart(int value) => CurrentConfig.RegisterSettings.InputRegisterStart = value;
        private void SetInputRegisterCount(int value) => CurrentConfig.RegisterSettings.InputRegisterCount = value;
        private void SetInputRegistersGlobalType(string value) => CurrentConfig.RegisterSettings.InputRegistersGlobalType = value;
        private void SetDiscreteInputStart(int value) => CurrentConfig.RegisterSettings.DiscreteInputStart = value;
        private void SetDiscreteInputCount(int value) => CurrentConfig.RegisterSettings.DiscreteInputCount = value;

        private bool CanConnect() => _connectionCoordinator.CanConnect(IsConnected);

        private async Task ConnectAsync()
        {
            await _connectionCoordinator.ConnectAsync(ServerAddress, Port, IsServerMode,
                msg => StatusMessage = msg, 
                connected => 
                {
                    IsConnected = connected;
                    if (connected && IsServerMode)
                    {
                        PopulateAvailableUnitIds();
                    }
                }, 
                ServerUnitId);
        }

        private void PopulateAvailableUnitIds()
        {
            AvailableUnitIds.Clear();
            if (_serverService is ModbusServerService srv)
            {
                var unitIds = srv.GetUnitIds();
                foreach (var id in unitIds.OrderBy(x => x))
                {
                    AvailableUnitIds.Add(id);
                }
                // Set selected to first available ID if current selection isn't in the list
                if (!AvailableUnitIds.Contains(SelectedUnitId) && AvailableUnitIds.Count > 0)
                {
                    SelectedUnitId = AvailableUnitIds[0];
                }
            }
        }

        private bool CanDisconnect() => _connectionCoordinator.CanDisconnect(IsConnected);

        private async Task DisconnectAsync()
        {
            await _connectionCoordinator.DisconnectAsync(IsServerMode,
                msg => StatusMessage = msg, connected => IsConnected = connected);
        }

        private async Task RunDiagnosticsAsync()
        {
            await _connectionCoordinator.RunDiagnosticsAsync(ServerAddress, Port, EffectiveUnitId,
                msg => StatusMessage = msg);
        }

        private async Task ReadRegistersAsync()
        {
            await _registerCoordinator.ReadRegistersAsync(EffectiveUnitId, RegisterStart, RegisterCount,
                RegistersGlobalType, HoldingRegisters, msg => StatusMessage = msg,
                hasError => _hasConnectionError = hasError, HoldingMonitorEnabled, IsServerMode);
        }

        private async Task ReadInputRegistersAsync()
        {
            await _registerCoordinator.ReadInputRegistersAsync(EffectiveUnitId, InputRegisterStart, InputRegisterCount,
                InputRegistersGlobalType, InputRegisters, msg => StatusMessage = msg,
                hasError => _hasConnectionError = hasError, InputRegistersMonitorEnabled, IsServerMode);
        }

        private async Task WriteRegisterAsync()
        {
            await _registerCoordinator.WriteRegisterAsync(EffectiveUnitId, WriteRegisterAddress, WriteRegisterValue,
                msg => StatusMessage = msg, async () => await ReadRegistersAsync(), IsServerMode);
        }

        private async Task ReadCoilsAsync()
        {
            await _registerCoordinator.ReadCoilsAsync(EffectiveUnitId, CoilStart, CoilCount,
                Coils, msg => StatusMessage = msg,
                hasError => _hasConnectionError = hasError, CoilsMonitorEnabled, IsServerMode);
        }

        private async Task ReadDiscreteInputsAsync()
        {
            await _registerCoordinator.ReadDiscreteInputsAsync(EffectiveUnitId, DiscreteInputStart, DiscreteInputCount,
                DiscreteInputs, msg => StatusMessage = msg,
                hasError => _hasConnectionError = hasError, DiscreteInputsMonitorEnabled, IsServerMode);
        }

        private async Task WriteCoilAsync()
        {
            await _registerCoordinator.WriteCoilAsync(EffectiveUnitId, WriteCoilAddress, WriteCoilState,
                msg => StatusMessage = msg, async () => await ReadCoilsAsync(), IsServerMode);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Helper methods for inline editing from the view
        public async Task WriteRegisterAtAsync(int address, ushort value)
        {
            await _registerCoordinator.WriteRegisterAtAsync(EffectiveUnitId, address, value, IsServerMode);
        }

        public async Task WriteFloatAtAsync(int address, float value)
        {
            await _registerCoordinator.WriteFloatAtAsync(EffectiveUnitId, address, value, IsServerMode);
        }

        public async Task WriteStringAtAsync(int address, string text)
        {
            await _registerCoordinator.WriteStringAtAsync(EffectiveUnitId, address, text, IsServerMode);
        }

        public async Task WriteCoilAtAsync(int address, bool state)
        {
            await _registerCoordinator.WriteCoilAtAsync(EffectiveUnitId, address, state, IsServerMode);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _customTimer.Stop();
                        _customTimer.Tick -= CustomTimer_Tick;
                        _monitorTimer.Stop();
                        _monitorTimer.Tick -= MonitorTimer_Tick;
                        _trendTimer.Stop();
                        _trendTimer.Tick -= TrendTimer_Tick;
                        try { _trendLogger.Stop(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to stop trend logger"); }
                        try
                        {
                            CustomEntries.CollectionChanged -= CustomEntries_CollectionChanged;
                            foreach (var ce in CustomEntries)
                            {
                                ce.PropertyChanged -= CustomEntry_PropertyChanged;
                            }
                        }
                        catch (Exception ex) { _logger.LogDebug(ex, "Error detaching event handlers during disposal"); }
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Error during timer cleanup in Dispose"); }
                    try
                    {
                        // Attempt a graceful disconnect with timeout to avoid freezing
                        if (_modbusService != null && IsConnected)
                        {
                            var disconnectTask = _modbusService.DisconnectAsync();
                            if (!disconnectTask.Wait(TimeSpan.FromSeconds(2)))
                            {
                                _logger.LogWarning("Disconnect timed out during disposal");
                            }
                        }
                        IsConnected = false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during disconnect in Dispose");
                    }
                    _modbusService?.Dispose();
                }
                _disposed = true;
            }
        }


        ~MainViewModel()
        {
            Dispose(false);
        }

        // propagate global type selection to each row
        partial void OnRegistersGlobalTypeChanged(string value)
        {
            foreach (var r in HoldingRegisters)
            {
                r.Type = value;
            }
        }

        partial void OnInputRegistersGlobalTypeChanged(string value)
        {
            foreach (var r in InputRegisters)
            {
                r.Type = value;
            }
        }

        // Allow full byte range 0..255 for Unit ID (some devices like Micro850 require 0 or 255)
        partial void OnUnitIdChanged(byte value)
        {
            // No clamping - allow any valid byte value (0-255)
            _logger.LogDebug("UnitId changed to {Value}.", value);
        }

    }

    // Extensions to support the Custom tab logic within the ViewModel partial class
    public partial class MainViewModel
    {
        private void AddCustomEntry()
        {
            _customEntryCoordinator.AddCustomEntry(CustomEntries);
        }

        private async Task WriteCustomNowAsync(CustomEntry entry)
        {
            await _customEntryCoordinator.WriteCustomNowAsync(entry, EffectiveUnitId, msg => StatusMessage = msg, IsServerMode);
        }

        /// <summary>
        /// Writes a holding register value based on the entry's data type.
        /// </summary>
        private async Task WriteHoldingRegisterByTypeAsync(CustomEntry entry)
        {
            var type = (entry.Type ?? "uint").ToLowerInvariant();
            switch (type)
            {
                case "real":
                    await WriteRealValueAsync(entry);
                    break;
                case "string":
                    await WriteStringAtAsync(entry.Address, entry.Value ?? string.Empty);
                    StatusMessage = $"Wrote STRING '{entry.Value}' at {entry.Address}";
                    break;
                case "int":
                    await WriteIntValueAsync(entry);
                    break;
                default: // uint
                    await WriteUIntValueAsync(entry);
                    break;
            }
        }

        /// <summary>
        /// Writes a float (REAL) value to holding registers.
        /// </summary>
        private async Task WriteRealValueAsync(CustomEntry entry)
        {
            if (!float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                if (!float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                {
                    StatusMessage = $"Invalid float: {entry.Value}";
                    return;
                }
            }
            await WriteFloatAtAsync(entry.Address, f);
            StatusMessage = $"Wrote REAL {f} at {entry.Address}";
        }

        /// <summary>
        /// Writes a signed int value to a holding register.
        /// </summary>
        private async Task WriteIntValueAsync(CustomEntry entry)
        {
            if (int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
            {
                await WriteRegisterAtAsync(entry.Address, unchecked((ushort)iv));
                StatusMessage = $"Wrote INT {iv} at {entry.Address}";
            }
            else
            {
                StatusMessage = $"Invalid int: {entry.Value}";
            }
        }

        /// <summary>
        /// Writes an unsigned int value to a holding register.
        /// </summary>
        private async Task WriteUIntValueAsync(CustomEntry entry)
        {
            if (uint.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uv))
            {
                if (uv > 0xFFFF) uv = 0xFFFF;
                await WriteRegisterAtAsync(entry.Address, (ushort)uv);
                StatusMessage = $"Wrote UINT {uv} at {entry.Address}";
            }
            else
            {
                StatusMessage = $"Invalid uint: {entry.Value}";
            }
        }

        /// <summary>
        /// Writes a coil (boolean) value.
        /// </summary>
        private async Task WriteCoilAsync(CustomEntry entry)
        {
            if (TryParseBool(entry.Value, out bool b))
            {
                await WriteCoilAtAsync(entry.Address, b);
                StatusMessage = $"Wrote COIL {(b ? 1 : 0)} at {entry.Address}";
            }
            else
            {
                StatusMessage = $"Invalid coil value: {entry.Value}. Use true/false or 1/0.";
            }
        }

        private async Task ReadCustomNowAsync(CustomEntry entry)
        {
            if (entry is null) return;
            try
            {
                var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
                switch (area)
                {
                    case "holdingregister":
                        await ReadHoldingRegisterByTypeAsync(entry);
                        break;
                    case "inputregister":
                        await ReadInputRegisterByTypeAsync(entry);
                        break;
                    case "coil":
                        await ReadCoilAsync(entry);
                        break;
                    case "discreteinput":
                        await ReadDiscreteInputAsync(entry);
                        break;
                    default:
                        StatusMessage = $"Unknown area: {entry.Area}";
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading custom entry");
                StatusMessage = $"Custom read error: {ex.Message}";
            }
        }

        /// <summary>
        /// Reads a holding register value based on the entry's data type.
        /// </summary>
        private async Task ReadHoldingRegisterByTypeAsync(CustomEntry entry)
        {
            await ReadRegisterGenericAsync(entry, _modbusService.ReadHoldingRegistersAsync, "HR");
        }

        /// <summary>
        /// Reads an input register value based on the entry's data type.
        /// </summary>
        private async Task ReadInputRegisterByTypeAsync(CustomEntry entry)
        {
            await ReadRegisterGenericAsync(entry, _modbusService.ReadInputRegistersAsync, "IR");
        }

        private async Task ReadRegisterGenericAsync(CustomEntry entry, Func<byte, int, int, Task<ushort[]?>> readFunc, string logPrefix)
        {
            var type = (entry.Type ?? "uint").ToLowerInvariant();
            var address = entry.Address;
            
            switch (type)
            {
                case "real":
                    var regsReal = await readFunc(UnitId, address, 2);
                    if (regsReal is null) return;
                    entry.Value = DataTypeConverter.ToSingle(regsReal[0], regsReal[1]).ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read REAL {entry.Value} from {logPrefix} {address}";
                    break;
                case "int":
                    var regsInt = await readFunc(UnitId, address, 1);
                    if (regsInt is null) return;
                    entry.Value = unchecked((short)regsInt[0]).ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read INT {entry.Value} from {logPrefix} {address}";
                    break;
                case "string":
                    var regsString = await readFunc(UnitId, address, 1);
                    if (regsString is null) return;
                    entry.Value = DataTypeConverter.ToString(regsString[0]);
                    StatusMessage = $"Read STRING '{entry.Value}' from {logPrefix} {address}";
                    break;
                default: // uint
                    var regsUInt = await readFunc(UnitId, address, 1);
                    if (regsUInt is null) return;
                    entry.Value = regsUInt[0].ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read UINT {entry.Value} from {logPrefix} {address}";
                    break;
            }
        }

        /// <summary>
        /// Reads a coil (boolean) value.
        /// </summary>
        private async Task ReadCoilAsync(CustomEntry entry)
        {
            var states = await _modbusService.ReadCoilsAsync(UnitId, entry.Address, 1);
            if (states is null) return;
            entry.Value = states[0] ? "1" : "0";
            StatusMessage = $"Read COIL {entry.Value} from {entry.Address}";
        }

        /// <summary>
        /// Reads a discrete input (boolean) value.
        /// </summary>
        private async Task ReadDiscreteInputAsync(CustomEntry entry)
        {
            var states = await _modbusService.ReadDiscreteInputsAsync(UnitId, entry.Address, 1);
            if (states is null) return;
            entry.Value = states[0] ? "1" : "0";
            StatusMessage = $"Read DI {entry.Value} from {entry.Address}";
        }

        private static bool TryParseBool(string? value, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            if (bool.TryParse(v, out result)) return true;
            if (v == "1") { result = true; return true; }
            if (v == "0") { result = false; return true; }
            return false;
        }

        private async void CustomTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsConnected) return;
            var now = DateTime.UtcNow;
            var snapshot = CustomEntries.ToList();
            foreach (var entry in snapshot)
            {
                if (!entry.Continuous) continue;
                int period = entry.PeriodMs <= 0 ? 1000 : entry.PeriodMs;
                if ((now - entry._lastWriteUtc).TotalMilliseconds >= period)
                {
                    try
                    {
                        await WriteCustomNowAsync(entry);
                        entry._lastWriteUtc = now;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Continuous write failed for {Area} {Address}", entry.Area, entry.Address);
                        entry.Continuous = false;
                        MessageBox.Show($"Continuous write failed for {entry.Area} {entry.Address}: {ex.Message}\n\nContinuous write has been paused for this entry. Fix the issue and re-enable if needed.", "Write Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            if (_isMonitoring) return;
            if (!IsConnected) return;

            if (_hasConnectionError && (DateTime.UtcNow - _lastErrorTime).TotalSeconds < 5)
            {
                return;
            }

            _isMonitoring = true;
            try
            {
                var now = DateTime.UtcNow;

                // Simple heartbeat: try a minimal read to verify connection is alive
                // Only do this if no monitoring is active
                if (!HoldingMonitorEnabled && !InputRegistersMonitorEnabled && 
                    !CoilsMonitorEnabled && !DiscreteInputsMonitorEnabled)
                {
                    try
                    {
                        // Quick connectivity check - read 1 register
                        var heartbeat = await _modbusService.ReadHoldingRegistersAsync(UnitId, 1, 1);
                        if (heartbeat == null)
                        {
                            // Connection lost
                            _logger.LogWarning("Heartbeat check failed - connection lost");
                            await _modbusService.DisconnectAsync();
                            IsConnected = false;
                            MessageBox.Show("Connection to server lost.\n\nPlease reconnect when the server is available.", 
                                "Connection Lost", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Heartbeat check failed");
                        await _modbusService.DisconnectAsync();
                        IsConnected = false;
                        MessageBox.Show("Connection to server lost.\n\nPlease reconnect when the server is available.", 
                            "Connection Lost", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (HoldingMonitorEnabled)
                {
                    int p = HoldingMonitorPeriodMs <= 0 ? 1000 : HoldingMonitorPeriodMs;
                    if ((now - _lastHoldingReadUtc).TotalMilliseconds >= p)
                    {
                        await ReadRegistersAsync();
                        _lastHoldingReadUtc = now;
                    }
                }

                if (InputRegistersMonitorEnabled)
                {
                    int p = InputRegistersMonitorPeriodMs <= 0 ? 1000 : InputRegistersMonitorPeriodMs;
                    if ((now - _lastInputRegReadUtc).TotalMilliseconds >= p)
                    {
                        await ReadInputRegistersAsync();
                        _lastInputRegReadUtc = now;
                    }
                }

                if (CoilsMonitorEnabled)
                {
                    int p = CoilsMonitorPeriodMs <= 0 ? 1000 : CoilsMonitorPeriodMs;
                    if ((now - _lastCoilsReadUtc).TotalMilliseconds >= p)
                    {
                        await ReadCoilsAsync();
                        _lastCoilsReadUtc = now;
                    }
                }

                if (DiscreteInputsMonitorEnabled)
                {
                    int p = DiscreteInputsMonitorPeriodMs <= 0 ? 1000 : DiscreteInputsMonitorPeriodMs;
                    if ((now - _lastDiscreteReadUtc).TotalMilliseconds >= p)
                    {
                        await ReadDiscreteInputsAsync();
                        _lastDiscreteReadUtc = now;
                    }
                }
                // Custom tab: per-row continuous READs are disabled.
                // Continuous reads for Custom entries are handled exclusively by TrendTimer_Tick
                // for rows where ce.Trend == true, gated by GlobalMonitorEnabled.
            }
            finally
            {
                _isMonitoring = false;
            }
        }

        private void SubscribeCustomEntries()
        {
            try
            {
                // Detach first to avoid duplicate subscriptions if called multiple times
                CustomEntries.CollectionChanged -= CustomEntries_CollectionChanged;
                foreach (var ce in CustomEntries)
                {
                    ce.PropertyChanged -= CustomEntry_PropertyChanged;
                }

                CustomEntries.CollectionChanged += CustomEntries_CollectionChanged;
                foreach (var ce in CustomEntries)
                {
                    ce.PropertyChanged += CustomEntry_PropertyChanged;
                }

                // Ensure currently existing Trend selections are registered
                foreach (var ce in CustomEntries)
                {
                    if (ce.Trend)
                    {
                        _trendLogger.Add(GetTrendKey(ce), GetTrendDisplayName(ce));
                    }
                }
            }
            catch { }
        }

        private void CustomEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var obj in e.NewItems)
                {
                    if (obj is CustomEntry ce)
                    {
                        ce.PropertyChanged += CustomEntry_PropertyChanged;
                        if (ce.Trend) _trendLogger.Add(GetTrendKey(ce), GetTrendDisplayName(ce));
                    }
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (var obj in e.OldItems)
                {
                    if (obj is CustomEntry ce)
                    {
                        ce.PropertyChanged -= CustomEntry_PropertyChanged;
                        if (ce.Trend) _trendLogger.Remove(GetTrendKey(ce));
                    }
                }
            }
        }

        private void CustomEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not CustomEntry ce) return;
            if (string.Equals(e.PropertyName, nameof(CustomEntry.Trend), StringComparison.Ordinal))
            {
                var key = GetTrendKey(ce);
                if (ce.Trend)
                {
                    _trendLogger.Add(key, GetTrendDisplayName(ce));
                }
                else
                {
                    _trendLogger.Remove(key);
                }
            }
        }

        private static string GetTrendKey(CustomEntry ce) => $"{(ce.Area ?? "HoldingRegister")}:{ce.Address}";
        private static string GetTrendDisplayName(CustomEntry ce)
        {
            var name = (ce.Name ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(name)) return name;
            return $"{(ce.Area ?? "HR")} {ce.Address} ({ce.Type})";
        }

        private async void TrendTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsConnected) return;
            if (!GlobalMonitorEnabled) return;

            var trendEntries = CustomEntries.Where(c => c.Trend);
            await _trendCoordinator.ProcessTrendSamplingAsync(
                trendEntries,
                UnitId,
                IsServerMode,
                enabled => SetGlobalMonitorEnabled(enabled));
        }


        private async Task SaveCustomAsync()
        {
            await _customEntryCoordinator.SaveCustomAsync(CustomEntries, msg => StatusMessage = msg);
        }

        private async Task LoadCustomAsync()
        {
            await _customEntryCoordinator.LoadCustomAsync(CustomEntries, msg => StatusMessage = msg);
            SubscribeCustomEntries();
        }

        private async Task SaveAllConfigAsync()
        {
            await _configurationCoordinator.SaveAllConfigAsync(
                Mode, ServerAddress, Port, UnitId, CustomEntries,
                _visualNodeEditorViewModel.Nodes,
                _visualNodeEditorViewModel.Connections,
                msg => StatusMessage = msg);
        }

        private async Task LoadAllConfigAsync()
        {
            var config = await _configurationCoordinator.LoadAllConfigAsync(msg => StatusMessage = msg);
            if (config != null)
            {
                _configurationCoordinator.ApplyConfiguration(
                    config,
                    m => Mode = m,
                    addr => ServerAddress = addr,
                    p => Port = p,
                    u => UnitId = u,
                    CustomEntries,
                    _visualNodeEditorViewModel.Nodes,
                    _visualNodeEditorViewModel.Connections,
                    SubscribeCustomEntries);
            }
        }

        private string GenerateAutoFileName()
        {
            try
            {
                var ipAddress = IsServerMode ? "Server" : SanitizeIpAddress(ServerAddress);
                var unitId = IsServerMode ? SelectedUnitId : UnitId;
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                return $"MBIP{ipAddress}_ID{unitId}_{timestamp}";
            }
            catch
            {
                // Fallback to simple timestamp if IP address processing fails
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var unitId = IsServerMode ? SelectedUnitId : UnitId;
                return $"ModbusForge_ID{unitId}_{timestamp}";
            }
        }

        private string SanitizeIpAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return "Unknown";

            // Remove invalid characters and replace dots with zeros for filename compatibility
            var sanitized = ipAddress.Replace(".", "000");
            
            // Remove any remaining invalid filename characters
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }
            
            // Ensure it doesn't start with a number (for filename compatibility)
            if (char.IsDigit(sanitized[0]))
            {
                sanitized = "IP" + sanitized;
            }
            
            return sanitized;
        }

        private async Task SaveProjectAsync()
        {
            try
            {
                var defaultFileName = GenerateAutoFileName();
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*",
                    DefaultExt = "mfp",
                    Title = IsServerMode ? "Save Server Project" : "Save Client Project",
                    FileName = defaultFileName
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    ProjectConfiguration projectConfig;

                    if (IsServerMode)
                    {
                        // Server mode: Save all Unit ID configurations
                        projectConfig = new ProjectConfiguration
                        {
                            ProjectInfo = new ProjectInfo
                            {
                                Name = System.IO.Path.GetFileNameWithoutExtension(saveFileDialog.FileName),
                                Modified = DateTime.Now
                            },
                            GlobalSettings = new GlobalSettings
                            {
                                Mode = Mode,
                                ServerAddress = ServerAddress,
                                Port = Port,
                                ServerUnitId = ServerUnitId,
                                ClientUnitId = UnitId
                            },
                            UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>(UnitConfigurations),
                            // Save visual simulation data
                            VisualNodes = new List<VisualNode>(_visualNodeEditorViewModel.Nodes),
                            VisualConnections = new List<NodeConnection>(_visualNodeEditorViewModel.Connections)
                        };
                    }
                    else
                    {
                        // Client mode: Save only single client configuration
                        projectConfig = new ProjectConfiguration
                        {
                            ProjectInfo = new ProjectInfo
                            {
                                Name = System.IO.Path.GetFileNameWithoutExtension(saveFileDialog.FileName),
                                Modified = DateTime.Now
                            },
                            GlobalSettings = new GlobalSettings
                            {
                                Mode = Mode,
                                ServerAddress = ServerAddress,
                                Port = Port,
                                ServerUnitId = ServerUnitId,
                                ClientUnitId = UnitId
                            },
                            UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>
                            {
                                [UnitId] = CurrentConfig.Clone()
                            },
                            // Save visual simulation data
                            VisualNodes = new List<VisualNode>(_visualNodeEditorViewModel.Nodes),
                            VisualConnections = new List<NodeConnection>(_visualNodeEditorViewModel.Connections)
                        };
                    }

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    var json = JsonSerializer.Serialize(projectConfig, options);
                    await File.WriteAllTextAsync(saveFileDialog.FileName, json);
                    StatusMessage = $"{(IsServerMode ? "Server" : "Client")} project saved to {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving project");
                StatusMessage = $"Error saving project: {ex.Message}";
            }
        }

        private async Task LoadProjectAsync()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*",
                    Title = "Load ModbusForge Project"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var fileInfo = new FileInfo(openFileDialog.FileName);
                    if (fileInfo.Length > MaxProjectFileSize)
                    {
                        MessageBox.Show($"The selected project file is too large (max {MaxProjectFileSize / 1024 / 1024}MB).", "File Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    ProjectConfiguration? projectConfig;
                    using (var stream = File.OpenRead(openFileDialog.FileName))
                    {
                        projectConfig = await JsonSerializer.DeserializeAsync<ProjectConfiguration>(stream, options);
                    }

                    if (projectConfig != null)
                    {
                        // Apply global settings
                        Mode = projectConfig.GlobalSettings.Mode;
                        ServerAddress = projectConfig.GlobalSettings.ServerAddress;
                        Port = projectConfig.GlobalSettings.Port;
                        ServerUnitId = projectConfig.GlobalSettings.ServerUnitId;
                        UnitId = projectConfig.GlobalSettings.ClientUnitId;

                        // Apply Unit ID configurations
                        UnitConfigurations.Clear();
                        foreach (var kvp in projectConfig.UnitConfigurations)
                        {
                            UnitConfigurations[kvp.Key] = kvp.Value.Clone();
                        }

                        // Restore visual simulation data
                        _visualNodeEditorViewModel.Nodes.Clear();
                        _visualNodeEditorViewModel.Connections.Clear();
                        
                        if (projectConfig.VisualNodes != null)
                        {
                            foreach (var node in projectConfig.VisualNodes)
                            {
                                // Fix old nodes with invalid addresses (migration)
                                MigrateOldNodeAddresses(node);
                                _visualNodeEditorViewModel.Nodes.Add(node);
                            }
                        }
                        
                        if (projectConfig.VisualConnections != null)
                        {
                            foreach (var connection in projectConfig.VisualConnections)
                            {
                                _visualNodeEditorViewModel.Connections.Add(connection);
                            }
                        }

                        // Ensure we have a configuration for the selected Unit ID
                        if (!UnitConfigurations.ContainsKey(SelectedUnitId))
                        {
                            SelectedUnitId = UnitConfigurations.Keys.First();
                        }

                        // Refresh UI
                        OnPropertyChanged(nameof(CustomEntries));
                        OnPropertyChanged(nameof(SimulationEnabled));
                        OnPropertyChanged(nameof(GlobalMonitorEnabled));
                        OnPropertyChanged(nameof(HoldingMonitorEnabled));
                        OnPropertyChanged(nameof(InputRegistersMonitorEnabled));
                        OnPropertyChanged(nameof(CoilsMonitorEnabled));
                        OnPropertyChanged(nameof(DiscreteInputsMonitorEnabled));
                        OnPropertyChanged(nameof(CustomMonitorEnabled));
                        OnPropertyChanged(nameof(CustomReadMonitorEnabled));

                        StatusMessage = $"Project loaded: {System.IO.Path.GetFileName(openFileDialog.FileName)}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading project");
                StatusMessage = $"Error loading project: {ex.Message}";
            }
        }

        private async Task ImportUnitIdsAsync()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*",
                    Title = "Import Unit ID Configurations"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var fileInfo = new FileInfo(openFileDialog.FileName);
                    if (fileInfo.Length > MaxProjectFileSize)
                    {
                        MessageBox.Show($"The selected file is too large (max {MaxProjectFileSize / 1024 / 1024}MB).", "File Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    ProjectConfiguration? projectConfig;
                    using (var stream = File.OpenRead(openFileDialog.FileName))
                    {
                        projectConfig = await JsonSerializer.DeserializeAsync<ProjectConfiguration>(stream, options);
                    }

                    if (projectConfig?.UnitConfigurations != null)
                    {
                        var importedCount = 0;
                        foreach (var kvp in projectConfig.UnitConfigurations)
                        {
                            // Import only Unit IDs that don't already exist
                            if (!UnitConfigurations.ContainsKey(kvp.Key))
                            {
                                UnitConfigurations[kvp.Key] = kvp.Value.Clone();
                                importedCount++;
                            }
                        }

                        // Refresh AvailableUnitIds if in server mode
                        if (IsServerMode)
                        {
                            PopulateAvailableUnitIds();
                        }

                        StatusMessage = $"Imported {importedCount} new Unit ID configurations";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing Unit IDs");
                StatusMessage = $"Error importing Unit IDs: {ex.Message}";
            }
        }

        private async Task ExportUnitIdsAsync()
        {
            try
            {
                var defaultFileName = GenerateAutoFileName() + "_AllUnitIDs";
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*",
                    DefaultExt = "mfp",
                    Title = "Export Unit ID Configurations",
                    FileName = defaultFileName
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var projectConfig = new ProjectConfiguration
                    {
                        ProjectInfo = new ProjectInfo
                        {
                            Name = $"Exported Unit IDs - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                            Modified = DateTime.Now
                        },
                        GlobalSettings = new GlobalSettings
                        {
                            Mode = Mode,
                            ServerAddress = ServerAddress,
                            Port = Port,
                            ServerUnitId = ServerUnitId,
                            ClientUnitId = UnitId
                        },
                        UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>()
                    };

                    // Export all Unit ID configurations
                    foreach (var kvp in UnitConfigurations)
                    {
                        projectConfig.UnitConfigurations[kvp.Key] = kvp.Value.Clone();
                    }

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    var json = JsonSerializer.Serialize(projectConfig, options);
                    await File.WriteAllTextAsync(saveFileDialog.FileName, json);
                    StatusMessage = $"Exported {UnitConfigurations.Count} Unit ID configurations";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting Unit IDs");
                StatusMessage = $"Error exporting Unit IDs: {ex.Message}";
            }
        }

        private async Task ExportUnitIdAsync()
        {
            try
            {
                if (!IsServerMode)
                {
                    MessageBox.Show("Export Unit ID is only available in Server mode.", "Export Unit ID", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var defaultFileName = GenerateAutoFileName() + $"_ID{SelectedUnitId}";
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "ModbusForge Unit ID (*.mui)|*.mui|All Files (*.*)|*.*",
                    DefaultExt = "mui",
                    Title = $"Export Unit ID {SelectedUnitId}",
                    FileName = defaultFileName
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var unitConfig = CurrentConfig.Clone();
                    
                    var projectConfig = new ProjectConfiguration
                    {
                        ProjectInfo = new ProjectInfo
                        {
                            Name = $"Unit ID {SelectedUnitId} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                            Modified = DateTime.Now
                        },
                        GlobalSettings = new GlobalSettings
                        {
                            Mode = Mode,
                            ServerAddress = ServerAddress,
                            Port = Port,
                            ServerUnitId = ServerUnitId,
                            ClientUnitId = UnitId
                        },
                        UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>
                        {
                            [SelectedUnitId] = unitConfig
                        }
                    };

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    var json = JsonSerializer.Serialize(projectConfig, options);
                    await File.WriteAllTextAsync(saveFileDialog.FileName, json);
                    StatusMessage = $"Unit ID {SelectedUnitId} exported to {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting Unit ID");
                StatusMessage = $"Error exporting Unit ID: {ex.Message}";
            }
        }

        private void MigrateOldNodeAddresses(VisualNode node)
        {
            // Fix InputInt nodes with missing OutputAddress
            if (node.ElementType == PlcElementType.InputInt && node.OutputAddress == null)
            {
                node.OutputAddress = new PlcAddressReference 
                { 
                    Area = PlcArea.HoldingRegister, 
                    Address = node.Input1Address?.Address ?? 1 
                };
            }
            
            // Fix InputBool nodes with missing OutputAddress
            if (node.ElementType == PlcElementType.InputBool && node.OutputAddress == null)
            {
                node.OutputAddress = new PlcAddressReference 
                { 
                    Area = PlcArea.Coil, 
                    Address = node.Input1Address?.Address ?? 1 
                };
            }
            
            // Fix any nodes with Coil:0 addresses (invalid)
            if (node.OutputAddress?.Area == PlcArea.Coil && node.OutputAddress.Address == 0)
            {
                node.OutputAddress.Address = 1;
            }
            
            if (node.Input1Address?.Area == PlcArea.Coil && node.Input1Address.Address == 0)
            {
                node.Input1Address.Address = 1;
            }
            
            if (node.Input2Address?.Area == PlcArea.Coil && node.Input2Address.Address == 0)
            {
                node.Input2Address.Address = 1;
            }
        }

        private async Task ImportUnitIdAsAsync()
        {
            try
            {
                if (!IsServerMode)
                {
                    MessageBox.Show("Import Unit ID As is only available in Server mode.", "Import Unit ID As", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var openFileDialog = new OpenFileDialog
                {
                    Filter = "ModbusForge Unit ID (*.mui)|*.mui|ModbusForge Project (*.mfp)|*.mfp|All Files (*.*)|*.*",
                    Title = "Import Unit ID Configuration"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var fileInfo = new FileInfo(openFileDialog.FileName);
                    if (fileInfo.Length > MaxProjectFileSize)
                    {
                        MessageBox.Show($"The selected file is too large (max {MaxProjectFileSize / 1024 / 1024}MB).", "File Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    ProjectConfiguration? projectConfig;
                    using (var stream = File.OpenRead(openFileDialog.FileName))
                    {
                        projectConfig = await JsonSerializer.DeserializeAsync<ProjectConfiguration>(stream, options);
                    }

                    if (projectConfig?.UnitConfigurations != null && projectConfig.UnitConfigurations.Count > 0)
                    {
                        // Get the first Unit ID from the imported file
                        var importedUnitId = projectConfig.UnitConfigurations.Keys.First();
                        var importedConfig = projectConfig.UnitConfigurations[importedUnitId];

                        // Ask user for target Unit ID
                        var dialog = new InputDialog("Import Unit ID As", $"Enter target Unit ID (1-247) to import Unit ID {importedUnitId} as:", "1");
                        if (dialog.ShowDialog() == true)
                        {
                            if (byte.TryParse(dialog.InputText, out byte targetUnitId) && targetUnitId >= 1 && targetUnitId <= 247)
                            {
                                // Clone the imported configuration and change its Unit ID
                                var newConfig = importedConfig.Clone();
                                // Note: We would need to add a method to change the Unit ID in the configuration
                                // For now, we'll store it under the target Unit ID key

                                UnitConfigurations[targetUnitId] = newConfig;
                                SelectedUnitId = targetUnitId;

                                // Refresh AvailableUnitIds
                                PopulateAvailableUnitIds();

                                StatusMessage = $"Unit ID {importedUnitId} imported as Unit ID {targetUnitId}";
                            }
                            else
                            {
                                MessageBox.Show("Invalid Unit ID. Please enter a value between 1 and 247.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("No Unit ID configurations found in the selected file.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing Unit ID");
                StatusMessage = $"Error importing Unit ID: {ex.Message}";
            }
        }

        private async Task UpdateHoldingRegister(DataGridCellEditEndingEventArgs? e)
        {
            if (e is null || e.EditAction != DataGridEditAction.Commit)
                return;

            if (e.Row?.Item is RegisterEntry entry)
            {
                try
                {
                    string? editedText = (e.EditingElement as TextBox)?.Text;
                    string type = entry.Type?.ToLowerInvariant() ?? "uint";

                    if (!string.IsNullOrWhiteSpace(editedText))
                    {
                        var text = editedText.Trim().Replace(',', '.');
                        bool looksLikeFloat = text.Contains('.') || text.Contains("e", StringComparison.OrdinalIgnoreCase);

                        switch (type)
                        {
                            case "real":
                                {
                                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                                    {
                                        await WriteFloatAtAsync(entry.Address, f);
                                        e.Cancel = true;
                                        ReadRegistersCommand.Execute(null);
                                    }
                                    else
                                    {
                                        MessageBox.Show($"Invalid float value: '{editedText}'", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                        e.Cancel = true;
                                    }
                                    break;
                                }
                            case "string":
                                {
                                    await WriteStringAtAsync(entry.Address, editedText);
                                    e.Cancel = true;
                                    ReadRegistersCommand.Execute(null);
                                    break;
                                }
                            case "int":
                                {
                                    if (int.TryParse(text, out int iv))
                                    {
                                        ushort raw = unchecked((ushort)iv);
                                        await WriteRegisterAtAsync(entry.Address, raw);
                                        e.Cancel = true;
                                        ReadRegistersCommand.Execute(null);
                                    }
                                    else
                                    {
                                        MessageBox.Show($"Invalid integer value: '{editedText}'", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                        e.Cancel = true;
                                    }
                                    break;
                                }
                            default:
                                {
                                    if (looksLikeFloat && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                                    {
                                        await WriteFloatAtAsync(entry.Address, f);
                                        e.Cancel = true;
                                        ReadRegistersCommand.Execute(null);
                                        break;
                                    }
                                    if (uint.TryParse(text, out uint uv) && uv <= ushort.MaxValue)
                                    {
                                        ushort val = (ushort)uv;
                                        await WriteRegisterAtAsync(entry.Address, val);
                                        entry.Value = val;
                                        e.Cancel = true;
                                    }
                                    else
                                    {
                                        MessageBox.Show($"Invalid unsigned value: '{editedText}' (0..65535)", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                        e.Cancel = true;
                                    }
                                    break;
                                }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to write register {entry.Address}: {ex.Message}", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Cancel = true;
                }
            }
        }

    }
}