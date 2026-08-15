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
    /// MainViewModel - core members (lifecycle, unit configuration, status, child view models).
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable

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

        private readonly MqttGatewayService? _mqttGateway;

        private readonly IConsoleLoggerService? _consoleLoggerService;

        private readonly SemaphoreSlim _modbusIoGate = new(1, 1);

        private bool _disposed;

        private bool _isApplyingUnitConfiguration;


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


        public byte EffectiveUnitId => IsServerMode ? SelectedUnitId : (byte)UnitId;


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
            SignalGeneratorViewModel? signalGeneratorViewModel = null,
            VisualNodeEditorViewModel? visualNodeEditorViewModel = null,
            DecodeViewModel? decodeViewModel = null,
            IUnitConfigurationStore? unitConfigurationStore = null,
            IFileSystem? fileSystem = null,
            IDockingHost? dockingHost = null,
            IConsoleLoggerService? consoleLoggerService = null)
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
            _consoleLoggerService = consoleLoggerService;
            ConsoleMessages = consoleLoggerService?.LogMessages ?? _consoleMessageFallback;
            TrendViewModel = trendViewModel;
            FrameInspectorViewModel = frameInspectorViewModel;
            MqttViewModel = mqttViewModel;
            ScriptEditorViewModel = scriptEditorViewModel;
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

            _mqttGateway = mqttGateway;
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
            OpenTrendsCommand = new RelayCommand(() => SelectedTabIndex = TrendsTabIndex);
            OpenFrameInspectorCommand = new RelayCommand(() => SelectedTabIndex = FrameInspectorTabIndex);
            OpenScriptEditorCommand = new RelayCommand(() => SelectedTabIndex = ScriptEditorTabIndex);
            OpenPcapCommand = new RelayCommand(() =>
            {
                SelectedTabIndex = FrameInspectorTabIndex;
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

            // Navigation list entries in user-facing order; TabIndex points into the
            // MainTabControl item order, and IsVisible mirrors the tab-visibility flag.
            _allNavigationItems.AddRange(new[]
            {
                new NavigationItem("Dashboard", DashboardTabIndex, () => true),
                new NavigationItem("Trends", TrendsTabIndex, () => IsTrendTabVisible),
                new NavigationItem("Frame Inspector", FrameInspectorTabIndex, () => true),
                new NavigationItem("MQTT", MqttTabIndex, () => true),
                new NavigationItem("Script Editor", ScriptEditorTabIndex, () => true),
                new NavigationItem("Signal Generator", SignalGeneratorTabIndex, () => true),
                new NavigationItem("Simulation", SimulationTabIndex, () => IsSimulationTabVisible),
                new NavigationItem("Registers", HoldingRegistersTabIndex, () => IsRegistersTabVisible),
                new NavigationItem("Input Registers", InputRegistersTabIndex, () => IsInputRegistersTabVisible),
                new NavigationItem("Coils", CoilsTabIndex, () => IsCoilsTabVisible),
                new NavigationItem("Discrete Inputs", DiscreteInputsTabIndex, () => IsDiscreteInputsTabVisible),
                new NavigationItem("Custom Watch", CustomWatchTabIndex, () => IsCustomWatchTabVisible),
                new NavigationItem("Decode", DecodeTabIndex, () => IsDecodeTabVisible),
                new NavigationItem("Console", ConsoleTabIndex, () => IsConsoleTabVisible),
                new NavigationItem("Debug", DebugTabIndex, () => IsDebugTabVisible),
            });
            RefreshNavigationItems();
            SelectedNavigationItem = NavigationItems.FirstOrDefault() ?? _allNavigationItems[0];

            if (_themeService != null)
            {
                _themeService.ThemeChanged += ThemeService_ThemeChanged;
            }

            _connectionManager.ActiveProfileChanged += ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected += ConnectionManager_ProfileConnected;
            _connectionManager.ProfileDisconnected += ConnectionManager_ProfileDisconnected;
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
            UpdateCustomWatchMonitoringState();
        }


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


        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(CustomEntries))
            {
                HookCustomEntries();
                UpdateCustomWatchMonitoringState();
            }
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
            // The profile is mutated from thread-pool threads too (ConnectProfileAsync /
            // DisconnectProfileAsync set IsConnected/Status), so marshal to the UI thread
            // before touching view-model state.
            _ = _dispatcher.InvokeAsync(() => HandleActiveProfilePropertyChanged(e));
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



        public void Dispose()
        {
            _disposed = true;
            _connectionManager.ActiveProfileChanged -= ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected -= ConnectionManager_ProfileConnected;
            _connectionManager.ProfileDisconnected -= ConnectionManager_ProfileDisconnected;
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
            StopAutoReconnect();

            // The MQTT gateway is a long-lived singleton: its SnapshotProvider delegate
            // captures this view model, so it must be cleared or the whole view-model
            // graph stays alive after the window closes.
            if (_mqttGateway != null)
            {
                _mqttGateway.SnapshotProvider = null;
            }

            // Same retention problem via the cross-VM PropertyChanged subscription.
            if (VisualNodeEditorViewModel != null)
            {
                VisualNodeEditorViewModel.PropertyChanged -= OnVisualNodeEditorViewModelPropertyChanged;
            }

            // Release the per-entry PropertyChanged hooks so the custom entries can be collected.
            UnhookCustomEntries();
        }

    }
}
