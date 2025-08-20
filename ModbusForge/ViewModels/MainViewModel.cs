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

        public int Address { get => _address; set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } } }
        public string Type { get => _type; set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } } }
        public string Value { get => _value; set { if (_value != value) { _value = value; OnPropertyChanged(nameof(Value)); } } }
        public bool Continuous { get => _continuous; set { if (_continuous != value) { _continuous = value; OnPropertyChanged(nameof(Continuous)); } } }
        public int PeriodMs { get => _periodMs; set { if (_periodMs != value) { _periodMs = value; OnPropertyChanged(nameof(PeriodMs)); } } }

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

            // Start custom writer timer
            _customTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _customTimer.Tick += CustomTimer_Tick;
            _customTimer.Start();
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
                HoldingRegisters.Clear();
                for (int i = 0; i < values.Length; i++)
                {
                    HoldingRegisters.Add(new RegisterEntry
                    {
                        Address = RegisterStart + i,
                        Value = values[i],
                        Type = RegistersGlobalType
                    });
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

        public int Address
        {
            get => _address;
            set { if (_address != value) { _address = value; OnPropertyChanged(nameof(Address)); } }
        }

        public ushort Value
        {
            get => _value;
            set { if (_value != value) { _value = value; OnPropertyChanged(nameof(Value)); } }
        }

        public string Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } }
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
            CustomEntries.Add(new CustomEntry { Address = nextAddress, Type = "uint", Value = "0", Continuous = false, PeriodMs = 1000 });
        }

        private async Task WriteCustomNowAsync(CustomEntry entry)
        {
            if (entry is null) return;
            try
            {
                switch ((entry.Type ?? "uint").ToLowerInvariant())
                {
                    case "real":
                        if (float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                        {
                            await WriteFloatAtAsync(entry.Address, f);
                            StatusMessage = $"Wrote REAL {f} at {entry.Address}";
                        }
                        else
                        {
                            StatusMessage = $"Invalid float: {entry.Value}";
                        }
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing custom entry");
                StatusMessage = $"Custom write error: {ex.Message}";
            }
        }

        private async void CustomTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsConnected) return;
            var now = DateTime.UtcNow;
            foreach (var entry in CustomEntries)
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
    }
}
