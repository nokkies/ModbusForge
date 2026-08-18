using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
    public sealed partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger<MainViewModel> _logger;
        private readonly IDispatcher _dispatcher;
        private readonly IUnitConfigurationStore _unitConfigurationStore;
        private readonly ICustomEntryService? _customEntryService;
        private readonly IFileDialogService? _fileDialogService;
        private readonly IFileSystem _fileSystem;
        private readonly IInputDialogService? _inputDialogService;
        private readonly ICustomBulkAddDialogService? _customBulkAddDialogService;
        private readonly IMessageBoxService? _messageBoxService;
        private readonly ISettingsService? _settingsService;
        private readonly IThemeService? _themeService;
        private readonly IUpdateService? _updateService;
        private readonly IWindowService? _windowService;
        private readonly IApplicationLifetime? _applicationLifetime;
        private readonly IDockingHost? _dockingHost;
        private readonly ITrendLogger? _trendLogger;
        private readonly TagService? _tagService;
        private readonly ITrendSubscriptionService? _trendSubscriptionService;
        private CancellationTokenSource? _pollCts;
        private readonly object _pollLifecycleLock = new();
        private readonly object _pendingPollLock = new();
        private readonly HashSet<PlcArea> _pendingPollAreas = new();
        private readonly ObservableCollection<string> _fallbackConsoleMessages = new();
        private CancellationTokenSource? _customWatchCts;
        private readonly object _customWatchLifecycleLock = new();
        private readonly SemaphoreSlim _modbusIoGate = new(1, 1);
        private readonly object _monitorFailureLock = new();
        private readonly Dictionary<PlcArea, int> _monitorFailureCounts = new();
        private readonly Dictionary<PlcArea, DateTime> _lastMonitorFailureUtc = new();
        // Pens keep polling through transient failures (pausing a trend is
        // worse than a stale point), so a failing pen is reported once per
        // failure episode instead of once per cycle.
        private readonly object _failingTrendPensLock = new();
        private readonly HashSet<string> _failingTrendPens = new();
        private bool _disposed;
        private bool _isApplyingUnitConfiguration;
        private DateTime _lastHoldingReadUtc;
        private DateTime _lastInputRegReadUtc;
        private DateTime _lastCoilsReadUtc;
        private DateTime _lastDiscreteReadUtc;

        private const int PollLoopIntervalMs = 50;
        private const int MinimumMonitorPeriodMs = 50;
        private const int DefaultCustomPeriodMs = 1000;
        private const int MultiRegisterTypeIncrement = 2;
        private const int SingleRegisterTypeIncrement = 1;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        private int _startAddress = 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        private int _registerCount = 20;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadCustomNowCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCustomNowCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadAllCustomNowCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveCustomEntryCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        private bool _isContinuousRead = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCommand))]
        private PlcArea _selectedArea = PlcArea.HoldingRegister;

        [ObservableProperty]
        private int _selectedAreaIndex;

        [ObservableProperty]
        private string _globalType = "int";

        [ObservableProperty]
        private bool _swapBytes;

        [ObservableProperty]
        private bool _swapWords;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadHoldingRegistersCommand))]
        private int _holdingRegisterStart = 1;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadHoldingRegistersCommand))]
        private int _holdingRegisterCount = 20;

        [ObservableProperty]
        private string _registersGlobalType = "int";

        [ObservableProperty]
        private bool _registersSwapBytes;

        [ObservableProperty]
        private bool _registersSwapWords;

        [ObservableProperty]
        private bool _holdingMonitorEnabled = true;

        [ObservableProperty]
        private int _holdingMonitorPeriodMs = 1000;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadInputRegistersCommand))]
        private int _inputRegisterStart = 1;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadInputRegistersCommand))]
        private int _inputRegisterCount = 20;

        [ObservableProperty]
        private string _inputRegistersGlobalType = "int";

        [ObservableProperty]
        private bool _inputRegistersSwapBytes;

        [ObservableProperty]
        private bool _inputRegistersSwapWords;

        [ObservableProperty]
        private bool _inputRegistersMonitorEnabled;

        [ObservableProperty]
        private int _inputRegistersMonitorPeriodMs = 1000;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCoilsCommand))]
        private int _coilStart = 1;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCoilsCommand))]
        private int _coilCount = 20;

        [ObservableProperty]
        private bool _coilsMonitorEnabled;

        [ObservableProperty]
        private int _coilsMonitorPeriodMs = 1000;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadDiscreteInputsCommand))]
        private int _discreteInputStart = 1;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadDiscreteInputsCommand))]
        private int _discreteInputCount = 20;

        [ObservableProperty]
        private bool _discreteInputsMonitorEnabled;

        [ObservableProperty]
        private int _discreteInputsMonitorPeriodMs = 1000;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UnitId))]
        private ConnectionProfile? _activeProfile;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveCustomCommand))]
        [NotifyCanExecuteChangedFor(nameof(LoadCustomCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand))]
        [NotifyCanExecuteChangedFor(nameof(LoadProjectCommand))]
        private bool _isCustomWatchMonitoring;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCustomEntryCommand))]
        private CustomEntry? _selectedCustomEntry;

        [ObservableProperty]
        private ObservableCollection<RegisterEntry> _holdingRegisters = new();

        [ObservableProperty]
        private ObservableCollection<RegisterEntry> _inputRegisters = new();

        [ObservableProperty]
        private ObservableCollection<CoilEntry> _coils = new();

        [ObservableProperty]
        private ObservableCollection<CoilEntry> _discreteInputs = new();

        public ObservableCollection<RegisterEntry> Registers => HoldingRegisters;

        /// <summary>
        /// Compatibility property for the custom-watch bindings. The collection now
        /// belongs to the selected Unit ID configuration instead of one global list.
        /// </summary>
        public ObservableCollection<CustomEntry> CustomEntries => _unitConfigurationStore.CurrentConfig.CustomEntries;

        public IModbusService? ActiveService => _connectionManager.ActiveService;

        public int UnitId
        {
            get => ActiveProfile?.UnitId ?? SelectedUnitId;
            set
            {
                var byteValue = (byte)Math.Clamp(value, 1, 247);

                if (ActiveProfile != null && ActiveProfile.UnitId != byteValue)
                {
                    ActiveProfile.UnitId = byteValue;
                }

                _unitConfigurationStore.GetOrCreateConfiguration(byteValue);
                if (!IsServerMode && SelectedUnitId != byteValue)
                {
                    SelectedUnitId = byteValue;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveUnitId));
            }
        }

        public bool IsRegisterArea => SelectedArea is PlcArea.HoldingRegister or PlcArea.InputRegister;

        public IReadOnlyList<string> RegisterTypes => RegisterEntry.AvailableTypes;

        public IReadOnlyList<string> CustomAreas => CustomEntry.AvailableAreas;

        public List<string> Modes { get; } = new() { "Client", "Server" };

        /// <summary>
        /// Index of the current <see cref="Mode"/> in <see cref="Modes"/>.
        /// Using an index binding instead of SelectedItem avoids a race when the
        /// ComboBox ItemsSource is populated after the selection binding is applied.
        /// </summary>
        public int ModeIndex
        {
            get => Modes.IndexOf(Mode);
            set
            {
                if (value >= 0 && value < Modes.Count)
                {
                    Mode = Modes[value];
                }
            }
        }

        /// <summary>
        /// The available server Unit IDs are owned by the shared store. The property is
        /// intentionally kept on the view model for existing bindings and callers.
        /// </summary>
        public ObservableCollection<byte> AvailableUnitIds => _unitConfigurationStore.AvailableUnitIds;

        /// <summary>
        /// The complete per-Unit ID state currently held by the shared store.
        /// </summary>
        public IReadOnlyDictionary<byte, UnitIdConfiguration> UnitConfigurations => _unitConfigurationStore.UnitConfigurations;

        /// <summary>
        /// The configuration selected in the shared Unit ID store.
        /// </summary>
        public UnitIdConfiguration CurrentConfig => _unitConfigurationStore.CurrentConfig;

        /// <summary>
        /// Adds a register to the trend pen list (used by the register-grid
        /// context menus). Shares the subscription plumbing with the Trends
        /// view's Add dialog, so pens can be managed from one place.
        /// </summary>
        public void AddRegisterToTrend(int address, string area, string? type)
        {
            if (_trendSubscriptionService is null)
            {
                StatusMessage = "Trend subscriptions are not available.";
                return;
            }

            var key = _trendSubscriptionService.AddPen(
                area,
                address,
                _trendSubscriptionService.DefaultName(area, address),
                1000,
                type);
            StatusMessage = $"Trend pen '{key}' added. It appears in the chart as data is read.";
        }

        public ObservableCollection<ConnectionProfile> ConnectionProfiles => _connectionManager.Profiles;

        /// <summary>
        /// True when at least one connection profile exists (drives the
        /// dashboard profile list and its empty-state placeholder).
        /// </summary>
        public bool HasConnectionProfiles => ConnectionProfiles.Count > 0;

        /// <summary>
        /// True when the dashboard should show the no-profiles placeholder.
        /// </summary>
        public bool ShowNoProfilesPlaceholder => !HasConnectionProfiles;

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

        public string Mode
        {
            get => ActiveProfile?.Mode is { } mode && !string.IsNullOrWhiteSpace(mode) ? mode : "Client";
            set
            {
                if (ActiveProfile != null && ActiveProfile.Mode != value)
                {
                    ActiveProfile.Mode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ModeIndex));
                    OnPropertyChanged(nameof(IsServerMode));
                    OnPropertyChanged(nameof(ConnectButtonText));
                    OnPropertyChanged(nameof(ToggleConnectionButtonText));
                    OnPropertyChanged(nameof(DebugSummary));
                    OnPropertyChanged(nameof(ConnectionHeader));
                    OnPropertyChanged(nameof(AddressLabel));
                    OnPropertyChanged(nameof(ShowClientFields));
                    OnPropertyChanged(nameof(ShowServerFields));
                }
            }
        }

        public bool IsServerMode => string.Equals(Mode, "Server", StringComparison.OrdinalIgnoreCase);

        public bool ShowClientFields => !IsServerMode;

        public bool ShowServerFields => IsServerMode;

        public string ConnectButtonText => IsServerMode ? "Start Server" : "Connect";

        public string ToggleConnectionButtonText => IsConnected ? "Disconnect" : ConnectButtonText;

        public string ConnectionHeader => IsServerMode ? "Modbus Connection (Server)" : "Modbus Connection (Client)";

        public string AddressLabel => IsServerMode ? "Interface:" : "Server:";

        public byte EffectiveUnitId => IsServerMode ? SelectedUnitId : (byte)UnitId;

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

        public byte SelectedUnitId
        {
            get => _unitConfigurationStore.SelectedUnitId;
            set
            {
                var normalized = (byte)Math.Clamp((int)value, 1, 247);
                if (_unitConfigurationStore.SelectedUnitId == normalized)
                {
                    _unitConfigurationStore.GetOrCreateConfiguration(normalized);
                    return;
                }

                SyncCurrentUnitConfiguration();
                _unitConfigurationStore.GetOrCreateConfiguration(normalized);
                _unitConfigurationStore.SelectedUnitId = normalized;
            }
        }

        public TrendViewModel? TrendViewModel { get; }

        public FrameInspectorViewModel? FrameInspectorViewModel { get; }

        public MqttViewModel? MqttViewModel { get; }

        public ScriptEditorViewModel? ScriptEditorViewModel { get; }

        public SignalGeneratorViewModel? SignalGeneratorViewModel { get; }

        public ScriptRulesViewModel? RulesViewModel { get; }

        public VisualNodeEditorViewModel? VisualNodeEditorViewModel { get; }

        public DecodeViewModel? DecodeViewModel { get; }

        public MainViewModel(
            IConnectionManager connectionManager,
            ILogger<MainViewModel> logger,
            IDispatcher dispatcher,
            ICustomEntryService? customEntryService = null,
            IFileDialogService? fileDialogService = null,
            IInputDialogService? inputDialogService = null,
            ICustomBulkAddDialogService? customBulkAddDialogService = null,
            IMessageBoxService? messageBoxService = null,
            ISettingsService? settingsService = null,
            IThemeService? themeService = null,
            IUpdateService? updateService = null,
            IWindowService? windowService = null,
            IApplicationLifetime? applicationLifetime = null,
            ITrendLogger? trendLogger = null,
            TrendViewModel? trendViewModel = null,
            FrameInspectorViewModel? frameInspectorViewModel = null,
            MqttViewModel? mqttViewModel = null,
            MqttGatewayService? mqttGateway = null,
            ScriptEditorViewModel? scriptEditorViewModel = null,
            ScriptRulesViewModel? rulesViewModel = null,
            SignalGeneratorViewModel? signalGeneratorViewModel = null,
            VisualNodeEditorViewModel? visualNodeEditorViewModel = null,
            DecodeViewModel? decodeViewModel = null,
            IUnitConfigurationStore? unitConfigurationStore = null,
            IFileSystem? fileSystem = null,
            IDockingHost? dockingHost = null,
            TagService? tagService = null,
            ITrendSubscriptionService? trendSubscriptionService = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _unitConfigurationStore = unitConfigurationStore ?? new UnitConfigurationStore(dispatcher);
            _fileSystem = fileSystem ?? new FileSystem();
            _customEntryService = customEntryService;
            _fileDialogService = fileDialogService;
            _inputDialogService = inputDialogService;
            _customBulkAddDialogService = customBulkAddDialogService;
            _messageBoxService = messageBoxService;
            _settingsService = settingsService;
            _themeService = themeService;
            _updateService = updateService;
            _windowService = windowService;
            _applicationLifetime = applicationLifetime;
            _dockingHost = dockingHost;
            _trendLogger = trendLogger;
            _tagService = tagService;
            _trendSubscriptionService = trendSubscriptionService;
            TrendViewModel = trendViewModel;
            FrameInspectorViewModel = frameInspectorViewModel;
            MqttViewModel = mqttViewModel;
            ScriptEditorViewModel = scriptEditorViewModel;
            RulesViewModel = rulesViewModel;
            SignalGeneratorViewModel = signalGeneratorViewModel;
            VisualNodeEditorViewModel = visualNodeEditorViewModel;
            if (VisualNodeEditorViewModel != null)
            {
                VisualNodeEditorViewModel.PropertyChanged += OnVisualNodeEditorViewModelPropertyChanged;
            }

            DecodeViewModel = decodeViewModel;
            if (DecodeViewModel != null)
            {
                DecodeViewModel.UnitIdProvider = () => EffectiveUnitId;
            }

            if (mqttGateway is not null)
            {
                mqttGateway.SnapshotProvider = BuildMqttSnapshot;
            }

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect());
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect());
            ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => ActiveProfile != null && !IsBusy);
            ReadCommand = new AsyncRelayCommand(ReadAsync, () => CanRead());
            WriteCommand = new AsyncRelayCommand(WriteAsync, () => CanWrite());
            ReadHoldingRegistersCommand = new AsyncRelayCommand(ReadHoldingRegistersAsync, () => CanRead(PlcArea.HoldingRegister));
            ReadInputRegistersCommand = new AsyncRelayCommand(ReadInputRegistersAsync, () => CanRead(PlcArea.InputRegister));
            ReadCoilsCommand = new AsyncRelayCommand(ReadCoilsAsync, () => CanRead(PlcArea.Coil));
            ReadDiscreteInputsCommand = new AsyncRelayCommand(ReadDiscreteInputsAsync, () => CanRead(PlcArea.DiscreteInput));
            WriteHoldingRegisterCommand = new AsyncRelayCommand(WriteHoldingRegisterAsync, () => CanWrite(PlcArea.HoldingRegister));
            WriteCoilCommand = new AsyncRelayCommand(WriteCoilAsync, () => CanWrite(PlcArea.Coil));

            AddCustomEntryCommand = new AsyncRelayCommand(AddCustomEntryAsync, () => !IsBusy);
            AddBulkCustomEntryCommand = new AsyncRelayCommand(AddCustomBulkEntryAsync, () => !IsBusy);
            RemoveCustomEntryCommand = new AsyncRelayCommand(RemoveCustomEntryAsync, () => CanRemoveCustomEntry());
            ReadCustomEntryCommand = new AsyncRelayCommand(ReadSelectedCustomEntryAsync, () => CanReadCustomEntry());
            WriteCustomEntryCommand = new AsyncRelayCommand(WriteSelectedCustomEntryAsync, () => CanWriteCustomEntry());
            ReadCustomNowCommand = new AsyncRelayCommand<CustomEntry?>(ReadCustomEntryNowAsync, CanReadCustomEntry);
            WriteCustomNowCommand = new AsyncRelayCommand<CustomEntry?>(WriteCustomEntryNowAsync, CanWriteCustomEntry);
            DeleteCustomEntryCommand = new AsyncRelayCommand<CustomEntry?>(DeleteCustomEntryAsync, entry => entry != null);
            ReadAllCustomNowCommand = new AsyncRelayCommand(ReadAllCustomEntriesAsync, CanReadAllCustomEntries);
            SaveCustomCommand = new AsyncRelayCommand(SaveCustomAsync, () => CanSaveCustom());
            LoadCustomCommand = new AsyncRelayCommand(LoadCustomAsync, () => CanLoadCustom());
            SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, () => CanSaveProject());
            LoadProjectCommand = new AsyncRelayCommand(LoadProjectAsync, () => CanLoadProject());

            OpenPreferencesCommand = new RelayCommand(() => _windowService?.ShowPreferences());
            OpenAboutCommand = new RelayCommand(() => _windowService?.ShowAbout());
            OpenHelpCommand = new RelayCommand(() => _windowService?.ShowHelp());
            OpenKeyboardShortcutsCommand = new RelayCommand(() => _windowService?.ShowKeyboardShortcuts());
            OpenTroubleshootingCommand = new RelayCommand(() => _windowService?.ShowTroubleshooting());
            ToggleThemeCommand = new RelayCommand(ToggleTheme);
            CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
            ExitCommand = new RelayCommand(() => _applicationLifetime?.Shutdown());
            ReadShortcutCommand = new RelayCommand(() => ReadCommand.Execute(null));
            OpenTrendsCommand = new RelayCommand(() => SelectedTabIndex = 1);
            OpenFrameInspectorCommand = new RelayCommand(() => SelectedTabIndex = 2);
            OpenPcapCommand = new RelayCommand(() =>
            {
                SelectedTabIndex = 2;
                FrameInspectorViewModel?.ImportPcapCommand.Execute(null);
            });
            OpenTagBrowserCommand = new RelayCommand(() => _dockingHost?.ShowTagBrowser());
            OpenWatchWindowCommand = new RelayCommand(() => _dockingHost?.ShowWatchWindow());
            OpenConnectionManagerCommand = new RelayCommand(() => _dockingHost?.ShowConnectionManager());
            ImportUnitIdsCommand = new AsyncRelayCommand(ImportUnitIdsAsync);
            ExportUnitIdsCommand = new AsyncRelayCommand(ExportUnitIdsAsync);
            ImportUnitIdAsCommand = new AsyncRelayCommand(ImportUnitIdAsAsync, CanImportUnitIdAs);
            ExportUnitIdCommand = new AsyncRelayCommand(ExportUnitIdAsync, CanExportUnitId);
            SaveAllConfigCommand = new AsyncRelayCommand(SaveProjectAsync);
            ShowAllTabsCommand = new RelayCommand(ShowAllTabs);
            ResetTabsCommand = new RelayCommand(ResetTabs);
            ClearConsoleCommand = new RelayCommand(() => ConsoleMessages.Clear());
            ClearDebugCommand = new RelayCommand(() => DebugMessages.Clear());

            if (_themeService != null)
            {
                _themeService.ThemeChanged += ThemeService_ThemeChanged;
            }

            _connectionManager.ActiveProfileChanged += ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected += ConnectionManager_ProfileConnected;
            _connectionManager.ProfileDisconnected += ConnectionManager_ProfileDisconnected;
            ConnectionProfiles.CollectionChanged += OnConnectionProfilesCollectionChanged;
            _unitConfigurationStore.SelectedUnitIdChanged += UnitConfigurationStore_SelectedUnitIdChanged;
            _unitConfigurationStore.AvailableUnitIdsChanged += UnitConfigurationStore_AvailableUnitIdsChanged;

            ActiveProfile = _connectionManager.ActiveProfile;

            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged += ActiveProfile_PropertyChanged;
                if (!ActiveProfile.IsServerMode)
                {
                    _unitConfigurationStore.SelectedUnitId = ActiveProfile.UnitId;
                }

                // Default to Server mode so the user can press Start without changing the Mode combo.
                if (string.IsNullOrWhiteSpace(ActiveProfile.Mode))
                {
                    Mode = "Server";
                }

                RefreshAvailableUnitIds();
            }

            if (!_unitConfigurationStore.TryGetConfiguration(_unitConfigurationStore.SelectedUnitId, out _))
            {
                SyncCurrentUnitConfiguration();
            }
            else
            {
                ApplyCurrentUnitConfiguration();
            }

            StatusMessage = ActiveProfile != null
                ? $"Active profile: {ActiveProfile.DisplayName}"
                : "No active connection profile";

            // Force the top toolbar dropdowns to refresh once the DataContext is attached.
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(ModeIndex));
            OnPropertyChanged(nameof(SelectedUnitId));

            ShowAllTabs();
            HookCustomEntries();
            HookTrendPens();
            UpdateCustomWatchMonitoringState();
        }

        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand ToggleConnectionCommand { get; }
        public IAsyncRelayCommand ReadCommand { get; }
        public IAsyncRelayCommand WriteCommand { get; }
        public IAsyncRelayCommand ReadHoldingRegistersCommand { get; }
        public IAsyncRelayCommand ReadInputRegistersCommand { get; }
        public IAsyncRelayCommand ReadCoilsCommand { get; }
        public IAsyncRelayCommand ReadDiscreteInputsCommand { get; }
        public IAsyncRelayCommand WriteHoldingRegisterCommand { get; }
        public IAsyncRelayCommand WriteCoilCommand { get; }

        public IAsyncRelayCommand AddCustomEntryCommand { get; }
        public IAsyncRelayCommand AddBulkCustomEntryCommand { get; }
        public IAsyncRelayCommand RemoveCustomEntryCommand { get; }
        public IAsyncRelayCommand ReadCustomEntryCommand { get; }
        public IAsyncRelayCommand WriteCustomEntryCommand { get; }
        public IAsyncRelayCommand<CustomEntry?> ReadCustomNowCommand { get; }
        public IAsyncRelayCommand<CustomEntry?> WriteCustomNowCommand { get; }
        public IAsyncRelayCommand<CustomEntry?> DeleteCustomEntryCommand { get; }
        public IAsyncRelayCommand ReadAllCustomNowCommand { get; }
        public IAsyncRelayCommand SaveCustomCommand { get; }
        public IAsyncRelayCommand LoadCustomCommand { get; }
        public IAsyncRelayCommand SaveProjectCommand { get; }
        public IAsyncRelayCommand LoadProjectCommand { get; }

        public ICommand OpenPreferencesCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand OpenHelpCommand { get; }
        public ICommand OpenKeyboardShortcutsCommand { get; }
        public ICommand OpenTroubleshootingCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand ReadShortcutCommand { get; }
        public ICommand OpenTrendsCommand { get; }
        public ICommand OpenFrameInspectorCommand { get; }
        public ICommand OpenPcapCommand { get; }
        public ICommand OpenTagBrowserCommand { get; }
        public ICommand OpenWatchWindowCommand { get; }
        public ICommand OpenConnectionManagerCommand { get; }
        public IAsyncRelayCommand ImportUnitIdsCommand { get; }
        public IAsyncRelayCommand ExportUnitIdsCommand { get; }
        public IAsyncRelayCommand ImportUnitIdAsCommand { get; }
        public IAsyncRelayCommand ExportUnitIdCommand { get; }
        public IAsyncRelayCommand SaveAllConfigCommand { get; }
        public ICommand ShowAllTabsCommand { get; }
        public ICommand ResetTabsCommand { get; }
        public ICommand ClearConsoleCommand { get; }
        public ICommand ClearDebugCommand { get; }

        public bool IsDarkMode
        {
            get => _themeService?.IsDarkMode ?? false;
            set
            {
                if (_themeService != null && _themeService.IsDarkMode != value)
                {
                    _themeService.SetTheme(value);
                }

                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private int _selectedTabIndex;

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
        private bool _isRegisterGridEditing;

        [ObservableProperty]
        private bool _isConsoleTabVisible = true;

        [ObservableProperty]
        private bool _isDebugTabVisible = true;

        [ObservableProperty]
        private bool _hasConnectionError;

        public ObservableCollection<string> ConsoleMessages { get; } = new();

        public ObservableCollection<string> DebugMessages { get; } = new();

        public string VersionText => $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";

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

        partial void OnStatusMessageChanged(string value)
        {
            AppendConsoleMessage(value);
            AppendDebugMessage($"{DateTime.Now:HH:mm:ss.fff} {value}");
            OnPropertyChanged(nameof(DebugSummary));
        }

        partial void OnHasConnectionErrorChanged(bool value)
        {
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(IsConnectionErrorVisible));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(DebugSummary));
        }

        partial void OnIsRegistersTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsInputRegistersTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsCoilsTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsDiscreteInputsTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsCustomWatchTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsSimulationTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsDecodeTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsTrendTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsConsoleTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();
        partial void OnIsDebugTabVisibleChanged(bool value) => EnsureSelectedTabIsVisible();

        private void AppendConsoleMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            ConsoleMessages.Add(message);
            while (ConsoleMessages.Count > 1000)
            {
                ConsoleMessages.RemoveAt(0);
            }
        }

        private void AppendDebugMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            DebugMessages.Add(message);
            while (DebugMessages.Count > 1000)
            {
                DebugMessages.RemoveAt(0);
            }
        }

        private void OnVisualNodeEditorViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VisualNodeEditorViewModel.StatusText) &&
                VisualNodeEditorViewModel is not null)
            {
                var message = VisualNodeEditorViewModel.StatusText;
                if (!string.IsNullOrWhiteSpace(message))
                {
                    AppendConsoleMessage(message);
                    AppendDebugMessage($"{DateTime.Now:HH:mm:ss.fff} {message}");
                }
            }
        }

        public void ShowAllTabs()
        {
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
        }

        public void ResetTabs() => ShowAllTabs();

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

        public void SetVisibleTabs(IReadOnlyCollection<string>? visibleTabs)
        {
            if (visibleTabs == null || visibleTabs.Count == 0)
            {
                ShowAllTabs();
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

        private void EnsureSelectedTabIsVisible()
        {
            if (IsTabIndexVisible(SelectedTabIndex)) return;

            SelectedTabIndex = Enumerable.Range(0, 16).FirstOrDefault(IsTabIndexVisible);
        }

        private bool IsTabIndexVisible(int index)
        {
            // Tab order: Dashboard(0), Trends(1), Frame Inspector(2), MQTT(3),
            // Script Editor(4), Rules(5), Signal Generator(6), Simulation(7),
            // Holding(8), Coils(9), Input(10), Discrete(11), Custom Watch(12),
            // Decode(13), Console(14), Debug(15).
            return index switch
            {
                0 => true,
                1 => IsTrendTabVisible,
                7 => IsSimulationTabVisible,
                8 => IsRegistersTabVisible,
                9 => IsCoilsTabVisible,
                10 => IsInputRegistersTabVisible,
                11 => IsDiscreteInputsTabVisible,
                12 => IsCustomWatchTabVisible,
                13 => IsDecodeTabVisible,
                14 => IsConsoleTabVisible,
                15 => IsDebugTabVisible,
                _ => true
            };
        }

        private void ThemeService_ThemeChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(IsDarkMode));
        }

        partial void OnSelectedAreaChanged(PlcArea value)
        {
            IsRegisterGridEditing = false;
            SelectedAreaIndex = (int)value;
            OnPropertyChanged(nameof(IsRegisterArea));
            OnPropertyChanged(nameof(CanWrite));
            WriteCommand.NotifyCanExecuteChanged();

            // Keep legacy global properties in sync with the newly selected area
            StartAddress = GetAreaStart(value);
            RegisterCount = GetAreaCount(value);
            GlobalType = GetAreaGlobalType(value);
            SwapBytes = GetAreaSwapBytes(value);
            SwapWords = GetAreaSwapWords(value);
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            IsRegisterGridEditing = false;
        }

        partial void OnSelectedAreaIndexChanged(int value)
        {
            SelectedArea = (PlcArea)value;
        }

        partial void OnIsContinuousReadChanged(bool value)
        {
            HoldingMonitorEnabled = value;
        }

        partial void OnStartAddressChanged(int value)
        {
            SetAreaStart(SelectedArea, value);
        }

        partial void OnRegisterCountChanged(int value)
        {
            SetAreaCount(SelectedArea, value);
        }

        partial void OnGlobalTypeChanged(string value)
        {
            SetAreaGlobalType(SelectedArea, value);
        }

        partial void OnSwapBytesChanged(bool value)
        {
            SetAreaSwapBytes(SelectedArea, value);
        }

        partial void OnSwapWordsChanged(bool value)
        {
            SetAreaSwapWords(SelectedArea, value);
        }

        partial void OnHoldingMonitorEnabledChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else if (!AnyMonitorEnabled())
            {
                StopPolling();
            }
        }

        partial void OnInputRegistersMonitorEnabledChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else if (!AnyMonitorEnabled())
            {
                StopPolling();
            }
        }

        partial void OnCoilsMonitorEnabledChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else if (!AnyMonitorEnabled())
            {
                StopPolling();
            }
        }

        partial void OnDiscreteInputsMonitorEnabledChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else if (!AnyMonitorEnabled())
            {
                StopPolling();
            }
        }

        partial void OnIsCustomWatchMonitoringChanged(bool value)
        {
            if (value)
            {
                StartCustomWatchMonitoring();
            }
            else
            {
                StopCustomWatchMonitoring();
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(CustomEntries))
            {
                HookCustomEntries();
                HookTrendPens();
                UpdateCustomWatchMonitoringState();
            }
        }

        private ObservableCollection<CustomEntry>? _hookedCustomEntries;

        private void HookCustomEntries()
        {
            if (_hookedCustomEntries == CustomEntries) return;

            if (_hookedCustomEntries != null)
            {
                _hookedCustomEntries.CollectionChanged -= OnCustomEntriesCollectionChanged;
                foreach (var entry in _hookedCustomEntries)
                {
                    entry.PropertyChanged -= OnCustomEntryPropertyChanged;
                }
            }

            _hookedCustomEntries = CustomEntries;

            if (_hookedCustomEntries != null)
            {
                _hookedCustomEntries.CollectionChanged += OnCustomEntriesCollectionChanged;
                foreach (var entry in _hookedCustomEntries)
                {
                    entry.PropertyChanged += OnCustomEntryPropertyChanged;
                }
            }
        }

        private void OnCustomEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (CustomEntry item in e.NewItems)
                {
                    item.PropertyChanged += OnCustomEntryPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (CustomEntry item in e.OldItems)
                {
                    item.PropertyChanged -= OnCustomEntryPropertyChanged;
                }
            }

            UpdateCustomWatchMonitoringState();
        }

        private void OnCustomEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CustomEntry.Monitor) or nameof(CustomEntry.Continuous))
            {
                UpdateCustomWatchMonitoringState();
            }
        }

        private void UpdateCustomWatchMonitoringState()
        {
            // Trend pens poll in the same loop: a unit with pens but no
            // monitored watch entries still needs the loop running.
            var shouldMonitor = CustomEntries.Any(entry => entry.Monitor || entry.Continuous)
                || CurrentConfig.TrendPens.Count > 0;
            if (IsCustomWatchMonitoring != shouldMonitor)
            {
                IsCustomWatchMonitoring = shouldMonitor;
            }
        }

        // The current unit's pen collection is hooked (like the entries
        // collection) so any pen mutation - Trends view, context menus, API
        // - re-evaluates whether the polling loop must run.
        private ObservableCollection<TrendPen>? _hookedTrendPens;

        private void HookTrendPens()
        {
            var pens = CurrentConfig.TrendPens;
            if (_hookedTrendPens == pens) return;

            if (_hookedTrendPens != null)
            {
                _hookedTrendPens.CollectionChanged -= OnTrendPensCollectionChanged;
            }

            _hookedTrendPens = pens;
            pens.CollectionChanged += OnTrendPensCollectionChanged;
        }

        private void OnTrendPensCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateCustomWatchMonitoringState();
        }

        partial void OnActiveProfileChanged(ConnectionProfile? value)
        {
            RefreshConnectionDependentProperties();
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            ToggleConnectionCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsBusyChanged(bool value)
        {
            OnPropertyChanged(nameof(DebugSummary));
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            ToggleConnectionCommand.NotifyCanExecuteChanged();
            ReadHoldingRegistersCommand.NotifyCanExecuteChanged();
            ReadInputRegistersCommand.NotifyCanExecuteChanged();
            ReadCoilsCommand.NotifyCanExecuteChanged();
            ReadDiscreteInputsCommand.NotifyCanExecuteChanged();
            WriteHoldingRegisterCommand.NotifyCanExecuteChanged();
            WriteCoilCommand.NotifyCanExecuteChanged();
            ReadCustomNowCommand.NotifyCanExecuteChanged();
            WriteCustomNowCommand.NotifyCanExecuteChanged();
            ReadAllCustomNowCommand.NotifyCanExecuteChanged();
            ExportUnitIdCommand.NotifyCanExecuteChanged();
            ImportUnitIdAsCommand.NotifyCanExecuteChanged();
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

        private bool CanRead() => CanRead(SelectedArea);

        private bool CanRead(PlcArea area)
        {
            if (ActiveProfile is not { IsConnected: true } || IsBusy)
                return false;

            var (start, count) = GetAreaStartCount(area);
            var validator = new ModbusAddressValidator();
            return area is PlcArea.HoldingRegister or PlcArea.InputRegister
                ? validator.IsValidAddressRange(start, count)
                : validator.IsValidRange(start, count, area);
        }

        private bool CanWrite() => CanWrite(SelectedArea);

        private bool CanWrite(PlcArea area) => ActiveProfile is { IsConnected: true } && !IsBusy &&
                                               (area is PlcArea.HoldingRegister or PlcArea.Coil);

        private (int Start, int Count) GetAreaStartCount(PlcArea area) => (GetAreaStart(area), GetAreaCount(area));

        private int GetAreaStart(PlcArea area)
        {
            return area switch
            {
                PlcArea.HoldingRegister => HoldingRegisterStart,
                PlcArea.InputRegister => InputRegisterStart,
                PlcArea.Coil => CoilStart,
                PlcArea.DiscreteInput => DiscreteInputStart,
                _ => 0
            };
        }

        private int GetAreaCount(PlcArea area)
        {
            return area switch
            {
                PlcArea.HoldingRegister => HoldingRegisterCount,
                PlcArea.InputRegister => InputRegisterCount,
                PlcArea.Coil => CoilCount,
                PlcArea.DiscreteInput => DiscreteInputCount,
                _ => 0
            };
        }

        private string GetAreaGlobalType(PlcArea area)
        {
            return area switch
            {
                PlcArea.HoldingRegister => RegistersGlobalType,
                PlcArea.InputRegister => InputRegistersGlobalType,
                _ => "int"
            };
        }

        private bool GetAreaSwapBytes(PlcArea area)
        {
            return area switch
            {
                PlcArea.HoldingRegister => RegistersSwapBytes,
                PlcArea.InputRegister => InputRegistersSwapBytes,
                _ => false
            };
        }

        private bool GetAreaSwapWords(PlcArea area)
        {
            return area switch
            {
                PlcArea.HoldingRegister => RegistersSwapWords,
                PlcArea.InputRegister => InputRegistersSwapWords,
                _ => false
            };
        }

        private void SetAreaStart(PlcArea area, int value)
        {
            switch (area)
            {
                case PlcArea.HoldingRegister: HoldingRegisterStart = value; break;
                case PlcArea.InputRegister: InputRegisterStart = value; break;
                case PlcArea.Coil: CoilStart = value; break;
                case PlcArea.DiscreteInput: DiscreteInputStart = value; break;
            }
        }

        private void SetAreaCount(PlcArea area, int value)
        {
            switch (area)
            {
                case PlcArea.HoldingRegister: HoldingRegisterCount = value; break;
                case PlcArea.InputRegister: InputRegisterCount = value; break;
                case PlcArea.Coil: CoilCount = value; break;
                case PlcArea.DiscreteInput: DiscreteInputCount = value; break;
            }
        }

        private void SetAreaGlobalType(PlcArea area, string value)
        {
            if (area == PlcArea.HoldingRegister) RegistersGlobalType = value;
            else if (area == PlcArea.InputRegister) InputRegistersGlobalType = value;
        }

        private void SetAreaSwapBytes(PlcArea area, bool value)
        {
            if (area == PlcArea.HoldingRegister) RegistersSwapBytes = value;
            else if (area == PlcArea.InputRegister) InputRegistersSwapBytes = value;
        }

        private void SetAreaSwapWords(PlcArea area, bool value)
        {
            if (area == PlcArea.HoldingRegister) RegistersSwapWords = value;
            else if (area == PlcArea.InputRegister) InputRegistersSwapWords = value;
        }

        private bool AnyMonitorEnabled() => HoldingMonitorEnabled || InputRegistersMonitorEnabled || CoilsMonitorEnabled || DiscreteInputsMonitorEnabled;

        private bool CanRemoveCustomEntry() => SelectedCustomEntry != null && !IsBusy;

        private bool CanReadCustomEntry() => SelectedCustomEntry != null && ActiveProfile is { IsConnected: true } && !IsBusy;

        private bool CanReadCustomEntry(CustomEntry? entry) => entry != null && ActiveProfile is { IsConnected: true } && !IsBusy;

        private bool CanWriteCustomEntry() => SelectedCustomEntry != null && ActiveProfile is { IsConnected: true } && !IsBusy;

        private bool CanWriteCustomEntry(CustomEntry? entry) => entry != null && ActiveProfile is { IsConnected: true } && !IsBusy;

        private bool CanReadAllCustomEntries() => CustomEntries.Count > 0 && ActiveProfile is { IsConnected: true } && !IsBusy;

        private bool CanSaveCustom() => _customEntryService != null && !IsBusy;

        private bool CanLoadCustom() => _customEntryService != null && !IsBusy;

        private bool CanSaveProject() => _fileDialogService != null && !IsBusy;

        private bool CanLoadProject() => _fileDialogService != null && !IsBusy;

        private bool CanExportUnitId() => _fileDialogService != null && IsServerMode && !IsBusy;

        private bool CanImportUnitIdAs() => _fileDialogService != null && _inputDialogService != null && IsServerMode && !IsBusy;

        private void UnitConfigurationStore_SelectedUnitIdChanged(object? sender, EventArgs e)
        {
            if (_isApplyingUnitConfiguration)
            {
                return;
            }

            ApplyCurrentUnitConfiguration();
            OnPropertyChanged(nameof(SelectedUnitId));
            OnPropertyChanged(nameof(CurrentConfig));
            OnPropertyChanged(nameof(CustomEntries));
            OnPropertyChanged(nameof(EffectiveUnitId));
            ReadAllCustomNowCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            LoadProjectCommand.NotifyCanExecuteChanged();
            ExportUnitIdCommand.NotifyCanExecuteChanged();
            ImportUnitIdAsCommand.NotifyCanExecuteChanged();
        }

        private void UnitConfigurationStore_AvailableUnitIdsChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(AvailableUnitIds));
            OnPropertyChanged(nameof(UnitConfigurations));
            OnPropertyChanged(nameof(SelectedUnitId));
        }

        /// <summary>
        /// Copies the live Avalonia controls' state into the selected Unit ID
        /// configuration. The store owns the configuration object; this method is
        /// the compatibility bridge for the existing per-area view-model fields.
        /// </summary>
        private void SyncCurrentUnitConfiguration()
        {
            if (_isApplyingUnitConfiguration)
            {
                return;
            }

            var configuration = _unitConfigurationStore.GetOrCreateConfiguration(_unitConfigurationStore.SelectedUnitId);
            var settings = configuration.RegisterSettings;
            settings.RegisterStart = HoldingRegisterStart;
            settings.RegisterCount = HoldingRegisterCount;
            settings.WriteRegisterAddress = 1;
            settings.RegistersGlobalType = RegistersGlobalType;
            settings.RegistersSwapBytes = RegistersSwapBytes;
            settings.RegistersSwapWords = RegistersSwapWords;
            settings.CoilStart = CoilStart;
            settings.CoilCount = CoilCount;
            settings.InputRegisterStart = InputRegisterStart;
            settings.InputRegisterCount = InputRegisterCount;
            settings.InputRegistersGlobalType = InputRegistersGlobalType;
            settings.InputRegistersSwapBytes = InputRegistersSwapBytes;
            settings.InputRegistersSwapWords = InputRegistersSwapWords;
            settings.DiscreteInputStart = DiscreteInputStart;
            settings.DiscreteInputCount = DiscreteInputCount;
            var holdingMetadata = HoldingRegisters
                .Select(entry => new RegisterMetadata
                {
                    Address = entry.Address,
                    Type = entry.Type ?? settings.RegistersGlobalType,
                    SwapBytes = entry.SwapBytes,
                    SwapWords = entry.SwapWords
                })
                .ToList();
            if (holdingMetadata.Count > 0)
            {
                settings.HoldingRegisterMetadata = holdingMetadata;
            }

            var inputMetadata = InputRegisters
                .Select(entry => new RegisterMetadata
                {
                    Address = entry.Address,
                    Type = entry.Type ?? settings.InputRegistersGlobalType,
                    SwapBytes = entry.SwapBytes,
                    SwapWords = entry.SwapWords
                })
                .ToList();
            if (inputMetadata.Count > 0)
            {
                settings.InputRegisterMetadata = inputMetadata;
            }

            var monitoring = configuration.MonitoringSettings;
            monitoring.HoldingMonitorEnabled = HoldingMonitorEnabled;
            monitoring.HoldingMonitorPeriodMs = HoldingMonitorPeriodMs;
            monitoring.InputRegistersMonitorEnabled = InputRegistersMonitorEnabled;
            monitoring.InputRegistersMonitorPeriodMs = InputRegistersMonitorPeriodMs;
            monitoring.CoilsMonitorEnabled = CoilsMonitorEnabled;
            monitoring.CoilsMonitorPeriodMs = CoilsMonitorPeriodMs;
            monitoring.DiscreteInputsMonitorEnabled = DiscreteInputsMonitorEnabled;
            monitoring.DiscreteInputsMonitorPeriodMs = DiscreteInputsMonitorPeriodMs;
            monitoring.CustomMonitorEnabled = IsCustomWatchMonitoring;
            monitoring.CustomReadMonitorEnabled = IsCustomWatchMonitoring;

            if (VisualNodeEditorViewModel != null)
            {
                configuration.SimulationSettings.VisualNodes = new ObservableCollection<VisualNode>(VisualNodeEditorViewModel.Nodes);
                configuration.SimulationSettings.VisualConnections = new ObservableCollection<NodeConnection>(VisualNodeEditorViewModel.Connections);
            }
        }

        /// <summary>
        /// Applies the selected Unit ID's persisted settings to the existing
        /// Avalonia controls without replacing the live collections.
        /// </summary>
        private void ApplyCurrentUnitConfiguration()
        {
            var configuration = _unitConfigurationStore.CurrentConfig;
            var settings = configuration.RegisterSettings;
            var monitoring = configuration.MonitoringSettings;

            _isApplyingUnitConfiguration = true;
            try
            {
                HoldingRegisterStart = Math.Max(0, settings.RegisterStart);
                HoldingRegisterCount = Math.Max(1, settings.RegisterCount);
                RegistersGlobalType = settings.RegistersGlobalType ?? "int";
                RegistersSwapBytes = settings.RegistersSwapBytes;
                RegistersSwapWords = settings.RegistersSwapWords;
                InputRegisterStart = Math.Max(0, settings.InputRegisterStart);
                InputRegisterCount = Math.Max(1, settings.InputRegisterCount);
                InputRegistersGlobalType = settings.InputRegistersGlobalType ?? "int";
                InputRegistersSwapBytes = settings.InputRegistersSwapBytes;
                InputRegistersSwapWords = settings.InputRegistersSwapWords;
                CoilStart = Math.Max(0, settings.CoilStart);
                CoilCount = Math.Max(1, settings.CoilCount);
                DiscreteInputStart = Math.Max(0, settings.DiscreteInputStart);
                DiscreteInputCount = Math.Max(1, settings.DiscreteInputCount);
                HoldingMonitorEnabled = monitoring.HoldingMonitorEnabled;
                HoldingMonitorPeriodMs = monitoring.HoldingMonitorPeriodMs;
                InputRegistersMonitorEnabled = monitoring.InputRegistersMonitorEnabled;
                InputRegistersMonitorPeriodMs = monitoring.InputRegistersMonitorPeriodMs;
                CoilsMonitorEnabled = monitoring.CoilsMonitorEnabled;
                CoilsMonitorPeriodMs = monitoring.CoilsMonitorPeriodMs;
                DiscreteInputsMonitorEnabled = monitoring.DiscreteInputsMonitorEnabled;
                DiscreteInputsMonitorPeriodMs = monitoring.DiscreteInputsMonitorPeriodMs;
                IsCustomWatchMonitoring = monitoring.CustomMonitorEnabled;

                ApplyRegisterMetadata(HoldingRegisters, settings.HoldingRegisterMetadata);
                ApplyRegisterMetadata(InputRegisters, settings.InputRegisterMetadata);
                SelectedCustomEntry = CustomEntries.FirstOrDefault();
            }
            finally
            {
                _isApplyingUnitConfiguration = false;
            }

            OnPropertyChanged(nameof(CurrentConfig));
            OnPropertyChanged(nameof(CustomEntries));
            ReadAllCustomNowCommand.NotifyCanExecuteChanged();
        }

        private static void ApplyRegisterMetadata(
            IEnumerable<RegisterEntry> entries,
            IEnumerable<RegisterMetadata>? metadata)
        {
            if (metadata == null)
            {
                return;
            }

            var byAddress = metadata.ToDictionary(item => item.Address);
            foreach (var entry in entries)
            {
                if (!byAddress.TryGetValue(entry.Address, out var saved))
                {
                    continue;
                }

                entry.Type = saved.Type ?? "int";
                entry.SwapBytes = saved.SwapBytes;
                entry.SwapWords = saved.SwapWords;
            }
        }

        private void ActiveProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Profile properties can change from a worker thread (e.g. when the
            // service detects a lost connection). The notifications below feed
            // UI-bound properties, so re-post to the dispatcher when needed.
            if (!_dispatcher.CheckAccess)
            {
                _ = _dispatcher.InvokeAsync(() => ActiveProfile_PropertyChanged(sender, e));
                return;
            }

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
            // May be raised from a worker thread when the service detects a lost
            // connection; marshal the UI-facing work to the dispatcher (the
            // dispatcher adapter runs inline when already on the UI thread).
            _ = _dispatcher.InvokeAsync(() => OnProfileDisconnected(e));
        }

        private void OnConnectionProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasConnectionProfiles));
            OnPropertyChanged(nameof(ShowNoProfilesPlaceholder));
        }

        private void OnProfileDisconnected(ConnectionProfile e)
        {
            _logger.LogInformation("Profile disconnected: {Name}", e.Name);
            OnPropertyChanged(nameof(ServerIpAddress));
            OnPropertyChanged(nameof(ActiveProfileDisplayName));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(IsConnectionErrorVisible));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(DebugSummary));
            OnPropertyChanged(nameof(ToggleConnectionButtonText));
            ToggleConnectionCommand.NotifyCanExecuteChanged();
            _trendLogger?.Stop();
            StopPolling();
            StopCustomWatchMonitoring();
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
            // A new attempt supersedes the previous failure; otherwise the
            // error state (and any watcher of it, e.g. the REST API connect
            // endpoint) would react to a stale failure while the fresh attempt
            // is still in flight.
            HasConnectionError = false;
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

        private async Task ToggleConnectionAsync()
        {
            _logger.LogInformation("ToggleConnectionAsync invoked");
            if (IsConnected)
                await DisconnectAsync();
            else
                await ConnectAsync();
        }

        private async Task ReadAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            await ReadAreaWithBusyAsync(SelectedArea);
        }

        private async Task ReadHoldingRegistersAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            SelectedArea = PlcArea.HoldingRegister;
            await ReadAreaWithBusyAsync(PlcArea.HoldingRegister);
        }

        private async Task ReadInputRegistersAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            SelectedArea = PlcArea.InputRegister;
            await ReadAreaWithBusyAsync(PlcArea.InputRegister);
        }

        private async Task ReadCoilsAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            SelectedArea = PlcArea.Coil;
            await ReadAreaWithBusyAsync(PlcArea.Coil);
        }

        private async Task ReadDiscreteInputsAsync()
        {
            if (ActiveProfile == null || ActiveService == null) return;
            SelectedArea = PlcArea.DiscreteInput;
            await ReadAreaWithBusyAsync(PlcArea.DiscreteInput);
        }

        private async Task ReadAreaWithBusyAsync(PlcArea area)
        {
            IsBusy = true;
            try
            {
                await Task.Run(() => ReadAreaAsync(area, CancellationToken.None));
            }
            catch (Exception ex)
            {
                _dispatcher.Invoke(() => StatusMessage = $"Read error: {ex.Message}");
                _logger.LogError(ex, "Manual read failed");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task WriteAsync() => await PromptAndWriteAsync(SelectedArea);

        private async Task WriteHoldingRegisterAsync() => await PromptAndWriteAsync(PlcArea.HoldingRegister);

        private async Task WriteCoilAsync() => await PromptAndWriteAsync(PlcArea.Coil);

        private async Task PromptAndWriteAsync(PlcArea area)
        {
            if (ActiveProfile == null || ActiveService == null || _inputDialogService == null) return;
            if (area is not (PlcArea.HoldingRegister or PlcArea.Coil)) return;

            var address = PromptAddress($"Write {area}", GetAreaStart(area));
            if (!address.HasValue) return;

            try
            {
                IsBusy = true;
                await WriteValueToAreaAsync(area, address.Value);
                await ReadAreaAsync(area, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Write failed");
                _dispatcher.Invoke(() => StatusMessage = $"Write error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task WriteValueToAreaAsync(PlcArea area, int address)
        {
            if (_inputDialogService == null || ActiveService == null)
                return;

            var service = ActiveService;
            if (service == null) return;

            await _modbusIoGate.WaitAsync(CancellationToken.None);
            try
            {
                var unitId = EffectiveUnitId;

                if (area == PlcArea.HoldingRegister)
                {
                    var valueText = _inputDialogService.TryGetInput("Write Value", "Value:", "0", out var input) ? input : null;
                    if (string.IsNullOrWhiteSpace(valueText) || !ushort.TryParse(valueText, out var value))
                    {
                        _dispatcher.Invoke(() => StatusMessage = "Invalid register value.");
                        return;
                    }

                    await service.WriteSingleRegisterAsync(unitId, address, value);
                    _dispatcher.Invoke(() => StatusMessage = $"Wrote {value} to holding register {address}.");
                }
                else if (area == PlcArea.Coil)
                {
                    var valueText = _inputDialogService.TryGetInput("Write Coil", "Value (true/false):", "false", out var input) ? input : null;
                    if (string.IsNullOrWhiteSpace(valueText) || !TryParseBool(valueText, out var value))
                    {
                        _dispatcher.Invoke(() => StatusMessage = "Invalid coil value. Use true/false, 1/0, on/off.");
                        return;
                    }

                    await service.WriteSingleCoilAsync(unitId, address, value);
                    _dispatcher.Invoke(() => StatusMessage = $"Wrote {value} to coil {address}.");
                }
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }

        public async Task WriteHoldingRegisterFromEditAsync(RegisterEntry? entry)
        {
            if (entry == null || ActiveProfile == null || ActiveService == null) return;

            IsBusy = true;
            try
            {
                var unitId = EffectiveUnitId;
                var type = (entry.Type ?? "int").ToLowerInvariant();
                var text = (entry.ValueText ?? string.Empty).Trim().Replace(',', '.');

                await _modbusIoGate.WaitAsync(CancellationToken.None);
                try
                {
                    switch (type)
                    {
                        case "real":
                            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                            {
                                var words = DataTypeConverter.ToUInt16(f, RegistersSwapBytes, RegistersSwapWords);
                                await ActiveService.WriteRegistersAsync(unitId, entry.Address, words);
                            }
                            else
                            {
                                _dispatcher.Invoke(() => StatusMessage = $"Invalid float value: {entry.ValueText}");
                                return;
                            }
                            break;

                        case "string":
                            var stringWords = DataTypeConverter.ToUInt16(text);
                            await ActiveService.WriteRegistersAsync(unitId, entry.Address, stringWords);
                            break;

                        case "int":
                            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                            {
                                await ActiveService.WriteSingleRegisterAsync(unitId, entry.Address, unchecked((ushort)iv));
                            }
                            else
                            {
                                _dispatcher.Invoke(() => StatusMessage = $"Invalid integer value: {entry.ValueText}");
                                return;
                            }
                            break;

                        default:
                            if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uv) && uv <= ushort.MaxValue)
                            {
                                await ActiveService.WriteSingleRegisterAsync(unitId, entry.Address, (ushort)uv);
                            }
                            else
                            {
                                _dispatcher.Invoke(() => StatusMessage = $"Invalid unsigned value: {entry.ValueText}");
                                return;
                            }
                            break;
                    }
                }
                finally
                {
                    _modbusIoGate.Release();
                }

                await ReadAreaAsync(PlcArea.HoldingRegister, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Holding register write failed");
                _dispatcher.Invoke(() => StatusMessage = $"Write error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task WriteCoilFromEditAsync(CoilEntry? entry)
        {
            if (entry == null || ActiveProfile == null || ActiveService == null) return;

            IsBusy = true;
            try
            {
                await _modbusIoGate.WaitAsync(CancellationToken.None);
                try
                {
                    await ActiveService.WriteSingleCoilAsync(EffectiveUnitId, entry.Address, entry.State);
                }
                finally
                {
                    _modbusIoGate.Release();
                }

                await ReadAreaAsync(PlcArea.Coil, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Coil write failed");
                _dispatcher.Invoke(() => StatusMessage = $"Write error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private int? PromptAddress(string title, int defaultAddress)
        {
            if (_inputDialogService == null) return null;

            var defaultValue = defaultAddress.ToString(CultureInfo.InvariantCulture);
            if (!_inputDialogService.TryGetInput(title, "Address:", defaultValue, out var input) ||
                !int.TryParse(input, out var address))
            {
                _dispatcher.Invoke(() => StatusMessage = "Invalid address.");
                return null;
            }

            if (address < 0)
            {
                _dispatcher.Invoke(() => StatusMessage = "Address cannot be negative.");
                return null;
            }

            return address;
        }

        private void StartPolling()
        {
            if (_disposed || ActiveProfile is not { IsConnected: true } || !AnyMonitorEnabled()) return;

            lock (_pollLifecycleLock)
            {
                if (_pollCts != null) return;

                var cts = new CancellationTokenSource();
                _pollCts = cts;
                _ = Task.Run(() => PollLoopAsync(cts), cts.Token);
            }
        }

        private void StopPolling()
        {
            lock (_pollLifecycleLock)
            {
                _pollCts?.Cancel();
            }

            lock (_pendingPollLock)
            {
                _pendingPollAreas.Clear();
            }
        }

        private async Task PollLoopAsync(CancellationTokenSource loopCts)
        {
            var token = loopCts.Token;
            try
            {
                while (!token.IsCancellationRequested && ActiveProfile is { IsConnected: true } && AnyMonitorEnabled())
                {
                    var now = DateTime.UtcNow;
                    QueueDueAreaReads(now);
                    await DrainPendingAreaReadsAsync(token);
                    await Task.Delay(PollLoopIntervalMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Avalonia polling loop canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Avalonia polling loop failed");
                await _dispatcher.InvokeAsync(() => StatusMessage = $"Poll error: {ex.Message}");
            }
            finally
            {
                var restart = false;
                lock (_pollLifecycleLock)
                {
                    if (ReferenceEquals(_pollCts, loopCts))
                    {
                        _pollCts = null;
                        restart = !_disposed && ActiveProfile is { IsConnected: true } && AnyMonitorEnabled();
                    }
                }

                loopCts.Dispose();
                if (restart)
                {
                    StartPolling();
                }
            }
        }

        private void QueueDueAreaReads(DateTime now)
        {
            QueueAreaReadIfDue(PlcArea.HoldingRegister, HoldingMonitorEnabled, HoldingMonitorPeriodMs, _lastHoldingReadUtc, now, value => _lastHoldingReadUtc = value);
            QueueAreaReadIfDue(PlcArea.InputRegister, InputRegistersMonitorEnabled, InputRegistersMonitorPeriodMs, _lastInputRegReadUtc, now, value => _lastInputRegReadUtc = value);
            QueueAreaReadIfDue(PlcArea.Coil, CoilsMonitorEnabled, CoilsMonitorPeriodMs, _lastCoilsReadUtc, now, value => _lastCoilsReadUtc = value);
            QueueAreaReadIfDue(PlcArea.DiscreteInput, DiscreteInputsMonitorEnabled, DiscreteInputsMonitorPeriodMs, _lastDiscreteReadUtc, now, value => _lastDiscreteReadUtc = value);
        }

        private void QueueAreaReadIfDue(
            PlcArea area,
            bool enabled,
            int periodMs,
            DateTime lastReadUtc,
            DateTime now,
            Action<DateTime> setLastReadUtc)
        {
            if (!enabled || (now - lastReadUtc).TotalMilliseconds < Math.Max(MinimumMonitorPeriodMs, periodMs))
                return;

            lock (_pendingPollLock)
            {
                _pendingPollAreas.Add(area);
            }

            setLastReadUtc(now);
        }

        private async Task DrainPendingAreaReadsAsync(CancellationToken token)
        {
            PlcArea[] pending;
            lock (_pendingPollLock)
            {
                pending = _pendingPollAreas.ToArray();
                _pendingPollAreas.Clear();
            }

            foreach (var area in pending)
            {
                token.ThrowIfCancellationRequested();
                if (IsAreaMonitorEnabled(area))
                {
                    await ReadAreaAsync(area, token, true);
                }
            }
        }

        private async Task ReadAreaAsync(PlcArea area, CancellationToken token, bool isMonitoring = false)
        {
            await _modbusIoGate.WaitAsync(token);
            try
            {
                var service = ActiveService;
                if (service == null || ActiveProfile == null)
                {
                    await _dispatcher.InvokeAsync(() => StatusMessage = "No active service.");
                    return;
                }

                if (!service.IsConnected)
                {
                    await _dispatcher.InvokeAsync(() => StatusMessage = "Not connected to a Modbus device.");
                    return;
                }

                var unitId = EffectiveUnitId;
                var (start, count) = GetAreaStartCount(area);

                await _dispatcher.InvokeAsync(() => StatusMessage = $"Reading {area}...");
                token.ThrowIfCancellationRequested();

                switch (area)
                {
                    case PlcArea.HoldingRegister:
                        var holding = await service.ReadHoldingRegistersAsync(unitId, start, count)
                            ?? throw new InvalidOperationException("Read returned no response.");
                        var holdingPartial = holding.Length < count;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            SyncTags(PlcArea.HoldingRegister, start, holding);
                            if (IsRegisterGridEditing) return;
                            HoldingRegisters = ApplyRegisterValues(
                                HoldingRegisters,
                                start,
                                holding,
                                RegistersGlobalType,
                                RegistersSwapBytes,
                                RegistersSwapWords,
                                CurrentConfig.RegisterSettings.HoldingRegisterMetadata,
                                holdingPartial);
                            StatusMessage = holdingPartial
                                ? $"Partial read: {holding.Length} of {count} holding registers"
                                : $"Read {holding.Length} holding registers";
                        });
                        break;

                    case PlcArea.InputRegister:
                        var input = await service.ReadInputRegistersAsync(unitId, start, count)
                            ?? throw new InvalidOperationException("Read returned no response.");
                        var inputPartial = input.Length < count;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            SyncTags(PlcArea.InputRegister, start, input);
                            if (IsRegisterGridEditing) return;
                            InputRegisters = ApplyRegisterValues(
                                InputRegisters,
                                start,
                                input,
                                InputRegistersGlobalType,
                                InputRegistersSwapBytes,
                                InputRegistersSwapWords,
                                CurrentConfig.RegisterSettings.InputRegisterMetadata,
                                inputPartial);
                            StatusMessage = inputPartial
                                ? $"Partial read: {input.Length} of {count} input registers"
                                : $"Read {input.Length} input registers";
                        });
                        break;

                    case PlcArea.Coil:
                        var coils = await service.ReadCoilsAsync(unitId, start, count)
                            ?? throw new InvalidOperationException("Read returned no response.");
                        var coilsPartial = coils.Length < count;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            SyncTags(PlcArea.Coil, start, coils);
                            if (IsRegisterGridEditing) return;
                            Coils = ApplyCoilValues(Coils, start, coils, coilsPartial);
                            StatusMessage = coilsPartial
                                ? $"Partial read: {coils.Length} of {count} coils"
                                : $"Read {coils.Length} coils";
                        });
                        break;

                    case PlcArea.DiscreteInput:
                        var discrete = await service.ReadDiscreteInputsAsync(unitId, start, count)
                            ?? throw new InvalidOperationException("Read returned no response.");
                        var discretePartial = discrete.Length < count;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            SyncTags(PlcArea.DiscreteInput, start, discrete);
                            if (IsRegisterGridEditing) return;
                            DiscreteInputs = ApplyCoilValues(DiscreteInputs, start, discrete, discretePartial);
                            StatusMessage = discretePartial
                                ? $"Partial read: {discrete.Length} of {count} discrete inputs"
                                : $"Read {discrete.Length} discrete inputs";
                        });
                        break;
                }

                ClearMonitorFailure(area);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await HandleAreaReadFailureAsync(area, ex, isMonitoring);
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }

        /// <summary>
        /// Feeds freshly read values into the tag database so symbolic tags, the
        /// Tag Browser and the Watch Window see live data (values, freshness and
        /// alarms). Runs on the UI thread inside the read's dispatcher callback.
        /// No-op when no tags exist — the common case.
        /// </summary>
        private void SyncTags(PlcArea area, int start, ushort[] values)
        {
            var tagService = _tagService;
            if (tagService == null || tagService.Tags.Count == 0) return;

            // Batch update: multi-word tags (Float, Double, ...) are converted
            // from all of their words at once instead of being fed one word.
            tagService.UpdateRegisterValues(area, start, values);
        }

        private void SyncTags(PlcArea area, int start, bool[] values)
        {
            var tagService = _tagService;
            if (tagService == null || tagService.Tags.Count == 0) return;

            for (var i = 0; i < values.Length; i++)
                tagService.UpdateTagValue(area, start + i, values[i]);
        }

        private bool IsAreaMonitorEnabled(PlcArea area) => area switch
        {
            PlcArea.HoldingRegister => HoldingMonitorEnabled,
            PlcArea.InputRegister => InputRegistersMonitorEnabled,
            PlcArea.Coil => CoilsMonitorEnabled,
            PlcArea.DiscreteInput => DiscreteInputsMonitorEnabled,
            _ => false
        };

        private async Task HandleAreaReadFailureAsync(PlcArea area, Exception exception, bool isMonitoring)
        {
            var failureTime = DateTime.UtcNow;
            int failureCount;
            lock (_monitorFailureLock)
            {
                failureCount = GetMonitorFailureCountUnsafe(area) + 1;
                _monitorFailureCounts[area] = failureCount;
                _lastMonitorFailureUtc[area] = failureTime;
            }

            LastErrorTime = failureTime;
            HasConnectionError = true;
            _logger.LogError(exception, "Error reading {Area} (failure {FailureCount})", area, failureCount);

            var message = $"Failed to read {area}: {exception.Message}";
            if (isMonitoring && IsAreaMonitorEnabled(area))
            {
                message += "\n\nContinuous monitoring has been paused. Fix the issue and re-enable monitoring.";
                await _dispatcher.InvokeAsync(() => SetAreaMonitorEnabled(area, false));
            }

            await _dispatcher.InvokeAsync(() => StatusMessage = message);

            // A modal is only warranted for an explicit read the user just clicked:
            // every monitoring tick that fails while several areas are armed would
            // otherwise stack one dialog per area (and the connection-loss chain
            // already tells the user when the link itself drops).
            if (!isMonitoring && _messageBoxService != null)
            {
                Task<DialogResult>? dialogTask = null;
                await _dispatcher.InvokeAsync(() =>
                    dialogTask = _messageBoxService.ShowAsync(message, "Read Error", DialogButton.Ok, DialogIcon.Error));
                if (dialogTask != null)
                {
                    await dialogTask;
                }
            }
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
                HasConnectionError = false;
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

        private void SetAreaMonitorEnabled(PlcArea area, bool enabled)
        {
            switch (area)
            {
                case PlcArea.HoldingRegister:
                    HoldingMonitorEnabled = enabled;
                    break;
                case PlcArea.InputRegister:
                    InputRegistersMonitorEnabled = enabled;
                    break;
                case PlcArea.Coil:
                    CoilsMonitorEnabled = enabled;
                    break;
                case PlcArea.DiscreteInput:
                    DiscreteInputsMonitorEnabled = enabled;
                    break;
            }
        }

        private ObservableCollection<RegisterEntry> ApplyRegisterValues(
            ObservableCollection<RegisterEntry>? target,
            int start,
            ushort[] values,
            string globalType,
            bool swapBytes,
            bool swapWords,
            IEnumerable<RegisterMetadata>? metadata = null,
            bool isPartialRead = false)
        {
            target ??= new ObservableCollection<RegisterEntry>();
            var metadataByAddress = metadata?.ToDictionary(item => item.Address)
                                    ?? new Dictionary<int, RegisterMetadata>();

            var entriesByAddress = target.ToDictionary(e => e.Address);
            var usedAddresses = new HashSet<int>();

            int idx = 0;
            while (idx < values.Length)
            {
                var address = start + idx;
                var savedMetadata = metadataByAddress.GetValueOrDefault(address);
                var type = (savedMetadata?.Type ?? globalType).ToLowerInvariant();
                usedAddresses.Add(address);

                if (!entriesByAddress.TryGetValue(address, out var entry))
                {
                    entry = new RegisterEntry();
                    target.Add(entry);
                    entriesByAddress[address] = entry;
                }

                entry.Address = address;
                entry.Value = values[idx];
                entry.Type = type;
                entry.SwapBytes = savedMetadata?.SwapBytes ?? swapBytes;
                entry.SwapWords = savedMetadata?.SwapWords ?? swapWords;
                entry.IsReadError = false;
                entry.ReadErrorMessage = null;

                switch (type)
                {
                    case "int":
                        entry.ValueText = unchecked((short)values[idx]).ToString(CultureInfo.InvariantCulture);
                        idx += 1;
                        break;

                    case "real":
                        if (idx + 1 < values.Length)
                        {
                            entry.ValueText = DataTypeConverter.ToSingle(values[idx], values[idx + 1], entry.SwapBytes, entry.SwapWords).ToString(CultureInfo.InvariantCulture);

                            var nextAddress = address + 1;
                            usedAddresses.Add(nextAddress);
                            if (!entriesByAddress.TryGetValue(nextAddress, out var next))
                            {
                                next = new RegisterEntry();
                                target.Add(next);
                                entriesByAddress[nextAddress] = next;
                            }

                            next.Address = nextAddress;
                            next.Value = values[idx + 1];
                            next.Type = type;
                            next.SwapBytes = swapBytes;
                            next.SwapWords = swapWords;
                            next.ValueText = string.Empty;
                            next.IsReadError = false;
                            next.ReadErrorMessage = null;

                            idx += 2;
                        }
                        else
                        {
                            entry.ValueText = values[idx].ToString(CultureInfo.InvariantCulture);
                            idx += 1;
                        }
                        break;

                    case "string":
                        entry.ValueText = DataTypeConverter.ToString(values[idx]);
                        idx += 1;
                        break;

                    default:
                        entry.ValueText = values[idx].ToString(CultureInfo.InvariantCulture);
                        idx += 1;
                        break;
                }
            }

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!usedAddresses.Contains(target[i].Address))
                {
                    target.RemoveAt(i);
                }
            }

            if (isPartialRead)
            {
                var lastVisible = target
                    .Where(e => !string.IsNullOrEmpty(e.ValueText))
                    .OrderByDescending(e => e.Address)
                    .FirstOrDefault();
                if (lastVisible != null)
                {
                    lastVisible.IsReadError = true;
                    lastVisible.ReadErrorMessage = "Read stopped here. The remaining registers could not be read.";
                }
            }

            EnsureSortedByAddress(target);
            return target;
        }

        private static void EnsureSortedByAddress(ObservableCollection<RegisterEntry> collection)
        {
            if (collection.Count < 2) return;

            bool sorted = true;
            for (int i = 1; i < collection.Count; i++)
            {
                if (collection[i].Address < collection[i - 1].Address)
                {
                    sorted = false;
                    break;
                }
            }

            if (sorted) return;

            var entries = collection.OrderBy(e => e.Address).ToList();
            collection.Clear();
            foreach (var entry in entries)
            {
                collection.Add(entry);
            }
        }

        private ObservableCollection<CoilEntry> ApplyCoilValues(ObservableCollection<CoilEntry>? target, int start, bool[] values, bool isPartialRead = false)
        {
            target ??= new ObservableCollection<CoilEntry>();
            var entriesByAddress = target.ToDictionary(e => e.Address);
            var usedAddresses = new HashSet<int>();

            for (int i = 0; i < values.Length; i++)
            {
                var address = start + i;
                usedAddresses.Add(address);
                if (!entriesByAddress.TryGetValue(address, out var entry))
                {
                    entry = new CoilEntry { Address = address };
                    target.Add(entry);
                    entriesByAddress[address] = entry;
                }

                entry.State = values[i];
                entry.IsReadError = false;
                entry.ReadErrorMessage = null;
            }

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!usedAddresses.Contains(target[i].Address))
                {
                    target.RemoveAt(i);
                }
            }

            if (isPartialRead && target.Count > 0)
            {
                var lastCoil = target.OrderByDescending(e => e.Address).First();
                lastCoil.IsReadError = true;
                lastCoil.ReadErrorMessage = "Read stopped here. The remaining coils could not be read.";
            }

            EnsureSortedByAddress(target);
            return target;
        }

        private static void EnsureSortedByAddress(ObservableCollection<CoilEntry> collection)
        {
            if (collection.Count < 2) return;

            bool sorted = true;
            for (int i = 1; i < collection.Count; i++)
            {
                if (collection[i].Address < collection[i - 1].Address)
                {
                    sorted = false;
                    break;
                }
            }

            if (sorted) return;

            var entries = collection.OrderBy(e => e.Address).ToList();
            collection.Clear();
            foreach (var entry in entries)
            {
                collection.Add(entry);
            }
        }

        #region Custom Watch

        private async Task AddCustomEntryAsync()
        {
            await _dispatcher.InvokeAsync(() =>
            {
                int nextAddress = 1;
                string type = "int";
                string area = "HoldingRegister";
                string name = "Tag0";

                if (CustomEntries.Count > 0)
                {
                    var last = CustomEntries[^1];
                    type = last.Type ?? "int";
                    area = last.Area ?? "HoldingRegister";
                    name = GenerateNextName(last.Name);

                    int increment = IsMultiRegisterType(type)
                        ? MultiRegisterTypeIncrement
                        : SingleRegisterTypeIncrement;
                    nextAddress = Math.Max(1, last.Address + increment);
                }

                var entry = new CustomEntry
                {
                    Name = name,
                    Address = nextAddress,
                    Area = area,
                    Type = type,
                    Value = "0",
                    WriteValue = "0",
                    Continuous = false,
                    PeriodMs = DefaultCustomPeriodMs,
                    Monitor = false,
                    ReadPeriodMs = DefaultCustomPeriodMs
                };

                CustomEntries.Add(entry);
                SelectedCustomEntry = entry;
                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                StatusMessage = $"Added custom entry {name}.";
            });
        }

        private Task AddCustomBulkEntryAsync()
        {
            if (_customBulkAddDialogService == null) return Task.CompletedTask;

            return _dispatcher.InvokeAsync(() =>
            {
                if (!_customBulkAddDialogService.TryGetBulkAdd(out var result) || result == null)
                {
                    StatusMessage = "Bulk add cancelled.";
                    return;
                }

                int increment = IsMultiRegisterType(result.Type)
                    ? MultiRegisterTypeIncrement
                    : SingleRegisterTypeIncrement;

                for (int i = 0; i < result.Count; i++)
                {
                    int address = result.StartRegister + i * increment;
                    var entry = new CustomEntry
                    {
                        Name = $"{result.NamePrefix}{i}",
                        Address = address,
                        Area = result.Area,
                        Type = result.Type,
                        Value = "0",
                        WriteValue = "0",
                        Continuous = false,
                        PeriodMs = result.WritePeriodMs,
                        Monitor = false,
                        ReadPeriodMs = result.ReadPeriodMs
                    };

                    CustomEntries.Add(entry);
                }

                SelectedCustomEntry = CustomEntries[^1];
                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                StatusMessage = $"Added {result.Count} custom entries starting at {result.StartRegister}.";
            });
        }

        private static bool IsMultiRegisterType(string type) =>
            type.Equals("real", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("float", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("dword", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("dint", StringComparison.OrdinalIgnoreCase);

        private async Task RemoveCustomEntryAsync()
        {
            if (SelectedCustomEntry == null) return;

            await _dispatcher.InvokeAsync(() =>
            {
                CustomEntries.Remove(SelectedCustomEntry);
                SelectedCustomEntry = null;
                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                StatusMessage = "Removed custom entry.";
            });
        }

        private Task DeleteCustomEntryAsync(CustomEntry? entry)
        {
            if (entry == null) return Task.CompletedTask;

            return _dispatcher.InvokeAsync(() =>
            {
                CustomEntries.Remove(entry);
                if (SelectedCustomEntry == entry)
                {
                    SelectedCustomEntry = null;
                }

                ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                StatusMessage = $"Removed custom entry {entry.Name}.";
            });
        }

        private Task ReadSelectedCustomEntryAsync() => ReadCustomEntryNowAsync(SelectedCustomEntry);

        private Task WriteSelectedCustomEntryAsync() => WriteCustomEntryNowAsync(SelectedCustomEntry);

        private async Task ReadCustomEntryNowAsync(CustomEntry? entry)
        {
            if (entry == null || ActiveService == null || ActiveProfile == null) return;

            IsBusy = true;
            try
            {
                var value = await ReadCustomValueSerializedAsync(entry, CancellationToken.None);
                var readAt = DateTime.UtcNow;
                await _dispatcher.InvokeAsync(() =>
                {
                    entry.Value = value;
                    entry.LastReadUtc = readAt;
                    StatusMessage = $"Read {entry.Name} = {value}";
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await ReportCustomOperationFailureAsync("read", entry, ex, false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task WriteCustomEntryNowAsync(CustomEntry? entry)
        {
            if (entry == null || ActiveService == null || ActiveProfile == null) return;

            IsBusy = true;
            try
            {
                var result = await WriteCustomValueSerializedAsync(entry, CancellationToken.None);
                var writtenAt = DateTime.UtcNow;
                await _dispatcher.InvokeAsync(() =>
                {
                    if (result)
                    {
                        entry.LastWriteUtc = writtenAt;
                    }

                    StatusMessage = result
                        ? $"Wrote {entry.Name}"
                        : $"Write completed with issues for {entry.Name}.";
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await ReportCustomOperationFailureAsync("write", entry, ex, false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ReadAllCustomEntriesAsync()
        {
            if (ActiveService == null || ActiveProfile == null || CustomEntries.Count == 0) return;

            IsBusy = true;
            try
            {
                var entries = await _dispatcher.InvokeAsync(() => CustomEntries.ToList());
                var readCount = 0;
                var failureCount = 0;
                foreach (var entry in entries)
                {
                    try
                    {
                        var value = await ReadCustomValueSerializedAsync(entry, CancellationToken.None);
                        var readAt = DateTime.UtcNow;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            entry.Value = value;
                            entry.LastReadUtc = readAt;
                        });
                        readCount++;
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        // No per-entry dialog in batch mode; the summary below reports how many failed.
                        await ReportCustomOperationFailureAsync("read", entry, ex, false, showDialog: false);
                        failureCount++;
                    }
                }

                await _dispatcher.InvokeAsync(() => StatusMessage = failureCount > 0
                    ? $"Read {readCount} of {entries.Count} custom entries; {failureCount} failed."
                    : $"Read {readCount} of {entries.Count} custom entries.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<string> ReadCustomValueSerializedAsync(CustomEntry entry, CancellationToken token)
        {
            await _modbusIoGate.WaitAsync(token);
            try
            {
                return await ReadCustomValueAsync(entry);
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }

        private async Task<bool> WriteCustomValueSerializedAsync(CustomEntry entry, CancellationToken token)
        {
            await _modbusIoGate.WaitAsync(token);
            try
            {
                return await WriteCustomValueAsync(entry);
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }

        private async Task<string> ReadTrendPenValueSerializedAsync(TrendPen pen, CancellationToken token)
        {
            await _modbusIoGate.WaitAsync(token);
            try
            {
                return await ReadAddressValueAsync(pen.Area, pen.Address, pen.Type);
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }

        /// <summary>
        /// Reports a failed trend pen read. A pen is not paused on failure
        /// (that would strand the trend with no recovery), so it is reported
        /// once per failure episode: the first failure of a pen gets a log
        /// line and a status message; repeats stay silent until a read
        /// succeeds again (<see cref="MarkTrendPenRecovered"/>).
        /// </summary>
        private async Task ReportTrendPenFailureAsync(TrendPen pen, Exception exception)
        {
            bool firstInEpisode;
            lock (_failingTrendPensLock)
            {
                firstInEpisode = _failingTrendPens.Add(pen.Name);
            }
            if (!firstInEpisode)
            {
                return;
            }

            _logger.LogError(exception, "Trend pen {PenName} ({Area} {Address}) read failed; retrying each cycle until it recovers", pen.Name, pen.Area, pen.Address);
            try
            {
                await _dispatcher.InvokeAsync(() => StatusMessage = $"Trend pen '{pen.Name}' failed to read: {exception.Message}");
            }
            catch (OperationCanceledException)
            {
                // Loop is going away; the status bar is irrelevant.
            }
        }

        private void MarkTrendPenRecovered(string penName)
        {
            lock (_failingTrendPensLock)
            {
                _failingTrendPens.Remove(penName);
            }
        }

        /// <summary>
        /// Reports a failed custom-entry operation. A modal dialog is only shown for
        /// explicit single operations (the user asked for this exact entry and must
        /// hear about the failure); batch reads and the monitor loop report to the
        /// status bar instead, because a dialog per failed entry would stack up
        /// dozens of modals the instant a connection drops.
        /// </summary>
        private async Task ReportCustomOperationFailureAsync(string operation, CustomEntry entry, Exception exception, bool monitoring, bool showDialog = true)
        {
            _logger.LogError(exception, "Error {Operation} custom entry {Name}", operation, entry.Name);
            var suffix = monitoring ? " Continuous monitoring has been paused." : string.Empty;
            var message = $"Failed to {operation} custom entry '{entry.Name}': {exception.Message}.{suffix}";
            await _dispatcher.InvokeAsync(() => StatusMessage = message);
            if (showDialog && _messageBoxService != null)
            {
                Task<DialogResult>? dialogTask = null;
                await _dispatcher.InvokeAsync(() =>
                    dialogTask = _messageBoxService.ShowAsync(message, "Custom Watch Error", DialogButton.Ok, DialogIcon.Error));
                if (dialogTask != null)
                {
                    await dialogTask;
                }
            }
        }

        private async Task<string> ReadCustomValueAsync(CustomEntry entry)
            => await ReadAddressValueAsync(entry.Area, entry.Address, entry.Type);

        /// <summary>
        /// Reads one address from the active connection. Shared by custom watch
        /// entries and trend pens, which differ only in where the result goes
        /// (entry.Value vs the trend logger).
        /// </summary>
        private async Task<string> ReadAddressValueAsync(string? area, int address, string? type)
        {
            var service = ActiveService;
            if (service == null || ActiveProfile == null)
                throw new InvalidOperationException("No active service.");

            var unitId = EffectiveUnitId;
            area = (area ?? "HoldingRegister").ToLowerInvariant();
            type = (type ?? "int").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                case "inputregister":
                    int count = type == "real" ? 2 : 1;
                    var areaEnum = area == "holdingregister" ? PlcArea.HoldingRegister : PlcArea.InputRegister;
                    var values = areaEnum == PlcArea.HoldingRegister
                        ? await service.ReadHoldingRegistersAsync(unitId, address, count)
                        : await service.ReadInputRegistersAsync(unitId, address, count);

                    if (values == null || values.Length == 0)
                        throw new InvalidOperationException("Read returned no response.");

                    if (type == "real" && values.Length < 2)
                        throw new InvalidOperationException("A REAL value requires two registers.");

                    return type switch
                    {
                        "int" => unchecked((short)values[0]).ToString(CultureInfo.InvariantCulture),
                        "real" => DataTypeConverter.ToSingle(values[0], values[1], false, false).ToString(CultureInfo.InvariantCulture),
                        "string" => DataTypeConverter.ToString(values[0]),
                        _ => values[0].ToString(CultureInfo.InvariantCulture)
                    };

                case "coil":
                case "discreteinput":
                    var coilValues = area == "coil"
                        ? await service.ReadCoilsAsync(unitId, address, 1)
                        : await service.ReadDiscreteInputsAsync(unitId, address, 1);
                    if (coilValues == null || coilValues.Length == 0) return "No response";
                    return coilValues[0] ? "1" : "0";

                default:
                    return $"Unknown area: {area}";
            }
        }

        private async Task<bool> WriteCustomValueAsync(CustomEntry entry)
        {
            var service = ActiveService;
            if (service == null || ActiveProfile == null) return false;

            var unitId = EffectiveUnitId;
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "int").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                    switch (type)
                    {
                        case "real":
                            if (float.TryParse(entry.WriteValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ||
                                float.TryParse(entry.WriteValue, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                            {
                                var words = DataTypeConverter.ToUInt16(f, false, false);
                                await service.WriteRegistersAsync(unitId, entry.Address, words);
                                return true;
                            }
                            return false;

                        case "string":
                            var stringWords = DataTypeConverter.ToUInt16(entry.WriteValue ?? string.Empty);
                            await service.WriteRegistersAsync(unitId, entry.Address, stringWords);
                            return true;

                        case "int":
                            if (int.TryParse(entry.WriteValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                            {
                                await service.WriteSingleRegisterAsync(unitId, entry.Address, unchecked((ushort)iv));
                                return true;
                            }
                            return false;

                        default:
                            if (uint.TryParse(entry.WriteValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uv))
                            {
                                if (uv > 0xFFFF) uv = 0xFFFF;
                                await service.WriteSingleRegisterAsync(unitId, entry.Address, (ushort)uv);
                                return true;
                            }
                            return false;
                    }

                case "coil":
                    if (TryParseBool(entry.WriteValue, out bool b))
                    {
                        await service.WriteSingleCoilAsync(unitId, entry.Address, b);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        private async Task SaveCustomAsync()
        {
            if (_customEntryService == null) return;

            IsBusy = true;
            try
            {
                await _customEntryService.SaveCustomAsync(CustomEntries);
                StatusMessage = "Custom entries saved.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving custom entries");
                StatusMessage = $"Save error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadCustomAsync()
        {
            if (_customEntryService == null) return;

            IsBusy = true;
            try
            {
                var entries = await _customEntryService.LoadCustomAsync();
                if (entries != null)
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        CustomEntries.Clear();
                        foreach (var e in entries)
                        {
                            CustomEntries.Add(e);
                        }
                        // Legacy files may carry the old Trend flag on entries;
                        // convert those into pens of the current unit.
                        CurrentConfig.MigrateLegacyTrendEntries();
                        ReadAllCustomNowCommand.NotifyCanExecuteChanged();
                    });
                    StatusMessage = $"Loaded {entries.Count} custom entries.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom entries");
                StatusMessage = $"Load error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void StartCustomWatchMonitoring()
        {
            if (_disposed || ActiveProfile is not { IsConnected: true } || !IsCustomWatchMonitoring) return;

            lock (_customWatchLifecycleLock)
            {
                if (_customWatchCts != null) return;

                var cts = new CancellationTokenSource();
                _customWatchCts = cts;
                _ = Task.Run(() => CustomWatchLoopAsync(cts), cts.Token);
            }
        }

        private void StopCustomWatchMonitoring()
        {
            lock (_customWatchLifecycleLock)
            {
                _customWatchCts?.Cancel();
            }
        }

        private async Task CustomWatchLoopAsync(CancellationTokenSource loopCts)
        {
            var token = loopCts.Token;
            try
            {
                while (!token.IsCancellationRequested && ActiveProfile is { IsConnected: true } && IsCustomWatchMonitoring)
                {
                    var entries = await _dispatcher.InvokeAsync(() => CustomEntries.ToList());
                    var now = DateTime.UtcNow;

                    foreach (var entry in entries)
                    {
                        token.ThrowIfCancellationRequested();

                        var readPeriod = entry.ReadPeriodMs <= 0 ? DefaultCustomPeriodMs : entry.ReadPeriodMs;
                        if (entry.Monitor && (now - entry.LastReadUtc).TotalMilliseconds >= readPeriod)
                        {
                            try
                            {
                                var value = await ReadCustomValueSerializedAsync(entry, token);
                                var readAt = DateTime.UtcNow;
                                await _dispatcher.InvokeAsync(() =>
                                {
                                    entry.Value = value;
                                    entry.LastReadUtc = readAt;
                                });
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex) when (ex is not OutOfMemoryException)
                            {
                                await _dispatcher.InvokeAsync(() => entry.Monitor = false);
                                await ReportCustomOperationFailureAsync("read", entry, ex, true, showDialog: false);
                            }
                        }

                        var writePeriod = entry.PeriodMs <= 0 ? DefaultCustomPeriodMs : entry.PeriodMs;
                        if (entry.Continuous && (now - entry.LastWriteUtc).TotalMilliseconds >= writePeriod)
                        {
                            try
                            {
                                var success = await WriteCustomValueSerializedAsync(entry, token);
                                if (!success)
                                {
                                    throw new InvalidOperationException("The custom value is invalid for the selected type or area.");
                                }

                                var writeAt = DateTime.UtcNow;
                                await _dispatcher.InvokeAsync(() => entry.LastWriteUtc = writeAt);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex) when (ex is not OutOfMemoryException)
                            {
                                await _dispatcher.InvokeAsync(() => entry.Continuous = false);
                                await ReportCustomOperationFailureAsync("write", entry, ex, true, showDialog: false);
                            }
                        }
                    }

                    // Trend pens: first-class series sources polled in the
                    // same cycle. Unlike watch entries, a failing pen keeps
                    // retrying (pausing a trend would leave a flat line with
                    // no way to recover it), reported once per episode.
                    var pens = await _dispatcher.InvokeAsync(() => CurrentConfig.TrendPens.ToList());
                    foreach (var pen in pens)
                    {
                        token.ThrowIfCancellationRequested();

                        var penPeriod = pen.ReadPeriodMs <= 0 ? DefaultCustomPeriodMs : pen.ReadPeriodMs;
                        if ((now - pen.LastReadUtc).TotalMilliseconds < penPeriod)
                        {
                            continue;
                        }

                        try
                        {
                            var value = await ReadTrendPenValueSerializedAsync(pen, token);
                            var readAt = DateTime.UtcNow;
                            await _dispatcher.InvokeAsync(() => pen.LastReadUtc = readAt);

                            if (_trendLogger != null && TryParseTrendValue(value, out var trendValue))
                            {
                                _trendLogger.Publish(pen.Name, trendValue, readAt);
                            }

                            MarkTrendPenRecovered(pen.Name);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            await ReportTrendPenFailureAsync(pen, ex);
                        }
                    }

                    await Task.Delay(PollLoopIntervalMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Custom watch loop canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Custom watch loop failed");
                await _dispatcher.InvokeAsync(() => StatusMessage = $"Custom watch error: {ex.Message}");
            }
            finally
            {
                var restart = false;
                lock (_customWatchLifecycleLock)
                {
                    if (ReferenceEquals(_customWatchCts, loopCts))
                    {
                        _customWatchCts = null;
                        restart = !_disposed && ActiveProfile is { IsConnected: true } && IsCustomWatchMonitoring;
                    }
                }

                loopCts.Dispose();
                if (restart)
                {
                    StartCustomWatchMonitoring();
                }
            }
        }

        private static string GenerateNextName(string previousName)
        {
            if (string.IsNullOrWhiteSpace(previousName))
                return "Tag0";

            int i = previousName.Length - 1;
            while (i >= 0 && char.IsDigit(previousName[i]))
                i--;

            if (i < previousName.Length - 1 &&
                int.TryParse(previousName[(i + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            {
                return previousName.Substring(0, i + 1) + (num + 1).ToString(CultureInfo.InvariantCulture);
            }

            return previousName + "1";
        }

        private static bool TryParseBool(string? text, out bool result)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                result = false;
                return false;
            }

            var trimmed = text.Trim();

            if (bool.TryParse(trimmed, out result))
                return true;

            if (int.TryParse(trimmed, out var value))
            {
                result = value != 0;
                return true;
            }

            if (trimmed.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (trimmed.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static bool TryParseTrendValue(string? text, out double result)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                result = 0;
                return false;
            }

            var trimmed = text.Trim();

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            if (bool.TryParse(trimmed, out var b))
            {
                result = b ? 1 : 0;
                return true;
            }

            return false;
        }

        private IEnumerable<MqttTagUpdate> BuildMqttSnapshot()
        {
            var updates = new List<MqttTagUpdate>();

            var entries = CustomEntries.ToList();
            var unitId = UnitId;

            foreach (var entry in entries)
            {
                if (!Enum.TryParse<PlcArea>(entry.Area, true, out var area))
                {
                    area = PlcArea.HoldingRegister;
                }

                updates.Add(new MqttTagUpdate
                {
                    UnitId = (byte)unitId,
                    TagName = entry.Name,
                    Area = area,
                    Address = entry.Address,
                    Value = entry.Value,
                    Timestamp = entry.LastReadUtc == default ? DateTime.UtcNow : entry.LastReadUtc
                });
            }

            return updates;
        }

        #endregion

        #region Project Save/Load

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
                        // Projects saved before trend pens existed carry the
                        // old Trend flag on watch entries; convert them.
                        configuration.MigrateLegacyTrendEntries();
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
                    Version = "2026.7.24",
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

        private void ToggleTheme()
        {
            _themeService?.ToggleTheme();
        }

        private async Task CheckForUpdatesAsync()
        {
            if (_updateService == null) return;

            try
            {
                var result = await _updateService.CheckForUpdateAsync();
                if (result.IsUpdateAvailable)
                {
                    var msg = $"A newer version is available: {result.LatestVersion}\nCurrent: {result.CurrentVersion}\n\nDownload and install it now?";
                    if (_messageBoxService != null)
                    {
                        var dialogResult = await _messageBoxService.ShowAsync(msg, "Update Available", DialogButton.YesNo, DialogIcon.Question);
                        if (dialogResult == DialogResult.Yes)
                        {
                            if (string.IsNullOrWhiteSpace(result.AssetDownloadUrl))
                            {
                                OpenUrl(result.ReleaseUrl);
                                return;
                            }

                            var installerPath = Path.Combine(
                                Path.GetTempPath(),
                                $"ModbusForge-{result.LatestVersion}-setup.exe");
                            var progress = new Progress<double>(value =>
                            {
                                StatusMessage = $"Downloading update... {value:P0}";
                            });

                            StatusMessage = "Downloading update...";
                            var downloaded = await _updateService.DownloadInstallerAsync(
                                result.AssetDownloadUrl,
                                installerPath,
                                progress);
                            if (!downloaded)
                            {
                                StatusMessage = "Update download failed.";
                                return;
                            }

                            StatusMessage = "Launching update installer...";
                            _updateService.LaunchInstaller(installerPath);
                            _applicationLifetime?.Shutdown();
                        }
                    }
                    else
                    {
                        StatusMessage = $"Update available: {result.LatestVersion}";
                    }
                }
                else
                {
                    StatusMessage = $"Up to date ({result.CurrentVersion}).";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Update check failed: {ex.Message}";
                _logger.LogWarning(ex, "Update check failed");
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.FileName = url;
                process.Start();
            }
            catch (Exception)
            {
                // ignore
            }
        }

        #endregion

        public void Dispose()
        {
            _disposed = true;
            _connectionManager.ActiveProfileChanged -= ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected -= ConnectionManager_ProfileConnected;
            _connectionManager.ProfileDisconnected -= ConnectionManager_ProfileDisconnected;
            ConnectionProfiles.CollectionChanged -= OnConnectionProfilesCollectionChanged;
            _unitConfigurationStore.SelectedUnitIdChanged -= UnitConfigurationStore_SelectedUnitIdChanged;
            _unitConfigurationStore.AvailableUnitIdsChanged -= UnitConfigurationStore_AvailableUnitIdsChanged;
            if (_themeService != null)
            {
                _themeService.ThemeChanged -= ThemeService_ThemeChanged;
            }

            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged -= ActiveProfile_PropertyChanged;
            }

            DecodeViewModel?.Dispose();
            _trendLogger?.Stop();
            StopPolling();
            StopCustomWatchMonitoring();
        }
    }
}
