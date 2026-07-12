using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.ViewModels.Coordinators
{
    /// <summary>
    /// Coordinates the periodic monitoring timers (custom writes, area reads, trend sampling)
    /// for <see cref="MainViewModel"/> while keeping the view model as the binding surface.
    /// </summary>
    public class MonitoringCoordinator
    {
        private readonly IMonitoringCallbacks _callbacks;
        private readonly IPeriodicScheduler _customScheduler;
        private readonly IPeriodicScheduler _monitorScheduler;
        private readonly IPeriodicScheduler _trendScheduler;
        private readonly ILogger<MonitoringCoordinator> _logger;
        private readonly int _trendPeriodMs;

        private bool _isCustomTimerRunning;
        private bool _isMonitoring;
        private bool _isTrendTimerRunning;

        private const int CustomTimerIntervalMs = 250;
        private const int MonitorTimerIntervalMs = 250;
        private const int DefaultTrendPeriodMs = 250;

        public MonitoringCoordinator(
            IMonitoringCallbacks callbacks,
            IPeriodicScheduler customScheduler,
            IPeriodicScheduler monitorScheduler,
            IPeriodicScheduler trendScheduler,
            ILogger<MonitoringCoordinator> logger,
            int trendPeriodMs = DefaultTrendPeriodMs)
        {
            _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            _customScheduler = customScheduler ?? throw new ArgumentNullException(nameof(customScheduler));
            _monitorScheduler = monitorScheduler ?? throw new ArgumentNullException(nameof(monitorScheduler));
            _trendScheduler = trendScheduler ?? throw new ArgumentNullException(nameof(trendScheduler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _trendPeriodMs = trendPeriodMs > 0 ? trendPeriodMs : DefaultTrendPeriodMs;
        }

        public void Start()
        {
            _customScheduler.Start(TimeSpan.FromMilliseconds(CustomTimerIntervalMs), CustomTick);
            _monitorScheduler.Start(TimeSpan.FromMilliseconds(MonitorTimerIntervalMs), MonitorTick);
            _trendScheduler.Start(TimeSpan.FromMilliseconds(_trendPeriodMs), TrendTick);
        }

        public void Stop()
        {
            _customScheduler.Stop();
            _monitorScheduler.Stop();
            _trendScheduler.Stop();
        }

        public void Dispose()
        {
            Stop();
            _customScheduler.Dispose();
            _monitorScheduler.Dispose();
            _trendScheduler.Dispose();
        }

        internal async Task CustomTick(CancellationToken cancellationToken)
        {
            if (_isCustomTimerRunning) return;
            if (!_callbacks.IsConnected) return;

            _isCustomTimerRunning = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var now = DateTime.UtcNow;
                var snapshot = _callbacks.GetCustomEntriesSnapshot();

                foreach (var entry in snapshot)
                {
                    if (!entry.Continuous) continue;

                    int period = entry.PeriodMs <= 0 ? 1000 : entry.PeriodMs;
                    if ((now - entry._lastWriteUtc).TotalMilliseconds >= period)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await _callbacks.WriteCustomNowAsync(entry).ConfigureAwait(false);
                            entry._lastWriteUtc = now;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            _logger.LogError(ex, "Continuous write failed for {Area} {Address}", entry.Area, entry.Address);
                            entry.Continuous = false;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Custom tick operation was canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Error in CustomTick");
            }
            finally
            {
                _isCustomTimerRunning = false;
            }
        }

        internal async Task MonitorTick(CancellationToken cancellationToken)
        {
            if (_isMonitoring) return;
            if (!_callbacks.IsConnected) return;

            var now = DateTime.UtcNow;
            if (_callbacks.HasConnectionError && (now - _callbacks.LastErrorTime).TotalSeconds < 5)
            {
                return;
            }

            _isMonitoring = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_callbacks.HoldingMonitorEnabled &&
                    !_callbacks.InputRegistersMonitorEnabled &&
                    !_callbacks.CoilsMonitorEnabled &&
                    !_callbacks.DiscreteInputsMonitorEnabled)
                {
                    await _callbacks.HeartbeatAsync();
                    if (!_callbacks.IsConnected) return;
                }

                if (_callbacks.HoldingMonitorEnabled)
                {
                    int p = _callbacks.HoldingMonitorPeriodMs <= 0 ? 1000 : _callbacks.HoldingMonitorPeriodMs;
                    if ((now - _callbacks.LastHoldingReadUtc).TotalMilliseconds >= p)
                    {
                        await _callbacks.ReadRegistersAsync();
                        _callbacks.LastHoldingReadUtc = now;
                    }
                }

                if (_callbacks.InputRegistersMonitorEnabled)
                {
                    int p = _callbacks.InputRegistersMonitorPeriodMs <= 0 ? 1000 : _callbacks.InputRegistersMonitorPeriodMs;
                    if ((now - _callbacks.LastInputRegReadUtc).TotalMilliseconds >= p)
                    {
                        await _callbacks.ReadInputRegistersAsync();
                        _callbacks.LastInputRegReadUtc = now;
                    }
                }

                if (_callbacks.CoilsMonitorEnabled)
                {
                    int p = _callbacks.CoilsMonitorPeriodMs <= 0 ? 1000 : _callbacks.CoilsMonitorPeriodMs;
                    if ((now - _callbacks.LastCoilsReadUtc).TotalMilliseconds >= p)
                    {
                        await _callbacks.ReadCoilsAsync();
                        _callbacks.LastCoilsReadUtc = now;
                    }
                }

                if (_callbacks.DiscreteInputsMonitorEnabled)
                {
                    int p = _callbacks.DiscreteInputsMonitorPeriodMs <= 0 ? 1000 : _callbacks.DiscreteInputsMonitorPeriodMs;
                    if ((now - _callbacks.LastDiscreteReadUtc).TotalMilliseconds >= p)
                    {
                        await _callbacks.ReadDiscreteInputsAsync();
                        _callbacks.LastDiscreteReadUtc = now;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Monitor tick operation was canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Error in MonitorTick");
            }
            finally
            {
                _isMonitoring = false;
            }
        }

        internal async Task TrendTick(CancellationToken cancellationToken)
        {
            if (_isTrendTimerRunning) return;
            if (!_callbacks.IsConnected) return;
            if (!_callbacks.GlobalMonitorEnabled) return;

            _isTrendTimerRunning = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _callbacks.ProcessTrendSamplingAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Trend tick operation was canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Error in TrendTick");
            }
            finally
            {
                _isTrendTimerRunning = false;
            }
        }
    }
}
