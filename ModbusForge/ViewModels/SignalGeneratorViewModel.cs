using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ModbusForge.Core.Simulation;
using ModbusForge.Services;
using SkiaSharp;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class SignalGeneratorViewModel : ObservableObject, IDisposable
    {
        /// <summary>
        /// Upper bound (ms) for the preview sample interval: at 50 ms the chart refreshes
        /// at 20 samples/second, which is plenty for a strip chart.
        /// </summary>
        private const int MaxSampleIntervalMs = 50;

        /// <summary>
        /// Lower bound (ms) for the preview sample interval: keeps short-period signals
        /// from hammering the register with writes.
        /// </summary>
        private const int MinSampleIntervalMs = 10;

        /// <summary>
        /// Number of preview points to keep (≈ 30 s at the maximum sample rate).
        /// </summary>
        private const int PreviewMaxPoints = 600;

        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private CancellationTokenSource? _cts;
        private Task? _runTask;
        private bool _waitingForConnection;

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

        private readonly ObservableCollection<DateTimePoint> _previewValues = new();

        /// <summary>
        /// Live strip-chart series of the generated signal (x = wall-clock time,
        /// y = value). Kept after Stop so the last run stays visible.
        /// </summary>
        public ObservableCollection<ISeries> PreviewSeries { get; }

        public Axis[] XAxes { get; } =
        {
            new Axis
            {
                LabelsRotation = 15,
                Labeler = value =>
                {
                    var date = DateTime.FromOADate(value);
                    return date.ToString("HH:mm:ss");
                }
            }
        };

        public Axis[] YAxes { get; } =
        {
            new Axis { Name = "Value" }
        };

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }

        public SignalGeneratorViewModel(IConnectionManager connectionManager, IDispatcher dispatcher)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            var series = new LineSeries<DateTimePoint>
            {
                Name = "Signal",
                Values = _previewValues,
                Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2 },
                GeometrySize = 0
            };
            PreviewSeries = new ObservableCollection<ISeries> { series };

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
            _previewValues.Clear();
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
                    var period = Math.Max(1, PeriodMs);
                    var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    var progress = (elapsedMs % period) / period;
                    var value = WaveformMath.Evaluate(Waveform, Amplitude, Offset, progress);

                    var intValue = (int)Math.Round(value);
                    var ushortValue = (ushort)Math.Clamp(intValue, 0, ushort.MaxValue);

                    if (service != null && service.IsConnected)
                    {
                        if (_waitingForConnection)
                        {
                            _waitingForConnection = false;
                            await _dispatcher.InvokeAsync(() => StatusMessage = $"Signal generator running ({Waveform})...");
                        }

                        await service.WriteSingleRegisterAsync(unitId, Address, ushortValue);
                    }
                    else if (!_waitingForConnection)
                    {
                        _waitingForConnection = true;
                        await _dispatcher.InvokeAsync(() => StatusMessage = "Connection lost — writing paused until the connection returns.");
                    }

                    var sampleTime = DateTime.UtcNow;
                    await _dispatcher.InvokeAsync(() =>
                    {
                        CurrentValue = value;
                        AppendPreview(sampleTime, value);
                    });

                    // Sample at least 10 times per period (so short periods still trace a
                    // recognizable shape) but never faster than MaxSampleIntervalMs.
                    var sampleIntervalMs = Math.Clamp(period / 10, MinSampleIntervalMs, MaxSampleIntervalMs);
                    await Task.Delay(sampleIntervalMs, token);
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

        private void AppendPreview(DateTime timeUtc, double value)
        {
            _previewValues.Add(new DateTimePoint(timeUtc, value));
            while (_previewValues.Count > PreviewMaxPoints)
            {
                _previewValues.RemoveAt(0);
            }
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
