using System;
using System.Windows;
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
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Specialized;
using ModbusForge.Models;
using System.Windows.Controls;
using ModbusForge.Helpers;
using ModbusForge.ViewModels.Coordinators;

namespace ModbusForge.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IDisposable
    {
        private IModbusService _modbusService;
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
        private readonly IConsoleLoggerService _consoleLoggerService;
        private readonly ConnectionCoordinator _connectionCoordinator;
        private readonly RegisterCoordinator _registerCoordinator;
        private readonly CustomEntryCoordinator _customEntryCoordinator;
        private bool _disposed = false;
        // Mode-aware UI helpers

        public bool IsServerMode => string.Equals(Mode, "Server", StringComparison.OrdinalIgnoreCase);
        public bool ShowClientFields => !IsServerMode; // show IP/UnitId only in client mode
        public string ConnectButtonText => IsServerMode ? "Start Server" : "Connect";
        public string ConnectionHeader => IsServerMode ? "Modbus Connection (Server)" : "Modbus Connection (Client)";

        public MainViewModel() : this(
            App.ServiceProvider.GetRequiredService<ModbusTcpService>(),
            App.ServiceProvider.GetRequiredService<ModbusServerService>(),
            App.ServiceProvider.GetRequiredService<ILogger<MainViewModel>>(),
            App.ServiceProvider.GetRequiredService<IOptions<ServerSettings>>(),
            App.ServiceProvider.GetRequiredService<ITrendLogger>(),
            App.ServiceProvider.GetRequiredService<ISimulationService>(),
            App.ServiceProvider.GetRequiredService<ICustomEntryService>(),
            App.ServiceProvider.GetRequiredService<IConsoleLoggerService>(),
            App.ServiceProvider.GetRequiredService<ConnectionCoordinator>(),
            App.ServiceProvider.GetRequiredService<RegisterCoordinator>(),
            App.ServiceProvider.GetRequiredService<CustomEntryCoordinator>())
        {
        }

        private async Task ReadAllCustomNowAsync()
        {
            if (!IsConnected) return;
            var snapshot = CustomEntries.ToList();
            foreach (var ce in snapshot)
            {
                try { await ReadCustomNowAsync(ce); }
                catch (Exception ex) { _logger.LogDebug(ex, "ReadAllCustomNow: failed for {Area} {Address}", ce.Area, ce.Address); }
            }
            StatusMessage = $"Read {snapshot.Count} custom entries";
        }

        public MainViewModel(ModbusTcpService clientService, ModbusServerService serverService, ILogger<MainViewModel> logger, IOptions<ServerSettings> options, ITrendLogger trendLogger, ISimulationService simulationService, ICustomEntryService customEntryService, IConsoleLoggerService consoleLoggerService, ConnectionCoordinator connectionCoordinator, RegisterCoordinator registerCoordinator, CustomEntryCoordinator customEntryCoordinator)
        {
            // Store dependencies
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
            _simulationService = simulationService ?? throw new ArgumentNullException(nameof(simulationService));
            _customEntryService = customEntryService ?? throw new ArgumentNullException(nameof(customEntryService));
            _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));
            _connectionCoordinator = connectionCoordinator ?? throw new ArgumentNullException(nameof(connectionCoordinator));
            _registerCoordinator = registerCoordinator ?? throw new ArgumentNullException(nameof(registerCoordinator));
            _customEntryCoordinator = customEntryCoordinator ?? throw new ArgumentNullException(nameof(customEntryCoordinator));
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
            catch { /* best-effort defaults from config */ }
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
                    await ReadCustomNowAsync(ce);
            });
            ReadAllCustomNowCommand = new RelayCommand(async () => await ReadAllCustomNowAsync());
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
                    Title = "ModbusForge v2.3.0";
                    Version = "2.3.0";
                }
            }
            catch
            {
                Title = "ModbusForge v2.3.0";
                Version = "2.3.0";
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
                var version = FileVersionInfo.GetVersionInfo(procPath)?.ProductVersion;
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }

            // Fallback to assembly attribute
            return Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
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
            try { _trendLogger.Start(); } catch { }
            SubscribeCustomEntries();
            _simulationService.Start(this);
        }

        [ObservableProperty]
        private string _title = "ModbusForge";

        [ObservableProperty]
        private string _version = "2.1.1";

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
                    try { _modbusService.DisconnectAsync().GetAwaiter().GetResult(); }
                    catch { }
                    IsConnected = false;
                    StatusMessage = "Disconnected";
                }

                _modbusService = IsServerMode ? _serverService : _clientService;

                // Update dependent computed properties
                OnPropertyChanged(nameof(IsServerMode));
                OnPropertyChanged(nameof(ShowClientFields));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(ConnectionHeader));
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

        // Modbus addressing defaults
        [ObservableProperty]
        private byte _unitId = 1;

        // Registers UI state
        [ObservableProperty]
        private int _registerStart = 1;

        [ObservableProperty]
        private int _registerCount = 10;

        [ObservableProperty]
        private int _writeRegisterAddress = 1;

        [ObservableProperty]
        private ushort _writeRegisterValue = 0;

        public ObservableCollection<RegisterEntry> HoldingRegisters { get; } = new();

        // Coils UI state
        [ObservableProperty]
        private int _coilStart = 1;

        [ObservableProperty]
        private int _coilCount = 16;

        [ObservableProperty]
        private int _writeCoilAddress = 1;

        [ObservableProperty]
        private bool _writeCoilState = false;

        public ObservableCollection<CoilEntry> Coils { get; } = new();

        // Input Registers UI state
        [ObservableProperty]
        private int _inputRegisterStart = 1;

        [ObservableProperty]
        private int _inputRegisterCount = 10;

        public ObservableCollection<RegisterEntry> InputRegisters { get; } = new();

        // Discrete Inputs UI state
        [ObservableProperty]
        private int _discreteInputStart = 1;

        [ObservableProperty]
        private int _discreteInputCount = 16;

        public ObservableCollection<CoilEntry> DiscreteInputs { get; } = new();

        // Global type selectors for registers
        [ObservableProperty]
        private string _registersGlobalType = "uint"; // options: uint,int,real,string

        [ObservableProperty]
        private string _inputRegistersGlobalType = "uint";

        private IRelayCommand? _disconnectCommand;
        private readonly ILogger<MainViewModel> _logger;
        private DispatcherTimer _customTimer;
        private DispatcherTimer _monitorTimer;
        private DispatcherTimer _trendTimer;
        private readonly ITrendLogger _trendLogger;
        private readonly ICustomEntryService _customEntryService;
        private bool _isMonitoring;
        private bool _isTrending;
        private readonly ISimulationService _simulationService;
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

        public ObservableCollection<string> ConsoleMessages => _consoleLoggerService.LogMessages;

        // Custom tab
        public ObservableCollection<CustomEntry> CustomEntries { get; } = new();
        public ICommand AddCustomEntryCommand { get; private set; }
        public ICommand WriteCustomNowCommand { get; private set; }
        public ICommand ReadCustomNowCommand { get; private set; }
        public IRelayCommand ReadAllCustomNowCommand { get; private set; }
        public IRelayCommand SaveCustomCommand { get; private set; }
        public IRelayCommand LoadCustomCommand { get; private set; }
        public IRelayCommand SaveAllConfigCommand { get; private set; }
        public IRelayCommand LoadAllConfigCommand { get; private set; }
        public IAsyncRelayCommand<DataGridCellEditEndingEventArgs> UpdateHoldingRegisterCommand { get; private set; }

        // Global toggles for Custom tab
        [ObservableProperty]
        private bool _customMonitorEnabled = false;

        [ObservableProperty]
        private bool _customReadMonitorEnabled = false;

        // Global continuous read toggle (gates all periodic reads including trend sampling)
        [ObservableProperty]
        private bool _globalMonitorEnabled = false;

        // Monitoring toggles and periods
        [ObservableProperty]
        private bool _holdingMonitorEnabled = false;

        [ObservableProperty]
        private int _holdingMonitorPeriodMs = 1000;

        [ObservableProperty]
        private bool _inputRegistersMonitorEnabled = false;

        [ObservableProperty]
        private int _inputRegistersMonitorPeriodMs = 1000;

        [ObservableProperty]
        private bool _coilsMonitorEnabled = false;

        [ObservableProperty]
        private int _coilsMonitorPeriodMs = 1000;

        [ObservableProperty]
        private bool _discreteInputsMonitorEnabled = false;

        [ObservableProperty]
        private int _discreteInputsMonitorPeriodMs = 1000;

        // Simulation tab configuration
        [ObservableProperty]
        private bool _simulationEnabled = false;

        [ObservableProperty]
        private int _simulationPeriodMs = 500;

        // Holding Registers ramp
        [ObservableProperty]
        private bool _simHoldingsEnabled = false;

        [ObservableProperty]
        private int _simHoldingStart = 1;

        [ObservableProperty]
        private int _simHoldingCount = 4;

        [ObservableProperty]
        private int _simHoldingMin = 0;

        [ObservableProperty]
        private int _simHoldingMax = 100;

        // Holding Registers waveform parameters
        [ObservableProperty]
        private string _simHoldingWaveformType = "Ramp"; // Ramp, Sine, Triangle, Square

        [ObservableProperty]
        private double _simHoldingAmplitude = 1000.0;

        [ObservableProperty]
        private double _simHoldingFrequencyHz = 0.5;

        [ObservableProperty]
        private double _simHoldingOffset = 0.0;

        // Coils toggle
        [ObservableProperty]
        private bool _simCoilsEnabled = false;

        [ObservableProperty]
        private int _simCoilStart = 1;

        [ObservableProperty]
        private int _simCoilCount = 8;

        // Input Registers ramp
        [ObservableProperty]
        private bool _simInputsEnabled = false;

        [ObservableProperty]
        private int _simInputStart = 1;

        [ObservableProperty]
        private int _simInputCount = 4;

        [ObservableProperty]
        private int _simInputMin = 0;

        [ObservableProperty]
        private int _simInputMax = 100;

        // Discrete Inputs toggle
        [ObservableProperty]
        private bool _simDiscreteEnabled = false;

        [ObservableProperty]
        private int _simDiscreteStart = 1;

        [ObservableProperty]
        private int _simDiscreteCount = 8;


        private bool CanConnect() => _connectionCoordinator.CanConnect(IsConnected);

        private async Task ConnectAsync()
        {
            await _connectionCoordinator.ConnectAsync(ServerAddress, Port, IsServerMode,
                msg => StatusMessage = msg, connected => IsConnected = connected);
        }

        private bool CanDisconnect() => _connectionCoordinator.CanDisconnect(IsConnected);

        private async Task DisconnectAsync()
        {
            await _connectionCoordinator.DisconnectAsync(IsServerMode,
                msg => StatusMessage = msg, connected => IsConnected = connected);
        }

        private async Task RunDiagnosticsAsync()
        {
            await _connectionCoordinator.RunDiagnosticsAsync(ServerAddress, Port, UnitId,
                msg => StatusMessage = msg);
        }

        private async Task ReadRegistersAsync()
        {
            await _registerCoordinator.ReadRegistersAsync(UnitId, RegisterStart, RegisterCount,
                RegistersGlobalType, HoldingRegisters, msg => StatusMessage = msg,
                hasError => _hasConnectionError = hasError, HoldingMonitorEnabled, IsServerMode);
        }

        private async Task ReadInputRegistersAsync()
        {
            await _registerCoordinator.ReadInputRegistersAsync(UnitId, InputRegisterStart, InputRegisterCount,
                InputRegistersGlobalType, InputRegisters, msg => StatusMessage = msg,
                hasError => _hasConnectionError = hasError, InputRegistersMonitorEnabled, IsServerMode);
        }

        private async Task WriteRegisterAsync()
        {
            await _registerCoordinator.WriteRegisterAsync(UnitId, WriteRegisterAddress, WriteRegisterValue,
                msg => StatusMessage = msg, async () => await ReadRegistersAsync(), IsServerMode);
        }

        private async Task ReadCoilsAsync()
        {
            await _registerCoordinator.ReadCoilsAsync(UnitId, CoilStart, CoilCount,
                Coils, msg => StatusMessage = msg,
                hasError => _hasConnectionError = hasError, CoilsMonitorEnabled, IsServerMode);
        }

        private async Task ReadDiscreteInputsAsync()
        {
            await _registerCoordinator.ReadDiscreteInputsAsync(UnitId, DiscreteInputStart, DiscreteInputCount,
                DiscreteInputs, msg => StatusMessage = msg,
                hasError => _hasConnectionError = hasError, DiscreteInputsMonitorEnabled, IsServerMode);
        }

        private async Task WriteCoilAsync()
        {
            await _registerCoordinator.WriteCoilAsync(UnitId, WriteCoilAddress, WriteCoilState,
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
            await _registerCoordinator.WriteRegisterAtAsync(UnitId, address, value, IsServerMode);
        }

        public async Task WriteFloatAtAsync(int address, float value)
        {
            await _registerCoordinator.WriteFloatAtAsync(UnitId, address, value, IsServerMode);
        }

        public async Task WriteStringAtAsync(int address, string text)
        {
            await _registerCoordinator.WriteStringAtAsync(UnitId, address, text, IsServerMode);
        }

        public async Task WriteCoilAtAsync(int address, bool state)
        {
            await _registerCoordinator.WriteCoilAtAsync(UnitId, address, state, IsServerMode);
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
                        _simulationService.Stop();
                        try { _trendLogger.Stop(); } catch { }
                        try
                        {
                            CustomEntries.CollectionChanged -= CustomEntries_CollectionChanged;
                            foreach (var ce in CustomEntries)
                            {
                                ce.PropertyChanged -= CustomEntry_PropertyChanged;
                            }
                        }
                        catch { }
                    }
                    catch { }
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
            await _customEntryCoordinator.WriteCustomNowAsync(entry, UnitId, msg => StatusMessage = msg, IsServerMode);
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
            var type = (entry.Type ?? "uint").ToLowerInvariant();
            var address = entry.Address;
            
            switch (type)
            {
                case "real":
                    var regsReal = await _modbusService.ReadHoldingRegistersAsync(UnitId, address, 2);
                    if (regsReal is null) return;
                    entry.Value = DataTypeConverter.ToSingle(regsReal[0], regsReal[1]).ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read REAL {entry.Value} from HR {address}";
                    break;
                case "int":
                    var regsInt = await _modbusService.ReadHoldingRegistersAsync(UnitId, address, 1);
                    if (regsInt is null) return;
                    entry.Value = unchecked((short)regsInt[0]).ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read INT {entry.Value} from HR {address}";
                    break;
                case "string":
                    var regsString = await _modbusService.ReadHoldingRegistersAsync(UnitId, address, 1);
                    if (regsString is null) return;
                    entry.Value = DataTypeConverter.ToString(regsString[0]);
                    StatusMessage = $"Read STRING '{entry.Value}' from HR {address}";
                    break;
                default: // uint
                    var regsUInt = await _modbusService.ReadHoldingRegistersAsync(UnitId, address, 1);
                    if (regsUInt is null) return;
                    entry.Value = regsUInt[0].ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read UINT {entry.Value} from HR {address}";
                    break;
            }
        }

        /// <summary>
        /// Reads an input register value based on the entry's data type.
        /// </summary>
        private async Task ReadInputRegisterByTypeAsync(CustomEntry entry)
        {
            var type = (entry.Type ?? "uint").ToLowerInvariant();
            var address = entry.Address;
            
            switch (type)
            {
                case "real":
                    var regsReal = await _modbusService.ReadInputRegistersAsync(UnitId, address, 2);
                    if (regsReal is null) return;
                    entry.Value = DataTypeConverter.ToSingle(regsReal[0], regsReal[1]).ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read REAL {entry.Value} from IR {address}";
                    break;
                case "int":
                    var regsInt = await _modbusService.ReadInputRegistersAsync(UnitId, address, 1);
                    if (regsInt is null) return;
                    entry.Value = unchecked((short)regsInt[0]).ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read INT {entry.Value} from IR {address}";
                    break;
                case "string":
                    var regsString = await _modbusService.ReadInputRegistersAsync(UnitId, address, 1);
                    if (regsString is null) return;
                    entry.Value = DataTypeConverter.ToString(regsString[0]);
                    StatusMessage = $"Read STRING '{entry.Value}' from IR {address}";
                    break;
                default: // uint
                    var regsUInt = await _modbusService.ReadInputRegistersAsync(UnitId, address, 1);
                    if (regsUInt is null) return;
                    entry.Value = regsUInt[0].ToString(CultureInfo.InvariantCulture);
                    StatusMessage = $"Read UINT {entry.Value} from IR {address}";
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
            if (_isTrending) return;
            if (!IsConnected) return;
            if (!GlobalMonitorEnabled) return;

            var snapshot = CustomEntries.Where(c => c.Trend).ToList();
            if (snapshot.Count == 0) return;

            _isTrending = true;
            try
            {
                var now = DateTime.UtcNow;
                int errorCount = 0;
                foreach (var ce in snapshot)
                {
                    try
                    {
                        var val = await ReadValueForTrendAsync(ce);
                        if (val.HasValue)
                        {
                            _trendLogger.Publish(GetTrendKey(ce), val.Value, now);
                            var area = (ce.Area ?? "HoldingRegister").ToLowerInvariant();
                            var type = (ce.Type ?? "uint").ToLowerInvariant();
                            string display;
                            if (area == "coil" || area == "discreteinput")
                            {
                                display = val.Value != 0.0 ? "1" : "0";
                            }
                            else if (type == "real")
                            {
                                display = val.Value.ToString("G9", CultureInfo.InvariantCulture);
                            }
                            else if (type == "int")
                            {
                                display = ((short)val.Value).ToString(CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                display = ((ushort)val.Value).ToString(CultureInfo.InvariantCulture);
                            }
                            ce.Value = display;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Trend read failed for {Area} {Address}", ce.Area, ce.Address);
                        errorCount++;
                    }
                }
                
                if (errorCount > 0 && errorCount == snapshot.Count)
                {
                    GlobalMonitorEnabled = false;
                    MessageBox.Show($"All trend reads failed. Continuous monitoring has been paused. Fix the issue and re-enable monitoring.", "Trend Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                _isTrending = false;
            }
        }

        private async Task<double?> ReadValueForTrendAsync(CustomEntry entry)
        {
            var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
            switch (area)
            {
                case "holdingregister":
                    {
                        var t = (entry.Type ?? "uint").ToLowerInvariant();
                        if (t == "real")
                        {
                            var regs = await _modbusService.ReadHoldingRegistersAsync(UnitId, entry.Address, 2);
                            if (regs is null) return null;
                            return DataTypeConverter.ToSingle(regs[0], regs[1]);
                        }
                        else if (t == "int")
                        {
                            var regs = await _modbusService.ReadHoldingRegistersAsync(UnitId, entry.Address, 1);
                            if (regs is null) return null;
                            short sv = unchecked((short)regs[0]);
                            return (double)sv;
                        }
                        else if (t == "string")
                        {
                            // not a numeric trend; skip
                            return null;
                        }
                        else // uint
                        {
                            var regs = await _modbusService.ReadHoldingRegistersAsync(UnitId, entry.Address, 1);
                            if (regs is null) return null;
                            return (double)regs[0];
                        }
                    }
                case "inputregister":
                    {
                        var t = (entry.Type ?? "uint").ToLowerInvariant();
                        if (t == "real")
                        {
                            var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 2);
                            if (regs is null) return null;
                            return DataTypeConverter.ToSingle(regs[0], regs[1]);
                        }
                        else if (t == "int")
                        {
                            var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 1);
                            if (regs is null) return null;
                            short sv = unchecked((short)regs[0]);
                            return (double)sv;
                        }
                        else if (t == "string")
                        {
                            return null;
                        }
                        else // uint
                        {
                            var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 1);
                            if (regs is null) return null;
                            return (double)regs[0];
                        }
                    }
                case "coil":
                    {
                        var states = await _modbusService.ReadCoilsAsync(UnitId, entry.Address, 1);
                        if (states is null) return null;
                        return states[0] ? 1.0 : 0.0;
                    }
                case "discreteinput":
                    {
                        var states = await _modbusService.ReadDiscreteInputsAsync(UnitId, entry.Address, 1);
                        if (states is null) return null;
                        return states[0] ? 1.0 : 0.0;
                    }
                default:
                    return null;
            }
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
                        Mode = Mode,
                        ServerAddress = ServerAddress,
                        Port = Port,
                        UnitId = UnitId,
                        CustomEntries = CustomEntries.ToList()
                    };

                    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(dialog.FileName, json);
                    StatusMessage = $"Saved configuration to {Path.GetFileName(dialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving configuration");
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadAllConfigAsync()
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
                    var json = await File.ReadAllTextAsync(dialog.FileName);
                    var config = JsonSerializer.Deserialize<AppConfiguration>(json);

                    if (config != null)
                    {
                        if (!string.IsNullOrWhiteSpace(config.Mode))
                            Mode = config.Mode;
                        if (!string.IsNullOrWhiteSpace(config.ServerAddress))
                            ServerAddress = config.ServerAddress;
                        if (config.Port > 0)
                            Port = config.Port;
                        if (config.UnitId > 0)
                            UnitId = config.UnitId;

                        if (config.CustomEntries != null && config.CustomEntries.Any())
                        {
                            CustomEntries.Clear();
                            foreach (var ce in config.CustomEntries)
                                CustomEntries.Add(ce);
                            SubscribeCustomEntries();
                        }

                        StatusMessage = $"Loaded configuration from {Path.GetFileName(dialog.FileName)}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration");
                MessageBox.Show($"Failed to load configuration: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
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