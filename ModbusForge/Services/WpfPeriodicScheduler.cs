using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ModbusForge.Services
{
    /// <summary>
    /// WPF implementation of <see cref="IPeriodicScheduler"/> that uses a
    /// <see cref="DispatcherTimer"/> and marshals tick callbacks to the dispatcher.
    /// </summary>
    public class WpfPeriodicScheduler : IPeriodicScheduler
    {
        private readonly Dispatcher _dispatcher;
        private DispatcherTimer? _timer;
        private Func<CancellationToken, Task>? _tick;
        private CancellationTokenSource _cts = new();

        public WpfPeriodicScheduler()
            : this(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
        {
        }

        public WpfPeriodicScheduler(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Start(TimeSpan interval, Func<CancellationToken, Task> tick)
        {
            Stop();

            _cts = new CancellationTokenSource();
            _tick = tick ?? throw new ArgumentNullException(nameof(tick));

            _timer = new DispatcherTimer(interval, DispatcherPriority.Normal, OnTick, _dispatcher);
            _timer.Start();
        }

        private async void OnTick(object? sender, EventArgs e)
        {
            var tick = _tick;
            if (tick == null || _cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await tick(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when the scheduler is stopped.
            }
            catch (Exception)
            {
                // Ticks are responsible for their own logging; swallow so the
                // dispatcher timer doesn't bring down the application.
            }
        }

        public void Stop()
        {
            _timer?.Stop();
            _cts.Cancel();
        }

        public void Dispose()
        {
            Stop();

            if (_timer != null)
            {
                _timer.Tick -= OnTick;
                _timer = null;
            }

            _tick = null;
            _cts.Dispose();
        }
    }
}
