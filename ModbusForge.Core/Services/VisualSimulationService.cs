using System;
using System.Timers;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using Timer = System.Timers.Timer;

namespace ModbusForge.Services
{
    /// <summary>
    /// Avalonia/headless visual simulation service. Runs the shared simulation engine
    /// on a <see cref="System.Timers.Timer"/> and updates the configured nodes.
    /// </summary>
    public sealed class AvaloniaVisualSimulationService : VisualSimulationServiceBase<AvaloniaVisualSimulationService>
    {
        private readonly Timer _timer;

        public AvaloniaVisualSimulationService(
            ILogger<AvaloniaVisualSimulationService>? logger = null,
            IConsoleLoggerService? consoleLoggerService = null,
            IConnectionManager? connectionManager = null)
            : base(logger, consoleLoggerService, connectionManager)
        {
            _timer = new Timer(100);
            _timer.Elapsed += Timer_Elapsed;
            _timer.AutoReset = true;
        }

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                UpdateNodeValues();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                LogError(ex);
            }
        }

        protected override void OnStartTimer()
        {
            _timer.Start();
        }

        protected override void OnStopTimer()
        {
            _timer.Stop();
        }

        public override void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }
}
