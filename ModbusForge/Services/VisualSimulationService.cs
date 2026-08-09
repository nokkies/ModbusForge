using System;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// WPF visual simulation service. Runs the shared simulation engine on a
    /// <see cref="DispatcherTimer"/> and updates the configured nodes.
    /// </summary>
    public sealed class VisualSimulationService : VisualSimulationServiceBase<VisualSimulationService>
    {
        private DispatcherTimer? _animationTimer;

        public VisualSimulationService(
            ILogger<VisualSimulationService> logger,
            IConnectionManager? connectionManager = null,
            IConsoleLoggerService? consoleLoggerService = null)
            : base(logger, consoleLoggerService, connectionManager)
        {
        }

        protected override void OnStartTimer()
        {
            if (_animationTimer == null)
            {
                _animationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _animationTimer.Tick += AnimationTimer_Tick;
            }

            _animationTimer.Start();
        }

        protected override void OnStopTimer()
        {
            _animationTimer?.Stop();
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
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

        public override void Dispose()
        {
            Stop();
            _animationTimer = null;
        }
    }
}
