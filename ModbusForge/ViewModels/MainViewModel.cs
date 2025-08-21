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

namespace ModbusForge.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly IModbusService _modbusService;
        private bool _disposed = false;

        public MainViewModel() : this(
            App.ServiceProvider.GetRequiredService<IModbusService>(),
            App.ServiceProvider.GetRequiredService<ILogger<MainViewModel>>(),
            App.ServiceProvider.GetRequiredService<IOptions<ServerSettings>>())
        {
        }

    public class CustomEntry : INotifyPropertyChanged
    {
        private int _address;
        private string _type = "uint"; // uint,int,real
        private string _value = "0";
        private bool _continuous = false;
        private int _periodMs = 1000;
        internal DateTime _lastWriteUtc = DateTime.MinValue;
        // Read monitoring support
        private bool _monitor = false;
        private int _readPeriodMs = 1000;
        internal DateTime _lastReadUtc = DateTime.MinValue;
        private string _area = "HoldingRegister"; // HoldingRegister, Coil, InputRegister, DiscreteInput

        public int Address { get => _address; set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } } }
        public string Type { get => _type; set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } } }
        public string Value { get => _value; set { if (_value != value) { _value = value; OnPropertyChanged(nameof(Value)); } } }
        public bool Continuous { get => _continuous; set { if (_continuous != value) { _continuous = value; OnPropertyChanged(nameof(Continuous)); } } }
        public int PeriodMs { get => _periodMs; set { if (_periodMs != value) { _periodMs = value; OnPropertyChanged(nameof(PeriodMs)); } } }
        // Per-row continuous read
        public bool Monitor { get => _monitor; set { if (_monitor != value) { _monitor = value; OnPropertyChanged(nameof(Monitor)); } } }
        public int ReadPeriodMs { get => _readPeriodMs; set { if (_readPeriodMs != value) { _readPeriodMs = value; OnPropertyChanged(nameof(ReadPeriodMs)); } } }
        public string Area { get => _area; set { if (_area != value) { _area = value; OnPropertyChanged(nameof(Area)); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

        public MainViewModel(IModbusService modbusService, ILogger<MainViewModel> logger, IOptions<ServerSettings> options)
        {
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var settings = options?.Value ?? new ServerSettings();
            
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
            IsConnected = _modbusService.IsConnected;
            StatusMessage = IsConnected ? "Connected" : "Disconnected";

            // Load defaults from configuration
            Port = settings.DefaultPort;
            try { UnitId = Convert.ToByte(settings.DefaultUnitId); } catch { UnitId = 1; }
            
            _logger.LogInformation("MainViewModel initialized");

            // Custom tab commands
            AddCustomEntryCommand = new RelayCommand(AddCustomEntry);
            WriteCustomNowCommand = new RelayCommand<CustomEntry>(async ce => await WriteCustomNowAsync(ce));
            ReadCustomNowCommand = new RelayCommand<CustomEntry>(async ce => await ReadCustomNowAsync(ce));
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
        }

        [ObservableProperty]
        private string _title = "ModbusForge";

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
        private bool _isMonitoring;
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
        public IRelayCommand SaveCustomCommand { get; }
        public IRelayCommand LoadCustomCommand { get; }

        // Global toggles for Custom tab
        [ObservableProperty]
        private bool _customMonitorEnabled = false;

        [ObservableProperty]
        private bool _customReadMonitorEnabled = false;

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

        private bool CanConnect() => !IsConnected;

        private async Task ConnectAsync()
        {
            try
            {
                StatusMessage = "Connecting...";
                var success = await _modbusService.ConnectAsync(ServerAddress, Port);
                
                if (success)
                {
                    IsConnected = true;
                    StatusMessage = "Connected to Modbus server";
                    _logger.LogInformation("Successfully connected to Modbus server");

                    // Quick probe for the configured UnitId to warn early if it's not responding
                    try
                    {
                        // minimal read to test unit id presence
                        await _modbusService.ReadHoldingRegistersAsync(UnitId, 0, 1);
                    }
                    catch (Exception ex)
                    {
                        var msg = $"Connected, but Unit ID {UnitId} did not respond to a test read. Please check the Unit ID.";
                        StatusMessage = msg;
                        _logger.LogWarning(ex, msg);
                        MessageBox.Show(msg + "\n" + ex.Message, "Unit ID Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    StatusMessage = "Failed to connect";
                    _logger.LogWarning("Failed to connect to Modbus server");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _logger.LogError(ex, "Error connecting to Modbus server");
                MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanDisconnect() => IsConnected;

        private async Task DisconnectAsync()
        {
            try
            {
                _logger.LogInformation("Disconnecting from Modbus server");
                await _modbusService.DisconnectAsync();
                IsConnected = false;
                StatusMessage = "Disconnected";
                _logger.LogInformation("Successfully disconnected from Modbus server");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error disconnecting: {ex.Message}";
                _logger.LogError(ex, "Error disconnecting from Modbus server");
                MessageBox.Show($"Failed to disconnect: {ex.Message}", "Disconnection Error", 
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
                            ushort high = values[idx];
                            ushort low = values[idx + 1];
                            byte[] b = new byte[4]
                            {
                                (byte)(high >> 8), (byte)(high & 0xFF),
                                (byte)(low >> 8),  (byte)(low & 0xFF)
                            };
                            if (BitConverter.IsLittleEndian)
                                Array.Reverse(b);
                            float f = BitConverter.ToSingle(b, 0);
                            entry.ValueText = f.ToString(CultureInfo.InvariantCulture);
                            // blank the following word to avoid overlapping display of the pair
                            HoldingRegisters[idx + 1].ValueText = string.Empty;
                        }
                        else
                        {
                            entry.ValueText = entry.Value.ToString(CultureInfo.InvariantCulture);
                        }
                        idx += 2; // skip the paired word
                    }
                    else if (t == "string")
                    {
                        char c1 = (char)(values[idx] >> 8);
                        char c2 = (char)(values[idx] & 0xFF);
                        var s = new string(new[] { c1, c2 }).TrimEnd('\0');
                        entry.ValueText = s;
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

        // Write a 32-bit float (IEEE754) across two consecutive 16-bit registers at the given address using Big-Endian word/byte order.
        public async Task WriteFloatAtAsync(int address, float value)
        {
            // Convert to big-endian byte order (network order)
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            ushort high = (ushort)((bytes[0] << 8) | bytes[1]);
            ushort low  = (ushort)((bytes[2] << 8) | bytes[3]);

            await _modbusService.WriteSingleRegisterAsync(UnitId, address, high);
            await _modbusService.WriteSingleRegisterAsync(UnitId, address + 1, low);
        }

        // Write an ASCII string across consecutive 16-bit registers.
        // Each register stores two bytes: high = first char, low = second char. Pads with 0 if odd length.
        public async Task WriteStringAtAsync(int address, string text)
        {
            text ??= string.Empty;
            var bytes = Encoding.ASCII.GetBytes(text);
            // pad to even length
            if ((bytes.Length & 1) != 0)
            {
                Array.Resize(ref bytes, bytes.Length + 1);
                bytes[^1] = 0;
            }

            for (int i = 0; i < bytes.Length; i += 2)
            {
                ushort reg = (ushort)((bytes[i] << 8) | bytes[i + 1]);
                await _modbusService.WriteSingleRegisterAsync(UnitId, address + (i / 2), reg);
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

    public class RegisterEntry : INotifyPropertyChanged
    {
        private int _address;
        private ushort _value;
        private string _type = "uint";
        private string _valueText = "0"; // used for editing/display to avoid WPF ConvertBack to ushort

        public int Address
        {
            get => _address;
            set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } }
        }

        public ushort Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                    // keep ValueText in sync for display
                    var s = _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (_valueText != s)
                    {
                        _valueText = s;
                        OnPropertyChanged(nameof(ValueText));
                    }
                }
            }
        }

        public string Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } }
        }

        // String representation shown/edited in the grid to avoid ConvertBack to ushort for 'real'/'string'
        public string ValueText
        {
            get => _valueText;
            set
            {
                if (_valueText != value)
                {
                    _valueText = value;
                    OnPropertyChanged(nameof(ValueText));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class CoilEntry : INotifyPropertyChanged
    {
        private int _address;
        private bool _state;

        public int Address
        {
            get => _address;
            set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } }
        }

        public bool State
        {
            get => _state;
            set { if (_state != value) { _state = value; OnPropertyChanged(nameof(State)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static class TypeOptions
    {
        public static readonly string[] All = new[] { "uint", "int", "real", "string" };
    }

    public static class AreaOptions
    {
        // HoldingRegister and Coil are writable; InputRegister and DiscreteInput are read-only per Modbus spec
        public static readonly string[] All = new[] { "HoldingRegister", "Coil", "InputRegister", "DiscreteInput" };
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
                            ushort high = regs[0];
                            ushort low = regs[1];
                            byte[] b = new byte[4]
                            {
                                (byte)(high >> 8), (byte)(high & 0xFF),
                                (byte)(low >> 8),  (byte)(low & 0xFF)
                            };
                            if (BitConverter.IsLittleEndian)
                                Array.Reverse(b);
                            float f = BitConverter.ToSingle(b, 0);
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
                            char c1 = (char)(regs[0] >> 8);
                            char c2 = (char)(regs[0] & 0xFF);
                            var s = new string(new[] { c1, c2 }).TrimEnd('\0');
                            entry.Value = s;
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
                            ushort high = regs[0];
                            ushort low = regs[1];
                            byte[] b = new byte[4]
                            {
                                (byte)(high >> 8), (byte)(high & 0xFF),
                                (byte)(low >> 8),  (byte)(low & 0xFF)
                            };
                            if (BitConverter.IsLittleEndian)
                                Array.Reverse(b);
                            float f = BitConverter.ToSingle(b, 0);
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
                            char c1 = (char)(regs[0] >> 8);
                            char c2 = (char)(regs[0] & 0xFF);
                            var s = new string(new[] { c1, c2 }).TrimEnd('\0');
                            entry.Value = s;
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
            if (!CustomMonitorEnabled) return;
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

                // Custom tab: continuous reads per row
                if (CustomReadMonitorEnabled)
                {
                    var snapshot = CustomEntries.ToList();
                    foreach (var entry in snapshot)
                    {
                        if (!entry.Monitor) continue;
                        int p = entry.ReadPeriodMs <= 0 ? 1000 : entry.ReadPeriodMs;
                        if ((now - entry._lastReadUtc).TotalMilliseconds >= p)
                        {
                            await ReadCustomNowAsync(entry);
                            entry._lastReadUtc = now;
                        }
                    }
                }
            }
            finally
            {
                _isMonitoring = false;
            }
        }

        private async Task SaveCustomAsync()
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = "custom-entries.json",
                    Title = "Save Custom Entries"
                };
                if (dlg.ShowDialog() == true)
                {
                    var data = CustomEntries.Select(e => new
                    {
                        e.Address,
                        e.Type,
                        e.Value,
                        e.Continuous,
                        e.PeriodMs,
                        e.Monitor,
                        e.ReadPeriodMs,
                        e.Area
                    }).ToList();
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(data, options);
                    await File.WriteAllTextAsync(dlg.FileName, json);
                    StatusMessage = $"Saved {data.Count} custom entries.";
                }
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
                var dlg = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    Title = "Load Custom Entries"
                };
                if (dlg.ShowDialog() == true)
                {
                    var json = await File.ReadAllTextAsync(dlg.FileName);
                    using var doc = JsonDocument.Parse(json);
                    var list = new System.Collections.Generic.List<CustomEntry>();
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var ce = new CustomEntry
                        {
                            Address = item.GetProperty("Address").GetInt32(),
                            Type = item.TryGetProperty("Type", out var t) ? t.GetString() ?? "uint" : "uint",
                            Value = item.TryGetProperty("Value", out var v) ? v.GetString() ?? "0" : "0",
                            Continuous = item.TryGetProperty("Continuous", out var c) && c.GetBoolean(),
                            PeriodMs = item.TryGetProperty("PeriodMs", out var p) ? p.GetInt32() : 1000,
                            Monitor = item.TryGetProperty("Monitor", out var mr) && mr.GetBoolean(),
                            ReadPeriodMs = item.TryGetProperty("ReadPeriodMs", out var rp) ? rp.GetInt32() : 1000,
                            Area = item.TryGetProperty("Area", out var a) ? a.GetString() ?? "HoldingRegister" : "HoldingRegister"
                        };
                        list.Add(ce);
                    }

                    CustomEntries.Clear();
                    foreach (var ce in list)
                        CustomEntries.Add(ce);

                    StatusMessage = $"Loaded {list.Count} custom entries.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom entries");
                MessageBox.Show($"Failed to load custom entries: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
