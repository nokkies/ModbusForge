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
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using System.Collections.ObjectModel;

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

namespace ModbusForge.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IDisposable, IMonitoringCallbacks
    {
        // Partial method declarations for delegated properties (required by CommunityToolkit.Mvvm)
        partial void OnRegistersGlobalTypeChanged(string value);
        partial void OnInputRegistersGlobalTypeChanged(string value);

        // Initialized in InitializeServiceState called from constructor
        private IModbusService _modbusService = null!;
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
        private readonly IConsoleLoggerService _consoleLoggerService;
        private readonly ConnectionCoordinator _connectionCoordinator;
        private readonly RegisterCoordinator _registerCoordinator;
        private readonly CustomEntryCoordinator _customEntryCoordinator;
        private readonly TrendCoordinator _trendCoordinator;
        private readonly ConfigurationCoordinator _configurationCoordinator;
        private readonly IDialogService _dialogService;
        private readonly IDispatcher _dispatcher;
        private readonly VisualNodeEditorViewModel _visualNodeEditorViewModel;
        private bool _disposed = false;
        // Mode-aware UI helpers

        public bool IsServerMode => string.Equals(Mode, "Server", StringComparison.OrdinalIgnoreCase);
        public bool ShowClientFields => !IsServerMode; // show IP/UnitId only in client mode
        public string ConnectButtonText => IsServerMode ? "Start Server" : "Connect";
        public string ConnectionHeader => IsServerMode ? "Modbus Connection (Server)" : "Modbus Connection (Client)";
        public string AddressLabel => IsServerMode ? "Interface:" : "Server:";

        private async Task ReadAllCustomNowAsync()
        {
            if (!IsConnected) return;
            var snapshot = CustomEntries.ToList();
            await _customEntryCoordinator.ReadCustomEntriesAsync(snapshot, EffectiveUnitId, msg => StatusMessage = msg, IsServerMode);
        }

        public VisualNodeEditorViewModel VisualNodeEditorViewModel => _visualNodeEditorViewModel;

        public MainViewModel(ModbusTcpService clientService, ModbusServerService serverService, ILogger<MainViewModel> logger, IOptions<ServerSettings> options, ITrendLogger trendLogger, ICustomEntryService customEntryService, IConsoleLoggerService consoleLoggerService, ConnectionCoordinator connectionCoordinator, RegisterCoordinator registerCoordinator, CustomEntryCoordinator customEntryCoordinator, TrendCoordinator trendCoordinator, ConfigurationCoordinator configurationCoordinator, MonitoringCoordinator monitoringCoordinator, IDialogService? dialogService = null, VisualNodeEditorViewModel? visualNodeEditorViewModel = null, IDispatcher? dispatcher = null)
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
            _monitoringCoordinator = monitoringCoordinator ?? throw new ArgumentNullException(nameof(monitoringCoordinator));
            _dialogService = dialogService ?? new NullDialogService();
            _dispatcher = dispatcher ?? new WpfDispatcher();
            // Initialize visual node editor
            _visualNodeEditorViewModel = visualNodeEditorViewModel ?? new VisualNodeEditorViewModel();
            // VisualSimulationService will be started/stopped by ShowLiveValues toggle

            var settings = options?.Value ?? new ServerSettings();

            // Initialize in logical order
            InitializeMode(settings);
            InitializeDefaultsFromConfig(settings);
            InitializeCommands();
            InitializeServiceState();
            InitializeWindowTitle();
            InitializeMonitoringServices();
            _monitoringCoordinator.Start();

            // Initialize collection views for filtering
            HoldingRegistersView = CollectionViewSource.GetDefaultView(HoldingRegisters);
            InputRegistersView = CollectionViewSource.GetDefaultView(InputRegisters);
            CoilsView = CollectionViewSource.GetDefaultView(Coils);
            DiscreteInputsView = CollectionViewSource.GetDefaultView(DiscreteInputs);

            HoldingRegistersView.Filter = item =>
            {
                if (string.IsNullOrWhiteSpace(HoldingSearchText)) return true;
                if (item is RegisterEntry reg)
                {
                    return reg.Address.ToString().Contains(HoldingSearchText, StringComparison.OrdinalIgnoreCase)
                        || (reg.ValueText != null && reg.ValueText.Contains(HoldingSearchText, StringComparison.OrdinalIgnoreCase))
                        || (reg.Type != null && reg.Type.Contains(HoldingSearchText, StringComparison.OrdinalIgnoreCase));
                }
                return true;
            };

            InputRegistersView.Filter = item =>
            {
                if (string.IsNullOrWhiteSpace(InputSearchText)) return true;
                if (item is RegisterEntry reg)
                {
                    return reg.Address.ToString().Contains(InputSearchText, StringComparison.OrdinalIgnoreCase)
                        || reg.Value.ToString().Contains(InputSearchText, StringComparison.OrdinalIgnoreCase)
                        || (reg.Type != null && reg.Type.Contains(InputSearchText, StringComparison.OrdinalIgnoreCase));
                }
                return true;
            };

            CoilsView.Filter = item =>
            {
                if (string.IsNullOrWhiteSpace(CoilsSearchText)) return true;
                if (item is CoilEntry coil)
                {
                    return coil.Address.ToString().Contains(CoilsSearchText, StringComparison.OrdinalIgnoreCase)
                        || coil.State.ToString().Contains(CoilsSearchText, StringComparison.OrdinalIgnoreCase);
                }
                return true;
            };

            DiscreteInputsView.Filter = item =>
            {
                if (string.IsNullOrWhiteSpace(DiscreteSearchText)) return true;
                if (item is CoilEntry coil)
                {
                    return coil.Address.ToString().Contains(DiscreteSearchText, StringComparison.OrdinalIgnoreCase)
                        || coil.State.ToString().Contains(DiscreteSearchText, StringComparison.OrdinalIgnoreCase);
                }
                return true;
            };

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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException)) { _logger.LogDebug(ex, "Failed to load settings, using defaults"); }
        }

        /// <summary>
        /// Initializes all command objects for UI bindings.
        /// </summary>
        private void InitializeCommands()
        {
            // Modbus operation commands
            UpdateHoldingRegisterCommand = new AsyncRelayCommand<DataGridCellEditEndingEventArgs>(UpdateHoldingRegister);
            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => CanConnect() && !_isConnecting);
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
                {
                    var result = await _customEntryCoordinator.ReadCustomNowAsync(ce, EffectiveUnitId, IsServerMode);
                    StatusMessage = result.Message;
                }
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

            // Keyboard shortcut commands
            TrendCommand = new RelayCommand(ShowTrendView);
            RefreshCommand = new RelayCommand(async () => await RefreshAsync());
            ShowHelpCommand = new RelayCommand(ShowHelp);
            ShowScriptEditorCommand = new RelayCommand(ShowScriptEditor);
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
        /// Initializes monitoring services and subscriptions. The coordinator timers are
        /// started separately by the constructor after this method completes.
        /// </summary>
        private void InitializeMonitoringServices()
        {
            // Subscribe to console log events
            _consoleLoggerService.LogMessageReceived += ConsoleLoggerService_LogMessageReceived;

            // Start services
            try { _trendLogger.Start(); } catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException)) { _logger.LogWarning(ex, "Failed to start trend logger"); }
            SubscribeCustomEntries();
        }

        private void ConsoleLoggerService_LogMessageReceived(object? sender, LogMessageEventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                ConsoleMessages.Add(e.Message);

                // Keep the last 1000 messages by default
                while (ConsoleMessages.Count > 1000)
                {
                    ConsoleMessages.RemoveAt(0);
                }
            });
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
                        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
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
        private readonly MonitoringCoordinator _monitoringCoordinator;
        private readonly ITrendLogger _trendLogger;
        private readonly ICustomEntryService _customEntryService;
        private bool _isConnecting = false;

        public IRelayCommand DisconnectCommand { get; private set; } = null!;
        public ICommand RunDiagnosticsCommand { get; private set; } = null!;
        public IRelayCommand ReadRegistersCommand { get; private set; } = null!;
        public IRelayCommand WriteRegisterCommand { get; private set; } = null!;
        public IRelayCommand ReadCoilsCommand { get; private set; } = null!;
        public IRelayCommand WriteCoilCommand { get; private set; } = null!;
        public IRelayCommand ReadInputRegistersCommand { get; private set; } = null!;
        public IRelayCommand ReadDiscreteInputsCommand { get; private set; } = null!;
        public ICommand AddCustomEntryCommand { get; private set; } = null!;
        public ICommand WriteCustomNowCommand { get; private set; } = null!;
        public ICommand ReadCustomNowCommand { get; private set; } = null!;
        public IRelayCommand ReadAllCustomNowCommand { get; private set; } = null!;
        public ICommand SaveProjectCommand { get; private set; } = null!;
        public ICommand LoadProjectCommand { get; private set; } = null!;
        public ICommand ImportUnitIdsCommand { get; private set; } = null!;
        public ICommand ExportUnitIdsCommand { get; private set; } = null!;
        public ICommand ExportUnitIdCommand { get; private set; } = null!;
        public ICommand ImportUnitIdAsCommand { get; private set; } = null!;
        public IRelayCommand SaveCustomCommand { get; private set; } = null!;
        public IRelayCommand LoadCustomCommand { get; private set; } = null!;
        public IRelayCommand SaveAllConfigCommand { get; private set; } = null!;
        public IRelayCommand LoadAllConfigCommand { get; private set; } = null!;
        public IRelayCommand TrendCommand { get; private set; } = null!;
        public IRelayCommand RefreshCommand { get; private set; } = null!;
        public IRelayCommand ShowHelpCommand { get; private set; } = null!;
        public IRelayCommand ShowScriptEditorCommand { get; private set; } = null!;

        public ObservableCollection<string> ConsoleMessages { get; } = new();

        // Register collections (shared across all Unit IDs for display)
        public ObservableCollection<RegisterEntry> HoldingRegisters { get; } = new();
        public ObservableCollection<CoilEntry> Coils { get; } = new();
        public ObservableCollection<RegisterEntry> InputRegisters { get; } = new();
        public ObservableCollection<CoilEntry> DiscreteInputs { get; } = new();

        // Filtered collection views for grids
        public ICollectionView HoldingRegistersView { get; }
        public ICollectionView InputRegistersView { get; }
        public ICollectionView CoilsView { get; }
        public ICollectionView DiscreteInputsView { get; }

        [ObservableProperty]
        private string _holdingSearchText = "";

        [ObservableProperty]
        private string _inputSearchText = "";

        [ObservableProperty]
        private string _coilsSearchText = "";

        [ObservableProperty]
        private string _discreteSearchText = "";

        partial void OnHoldingSearchTextChanged(string value) => HoldingRegistersView.Refresh();
        partial void OnInputSearchTextChanged(string value) => InputRegistersView.Refresh();
        partial void OnCoilsSearchTextChanged(string value) => CoilsView.Refresh();
        partial void OnDiscreteSearchTextChanged(string value) => DiscreteInputsView.Refresh();

        // Tab visibility settings
        [ObservableProperty]
        private bool _isRegistersTabVisible = true;

        [ObservableProperty]
        private bool _isInputRegistersTabVisible = true;

        [ObservableProperty]
        private bool _isCoilsTabVisible = true;

        [ObservableProperty]
        private bool _isDiscreteInputsTabVisible = true;

        [ObservableProperty]
        private bool _isCustomWatchTabVisible = true;

        [ObservableProperty]
        private bool _isSimulationTabVisible = true;

        [ObservableProperty]
        private bool _isDecodeTabVisible = true;

        [ObservableProperty]
        private bool _isTrendTabVisible = true;

        [ObservableProperty]
        private bool _isConsoleTabVisible = true;

        [ObservableProperty]
        private bool _isDebugTabVisible = true;

        public IAsyncRelayCommand<DataGridCellEditEndingEventArgs> UpdateHoldingRegisterCommand { get; private set; } = null!;
        public IRelayCommand ConnectCommand { get; private set; } = null!;

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

        // IMonitoringCallbacks state used by MonitoringCoordinator
        public DateTime LastHoldingReadUtc { get; set; } = DateTime.MinValue;
        public DateTime LastInputRegReadUtc { get; set; } = DateTime.MinValue;
        public DateTime LastCoilsReadUtc { get; set; } = DateTime.MinValue;
        public DateTime LastDiscreteReadUtc { get; set; } = DateTime.MinValue;
        public bool HasConnectionError { get; set; }
        public DateTime LastErrorTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Returns the list of visible tab content IDs for saving with the project.
        /// </summary>
        public List<string> GetVisibleTabs()
        {
            var visibleTabs = new List<string>();
            if (IsRegistersTabVisible) visibleTabs.Add("Registers");
            if (IsInputRegistersTabVisible) visibleTabs.Add("InputRegisters");
            if (IsCoilsTabVisible) visibleTabs.Add("Coils");
            if (IsDiscreteInputsTabVisible) visibleTabs.Add("DiscreteInputs");
            if (IsCustomWatchTabVisible) visibleTabs.Add("CustomWatch");
            if (IsSimulationTabVisible) visibleTabs.Add("Simulation");
            if (IsDecodeTabVisible) visibleTabs.Add("Decode");
            if (IsTrendTabVisible) visibleTabs.Add("Trend");
            if (IsConsoleTabVisible) visibleTabs.Add("Console");
            if (IsDebugTabVisible) visibleTabs.Add("Debug");
            return visibleTabs;
        }

        /// <summary>
        /// Sets tab visibility from a saved list of content IDs.
        /// </summary>
        public void SetVisibleTabs(List<string>? visibleTabs)
        {
            if (visibleTabs == null || visibleTabs.Count == 0)
            {
                // Default: all visible
                IsRegistersTabVisible = true;
                IsInputRegistersTabVisible = true;
                IsCoilsTabVisible = true;
                IsDiscreteInputsTabVisible = true;
                IsCustomWatchTabVisible = true;
                IsSimulationTabVisible = true;
                IsDecodeTabVisible = true;
                IsTrendTabVisible = true;
                IsConsoleTabVisible = true;
                IsDebugTabVisible = true;
                return;
            }

            IsRegistersTabVisible = visibleTabs.Contains("Registers");
            IsInputRegistersTabVisible = visibleTabs.Contains("InputRegisters");
            IsCoilsTabVisible = visibleTabs.Contains("Coils");
            IsDiscreteInputsTabVisible = visibleTabs.Contains("DiscreteInputs");
            IsCustomWatchTabVisible = visibleTabs.Contains("CustomWatch");
            IsSimulationTabVisible = visibleTabs.Contains("Simulation");
            IsDecodeTabVisible = visibleTabs.Contains("Decode");
            IsTrendTabVisible = visibleTabs.Contains("Trend");
            IsConsoleTabVisible = visibleTabs.Contains("Console");
            IsDebugTabVisible = visibleTabs.Contains("Debug");
        }

        // Properties that now delegate to current configuration
        public ObservableCollection<CustomEntry> CustomEntries => CurrentConfig.CustomEntries;
        public bool SimulationEnabled => CurrentConfig.SimulationSettings.SimulationEnabled;
        public int SimulationPeriodMs => CurrentConfig.SimulationSettings.SimulationPeriodMs;

        // Monitoring properties that delegate to current configuration
        public bool GlobalMonitorEnabled
        {
            get => CurrentConfig.MonitoringSettings.GlobalMonitorEnabled;
            set
            {
                if (CurrentConfig.MonitoringSettings.GlobalMonitorEnabled != value)
                {
                    SetGlobalMonitorEnabled(value);
                    OnPropertyChanged(nameof(GlobalMonitorEnabled));
                }
            }
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

        private bool CanConnect() => _connectionCoordinator.CanConnect(IsConnected) && !_isConnecting;

        private async Task ConnectAsync()
        {
            _isConnecting = true;
            ConnectCommand.NotifyCanExecuteChanged();
            try
            {
                // Snapshot monitoring state before connect so we can restore on success
                bool wasHoldingMon = HoldingMonitorEnabled;
                bool wasInputRegMon = InputRegistersMonitorEnabled;
                bool wasCoilsMon = CoilsMonitorEnabled;
                bool wasDiscreteMon = DiscreteInputsMonitorEnabled;
                bool wasGlobalMon = GlobalMonitorEnabled;

                await _connectionCoordinator.ConnectAsync(ServerAddress, Port, IsServerMode,
                    msg => StatusMessage = msg,
                    connected =>
                    {
                        IsConnected = connected;
                        if (connected)
                        {
                            if (IsServerMode)
                                PopulateAvailableUnitIds();

                            // Auto-restore monitoring that was active before disconnect/loss.
                            // If GlobalMonitorEnabled is still set from before, restore per-area monitors.
                            if (GlobalMonitorEnabled)
                            {
                                if (wasHoldingMon) HoldingMonitorEnabled = true;
                                if (wasInputRegMon) InputRegistersMonitorEnabled = true;
                                if (wasCoilsMon) CoilsMonitorEnabled = true;
                                if (wasDiscreteMon) DiscreteInputsMonitorEnabled = true;
                            }

                            HasConnectionError = false;
                        }
                    },
                    ServerUnitId);
            }
            finally
            {
                _isConnecting = false;
                ConnectCommand.NotifyCanExecuteChanged();
            }
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

        public async Task ReadRegistersAsync()
        {
            await _registerCoordinator.ReadRegistersAsync(EffectiveUnitId, RegisterStart, RegisterCount,
                RegistersGlobalType, HoldingRegisters, msg => StatusMessage = msg,
                hasError => HasConnectionError = hasError, HoldingMonitorEnabled, IsServerMode);
        }

        public async Task ReadInputRegistersAsync()
        {
            await _registerCoordinator.ReadInputRegistersAsync(EffectiveUnitId, InputRegisterStart, InputRegisterCount,
                InputRegistersGlobalType, InputRegisters, msg => StatusMessage = msg,
                hasError => HasConnectionError = hasError, InputRegistersMonitorEnabled, IsServerMode);
        }

        private async Task WriteRegisterAsync()
        {
            await _registerCoordinator.WriteRegisterAsync(EffectiveUnitId, WriteRegisterAddress, WriteRegisterValue,
                msg => StatusMessage = msg, async () => await ReadRegistersAsync(), IsServerMode);
        }

        public async Task ReadCoilsAsync()
        {
            await _registerCoordinator.ReadCoilsAsync(EffectiveUnitId, CoilStart, CoilCount,
                Coils, msg => StatusMessage = msg,
                hasError => HasConnectionError = hasError, CoilsMonitorEnabled, IsServerMode);
        }

        public async Task ReadDiscreteInputsAsync()
        {
            await _registerCoordinator.ReadDiscreteInputsAsync(EffectiveUnitId, DiscreteInputStart, DiscreteInputCount,
                DiscreteInputs, msg => StatusMessage = msg,
                hasError => HasConnectionError = hasError, DiscreteInputsMonitorEnabled, IsServerMode);
        }

        private async Task WriteCoilAsync()
        {
            await _registerCoordinator.WriteCoilAsync(EffectiveUnitId, WriteCoilAddress, WriteCoilState,
                msg => StatusMessage = msg, async () => await ReadCoilsAsync(), IsServerMode);
        }

        public async Task HeartbeatAsync()
        {
            if (!IsConnected) return;

            try
            {
                var heartbeat = await _modbusService.ReadHoldingRegistersAsync(UnitId, 1, 1);
                if (heartbeat == null)
                {
                    _logger.LogWarning("Heartbeat check failed - connection lost");
                    await _modbusService.DisconnectAsync();
                    IsConnected = false;
                    _dialogService.Show("Connection to server lost.\n\nPlease reconnect when the server is available.",
                        "Connection Lost", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogWarning(ex, "Heartbeat check failed");
                await _modbusService.DisconnectAsync();
                IsConnected = false;
                _dialogService.Show("Connection to server lost.\n\nPlease reconnect when the server is available.",
                    "Connection Lost", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public override void Dispose()
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

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _consoleLoggerService.LogMessageReceived -= ConsoleLoggerService_LogMessageReceived;
                        _monitoringCoordinator.Dispose();
                        try { _trendLogger.Stop(); } catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException)) { _logger.LogWarning(ex, "Failed to stop trend logger"); }
                        try
                        {
                            CustomEntries.CollectionChanged -= CustomEntries_CollectionChanged;
                            foreach (var ce in CustomEntries)
                            {
                                ce.PropertyChanged -= CustomEntry_PropertyChanged;
                            }
                        }
                        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException)) { _logger.LogDebug(ex, "Error detaching event handlers during disposal"); }

                        // Dispose VisualNodeEditorViewModel
                        _visualNodeEditorViewModel?.Dispose();
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException)) { _logger.LogDebug(ex, "Error during coordinator cleanup in Dispose"); }
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
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        _logger.LogError(ex, "Error during disconnect in Dispose");
                    }
                    _modbusService?.Dispose();
                }
                _disposed = true;
            }
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

        public async Task WriteCustomNowAsync(CustomEntry entry)
        {
            var result = await _customEntryCoordinator.WriteCustomNowAsync(entry, EffectiveUnitId, IsServerMode);
            StatusMessage = result.Message;
        }

        public IEnumerable<CustomEntry> GetCustomEntriesSnapshot() => CustomEntries.ToList();

        public async Task ProcessTrendSamplingAsync()
        {
            var trendEntries = CustomEntries.Where(c => c.Trend);
            await _trendCoordinator.ProcessTrendSamplingAsync(
                trendEntries,
                UnitId,
                IsServerMode,
                enabled => SetGlobalMonitorEnabled(enabled));
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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogWarning(ex, "Error subscribing to custom entries");
            }
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

                    // Automatically enable GlobalMonitorEnabled so the trend timer processes entries.
                    if (!GlobalMonitorEnabled)
                    {
                        GlobalMonitorEnabled = true;
                    }

                    // Also enable the per-area monitor switch for consistency with the UI.
                    if (ce.Area == "HoldingRegister" && !HoldingMonitorEnabled)
                    {
                        HoldingMonitorEnabled = true;
                    }
                    else if (ce.Area == "InputRegister" && !InputRegistersMonitorEnabled)
                    {
                        InputRegistersMonitorEnabled = true;
                    }
                    else if (ce.Area == "Coil" && !CoilsMonitorEnabled)
                    {
                        CoilsMonitorEnabled = true;
                    }
                    else if (ce.Area == "DiscreteInput" && !DiscreteInputsMonitorEnabled)
                    {
                        DiscreteInputsMonitorEnabled = true;
                    }
                }
                else
                {
                    _trendLogger.Remove(key);

                    // Disable GlobalMonitorEnabled if no more trended entries remain.
                    if (!CustomEntries.Any(c => c.Trend))
                    {
                        GlobalMonitorEnabled = false;
                    }
                }
            }
            else if (string.Equals(e.PropertyName, nameof(CustomEntry.Continuous), StringComparison.Ordinal))
            {
                if (ce.Continuous)
                {
                    // Auto-enable GlobalMonitorEnabled so the monitor timer processes the entry.
                    if (!GlobalMonitorEnabled)
                    {
                        GlobalMonitorEnabled = true;
                    }

                    // Enable the per-area monitor for this entry.
                    if (ce.Area == "HoldingRegister" && !HoldingMonitorEnabled)
                        HoldingMonitorEnabled = true;
                    else if (ce.Area == "InputRegister" && !InputRegistersMonitorEnabled)
                        InputRegistersMonitorEnabled = true;
                    else if (ce.Area == "Coil" && !CoilsMonitorEnabled)
                        CoilsMonitorEnabled = true;
                    else if (ce.Area == "DiscreteInput" && !DiscreteInputsMonitorEnabled)
                        DiscreteInputsMonitorEnabled = true;
                }
                else
                {
                    // Disable GlobalMonitorEnabled if no more continuous entries remain.
                    if (!CustomEntries.Any(c => c.Continuous))
                    {
                        GlobalMonitorEnabled = false;
                    }
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

                // Fix old nodes with invalid addresses (migration)
                _visualNodeEditorViewModel.MigrateNodes();
            }
        }

        private async Task SaveProjectAsync()
        {
            var snapshot = BuildWorkspaceSnapshot();
            var result = await _configurationCoordinator.SaveProjectAsync(snapshot);
            StatusMessage = result.Message;
        }

        private ProjectWorkspaceSnapshot BuildWorkspaceSnapshot()
        {
            var snapshot = new ProjectWorkspaceSnapshot
            {
                Mode = Mode,
                ServerAddress = ServerAddress,
                Port = Port,
                ServerUnitId = ServerUnitId,
                ClientUnitId = UnitId,
                SelectedUnitId = SelectedUnitId,
                IsServerMode = IsServerMode,
                VisibleTabs = GetVisibleTabs(),
                VisualNodes = new List<VisualNode>(_visualNodeEditorViewModel.Nodes),
                VisualConnections = new List<NodeConnection>(_visualNodeEditorViewModel.Connections)
            };

            foreach (var kvp in UnitConfigurations)
                snapshot.UnitConfigurations[kvp.Key] = kvp.Value.Clone();

            return snapshot;
        }

        private void ApplyWorkspaceSnapshot(ProjectWorkspaceSnapshot snapshot)
        {
            Mode = snapshot.Mode;
            ServerAddress = snapshot.ServerAddress;
            Port = snapshot.Port;
            ServerUnitId = snapshot.ServerUnitId;
            UnitId = snapshot.ClientUnitId;

            UnitConfigurations.Clear();
            foreach (var kvp in snapshot.UnitConfigurations)
                UnitConfigurations[kvp.Key] = kvp.Value.Clone();

            if (snapshot.UnitConfigurations.Count > 0 && !snapshot.UnitConfigurations.ContainsKey(SelectedUnitId))
            {
                SelectedUnitId = snapshot.UnitConfigurations.ContainsKey(snapshot.SelectedUnitId)
                    ? snapshot.SelectedUnitId
                    : snapshot.UnitConfigurations.Keys.First();
            }

            SetVisibleTabs(snapshot.VisibleTabs);

            _visualNodeEditorViewModel.Nodes.Clear();
            _visualNodeEditorViewModel.Connections.Clear();

            foreach (var node in snapshot.VisualNodes ?? new List<VisualNode>())
                _visualNodeEditorViewModel.Nodes.Add(node);
            foreach (var connection in snapshot.VisualConnections ?? new List<NodeConnection>())
                _visualNodeEditorViewModel.Connections.Add(connection);

            _visualNodeEditorViewModel.MigrateNodes();

            OnPropertyChanged(nameof(CustomEntries));
            OnPropertyChanged(nameof(SimulationEnabled));
            OnPropertyChanged(nameof(GlobalMonitorEnabled));
            OnPropertyChanged(nameof(HoldingMonitorEnabled));
            OnPropertyChanged(nameof(InputRegistersMonitorEnabled));
            OnPropertyChanged(nameof(CoilsMonitorEnabled));
            OnPropertyChanged(nameof(DiscreteInputsMonitorEnabled));
            OnPropertyChanged(nameof(CustomMonitorEnabled));
            OnPropertyChanged(nameof(CustomReadMonitorEnabled));
        }

        private async Task LoadProjectAsync()
        {
            var result = await _configurationCoordinator.LoadProjectAsync();
            if (result.Success && result.Snapshot != null)
            {
                ApplyWorkspaceSnapshot(result.Snapshot);
            }
            StatusMessage = result.Message;
        }

        private async Task ImportUnitIdsAsync()
        {
            var result = await _configurationCoordinator.ImportUnitIdsAsync();
            if (result.Success && result.Snapshot != null)
            {
                var importedCount = 0;
                foreach (var kvp in result.Snapshot.UnitConfigurations)
                {
                    if (!UnitConfigurations.ContainsKey(kvp.Key))
                    {
                        UnitConfigurations[kvp.Key] = kvp.Value.Clone();
                        importedCount++;
                    }
                }

                if (IsServerMode)
                    PopulateAvailableUnitIds();

                StatusMessage = $"Imported {importedCount} new Unit ID configurations";
            }
            else
            {
                StatusMessage = result.Message;
            }
        }

        private async Task ExportUnitIdsAsync()
        {
            var snapshot = BuildWorkspaceSnapshot();
            var result = await _configurationCoordinator.ExportUnitIdsAsync(snapshot);
            StatusMessage = result.Message;
        }

        private async Task ExportUnitIdAsync()
        {
            var snapshot = BuildWorkspaceSnapshot();
            var result = await _configurationCoordinator.ExportUnitIdAsync(snapshot, SelectedUnitId);
            StatusMessage = result.Message;
        }

        private async Task ImportUnitIdAsAsync()
        {
            if (!IsServerMode)
            {
                _dialogService.Show("Import Unit ID As is only available in Server mode.", "Import Unit ID As", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = await _configurationCoordinator.ImportUnitIdAsAsync();
            if (result.Success && result.ImportedUnitId.HasValue && result.ImportedConfiguration != null)
            {
                UnitConfigurations[result.ImportedUnitId.Value] = result.ImportedConfiguration.Clone();
                SelectedUnitId = result.ImportedUnitId.Value;
                PopulateAvailableUnitIds();
            }
            StatusMessage = result.Message;
        }

        private void ShowTrendView()
        {
            // Request navigation to trend view - MainWindow will handle this via property binding
            RequestedViewIndex = 7; // Trend view index
        }

        private void ShowHelp()
        {
            // This will be handled by MainWindow via event or direct call
            // For now, we'll trigger a property change that MainWindow can listen to
            RequestShowHelp = true;
        }

        private void ShowScriptEditor()
        {
            // This will be handled by MainWindow via event or direct call
            RequestShowScriptEditor = true;
        }

        [ObservableProperty]
        private int _requestedViewIndex = -1;

        [ObservableProperty]
        private bool _requestShowHelp = false;

        [ObservableProperty]
        private bool _requestShowScriptEditor = false;

        private async Task RefreshAsync()
        {
            if (!IsConnected)
            {
                StatusMessage = "Not connected - cannot refresh";
                return;
            }

            try
            {
                await ReadRegistersAsync();
                await ReadInputRegistersAsync();
                await ReadCoilsAsync();
                await ReadDiscreteInputsAsync();
                await ReadAllCustomNowAsync();
                StatusMessage = "All data refreshed";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error during refresh");
                StatusMessage = $"Refresh failed: {ex.Message}";
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
                                        _dialogService.Show($"Invalid float value: '{editedText}'", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                                        _dialogService.Show($"Invalid integer value: '{editedText}'", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                                        _dialogService.Show($"Invalid unsigned value: '{editedText}' (0..65535)", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                        e.Cancel = true;
                                    }
                                    break;
                                }
                        }
                    }
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _dialogService.Show($"Failed to write register {entry.Address}: {ex.Message}", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Cancel = true;
                }
            }
        }

    }
}
