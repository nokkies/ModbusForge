using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly IInputDialogService? _inputDialogService;
        private CancellationTokenSource? _pollCts;
        private byte _unitId = 1;

        [ObservableProperty]
        private int _startAddress = 0;

        [ObservableProperty]
        private int _registerCount = 20;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        [NotifyCanExecuteChangedFor(nameof(WriteCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
        private bool _isContinuousRead = true;

        [ObservableProperty]
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
        [NotifyPropertyChangedFor(nameof(UnitId))]
        private ConnectionProfile? _activeProfile;

        [ObservableProperty]
        private bool _isRegisterArea = true;

        public ObservableCollection<RegisterEntry> HoldingRegisters { get; } = new();

        public ObservableCollection<RegisterEntry> InputRegisters { get; } = new();

        public ObservableCollection<CoilEntry> Coils { get; } = new();

        public ObservableCollection<CoilEntry> DiscreteInputs { get; } = new();

        public ObservableCollection<RegisterEntry> Registers => HoldingRegisters;

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

        public IReadOnlyList<string> RegisterTypes { get; } = new[] { "uint", "int", "real", "string" };

        public MainViewModel(IConnectionManager connectionManager, ILogger<MainViewModel> logger, IDispatcher dispatcher, IInputDialogService? inputDialogService = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _inputDialogService = inputDialogService;

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect());
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect());
            ReadCommand = new AsyncRelayCommand(ReadAsync, () => CanRead());
            WriteCommand = new AsyncRelayCommand(WriteAsync, () => CanWrite());

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

        partial void OnSelectedAreaChanged(PlcArea value)
        {
            SelectedAreaIndex = (int)value;
            IsRegisterArea = value is PlcArea.HoldingRegister or PlcArea.InputRegister;
            OnPropertyChanged(nameof(CanWrite));
            WriteCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedAreaIndexChanged(int value)
        {
            SelectedArea = (PlcArea)value;
        }

        partial void OnIsContinuousReadChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else
            {
                StopPolling();
            }
        }

        private bool CanConnect() => ActiveProfile is { IsConnected: false } && !IsBusy;

        private bool CanDisconnect() => ActiveProfile is { IsConnected: true } && !IsBusy;

        private bool CanRead() => ActiveProfile is { IsConnected: true } && !IsBusy;

        private bool CanWrite() => ActiveProfile is { IsConnected: true } && !IsBusy &&
                                   (SelectedArea is PlcArea.HoldingRegister or PlcArea.Coil);

        private async Task WriteAsync()
        {
            if (ActiveProfile == null || ActiveService == null || _inputDialogService == null) return;

            var address = PromptAddress("Write Address");
            if (!address.HasValue) return;

            try
            {
                IsBusy = true;
                var unitId = ActiveProfile.UnitId;

                if (SelectedArea == PlcArea.HoldingRegister)
                {
                    var valueText = _inputDialogService.TryGetInput("Write Value", "Value:", "0", out var input) ? input : null;
                    if (string.IsNullOrWhiteSpace(valueText) || !ushort.TryParse(valueText, out var value))
                    {
                        _dispatcher.Invoke(() => StatusMessage = "Invalid register value.");
                        return;
                    }

                    await ActiveService.WriteSingleRegisterAsync(unitId, address.Value, value);
                    _dispatcher.Invoke(() => StatusMessage = $"Wrote {value} to holding register {address.Value}.");
                    await ReadCurrentAreaAsync(CancellationToken.None);
                }
                else if (SelectedArea == PlcArea.Coil)
                {
                    var valueText = _inputDialogService.TryGetInput("Write Coil", "Value (true/false):", "false", out var input) ? input : null;
                    if (string.IsNullOrWhiteSpace(valueText) || !bool.TryParse(valueText, out var value))
                    {
                        _dispatcher.Invoke(() => StatusMessage = "Invalid coil value. Use true or false.");
                        return;
                    }

                    await ActiveService.WriteSingleCoilAsync(unitId, address.Value, value);
                    _dispatcher.Invoke(() => StatusMessage = $"Wrote {value} to coil {address.Value}.");
                    await ReadCurrentAreaAsync(CancellationToken.None);
                }
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

        private int? PromptAddress(string title)
        {
            if (_inputDialogService == null) return null;

            var defaultValue = StartAddress.ToString(CultureInfo.InvariantCulture);
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

        private void ActiveProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConnectionProfile.IsConnected))
            {
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(CanDisconnect));
                OnPropertyChanged(nameof(CanRead));
                OnPropertyChanged(nameof(CanWrite));
                ConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
                ReadCommand.NotifyCanExecuteChanged();
                WriteCommand.NotifyCanExecuteChanged();
            }

            if (e.PropertyName == nameof(ConnectionProfile.Status))
            {
                StatusMessage = ActiveProfile?.Status ?? "Ready";
            }

            if (e.PropertyName == nameof(ConnectionProfile.UnitId))
            {
                OnPropertyChanged(nameof(UnitId));
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
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
            OnPropertyChanged(nameof(CanRead));
            OnPropertyChanged(nameof(CanWrite));
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            ReadCommand.NotifyCanExecuteChanged();
            WriteCommand.NotifyCanExecuteChanged();

            StatusMessage = e != null ? $"Active profile: {e.DisplayName}" : "No active connection profile";
        }

        private void ConnectionManager_ProfileConnected(object? sender, ConnectionProfile e)
        {
            _logger.LogInformation("Profile connected: {Name}", e.Name);
            StartPolling();
        }

        private void ConnectionManager_ProfileDisconnected(object? sender, ConnectionProfile e)
        {
            _logger.LogInformation("Profile disconnected: {Name}", e.Name);
            StopPolling();
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

            IsBusy = true;
            try
            {
                await Task.Run(() => ReadCurrentAreaAsync(CancellationToken.None));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Read error: {ex.Message}";
                _logger.LogError(ex, "Manual read failed");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void StartPolling()
        {
            if (_pollCts != null) return;
            if (ActiveProfile is not { IsConnected: true }) return;
            if (!IsContinuousRead) return;

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
            while (!token.IsCancellationRequested && ActiveProfile is { IsConnected: true } && IsContinuousRead)
            {
                try
                {
                    await ReadCurrentAreaAsync(token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Poll failed");
                    _dispatcher.Invoke(() => StatusMessage = $"Poll error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ReadCurrentAreaAsync(CancellationToken token)
        {
            var service = ActiveService;
            if (service == null || ActiveProfile == null)
            {
                _dispatcher.Invoke(() => StatusMessage = "No active service.");
                return;
            }

            var unitId = ActiveProfile.UnitId;
            var start = StartAddress;
            var count = RegisterCount;

            _dispatcher.Invoke(() => StatusMessage = $"Reading {SelectedArea}...");

            try
            {
                switch (SelectedArea)
                {
                    case PlcArea.HoldingRegister:
                        var holding = await service.ReadHoldingRegistersAsync(unitId, start, count);
                        if (holding != null)
                        {
                            _dispatcher.Invoke(() =>
                            {
                                ApplyRegisterValues(HoldingRegisters, start, holding, GlobalType, SwapBytes, SwapWords);
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
                                ApplyRegisterValues(InputRegisters, start, input, GlobalType, SwapBytes, SwapWords);
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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error reading {Area}", SelectedArea);
                _dispatcher.Invoke(() => StatusMessage = $"Error reading {SelectedArea}: {ex.Message}");
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

        public void Dispose()
        {
            _connectionManager.ActiveProfileChanged -= ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected -= ConnectionManager_ProfileConnected;
            _connectionManager.ProfileDisconnected -= ConnectionManager_ProfileDisconnected;

            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged -= ActiveProfile_PropertyChanged;
            }

            StopPolling();
        }
    }
}
