using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class SignalGeneratorViewModel : ObservableObject, IDisposable
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private CancellationTokenSource? _cts;
        private Task? _runTask;

        public static IReadOnlyList<string> Waveforms { get; } = new[] { "Ramp", "Sine", "Triangle", "Square" };

        [ObservableProperty]
        private string _waveform = "Ramp";

        [ObservableProperty]
        private int _periodMs = 1000;

        [ObservableProperty]
        private double _amplitude = 100.0;

        [ObservableProperty]
        private double _offset;

        [ObservableProperty]
        private int _address = 1;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private double _currentValue;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }

        public SignalGeneratorViewModel(IConnectionManager connectionManager, IDispatcher dispatcher)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            StartCommand = new AsyncRelayCommand(StartAsync, () => !IsRunning);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
        }

        partial void OnIsRunningChanged(bool value)
        {
            ((AsyncRelayCommand)StartCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StopCommand).NotifyCanExecuteChanged();
        }

        private async Task StartAsync()
        {
            var service = _connectionManager.ActiveService;
            if (service == null || !service.IsConnected)
            {
                StatusMessage = "Please connect to a Modbus device first.";
                return;
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            StatusMessage = $"Signal generator running ({Waveform})...";

            _runTask = RunLoopAsync(_cts.Token);
            await Task.CompletedTask;
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            var service = _connectionManager.ActiveService;
            var unitId = (byte)(_connectionManager.ActiveProfile?.UnitId ?? 1);
            var startTime = DateTime.UtcNow;

            while (!token.IsCancellationRequested && IsRunning)
            {
                try
                {
                    var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    var period = Math.Max(1, PeriodMs);
                    var progress = (elapsedMs % period) / period;

                    var value = Waveform switch
                    {
                        "Sine" => Amplitude * Math.Sin(2 * Math.PI * progress) + Offset,
                        "Triangle" => Amplitude * (1.0 - 4.0 * Math.Abs(progress - 0.5)) + Offset,
                        "Square" => (progress < 0.5 ? Amplitude : 0) + Offset,
                        "Ramp" or _ => Amplitude * progress + Offset
                    };

                    var intValue = (int)Math.Round(value);
                    var ushortValue = (ushort)Math.Clamp(intValue, 0, ushort.MaxValue);

                    if (service != null && service.IsConnected)
                    {
                        await service.WriteSingleRegisterAsync(unitId, Address, ushortValue);
                    }

                    await _dispatcher.InvokeAsync(() => CurrentValue = value);

                    await Task.Delay(period, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    await _dispatcher.InvokeAsync(() => StatusMessage = $"Signal generator error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }

            await _dispatcher.InvokeAsync(() =>
            {
                IsRunning = false;
                StatusMessage = "Signal generator stopped.";
            });
        }

        private void Stop()
        {
            _cts?.Cancel();
            IsRunning = false;
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
