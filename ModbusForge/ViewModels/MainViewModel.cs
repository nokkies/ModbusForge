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

namespace ModbusForge.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IDisposable
    {
        private IModbusService _modbusService;
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
        private readonly IConsoleLoggerService _consoleLoggerService;
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
            App.ServiceProvider.GetRequiredService<IConsoleLoggerService>())
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

        public MainViewModel(ModbusTcpService clientService, ModbusServerService serverService, ILogger<MainViewModel> logger, IOptions<ServerSettings> options, ITrendLogger trendLogger, ISimulationService simulationService, ICustomEntryService customEntryService, IConsoleLoggerService consoleLoggerService)
        {
            // Store dependencies
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
            _simulationService = simulationService ?? throw new ArgumentNullException(nameof(simulationService));
            _customEntryService = customEntryService ?? throw new ArgumentNullException(nameof(customEntryService));
            _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));
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
                Title = !string.IsNullOrWhiteSpace(version) 
                    ? $"ModbusForge v{version}" 
                    : "ModbusForge v2.1.0";
            }
            catch
            {
                Title = "ModbusForge v2.1.0";
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
        private string _version = "2.1.0";

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


        private bool CanConnect() => !IsConnected;

        private async Task ConnectAsync()
        {
            try
            {
                StatusMessage = IsServerMode ? "Starting server..." : "Connecting...";
                _consoleLoggerService.Log(StatusMessage);
                var success = await _modbusService.ConnectAsync(ServerAddress, Port);

                if (success)
                {
                    IsConnected = true;
                    _hasConnectionError = false;
                    StatusMessage = IsServerMode ? "Server started" : "Connected to Modbus server";
                    _logger.LogInformation(IsServerMode ? "Successfully started Modbus server" : "Successfully connected to Modbus server");
                    _consoleLoggerService.Log(StatusMessage);
                }
                else
                {
                    IsConnected = false;
                    StatusMessage = IsServerMode ? "Server failed to start" : "Connection failed";
                    _logger.LogWarning(IsServerMode ? "Failed to start Modbus server" : "Failed to connect to Modbus server");
                    _consoleLoggerService.Log(StatusMessage);
                    var msg = IsServerMode
                        ? $"Failed to start server on port {Port}. The port may be in use. Try another port (e.g., 1502) or stop the process using it."
                        : "Failed to connect to Modbus server.";
                    _consoleLoggerService.Log(msg);
                    MessageBox.Show(msg, IsServerMode ? "Server Error" : "Connection Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);

                    // If in Server mode, offer to retry automatically on alternative port 1502
                    if (IsServerMode)
                    {
                        var retry = MessageBox.Show(
                            "Would you like to retry starting the server on port 1502 now?",
                            "Try Alternative Port",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (retry == MessageBoxResult.Yes)
                        {
                            int originalPort = Port;
                            try
                            {
                                Port = 1502;
                                StatusMessage = $"Retrying server on port {Port}...";
                                _consoleLoggerService.Log(StatusMessage);
                                var retryOk = await _modbusService.ConnectAsync(ServerAddress, Port);
                                if (retryOk)
                                {
                                    IsConnected = true;
                                    StatusMessage = "Server started";
                                    _logger.LogInformation("Successfully started Modbus server on alternative port {AltPort}", Port);
                                    _consoleLoggerService.Log($"Successfully started Modbus server on alternative port {Port}");
                                }
                                else
                                {
                                    IsConnected = false;
                                    StatusMessage = "Server failed to start";
                                    _logger.LogWarning("Failed to start Modbus server on alternative port {AltPort}", Port);
                                    var failMsg = $"Failed to start server on alternative port {Port}. The port may also be in use or blocked.";
                                    _consoleLoggerService.Log(failMsg);
                                    MessageBox.Show(failMsg,
                                        "Server Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    // Restore original port so user sees their intended value
                                    Port = originalPort;
                                }
                            }
                            catch (Exception rex)
                            {
                                // Restore original port on any unexpected error
                                Port = originalPort;
                                StatusMessage = $"Server error: {rex.Message}";
                                _logger.LogError(rex, "Error retrying server start on alternative port 1502");
                                _consoleLoggerService.Log($"Failed to start server on alternative port 1502: {rex.Message}");
                                MessageBox.Show($"Failed to start server on alternative port 1502: {rex.Message}",
                                    "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = IsServerMode ? $"Server error: {ex.Message}" : $"Error: {ex.Message}";
                _logger.LogError(ex, IsServerMode ? "Error starting Modbus server" : "Error connecting to Modbus server");
                _consoleLoggerService.Log(StatusMessage);
                MessageBox.Show(IsServerMode ? $"Failed to start server: {ex.Message}" : $"Failed to connect: {ex.Message}", IsServerMode ? "Server Error" : "Connection Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanDisconnect() => IsConnected;

        private async Task DisconnectAsync()
        {
            try
            {
                var msg = IsServerMode ? "Stopping Modbus server" : "Disconnecting from Modbus server";
                _logger.LogInformation(msg);
                _consoleLoggerService.Log(msg);
                await _modbusService.DisconnectAsync();
                IsConnected = false;
                StatusMessage = IsServerMode ? "Server stopped" : "Disconnected";
                _logger.LogInformation(IsServerMode ? "Successfully stopped Modbus server" : "Successfully disconnected from Modbus server");
                _consoleLoggerService.Log(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = IsServerMode ? $"Error stopping server: {ex.Message}" : $"Error disconnecting: {ex.Message}";
                _logger.LogError(ex, IsServerMode ? "Error stopping Modbus server" : "Error disconnecting from Modbus server");
                _consoleLoggerService.Log(StatusMessage);
                MessageBox.Show(IsServerMode ? $"Failed to stop server: {ex.Message}" : $"Failed to disconnect: {ex.Message}", IsServerMode ? "Server Stop Error" : "Disconnection Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReadRegistersAsync()
        {
            try
            {
                StatusMessage = "Reading registers...";
                _consoleLoggerService.Log($"Reading {RegisterCount} holding registers from address {RegisterStart}");
                var values = await _modbusService.ReadHoldingRegistersAsync(UnitId, RegisterStart, RegisterCount);
                if (values is null)
                {
                    StatusMessage = "Failed to read registers (connection lost)";
                    return;
                }
                // Preserve per-address Type if rows already exist
                var typeByAddress = HoldingRegisters.ToDictionary(r => r.Address, r => r.Type);

                HoldingRegisters.Clear();
                for (int i = 0; i < values.Length; i++)
                {
                    int addr = RegisterStart + i;
                    var entry = new RegisterEntry
                    {
                        Address = addr,
                        Value = values[i],
                        Type = typeByAddress.TryGetValue(addr, out var t) ? t : RegistersGlobalType
                    };
                    HoldingRegisters.Add(entry);
                }

                // Compute ValueText based on Type for better display (floats, strings, signed ints)
                int idx = 0;
                while (idx < HoldingRegisters.Count)
                {
                    var entry = HoldingRegisters[idx];
                    var t = (entry.Type ?? "uint").ToLowerInvariant();
                    if (t == "int")
                    {
                        short sv = unchecked((short)values[idx]);
                        entry.ValueText = sv.ToString(CultureInfo.InvariantCulture);
                        idx += 1;
                    }
                    else if (t == "real")
                    {
                        if (idx + 1 < values.Length)
                        {
                            float f = DataTypeConverter.ToSingle(values[idx], values[idx + 1]);
                            entry.ValueText = f.ToString(CultureInfo.InvariantCulture);
                            HoldingRegisters[idx + 1].ValueText = string.Empty;
                        }
                        else
                        {
                            entry.ValueText = entry.Value.ToString(CultureInfo.InvariantCulture);
                        }
                        idx += 2;
                    }
                    else if (t == "string")
                    {
                        entry.ValueText = DataTypeConverter.ToString(values[idx]);
                        idx += 1;
                    }
                    else
                    {
                        entry.ValueText = entry.Value.ToString(CultureInfo.InvariantCulture);
                        idx += 1;
                    }
                }
                StatusMessage = $"Read {values.Length} registers";
                _hasConnectionError = false;
                _consoleLoggerService.Log(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error reading registers: {ex.Message}";
                _logger.LogError(ex, "Error reading registers");
                MessageBox.Show($"Failed to read registers: {ex.Message}", "Read Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReadInputRegistersAsync()
        {
            try
            {
                StatusMessage = "Reading input registers...";
                _consoleLoggerService.Log($"Reading {InputRegisterCount} input registers from address {InputRegisterStart}");
                var values = await _modbusService.ReadInputRegistersAsync(UnitId, InputRegisterStart, InputRegisterCount);
                if (values is null)
                {
                    StatusMessage = "Failed to read input registers (connection lost)";
                    return;
                }
                InputRegisters.Clear();
                for (int i = 0; i < values.Length; i++)
                {
                    InputRegisters.Add(new RegisterEntry
                    {
                        Address = InputRegisterStart + i,
                        Value = values[i],
                        Type = InputRegistersGlobalType
                    });
                }
                StatusMessage = $"Read {values.Length} input registers";
                _hasConnectionError = false;
                _consoleLoggerService.Log(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error reading input registers: {ex.Message}";
                _logger.LogError(ex, "Error reading input registers");
                MessageBox.Show($"Failed to read input registers: {ex.Message}", "Read Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task WriteRegisterAsync()
        {
            try
            {
                StatusMessage = "Writing register...";
                _consoleLoggerService.Log($"Writing register {WriteRegisterAddress} with value {WriteRegisterValue}");
                await _modbusService.WriteSingleRegisterAsync(UnitId, WriteRegisterAddress, WriteRegisterValue);
                StatusMessage = "Register written";
                _consoleLoggerService.Log(StatusMessage);
                // Optionally refresh
                await ReadRegistersAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error writing register: {ex.Message}";
                _logger.LogError(ex, "Error writing register");
                _consoleLoggerService.Log(StatusMessage);
                MessageBox.Show($"Failed to write register: {ex.Message}", "Write Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReadCoilsAsync()
        {
            try
            {
                StatusMessage = "Reading coils...";
                _consoleLoggerService.Log($"Reading {CoilCount} coils from address {CoilStart}");
                var states = await _modbusService.ReadCoilsAsync(UnitId, CoilStart, CoilCount);
                if (states is null)
                {
                    StatusMessage = "Failed to read coils (connection lost)";
                    return;
                }
                Coils.Clear();
                for (int i = 0; i < states.Length; i++)
                {
                    Coils.Add(new CoilEntry
                    {
                        Address = CoilStart + i,
                        State = states[i]
                    });
                }
                StatusMessage = $"Read {states.Length} coils";
                _hasConnectionError = false;
                _consoleLoggerService.Log(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error reading coils: {ex.Message}";
                _logger.LogError(ex, "Error reading coils");
                MessageBox.Show($"Failed to read coils: {ex.Message}", "Read Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReadDiscreteInputsAsync()
        {
            try
            {
                StatusMessage = "Reading discrete inputs...";
                _consoleLoggerService.Log($"Reading {DiscreteInputCount} discrete inputs from address {DiscreteInputStart}");
                var states = await _modbusService.ReadDiscreteInputsAsync(UnitId, DiscreteInputStart, DiscreteInputCount);
                if (states is null)
                {
                    StatusMessage = "Failed to read discrete inputs (connection lost)";
                    return;
                }
                DiscreteInputs.Clear();
                for (int i = 0; i < states.Length; i++)
                {
                    DiscreteInputs.Add(new CoilEntry
                    {
                        Address = DiscreteInputStart + i,
                        State = states[i]
                    });
                }
                StatusMessage = $"Read {states.Length} discrete inputs";
                _hasConnectionError = false;
                _consoleLoggerService.Log(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error reading discrete inputs: {ex.Message}";
                _logger.LogError(ex, "Error reading discrete inputs");
                MessageBox.Show($"Failed to read discrete inputs: {ex.Message}", "Read Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task WriteCoilAsync()
        {
            try
            {
                StatusMessage = "Writing coil...";
                _consoleLoggerService.Log($"Writing coil {WriteCoilAddress} with value {WriteCoilState}");
                await _modbusService.WriteSingleCoilAsync(UnitId, WriteCoilAddress, WriteCoilState);
                StatusMessage = "Coil written";
                _consoleLoggerService.Log(StatusMessage);
                // Optionally refresh
                await ReadCoilsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error writing coil: {ex.Message}";
                _logger.LogError(ex, "Error writing coil");
                _consoleLoggerService.Log(StatusMessage);
                MessageBox.Show($"Failed to write coil: {ex.Message}", "Write Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Helper methods for inline editing from the view
        public async Task WriteRegisterAtAsync(int address, ushort value)
        {
            await _modbusService.WriteSingleRegisterAsync(UnitId, address, value);
        }

        public async Task WriteFloatAtAsync(int address, float value)
        {
            var registers = DataTypeConverter.ToUInt16(value);
            await _modbusService.WriteSingleRegisterAsync(UnitId, address, registers[0]);
            await _modbusService.WriteSingleRegisterAsync(UnitId, address + 1, registers[1]);
        }

        public async Task WriteStringAtAsync(int address, string text)
        {
            var registers = DataTypeConverter.ToUInt16(text);
            for (int i = 0; i < registers.Length; i++)
            {
                await _modbusService.WriteSingleRegisterAsync(UnitId, address + i, registers[i]);
            }
        }

        public async Task WriteCoilAtAsync(int address, bool state)
        {
            await _modbusService.WriteSingleCoilAsync(UnitId, address, state);
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
                        // Attempt a graceful disconnect to avoid in-flight I/O errors
                        _modbusService?.DisconnectAsync().GetAwaiter().GetResult();
                        IsConnected = false;
                    }
                    catch { }
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

        // Clamp UnitId to Modbus spec range 1..247
        partial void OnUnitIdChanged(byte value)
        {
            byte clamped = value;
            if (value < 1) clamped = 1;
            else if (value > 247) clamped = 247;

            if (clamped != value)
            {
                _logger.LogWarning("UnitId {Original} is out of range. Clamping to {Clamped}.", value, clamped);
                // Reassign to trigger update only when necessary
                UnitId = clamped;
            }
        }
    }


    // Extensions to support the Custom tab logic within the ViewModel partial class
    public partial class MainViewModel
    {
        private void AddCustomEntry()
        {
            int nextAddress = 0;
            if (CustomEntries.Count > 0)
            {
                nextAddress = CustomEntries[^1].Address + 1;
            }
            CustomEntries.Add(new CustomEntry { Address = nextAddress, Area = "HoldingRegister", Type = "uint", Value = "0", Continuous = false, PeriodMs = 1000, Monitor = false, ReadPeriodMs = 1000 });
        }

        private async Task WriteCustomNowAsync(CustomEntry entry)
        {
            if (entry is null) return;
            try
            {
                var area = (entry.Area ?? "HoldingRegister").ToLowerInvariant();
                switch (area)
                {
                    case "holdingregister":
                        await WriteHoldingRegisterByTypeAsync(entry);
                        break;
                    case "coil":
                        await WriteCoilAsync(entry);
                        break;
                    case "inputregister":
                    case "discreteinput":
                        StatusMessage = $"{entry.Area} is read-only. Select HoldingRegister or Coil to write.";
                        break;
                    default:
                        StatusMessage = $"Unknown area: {entry.Area}";
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing custom entry");
                StatusMessage = $"Custom write error: {ex.Message}";
            }
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
            // Iterate over a snapshot to avoid InvalidOperationException when the collection is modified during awaits
            var snapshot = CustomEntries.ToList();
            foreach (var entry in snapshot)
            {
                if (!entry.Continuous) continue;
                int period = entry.PeriodMs <= 0 ? 1000 : entry.PeriodMs;
                if ((now - entry._lastWriteUtc).TotalMilliseconds >= period)
                {
                    await WriteCustomNowAsync(entry);
                    entry._lastWriteUtc = now;
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
                foreach (var ce in snapshot)
                {
                    try
                    {
                        var val = await ReadValueForTrendAsync(ce);
                        if (val.HasValue)
                        {
                            _trendLogger.Publish(GetTrendKey(ce), val.Value, now);
                            // Also update the UI-bound value for the Custom row so the grid reflects live reads
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
                            else // uint
                            {
                                display = ((ushort)val.Value).ToString(CultureInfo.InvariantCulture);
                            }
                            ce.Value = display;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Trend read failed for {Area} {Address}", ce.Area, ce.Address);
                    }
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
            try
            {
                await _customEntryService.SaveCustomAsync(CustomEntries);
                StatusMessage = $"Saved {CustomEntries.Count} custom entries.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving custom entries");
                MessageBox.Show($"Failed to save custom entries: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadCustomAsync()
        {
            try
            {
                var loadedEntries = await _customEntryService.LoadCustomAsync();
                if (loadedEntries.Any())
                {
                    CustomEntries.Clear();
                    foreach (var ce in loadedEntries)
                        CustomEntries.Add(ce);

                    SubscribeCustomEntries();
                    StatusMessage = $"Loaded {loadedEntries.Count} custom entries.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom entries");
                MessageBox.Show($"Failed to load custom entries: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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