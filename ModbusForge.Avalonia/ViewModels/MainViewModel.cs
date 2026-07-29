using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IModbusService _modbusService;
        private readonly ILogger<MainViewModel> _logger;
        private readonly IDispatcher _dispatcher;
        private CancellationTokenSource? _pollCts;

        [ObservableProperty]
        private string _host = "127.0.0.1";

        [ObservableProperty]
        private int _port = 502;

        [ObservableProperty]
        private int _unitId = 1;

        [ObservableProperty]
        private int _startAddress = 0;

        [ObservableProperty]
        private int _registerCount = 20;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private bool _isConnected;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        public ObservableCollection<RegisterEntry> Registers { get; } = new();

        public MainViewModel(IModbusService modbusService, ILogger<MainViewModel> logger, IDispatcher dispatcher)
        {
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsBusy);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        }

        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }

        private async Task ConnectAsync()
        {
            IsBusy = true;
            StatusMessage = $"Connecting to {Host}:{Port}...";

            try
            {
                var connected = await _modbusService.ConnectAsync(Host, Port, UnitId.ToString());
                if (!connected)
                {
                    StatusMessage = "Connection failed.";
                    _logger.LogWarning("Failed to connect to {Host}:{Port}", Host, Port);
                    return;
                }

                IsConnected = true;
                StatusMessage = $"Connected to {Host}:{Port} (unit {UnitId}).";
                _logger.LogInformation("Connected to {Host}:{Port}", Host, Port);

                _pollCts = new CancellationTokenSource();
                _ = Task.Run(() => PollLoopAsync(_pollCts.Token), _pollCts.Token);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection error: {ex.Message}";
                _logger.LogError(ex, "Error connecting to {Host}:{Port}", Host, Port);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DisconnectAsync()
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;

            await _modbusService.DisconnectAsync();
            IsConnected = false;
            StatusMessage = "Disconnected.";
            _logger.LogInformation("Disconnected");

            await _dispatcher.InvokeAsync(Registers.Clear);
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _modbusService.IsConnected)
            {
                try
                {
                    var values = await _modbusService.ReadHoldingRegistersAsync((byte)UnitId, StartAddress, RegisterCount);
                    await _dispatcher.InvokeAsync(() => ApplyValues(values));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Poll failed");
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

        private void ApplyValues(ushort[]? values)
        {
            if (values == null)
            {
                StatusMessage = "No response from device.";
                return;
            }

            Registers.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                Registers.Add(new RegisterEntry
                {
                    Address = StartAddress + i,
                    Value = values[i],
                    Type = "uint"
                });
            }

            StatusMessage = $"Read {values.Length} holding registers.";
        }

        public void Dispose()
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
            _modbusService?.DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
