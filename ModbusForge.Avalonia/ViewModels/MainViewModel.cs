using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly ICustomEntryService? _customEntryService;
        private readonly IFileDialogService? _fileDialogService;
        private readonly IInputDialogService? _inputDialogService;
        private readonly IMessageBoxService? _messageBoxService;
        private readonly ISettingsService? _settingsService;
        private readonly IThemeService? _themeService;
        private readonly IUpdateService? _updateService;
        private readonly IWindowService? _windowService;
        private readonly IApplicationLifetime? _applicationLifetime;
        private readonly ITrendLogger? _trendLogger;
        private CancellationTokenSource? _pollCts;
        private CancellationTokenSource? _customWatchCts;
        private DateTime _lastHoldingReadUtc;
        private DateTime _lastInputRegReadUtc;
        private DateTime _lastCoilsReadUtc;
        private DateTime _lastDiscreteReadUtc;
        private byte _unitId = 1;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        private int _startAddress = 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        private int _registerCount = 20;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadCustomEntryCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCustomEntryCommand))]
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
        private string _globalType = "uint";

        [ObservableProperty]
        private bool _swapBytes;

        [ObservableProperty]
        private bool _swapWords;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadHoldingRegistersCommand))]
        private int _holdingRegisterStart = 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadHoldingRegistersCommand))]
        private int _holdingRegisterCount = 20;

        [ObservableProperty]
        private string _registersGlobalType = "uint";

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
        private int _inputRegisterStart = 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadInputRegistersCommand))]
        private int _inputRegisterCount = 20;

        [ObservableProperty]
        private string _inputRegistersGlobalType = "uint";

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
        private int _coilStart = 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCoilsCommand))]
        private int _coilCount = 20;

        [ObservableProperty]
        private bool _coilsMonitorEnabled;

        [ObservableProperty]
        private int _coilsMonitorPeriodMs = 1000;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadDiscreteInputsCommand))]
        private int _discreteInputStart = 0;

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

        public ObservableCollection<RegisterEntry> HoldingRegisters { get; } = new();

        public ObservableCollection<RegisterEntry> InputRegisters { get; } = new();

        public ObservableCollection<CoilEntry> Coils { get; } = new();

        public ObservableCollection<CoilEntry> DiscreteInputs { get; } = new();

        public ObservableCollection<RegisterEntry> Registers => HoldingRegisters;

        public ObservableCollection<CustomEntry> CustomEntries { get; } = new();

        public IModbusService? ActiveService => _connectionManager.ActiveService;

        public int UnitId
        {
            get => ActiveProfile?.UnitId ?? _unitId;
            set
            {
                var byteValue = (byte)Math.Clamp(value, 1, 247);

                if (ActiveProfile != null && ActiveProfile.UnitId != byteValue)
                {
                    ActiveProfile.UnitId = byteValue;
                }

                if (_unitId != byteValue)
                {
                    _unitId = byteValue;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsRegisterArea => SelectedArea is PlcArea.HoldingRegister or PlcArea.InputRegister;

        public IReadOnlyList<string> RegisterTypes { get; } = new[] { "uint", "int", "real", "string" };

        public IReadOnlyList<string> CustomAreas { get; } = new[] { "HoldingRegister", "InputRegister", "Coil", "DiscreteInput" };

        public IReadOnlyList<string> Modes { get; } = new[] { "Client", "Server" };

        public ObservableCollection<byte> AvailableUnitIds { get; } = new();

        public string Mode
        {
            get => ActiveProfile?.Mode ?? "Client";
            set
            {
                if (ActiveProfile != null && ActiveProfile.Mode != value)
                {
                    ActiveProfile.Mode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsServerMode));
                    OnPropertyChanged(nameof(ConnectButtonText));
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
            get => _unitId;
            set
            {
                if (_unitId != value)
                {
                    _unitId = value;
                    OnPropertyChanged();
                }
            }
        }

        public TrendViewModel? TrendViewModel { get; }

        public FrameInspectorViewModel? FrameInspectorViewModel { get; }

        public MqttViewModel? MqttViewModel { get; }

        public ScriptEditorViewModel? ScriptEditorViewModel { get; }

        public SignalGeneratorViewModel? SignalGeneratorViewModel { get; }

        public VisualNodeEditorViewModel? VisualNodeEditorViewModel { get; }

        public MainViewModel(
            IConnectionManager connectionManager,
            ILogger<MainViewModel> logger,
            IDispatcher dispatcher,
            ICustomEntryService? customEntryService = null,
            IFileDialogService? fileDialogService = null,
            IInputDialogService? inputDialogService = null,
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
            VisualNodeEditorViewModel? visualNodeEditorViewModel = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _customEntryService = customEntryService;
            _fileDialogService = fileDialogService;
            _inputDialogService = inputDialogService;
            _messageBoxService = messageBoxService;
            _settingsService = settingsService;
            _themeService = themeService;
            _updateService = updateService;
            _windowService = windowService;
            _applicationLifetime = applicationLifetime;
            _trendLogger = trendLogger;
            TrendViewModel = trendViewModel;
            FrameInspectorViewModel = frameInspectorViewModel;
            MqttViewModel = mqttViewModel;
            ScriptEditorViewModel = scriptEditorViewModel;
            SignalGeneratorViewModel = signalGeneratorViewModel;
            VisualNodeEditorViewModel = visualNodeEditorViewModel;

            if (mqttGateway is not null)
            {
                mqttGateway.SnapshotProvider = BuildMqttSnapshot;
            }

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect());
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect());
            ReadCommand = new AsyncRelayCommand(ReadAsync, () => CanRead());
            WriteCommand = new AsyncRelayCommand(WriteAsync, () => CanWrite());
            ReadHoldingRegistersCommand = new AsyncRelayCommand(ReadHoldingRegistersAsync, () => CanRead(PlcArea.HoldingRegister));
            ReadInputRegistersCommand = new AsyncRelayCommand(ReadInputRegistersAsync, () => CanRead(PlcArea.InputRegister));
            ReadCoilsCommand = new AsyncRelayCommand(ReadCoilsAsync, () => CanRead(PlcArea.Coil));
            ReadDiscreteInputsCommand = new AsyncRelayCommand(ReadDiscreteInputsAsync, () => CanRead(PlcArea.DiscreteInput));
            WriteHoldingRegisterCommand = new AsyncRelayCommand(WriteHoldingRegisterAsync, () => CanWrite(PlcArea.HoldingRegister));
            WriteCoilCommand = new AsyncRelayCommand(WriteCoilAsync, () => CanWrite(PlcArea.Coil));

            AddCustomEntryCommand = new AsyncRelayCommand(AddCustomEntryAsync, () => !IsBusy);
            RemoveCustomEntryCommand = new AsyncRelayCommand(RemoveCustomEntryAsync, () => CanRemoveCustomEntry());
            ReadCustomEntryCommand = new AsyncRelayCommand(ReadCustomEntryAsync, () => CanReadCustomEntry());
            WriteCustomEntryCommand = new AsyncRelayCommand(WriteCustomEntryAsync, () => CanWriteCustomEntry());
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
            OpenTrendsCommand = new RelayCommand(() => SelectedTabIndex = 0);

            _connectionManager.ActiveProfileChanged += ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected += ConnectionManager_ProfileConnected;
            _connectionManager.ProfileDisconnected += ConnectionManager_ProfileDisconnected;

            ActiveProfile = _connectionManager.ActiveProfile;

            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged += ActiveProfile_PropertyChanged;
                _unitId = ActiveProfile.UnitId;
            }

            StatusMessage = ActiveProfile != null
                ? $"Active profile: {ActiveProfile.DisplayName}"
                : "No active connection profile";
        }

        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand ReadCommand { get; }
        public IAsyncRelayCommand WriteCommand { get; }
        public IAsyncRelayCommand ReadHoldingRegistersCommand { get; }
        public IAsyncRelayCommand ReadInputRegistersCommand { get; }
        public IAsyncRelayCommand ReadCoilsCommand { get; }
        public IAsyncRelayCommand ReadDiscreteInputsCommand { get; }
        public IAsyncRelayCommand WriteHoldingRegisterCommand { get; }
        public IAsyncRelayCommand WriteCoilCommand { get; }

        public IAsyncRelayCommand AddCustomEntryCommand { get; }
        public IAsyncRelayCommand RemoveCustomEntryCommand { get; }
        public IAsyncRelayCommand ReadCustomEntryCommand { get; }
        public IAsyncRelayCommand WriteCustomEntryCommand { get; }
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

        [ObservableProperty]
        private int _selectedTabIndex;

        partial void OnSelectedAreaChanged(PlcArea value)
        {
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

        partial void OnIsBusyChanged(bool value)
        {
            ReadHoldingRegistersCommand.NotifyCanExecuteChanged();
            ReadInputRegistersCommand.NotifyCanExecuteChanged();
            ReadCoilsCommand.NotifyCanExecuteChanged();
            ReadDiscreteInputsCommand.NotifyCanExecuteChanged();
            WriteHoldingRegisterCommand.NotifyCanExecuteChanged();
            WriteCoilCommand.NotifyCanExecuteChanged();
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
            return validator.IsValidRange(start, count);
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
                _ => "uint"
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

        private bool CanWriteCustomEntry() => SelectedCustomEntry != null && ActiveProfile is { IsConnected: true } && !IsBusy;

        private bool CanSaveCustom() => _customEntryService != null && !IsBusy;

        private bool CanLoadCustom() => _customEntryService != null && !IsBusy;

        private bool CanSaveProject() => _fileDialogService != null && !IsBusy;

        private bool CanLoadProject() => _fileDialogService != null && !IsBusy;

        private void ActiveProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConnectionProfile.IsConnected))
            {
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(CanDisconnect));
                OnPropertyChanged(nameof(CanRead));
                OnPropertyChanged(nameof(CanWrite));
                OnPropertyChanged(nameof(CanReadCustomEntry));
                OnPropertyChanged(nameof(CanWriteCustomEntry));
                ConnectCommand.NotifyCanExecuteChanged();
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
            }

            if (e.PropertyName == nameof(ConnectionProfile.Status))
            {
                StatusMessage = ActiveProfile?.Status ?? "Ready";
            }

            if (e.PropertyName == nameof(ConnectionProfile.UnitId))
            {
                OnPropertyChanged(nameof(UnitId));
            }

            if (e.PropertyName == nameof(ConnectionProfile.Mode))
            {
                OnPropertyChanged(nameof(Mode));
                OnPropertyChanged(nameof(IsServerMode));
                OnPropertyChanged(nameof(ShowClientFields));
                OnPropertyChanged(nameof(ShowServerFields));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(AddressLabel));
            }

            if (e.PropertyName == nameof(ConnectionProfile.ServerUnitIds))
            {
                OnPropertyChanged(nameof(ServerUnitIds));
            }
        }

        private void ConnectionManager_ActiveProfileChanged(object? sender, ConnectionProfile? e)
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged -= ActiveProfile_PropertyChanged;
            }

            ActiveProfile = e;

            if (e != null)
            {
                e.PropertyChanged += ActiveProfile_PropertyChanged;
                _unitId = e.UnitId;
            }

            OnPropertyChanged(nameof(ActiveProfile));
            OnPropertyChanged(nameof(ActiveService));
            OnPropertyChanged(nameof(UnitId));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(IsServerMode));
            OnPropertyChanged(nameof(ShowClientFields));
            OnPropertyChanged(nameof(ShowServerFields));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(AddressLabel));
            OnPropertyChanged(nameof(ServerUnitIds));
            OnPropertyChanged(nameof(AvailableUnitIds));
            OnPropertyChanged(nameof(SelectedUnitId));
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
            OnPropertyChanged(nameof(CanRead));
            OnPropertyChanged(nameof(CanWrite));
            OnPropertyChanged(nameof(CanReadCustomEntry));
            OnPropertyChanged(nameof(CanWriteCustomEntry));
            ConnectCommand.NotifyCanExecuteChanged();
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

            AvailableUnitIds.Clear();

            StatusMessage = e != null ? $"Active profile: {e.DisplayName}" : "No active connection profile";
        }

        private void ConnectionManager_ProfileConnected(object? sender, ConnectionProfile e)
        {
            _logger.LogInformation("Profile connected: {Name}", e.Name);

            if (e.IsServerMode && _connectionManager.ActiveService is ModbusServerService server)
            {
                AvailableUnitIds.Clear();

                foreach (var id in server.GetUnitIds())
                {
                    AvailableUnitIds.Add(id);
                }

                if (AvailableUnitIds.Count > 0)
                {
                    SelectedUnitId = AvailableUnitIds[0];
                }

                OnPropertyChanged(nameof(AvailableUnitIds));
                OnPropertyChanged(nameof(SelectedUnitId));
                OnPropertyChanged(nameof(ShowServerFields));
            }

            _trendLogger?.Start();
            StartPolling();
            StartCustomWatchMonitoring();
        }

        private void ConnectionManager_ProfileDisconnected(object? sender, ConnectionProfile e)
        {
            _logger.LogInformation("Profile disconnected: {Name}", e.Name);
            _trendLogger?.Stop();
            StopPolling();
            StopCustomWatchMonitoring();
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

            var unitId = EffectiveUnitId;

            if (area == PlcArea.HoldingRegister)
            {
                var valueText = _inputDialogService.TryGetInput("Write Value", "Value:", "0", out var input) ? input : null;
                if (string.IsNullOrWhiteSpace(valueText) || !ushort.TryParse(valueText, out var value))
                {
                    _dispatcher.Invoke(() => StatusMessage = "Invalid register value.");
                    return;
                }

                await ActiveService.WriteSingleRegisterAsync(unitId, address, value);
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

                await ActiveService.WriteSingleCoilAsync(unitId, address, value);
                _dispatcher.Invoke(() => StatusMessage = $"Wrote {value} to coil {address}.");
            }
        }

        public async Task WriteHoldingRegisterFromEditAsync(RegisterEntry? entry)
        {
            if (entry == null || ActiveProfile == null || ActiveService == null) return;

            IsBusy = true;
            try
            {
                var unitId = EffectiveUnitId;
                var type = (entry.Type ?? "uint").ToLowerInvariant();
                var text = (entry.ValueText ?? string.Empty).Trim().Replace(',', '.');

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
                await ActiveService.WriteSingleCoilAsync(EffectiveUnitId, entry.Address, entry.State);
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
            if (_pollCts != null) return;
            if (ActiveProfile is not { IsConnected: true }) return;
            if (!AnyMonitorEnabled()) return;

            _pollCts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_pollCts.Token), _pollCts.Token);
        }

        private void StopPolling()
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && ActiveProfile is { IsConnected: true } && AnyMonitorEnabled())
            {
                var now = DateTime.UtcNow;
                try
                {
                    if (HoldingMonitorEnabled && (now - _lastHoldingReadUtc).TotalMilliseconds >= Math.Max(50, HoldingMonitorPeriodMs))
                    {
                        _lastHoldingReadUtc = now;
                        await ReadAreaAsync(PlcArea.HoldingRegister, token);
                    }

                    if (InputRegistersMonitorEnabled && (now - _lastInputRegReadUtc).TotalMilliseconds >= Math.Max(50, InputRegistersMonitorPeriodMs))
                    {
                        _lastInputRegReadUtc = now;
                        await ReadAreaAsync(PlcArea.InputRegister, token);
                    }

                    if (CoilsMonitorEnabled && (now - _lastCoilsReadUtc).TotalMilliseconds >= Math.Max(50, CoilsMonitorPeriodMs))
                    {
                        _lastCoilsReadUtc = now;
                        await ReadAreaAsync(PlcArea.Coil, token);
                    }

                    if (DiscreteInputsMonitorEnabled && (now - _lastDiscreteReadUtc).TotalMilliseconds >= Math.Max(50, DiscreteInputsMonitorPeriodMs))
                    {
                        _lastDiscreteReadUtc = now;
                        await ReadAreaAsync(PlcArea.DiscreteInput, token);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Poll failed");
                    _dispatcher.Invoke(() => StatusMessage = $"Poll error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(100, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ReadAreaAsync(PlcArea area, CancellationToken token)
        {
            var service = ActiveService;
            if (service == null || ActiveProfile == null)
            {
                _dispatcher.Invoke(() => StatusMessage = "No active service.");
                return;
            }

            var unitId = EffectiveUnitId;
            var (start, count) = GetAreaStartCount(area);

            _dispatcher.Invoke(() => StatusMessage = $"Reading {area}...");

            try
            {
                token.ThrowIfCancellationRequested();
                switch (area)
                {
                    case PlcArea.HoldingRegister:
                        var holding = await service.ReadHoldingRegistersAsync(unitId, start, count);
                        if (holding != null)
                        {
                            _dispatcher.Invoke(() =>
                            {
                                ApplyRegisterValues(HoldingRegisters, start, holding, RegistersGlobalType, RegistersSwapBytes, RegistersSwapWords);
                                StatusMessage = $"Read {holding.Length} holding registers";
                            });
                        }
                        break;

                    case PlcArea.InputRegister:
                        var input = await service.ReadInputRegistersAsync(unitId, start, count);
                        if (input != null)
                        {
                            _dispatcher.Invoke(() =>
                            {
                                ApplyRegisterValues(InputRegisters, start, input, InputRegistersGlobalType, InputRegistersSwapBytes, InputRegistersSwapWords);
                                StatusMessage = $"Read {input.Length} input registers";
                            });
                        }
                        break;

                    case PlcArea.Coil:
                        var coils = await service.ReadCoilsAsync(unitId, start, count);
                        if (coils != null)
                        {
                            _dispatcher.Invoke(() =>
                            {
                                ApplyCoilValues(Coils, start, coils);
                                StatusMessage = $"Read {coils.Length} coils";
                            });
                        }
                        break;

                    case PlcArea.DiscreteInput:
                        var discrete = await service.ReadDiscreteInputsAsync(unitId, start, count);
                        if (discrete != null)
                        {
                            _dispatcher.Invoke(() =>
                            {
                                ApplyCoilValues(DiscreteInputs, start, discrete);
                                StatusMessage = $"Read {discrete.Length} discrete inputs";
                            });
                        }
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error reading {Area}", area);
                _dispatcher.Invoke(() => StatusMessage = $"Error reading {area}: {ex.Message}");
            }
        }

        private void ApplyRegisterValues(ObservableCollection<RegisterEntry> target, int start, ushort[] values, string globalType, bool swapBytes, bool swapWords)
        {
            target.Clear();

            int idx = 0;
            while (idx < values.Length)
            {
                var address = start + idx;
                var type = globalType.ToLowerInvariant();
                var entry = new RegisterEntry
                {
                    Address = address,
                    Value = values[idx],
                    Type = type,
                    SwapBytes = swapBytes,
                    SwapWords = swapWords
                };

                switch (type)
                {
                    case "int":
                        entry.ValueText = unchecked((short)values[idx]).ToString(CultureInfo.InvariantCulture);
                        target.Add(entry);
                        idx += 1;
                        break;

                    case "real":
                        if (idx + 1 < values.Length)
                        {
                            entry.ValueText = DataTypeConverter.ToSingle(values[idx], values[idx + 1], swapBytes, swapWords).ToString(CultureInfo.InvariantCulture);
                            target.Add(entry);

                            var next = new RegisterEntry
                            {
                                Address = address + 1,
                                Value = values[idx + 1],
                                Type = type,
                                SwapBytes = swapBytes,
                                SwapWords = swapWords,
                                ValueText = string.Empty
                            };
                            target.Add(next);

                            idx += 2;
                        }
                        else
                        {
                            entry.ValueText = values[idx].ToString(CultureInfo.InvariantCulture);
                            target.Add(entry);
                            idx += 1;
                        }
                        break;

                    case "string":
                        entry.ValueText = DataTypeConverter.ToString(values[idx]);
                        target.Add(entry);
                        idx += 1;
                        break;

                    default:
                        entry.ValueText = values[idx].ToString(CultureInfo.InvariantCulture);
                        target.Add(entry);
                        idx += 1;
                        break;
                }
            }
        }

        private void ApplyCoilValues(ObservableCollection<CoilEntry> target, int start, bool[] values)
        {
            target.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                target.Add(new CoilEntry
                {
                    Address = start + i,
                    State = values[i]
                });
            }
        }

        #region Custom Watch

        private async Task AddCustomEntryAsync()
        {
            await _dispatcher.InvokeAsync(() =>
            {
                int nextAddress = 0;
                string type = "uint";
                string area = "HoldingRegister";
                string name = "Tag0";

                if (CustomEntries.Count > 0)
                {
                    var last = CustomEntries[^1];
                    type = last.Type ?? "uint";
                    area = last.Area ?? "HoldingRegister";
                    name = GenerateNextName(last.Name);

                    int increment = (type == "real") ? 2 : 1;
                    nextAddress = Math.Max(0, last.Address + increment);
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
                    PeriodMs = 1000,
                    Monitor = false,
                    ReadPeriodMs = 1000
                };

                CustomEntries.Add(entry);
                SelectedCustomEntry = entry;
                StatusMessage = $"Added custom entry {name}.";
            });
        }

        private async Task RemoveCustomEntryAsync()
        {
            if (SelectedCustomEntry == null) return;

            await _dispatcher.InvokeAsync(() =>
            {
                CustomEntries.Remove(SelectedCustomEntry);
                SelectedCustomEntry = null;
                StatusMessage = "Removed custom entry.";
            });
        }

        private async Task ReadCustomEntryAsync()
        {
            if (SelectedCustomEntry == null || ActiveService == null || ActiveProfile == null) return;

            IsBusy = true;
            try
            {
                var value = await ReadCustomValueAsync(SelectedCustomEntry);
                await _dispatcher.InvokeAsync(() =>
                {
                    SelectedCustomEntry.Value = value;
                    SelectedCustomEntry.LastReadUtc = DateTime.UtcNow;
                    StatusMessage = $"Read {SelectedCustomEntry.Name} = {value}";
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading custom entry");
                StatusMessage = $"Read error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task WriteCustomEntryAsync()
        {
            if (SelectedCustomEntry == null || ActiveService == null || ActiveProfile == null) return;

            IsBusy = true;
            try
            {
                var result = await WriteCustomValueAsync(SelectedCustomEntry);
                await _dispatcher.InvokeAsync(() =>
                {
                    SelectedCustomEntry.LastWriteUtc = DateTime.UtcNow;
                    StatusMessage = result ? $"Wrote {SelectedCustomEntry.Name}" : "Write completed with issues.";
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing custom entry");
                StatusMessage = $"Write error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<string> ReadCustomValueAsync(CustomEntry entry)
        {
            var service = ActiveService;
            if (service == null || ActiveProfile == null) return string.Empty;

            var unitId = EffectiveUnitId;
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "uint").ToLowerInvariant();

            switch (area)
            {
                case "holdingregister":
                case "inputregister":
                    int count = type == "real" ? 2 : 1;
                    var areaEnum = area == "holdingregister" ? PlcArea.HoldingRegister : PlcArea.InputRegister;
                    var values = areaEnum == PlcArea.HoldingRegister
                        ? await service.ReadHoldingRegistersAsync(unitId, entry.Address, count)
                        : await service.ReadInputRegistersAsync(unitId, entry.Address, count);

                    if (values == null || values.Length == 0) return "No response";

                    return type switch
                    {
                        "int" => unchecked((short)values[0]).ToString(CultureInfo.InvariantCulture),
                        "real" when values.Length >= 2 => DataTypeConverter.ToSingle(values[0], values[1], entry.Type?.ToLowerInvariant() == "real" ? false : false, false).ToString(CultureInfo.InvariantCulture),
                        "string" => DataTypeConverter.ToString(values[0]),
                        _ => values[0].ToString(CultureInfo.InvariantCulture)
                    };

                case "coil":
                case "discreteinput":
                    var coilValues = area == "coil"
                        ? await service.ReadCoilsAsync(unitId, entry.Address, 1)
                        : await service.ReadDiscreteInputsAsync(unitId, entry.Address, 1);
                    if (coilValues == null || coilValues.Length == 0) return "No response";
                    return coilValues[0] ? "1" : "0";

                default:
                    return $"Unknown area: {entry.Area}";
            }
        }

        private async Task<bool> WriteCustomValueAsync(CustomEntry entry)
        {
            var service = ActiveService;
            if (service == null || ActiveProfile == null) return false;

            var unitId = EffectiveUnitId;
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            var type = (entry.Type ?? "uint").ToLowerInvariant();

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
            if (_customWatchCts != null) return;
            if (ActiveProfile is not { IsConnected: true }) return;
            if (!IsCustomWatchMonitoring) return;

            _customWatchCts = new CancellationTokenSource();
            _ = Task.Run(() => CustomWatchLoopAsync(_customWatchCts.Token), _customWatchCts.Token);
        }

        private void StopCustomWatchMonitoring()
        {
            _customWatchCts?.Cancel();
            _customWatchCts?.Dispose();
            _customWatchCts = null;
        }

        private async Task CustomWatchLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && ActiveProfile is { IsConnected: true } && IsCustomWatchMonitoring)
            {
                try
                {
                    var entries = await _dispatcher.InvokeAsync(() => CustomEntries.ToList());
                    var now = DateTime.UtcNow;

                    foreach (var entry in entries)
                    {
                        if (token.IsCancellationRequested) break;

                        if (entry.Monitor && (now - entry.LastReadUtc).TotalMilliseconds >= entry.ReadPeriodMs)
                        {
                            try
                            {
                                var value = await ReadCustomValueAsync(entry);
                                await _dispatcher.InvokeAsync(() =>
                                {
                                    entry.Value = value;
                                    entry.LastReadUtc = now;
                                });

                                if (entry.Trend && _trendLogger != null)
                                {
                                    if (TryParseTrendValue(value, out var trendValue))
                                    {
                                        _trendLogger.Publish(entry.Name, trendValue, now);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Custom read failed for {Name}", entry.Name);
                            }
                        }

                        if (entry.Continuous && (now - entry.LastWriteUtc).TotalMilliseconds >= entry.PeriodMs)
                        {
                            try
                            {
                                await WriteCustomValueAsync(entry);
                                await _dispatcher.InvokeAsync(() => entry.LastWriteUtc = now);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Custom write failed for {Name}", entry.Name);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Custom watch loop failed");
                }

                try
                {
                    await Task.Delay(100, token);
                }
                catch (OperationCanceledException)
                {
                    break;
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

        private async Task SaveProjectAsync()
        {
            if (_fileDialogService == null) return;

            IsBusy = true;
            try
            {
                var path = await _fileDialogService.ShowSaveFileDialogAsync(
                    "Save Project",
                    "ModbusForge Project (*.mfp)|*.mfp|JSON files (*.json)|*.json",
                    "project.mfp");

                if (path == null) return;

                var config = new AppConfiguration
                {
                    Profiles = _connectionManager.Profiles.ToList(),
                    ActiveProfileId = ActiveProfile?.Id,
                    CustomEntries = CustomEntries.ToList(),
                    StartAddress = StartAddress,
                    RegisterCount = RegisterCount,
                    SelectedArea = SelectedArea,
                    GlobalType = GlobalType,
                    SwapBytes = SwapBytes,
                    SwapWords = SwapWords,
                    UnitId = (byte)UnitId
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                await using var stream = File.Create(path);
                await JsonSerializer.SerializeAsync(stream, config, options);

                StatusMessage = $"Saved project to {Path.GetFileName(path)}.";
            }
            catch (Exception ex)
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
                    "Load Project",
                    "ModbusForge Project (*.mfp)|*.mfp|JSON files (*.json)|*.json");

                if (path == null) return;

                await using var stream = File.OpenRead(path);
                var config = await JsonSerializer.DeserializeAsync<AppConfiguration>(stream);
                if (config == null)
                {
                    StatusMessage = "Project file is empty.";
                    return;
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    _connectionManager.Profiles.Clear();
                    foreach (var profile in config.Profiles ?? new())
                    {
                        _connectionManager.AddProfile(profile);
                    }

                    var active = _connectionManager.Profiles.FirstOrDefault(p => p.Id == config.ActiveProfileId)
                                 ?? _connectionManager.Profiles.FirstOrDefault();

                    if (active != null)
                    {
                        _connectionManager.SetActiveProfile(active);
                    }

                    _connectionManager.SaveProfiles();

                    CustomEntries.Clear();
                    foreach (var e in config.CustomEntries ?? new())
                    {
                        CustomEntries.Add(e);
                    }

                    SelectedArea = config.SelectedArea;
                    StartAddress = config.StartAddress;
                    RegisterCount = config.RegisterCount;
                    GlobalType = config.GlobalType ?? "uint";
                    SwapBytes = config.SwapBytes;
                    SwapWords = config.SwapWords;
                    UnitId = config.UnitId;

                    StatusMessage = $"Loaded project from {Path.GetFileName(path)}.";
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading project");
                StatusMessage = $"Load error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
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
                    var msg = $"A newer version is available: {result.LatestVersion}\nCurrent: {result.CurrentVersion}\n\nOpen release page?";
                    if (_messageBoxService != null)
                    {
                        var dialogResult = await _messageBoxService.ShowAsync(msg, "Update Available", DialogButton.YesNo, DialogIcon.Question);
                        if (dialogResult == DialogResult.Yes)
                        {
                            OpenUrl(result.ReleaseUrl);
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
                    _messageBoxService?.ShowAsync($"You are running the latest version ({result.CurrentVersion}).", "No Update", DialogButton.Ok, DialogIcon.Information).ConfigureAwait(false);
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
            _connectionManager.ActiveProfileChanged -= ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected -= ConnectionManager_ProfileConnected;
            _connectionManager.ProfileDisconnected -= ConnectionManager_ProfileDisconnected;

            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged -= ActiveProfile_PropertyChanged;
            }

            _trendLogger?.Stop();
            StopPolling();
            StopCustomWatchMonitoring();
        }
    }
}
