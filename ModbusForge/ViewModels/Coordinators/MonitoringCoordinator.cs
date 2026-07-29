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
        private readonly IPollingEngine? _pollingEngine;
        private readonly ILogger<MonitoringCoordinator> _logger;
        private readonly int _trendPeriodMs;

        private bool _isCustomTimerRunning;
        private bool _isMonitoring;
        private bool _isTrendTimerRunning;
        private bool _pollingEngineStarted;

        private const int CustomTimerIntervalMs = 250;
        private const int MonitorTimerIntervalMs = 50;
        private const int DefaultTrendPeriodMs = 250;

        public MonitoringCoordinator(
            IMonitoringCallbacks callbacks,
            IPeriodicScheduler customScheduler,
            IPeriodicScheduler monitorScheduler,
            IPeriodicScheduler trendScheduler,
            ILogger<MonitoringCoordinator> logger,
            int trendPeriodMs = DefaultTrendPeriodMs,
            IPollingEngine? pollingEngine = null)
        {
            _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            _customScheduler = customScheduler ?? throw new ArgumentNullException(nameof(customScheduler));
            _monitorScheduler = monitorScheduler ?? throw new ArgumentNullException(nameof(monitorScheduler));
            _trendScheduler = trendScheduler ?? throw new ArgumentNullException(nameof(trendScheduler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _trendPeriodMs = trendPeriodMs > 0 ? trendPeriodMs : DefaultTrendPeriodMs;
            _pollingEngine = pollingEngine;

            if (_pollingEngine is not null)
            {
                _pollingEngine.Error += OnPollingError;
            }
        }

        public void Start()
        {
            _pollingEngine?.Start();
            _pollingEngineStarted = _pollingEngine is not null;

            _customScheduler.Start(TimeSpan.FromMilliseconds(CustomTimerIntervalMs), CustomTick);
            _monitorScheduler.Start(TimeSpan.FromMilliseconds(MonitorTimerIntervalMs), MonitorTick);
            _trendScheduler.Start(TimeSpan.FromMilliseconds(_trendPeriodMs), TrendTick);
        }

        public void Stop()
        {
            _customScheduler.Stop();
            _monitorScheduler.Stop();
            _trendScheduler.Stop();
            _pollingEngine?.Stop();
            _pollingEngineStarted = false;
        }

        public void Dispose()
        {
            Stop();

            if (_pollingEngine is not null)
            {
                _pollingEngine.Error -= OnPollingError;
            }

            _customScheduler.Dispose();
            _monitorScheduler.Dispose();
            _trendScheduler.Dispose();
            _logger.LogDebug("{Coordinator} disposed", nameof(MonitoringCoordinator));
        }

        private void OnPollingError(object? sender, PollingErrorEventArgs e)
        {
            _callbacks.SetStatusMessage($"Error reading {e.Command.Area}: {e.Result.ErrorMessage}");
            _callbacks.SetHasConnectionError(true);
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
                    // Continuous write
                    if (entry.Continuous)
                    {
                        int period = entry.PeriodMs <= 0 ? 1000 : entry.PeriodMs;
                        if ((now - entry.LastWriteUtc).TotalMilliseconds >= period)
                        {
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await _callbacks.WriteCustomNowAsync(entry);
                                entry.LastWriteUtc = now;
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

                    // Continuous read (Monitor column) - controlled by the custom-read master switch
                    if (entry.Monitor && _callbacks.CustomReadMonitorEnabled)
                    {
                        int readPeriod = entry.ReadPeriodMs <= 0 ? 1000 : entry.ReadPeriodMs;
                        if ((now - entry.LastReadUtc).TotalMilliseconds >= readPeriod)
                        {
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await _callbacks.ReadCustomNowAsync(entry);
                                entry.LastReadUtc = now;
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex) when (ex is not OutOfMemoryException)
                            {
                                _logger.LogError(ex, "Continuous read failed for {Area} {Address}", entry.Area, entry.Address);
                                entry.Monitor = false;
                            }
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

                if (_pollingEngine is not null)
                {
                    EnqueueAreaReads(now);
                    await DrainResultsAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Fallback when no polling engine is supplied (legacy / unit tests).
                    await ReadAreasDirectlyAsync(now, cancellationToken).ConfigureAwait(false);
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

        private void EnqueueAreaReads(DateTime now)
        {
            if (_callbacks.HoldingMonitorEnabled &&
                (now - _callbacks.LastHoldingReadUtc).TotalMilliseconds >= (_callbacks.HoldingMonitorPeriodMs <= 0 ? 1000 : _callbacks.HoldingMonitorPeriodMs))
            {
                _pollingEngine!.Enqueue(new PollingCommand
                {
                    Area = PlcArea.HoldingRegister,
                    UnitId = _callbacks.EffectiveUnitId,
                    StartAddress = _callbacks.HoldingStartAddress,
                    Count = _callbacks.HoldingCount,
                    IsServerMode = _callbacks.IsServerMode,
                });
                _callbacks.LastHoldingReadUtc = now;
            }

            if (_callbacks.InputRegistersMonitorEnabled &&
                (now - _callbacks.LastInputRegReadUtc).TotalMilliseconds >= (_callbacks.InputRegistersMonitorPeriodMs <= 0 ? 1000 : _callbacks.InputRegistersMonitorPeriodMs))
            {
                _pollingEngine!.Enqueue(new PollingCommand
                {
                    Area = PlcArea.InputRegister,
                    UnitId = _callbacks.EffectiveUnitId,
                    StartAddress = _callbacks.InputRegisterStartAddress,
                    Count = _callbacks.InputRegisterCount,
                    IsServerMode = _callbacks.IsServerMode,
                });
                _callbacks.LastInputRegReadUtc = now;
            }

            if (_callbacks.CoilsMonitorEnabled &&
                (now - _callbacks.LastCoilsReadUtc).TotalMilliseconds >= (_callbacks.CoilsMonitorPeriodMs <= 0 ? 1000 : _callbacks.CoilsMonitorPeriodMs))
            {
                _pollingEngine!.Enqueue(new PollingCommand
                {
                    Area = PlcArea.Coil,
                    UnitId = _callbacks.EffectiveUnitId,
                    StartAddress = _callbacks.CoilStartAddress,
                    Count = _callbacks.CoilCount,
                    IsServerMode = _callbacks.IsServerMode,
                });
                _callbacks.LastCoilsReadUtc = now;
            }

            if (_callbacks.DiscreteInputsMonitorEnabled &&
                (now - _callbacks.LastDiscreteReadUtc).TotalMilliseconds >= (_callbacks.DiscreteInputsMonitorPeriodMs <= 0 ? 1000 : _callbacks.DiscreteInputsMonitorPeriodMs))
            {
                _pollingEngine!.Enqueue(new PollingCommand
                {
                    Area = PlcArea.DiscreteInput,
                    UnitId = _callbacks.EffectiveUnitId,
                    StartAddress = _callbacks.DiscreteInputStartAddress,
                    Count = _callbacks.DiscreteInputCount,
                    IsServerMode = _callbacks.IsServerMode,
                });
                _callbacks.LastDiscreteReadUtc = now;
            }
        }

        private async Task DrainResultsAsync(CancellationToken cancellationToken)
        {
            if (_pollingEngine is null)
                return;

            // Drain all results currently available so the UI can batch-apply them.
            while (_pollingEngine.Results.TryRead(out var result))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!result.IsError)
                {
                    _callbacks.ApplyPollingResult(result);
                    _callbacks.SetHasConnectionError(false);
                }

                // Errors are surfaced by the PollingEngine.Error event handler
                // and already set the status / error flag, but we still keep the
                // result out of the channel.
            }

            // If no results are immediately available and we are not already
            // throttled, await the next one briefly without blocking the tick.
            if (_pollingEngine.Results.TryRead(out var nextResult))
            {
                if (!nextResult.IsError)
                {
                    _callbacks.ApplyPollingResult(nextResult);
                    _callbacks.SetHasConnectionError(false);
                }
            }
            else
            {
                // No result yet. The worker is still running; it will apply on the next tick.
                await Task.CompletedTask;
            }
        }

        private async Task ReadAreasDirectlyAsync(DateTime now, CancellationToken cancellationToken)
        {
            if (_callbacks.HoldingMonitorEnabled &&
                (now - _callbacks.LastHoldingReadUtc).TotalMilliseconds >= (_callbacks.HoldingMonitorPeriodMs <= 0 ? 1000 : _callbacks.HoldingMonitorPeriodMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _callbacks.ReadRegistersAsync();
                _callbacks.LastHoldingReadUtc = now;
            }

            if (_callbacks.InputRegistersMonitorEnabled &&
                (now - _callbacks.LastInputRegReadUtc).TotalMilliseconds >= (_callbacks.InputRegistersMonitorPeriodMs <= 0 ? 1000 : _callbacks.InputRegistersMonitorPeriodMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _callbacks.ReadInputRegistersAsync();
                _callbacks.LastInputRegReadUtc = now;
            }

            if (_callbacks.CoilsMonitorEnabled &&
                (now - _callbacks.LastCoilsReadUtc).TotalMilliseconds >= (_callbacks.CoilsMonitorPeriodMs <= 0 ? 1000 : _callbacks.CoilsMonitorPeriodMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _callbacks.ReadCoilsAsync();
                _callbacks.LastCoilsReadUtc = now;
            }

            if (_callbacks.DiscreteInputsMonitorEnabled &&
                (now - _callbacks.LastDiscreteReadUtc).TotalMilliseconds >= (_callbacks.DiscreteInputsMonitorPeriodMs <= 0 ? 1000 : _callbacks.DiscreteInputsMonitorPeriodMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _callbacks.ReadDiscreteInputsAsync();
                _callbacks.LastDiscreteReadUtc = now;
            }
        }
    }
}
