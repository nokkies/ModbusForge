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
using Microsoft.Win32;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Specialized;
using ModbusForge.Models;
using System.Windows.Controls;
using System.Windows;
using System.Globalization;
using ModbusForge.Helpers;

namespace ModbusForge.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IDisposable
    {
        private IModbusService _modbusService;
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
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
            App.ServiceProvider.GetRequiredService<ICustomEntryService>())
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

        public MainViewModel(ModbusTcpService clientService, ModbusServerService serverService, ILogger<MainViewModel> logger, IOptions<ServerSettings> options, ITrendLogger trendLogger, ISimulationService simulationService, ICustomEntryService customEntryService)
        {
            UpdateHoldingRegisterCommand = new AsyncRelayCommand<DataGridCellEditEndingEventArgs>(UpdateHoldingRegister);

            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
            _simulationService = simulationService ?? throw new ArgumentNullException(nameof(simulationService));
            _customEntryService = customEntryService ?? throw new ArgumentNullException(nameof(customEntryService));
            var settings = options?.Value ?? new ServerSettings();
            Mode = string.Equals(settings.Mode, "Server", StringComparison.OrdinalIgnoreCase) ? "Server" : "Client";

            // Initialize defaults from configuration
            try
            {
                if (settings.DefaultPort > 0)
                {
                    Port = settings.DefaultPort;
                }
                // In client mode, ensure a reasonable default server address if empty
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

            // Initialize commands
            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), CanConnect);
            _disconnectCommand = new RelayCommand(async () => await DisconnectAsync(), CanDisconnect);
            ReadRegistersCommand = new RelayCommand(async () => await ReadRegistersAsync(), () => IsConnected);
            WriteRegisterCommand = new RelayCommand(async () => await WriteRegisterAsync(), () => IsConnected);
            ReadCoilsCommand = new RelayCommand(async () => await ReadCoilsAsync(), () => IsConnected);
            WriteCoilCommand = new RelayCommand(async () => await WriteCoilAsync(), () => IsConnected);
            ReadInputRegistersCommand = new RelayCommand(async () => await ReadInputRegistersAsync(), () => IsConnected);
            ReadDiscreteInputsCommand = new RelayCommand(async () => await ReadDiscreteInputsAsync(), () => IsConnected);
            
            // Initialize DisconnectCommand property
            DisconnectCommand = _disconnectCommand;
            
            // Set initial state
            // Set initial service based on Mode and initialize state
            _modbusService = IsServerMode ? _serverService : _clientService;
            IsConnected = _modbusService.IsConnected;
            StatusMessage = IsConnected ? "Connected" : "Disconnected";

            // Load defaults from configuration
            Port = settings.DefaultPort;
            try { UnitId = Convert.ToByte(settings.DefaultUnitId); } catch { UnitId = 1; }
            
            _logger.LogInformation("MainViewModel initialized");

            // Set window title with version from assembly file info
            try
            {
                var ver = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location)?.ProductVersion ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(ver))
                {
                    Title = $"ModbusForge v{ver}";
                }
                else
                {
                    Title = "ModbusForge v1.2.1"; // fallback
                }
            }
            catch
            {
                Title = "ModbusForge v1.2.1"; // fallback on any error
            }

            // Custom tab commands
            AddCustomEntryCommand = new RelayCommand(AddCustomEntry);
            // Use AsyncRelayCommand<object?> to avoid strict type validation exceptions during template initialization
            WriteCustomNowCommand = new AsyncRelayCommand<object?>(async param =>
            {
                if (param is CustomEntry ce)
                {
                    await WriteCustomNowAsync(ce);
                }
            });
            ReadCustomNowCommand = new AsyncRelayCommand<object?>(async param =>
            {
                if (param is CustomEntry ce)
                {
                    await ReadCustomNowAsync(ce);
                }
            });
            ReadAllCustomNowCommand = new RelayCommand(async () => await ReadAllCustomNowAsync());
            SaveCustomCommand = new RelayCommand(async () => await SaveCustomAsync());
            LoadCustomCommand = new RelayCommand(async () => await LoadCustomAsync());

            // Start custom writer timer
            _customTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _customTimer.Tick += CustomTimer_Tick;
            _customTimer.Start();

            // Start monitor timer for continuous reads
            _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();

            // Trend sampling timer aligned with logger's sample rate
            _trendTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, _trendLogger.SampleRateMs)) };
            _trendTimer.Tick += TrendTimer_Tick;
            _trendTimer.Start();

            // Start trend logger and subscribe to CustomEntries changes
            try { _trendLogger.Start(); } catch { }
            SubscribeCustomEntries();

            _simulationService.Start(this);
        }

        [ObservableProperty]
        private string _title = "ModbusForge";

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
        private int _registerStart = 0;

        [ObservableProperty]
        private int _registerCount = 10;

        [ObservableProperty]
        private int _writeRegisterAddress = 0;

        [ObservableProperty]
        private ushort _writeRegisterValue = 0;

        public ObservableCollection<RegisterEntry> HoldingRegisters { get; } = new();

        // Coils UI state
        [ObservableProperty]
        private int _coilStart = 0;

        [ObservableProperty]
        private int _coilCount = 16;

        [ObservableProperty]
        private int _writeCoilAddress = 0;

        [ObservableProperty]
        private bool _writeCoilState = false;

        public ObservableCollection<CoilEntry> Coils { get; } = new();

        // Input Registers UI state
        [ObservableProperty]
        private int _inputRegisterStart = 0;

        [ObservableProperty]
        private int _inputRegisterCount = 10;

        public ObservableCollection<RegisterEntry> InputRegisters { get; } = new();

        // Discrete Inputs UI state
        [ObservableProperty]
        private int _discreteInputStart = 0;

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
        private readonly DispatcherTimer _customTimer;
        private readonly DispatcherTimer _monitorTimer;
        private readonly DispatcherTimer _trendTimer;
        private readonly ITrendLogger _trendLogger;
        private readonly ICustomEntryService _customEntryService;
        private bool _isMonitoring;
        private bool _isTrending;
        private readonly ISimulationService _simulationService;
        private DateTime _lastHoldingReadUtc = DateTime.MinValue;
        private DateTime _lastInputRegReadUtc = DateTime.MinValue;
        private DateTime _lastCoilsReadUtc = DateTime.MinValue;
        private DateTime _lastDiscreteReadUtc = DateTime.MinValue;

        public ICommand ConnectCommand { get; }
        public IRelayCommand DisconnectCommand { get; }
        public IRelayCommand ReadRegistersCommand { get; }
        public IRelayCommand WriteRegisterCommand { get; }
        public IRelayCommand ReadCoilsCommand { get; }
        public IRelayCommand WriteCoilCommand { get; }
        public IRelayCommand ReadInputRegistersCommand { get; }
        public IRelayCommand ReadDiscreteInputsCommand { get; }

        // Custom tab
        public ObservableCollection<CustomEntry> CustomEntries { get; } = new();
        public ICommand AddCustomEntryCommand { get; }
        public ICommand WriteCustomNowCommand { get; }
        public ICommand ReadCustomNowCommand { get; }
        public IRelayCommand ReadAllCustomNowCommand { get; }
        public IRelayCommand SaveCustomCommand { get; }
        public IRelayCommand LoadCustomCommand { get; }
        public IAsyncRelayCommand<DataGridCellEditEndingEventArgs> UpdateHoldingRegisterCommand { get; }

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
        private int _simHoldingStart = 0;

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
        private int _simCoilStart = 0;

        [ObservableProperty]
        private int _simCoilCount = 8;

        // Input Registers ramp
        [ObservableProperty]
        private bool _simInputsEnabled = false;

        [ObservableProperty]
        private int _simInputStart = 0;

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
        private int _simDiscreteStart = 0;

        [ObservableProperty]
        private int _simDiscreteCount = 8;


        private bool CanConnect() => !IsConnected;

        private async Task ConnectAsync()
        {
            try
            {
                StatusMessage = IsServerMode ? "Starting server..." : "Connecting...";
                var success = await _modbusService.ConnectAsync(ServerAddress, Port);
                
                if (success)
                {
                    IsConnected = true;
                    StatusMessage = IsServerMode ? "Server started" : "Connected to Modbus server";
                    _logger.LogInformation(IsServerMode ? "Successfully started Modbus server" : "Successfully connected to Modbus server");
                }
                else
                {
                    IsConnected = false;
                    StatusMessage = IsServerMode ? "Server failed to start" : "Connection failed";
                    _logger.LogWarning(IsServerMode ? "Failed to start Modbus server" : "Failed to connect to Modbus server");
                    var msg = IsServerMode
                        ? $"Failed to start server on port {Port}. The port may be in use. Try another port (e.g., 1502) or stop the process using it."
                        : "Failed to connect to Modbus server.";
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
                                var retryOk = await _modbusService.ConnectAsync(ServerAddress, Port);
                                if (retryOk)
                                {
                                    IsConnected = true;
                                    StatusMessage = "Server started";
                                    _logger.LogInformation("Successfully started Modbus server on alternative port {AltPort}", Port);
                                }
                                else
                                {
                                    IsConnected = false;
                                    StatusMessage = "Server failed to start";
                                    _logger.LogWarning("Failed to start Modbus server on alternative port {AltPort}", Port);
                                    MessageBox.Show($"Failed to start server on alternative port {Port}. The port may also be in use or blocked.",
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
                MessageBox.Show(IsServerMode ? $"Failed to start server: {ex.Message}" : $"Failed to connect: {ex.Message}", IsServerMode ? "Server Error" : "Connection Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanDisconnect() => IsConnected;

        private async Task DisconnectAsync()
        {
            try
            {
                _logger.LogInformation(IsServerMode ? "Stopping Modbus server" : "Disconnecting from Modbus server");
                await _modbusService.DisconnectAsync();
                IsConnected = false;
                StatusMessage = IsServerMode ? "Server stopped" : "Disconnected";
                _logger.LogInformation(IsServerMode ? "Successfully stopped Modbus server" : "Successfully disconnected from Modbus server");
            }
            catch (Exception ex)
            {
                StatusMessage = IsServerMode ? $"Error stopping server: {ex.Message}" : $"Error disconnecting: {ex.Message}";
                _logger.LogError(ex, IsServerMode ? "Error stopping Modbus server" : "Error disconnecting from Modbus server");
                MessageBox.Show(IsServerMode ? $"Failed to stop server: {ex.Message}" : $"Failed to disconnect: {ex.Message}", IsServerMode ? "Server Stop Error" : "Disconnection Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReadRegistersAsync()
        {
            try
            {
                StatusMessage = "Reading registers...";
                var values = await _modbusService.ReadHoldingRegistersAsync(UnitId, RegisterStart, RegisterCount);
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
                var values = await _modbusService.ReadInputRegistersAsync(UnitId, InputRegisterStart, InputRegisterCount);
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
                await _modbusService.WriteSingleRegisterAsync(UnitId, WriteRegisterAddress, WriteRegisterValue);
                StatusMessage = "Register written";
                // Optionally refresh
                await ReadRegistersAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error writing register: {ex.Message}";
                _logger.LogError(ex, "Error writing register");
                MessageBox.Show($"Failed to write register: {ex.Message}", "Write Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReadCoilsAsync()
        {
            try
            {
                StatusMessage = "Reading coils...";
                var states = await _modbusService.ReadCoilsAsync(UnitId, CoilStart, CoilCount);
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
                var states = await _modbusService.ReadDiscreteInputsAsync(UnitId, DiscreteInputStart, DiscreteInputCount);
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
                await _modbusService.WriteSingleCoilAsync(UnitId, WriteCoilAddress, WriteCoilState);
                StatusMessage = "Coil written";
                // Optionally refresh
                await ReadCoilsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error writing coil: {ex.Message}";
                _logger.LogError(ex, "Error writing coil");
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
                        switch ((entry.Type ?? "uint").ToLowerInvariant())
                        {
                            case "real":
                                if (!float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                                {
                                    if (!float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                                    {
                                        StatusMessage = $"Invalid float: {entry.Value}";
                                        break;
                                    }
                                }
                                await WriteFloatAtAsync(entry.Address, f);
                                StatusMessage = $"Wrote REAL {f} at {entry.Address}";
                                break;
                            case "string":
                                await WriteStringAtAsync(entry.Address, entry.Value ?? string.Empty);
                                StatusMessage = $"Wrote STRING '{entry.Value}' at {entry.Address}";
                                break;
                            case "int":
                                if (int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                                {
                                    await WriteRegisterAtAsync(entry.Address, unchecked((ushort)iv));
                                    StatusMessage = $"Wrote INT {iv} at {entry.Address}";
                                }
                                else
                                {
                                    StatusMessage = $"Invalid int: {entry.Value}";
                                }
                                break;
                            default: // uint
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
                                break;
                        }
                        break;
                    case "coil":
                        if (TryParseBool(entry.Value, out bool b))
                        {
                            await WriteCoilAtAsync(entry.Address, b);
                            StatusMessage = $"Wrote COIL {(b ? 1 : 0)} at {entry.Address}";
                        }
                        else
                        {
                            StatusMessage = $"Invalid coil value: {entry.Value}. Use true/false or 1/0.";
                        }
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

        private async Task ReadCustomNowAsync(CustomEntry entry)
        {
            if (entry is null) return;
            try
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
                            float f = DataTypeConverter.ToSingle(regs[0], regs[1]);
                            entry.Value = f.ToString(CultureInfo.InvariantCulture);
                            StatusMessage = $"Read REAL {entry.Value} from HR {entry.Address}";
                        }
                        else if (t == "int")
                        {
                            var regs = await _modbusService.ReadHoldingRegistersAsync(UnitId, entry.Address, 1);
                            short sv = unchecked((short)regs[0]);
                            entry.Value = sv.ToString(CultureInfo.InvariantCulture);
                            StatusMessage = $"Read INT {entry.Value} from HR {entry.Address}";
                        }
                        else if (t == "string")
                        {
                            var regs = await _modbusService.ReadHoldingRegistersAsync(UnitId, entry.Address, 1);
                            entry.Value = DataTypeConverter.ToString(regs[0]);
                            StatusMessage = $"Read STRING '{entry.Value}' from HR {entry.Address}";
                        }
                        else // uint
                        {
                            var regs = await _modbusService.ReadHoldingRegistersAsync(UnitId, entry.Address, 1);
                            entry.Value = regs[0].ToString(CultureInfo.InvariantCulture);
                            StatusMessage = $"Read UINT {entry.Value} from HR {entry.Address}";
                        }
                        break;
                    }
                    case "inputregister":
                    {
                        var t = (entry.Type ?? "uint").ToLowerInvariant();
                        if (t == "real")
                        {
                            var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 2);
                            float f = DataTypeConverter.ToSingle(regs[0], regs[1]);
                            entry.Value = f.ToString(CultureInfo.InvariantCulture);
                            StatusMessage = $"Read REAL {entry.Value} from IR {entry.Address}";
                        }
                        else if (t == "int")
                        {
                            var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 1);
                            short sv = unchecked((short)regs[0]);
                            entry.Value = sv.ToString(CultureInfo.InvariantCulture);
                            StatusMessage = $"Read INT {entry.Value} from IR {entry.Address}";
                        }
                        else if (t == "string")
                        {
                            var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 1);
                            entry.Value = DataTypeConverter.ToString(regs[0]);
                            StatusMessage = $"Read STRING '{entry.Value}' from IR {entry.Address}";
                        }
                        else // uint
                        {
                            var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 1);
                            entry.Value = regs[0].ToString(CultureInfo.InvariantCulture);
                            StatusMessage = $"Read UINT {entry.Value} from IR {entry.Address}";
                        }
                        break;
                    }
                    case "coil":
                    {
                        var states = await _modbusService.ReadCoilsAsync(UnitId, entry.Address, 1);
                        entry.Value = states[0] ? "1" : "0";
                        StatusMessage = $"Read COIL {entry.Value} from {entry.Address}";
                        break;
                    }
                    case "discreteinput":
                    {
                        var states = await _modbusService.ReadDiscreteInputsAsync(UnitId, entry.Address, 1);
                        entry.Value = states[0] ? "1" : "0";
                        StatusMessage = $"Read DI {entry.Value} from {entry.Address}";
                        break;
                    }
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
                        return DataTypeConverter.ToSingle(regs[0], regs[1]);
                    }
                    else if (t == "int")
                    {
                        var regs = await _modbusService.ReadHoldingRegistersAsync(UnitId, entry.Address, 1);
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
                        return (double)regs[0];
                    }
                }
                case "inputregister":
                {
                    var t = (entry.Type ?? "uint").ToLowerInvariant();
                    if (t == "real")
                    {
                        var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 2);
                        return DataTypeConverter.ToSingle(regs[0], regs[1]);
                    }
                    else if (t == "int")
                    {
                        var regs = await _modbusService.ReadInputRegistersAsync(UnitId, entry.Address, 1);
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
                        return (double)regs[0];
                    }
                }
                case "coil":
                {
                    var states = await _modbusService.ReadCoilsAsync(UnitId, entry.Address, 1);
                    return states[0] ? 1.0 : 0.0;
                }
                case "discreteinput":
                {
                    var states = await _modbusService.ReadDiscreteInputsAsync(UnitId, entry.Address, 1);
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
