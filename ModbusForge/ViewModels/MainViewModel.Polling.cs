using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Services;
using ModbusForge.Helpers;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// MainViewModel - Polling partial (split for navigability; behavior unchanged).
    /// </summary>
    public partial class MainViewModel
    {
        private CancellationTokenSource? _pollCts;

        private readonly object _pollLifecycleLock = new();

        private readonly object _pendingPollLock = new();

        private readonly HashSet<PlcArea> _pendingPollAreas = new();


        private const int PollLoopIntervalMs = 50;

        private const int MinimumMonitorPeriodMs = 50;


        [ObservableProperty]
        private bool _holdingMonitorEnabled = true;


        [ObservableProperty]
        private bool _inputRegistersMonitorEnabled;


        [ObservableProperty]
        private bool _coilsMonitorEnabled;


        [ObservableProperty]
        private bool _discreteInputsMonitorEnabled;


        partial void OnHoldingMonitorEnabledChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else if (!AnyMonitorEnabled())
            {
                StopPolling();
            }
        }


        partial void OnInputRegistersMonitorEnabledChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else if (!AnyMonitorEnabled())
            {
                StopPolling();
            }
        }


        partial void OnCoilsMonitorEnabledChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else if (!AnyMonitorEnabled())
            {
                StopPolling();
            }
        }


        partial void OnDiscreteInputsMonitorEnabledChanged(bool value)
        {
            if (value)
            {
                StartPolling();
            }
            else if (!AnyMonitorEnabled())
            {
                StopPolling();
            }
        }


        private bool AnyMonitorEnabled() => HoldingMonitorEnabled || InputRegistersMonitorEnabled || CoilsMonitorEnabled || DiscreteInputsMonitorEnabled;


        private void StartPolling()
        {
            if (_disposed || ActiveProfile is not { IsConnected: true } || !AnyMonitorEnabled()) return;

            lock (_pollLifecycleLock)
            {
                if (_pollCts != null) return;

                var cts = new CancellationTokenSource();
                _pollCts = cts;
                _ = Task.Run(() => PollLoopAsync(cts), cts.Token);
            }
        }


        private void StopPolling()
        {
            lock (_pollLifecycleLock)
            {
                _pollCts?.Cancel();
            }

            lock (_pendingPollLock)
            {
                _pendingPollAreas.Clear();
            }
        }


        private async Task PollLoopAsync(CancellationTokenSource loopCts)
        {
            var token = loopCts.Token;
            var hadError = false;
            try
            {
                while (!token.IsCancellationRequested && ActiveProfile is { IsConnected: true } && AnyMonitorEnabled())
                {
                    var now = DateTime.UtcNow;
                    QueueDueAreaReads(now);
                    await DrainPendingAreaReadsAsync(token);
                    await Task.Delay(PollLoopIntervalMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Avalonia polling loop canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                hadError = true;
                _logger.LogError(ex, "Avalonia polling loop failed");
                await _dispatcher.InvokeAsync(() => StatusMessage = $"Poll error: {ex.Message}");
            }
            finally
            {
                var restart = false;
                lock (_pollLifecycleLock)
                {
                    if (ReferenceEquals(_pollCts, loopCts))
                    {
                        _pollCts = null;
                        restart = !_disposed && ActiveProfile is { IsConnected: true } && AnyMonitorEnabled();
                    }
                }

                loopCts.Dispose();
                if (restart)
                {
                    // A clean stop (disconnected, monitors disabled) restarts immediately;
                    // an error exit backs off first so a persistently failing service
                    // cannot spin a tight error → restart loop.
                    if (hadError)
                        RestartPollingAfterBackoff();
                    else
                        StartPolling();
                }
            }
        }


        private void RestartPollingAfterBackoff()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(PollRestartBackoffMs);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // Re-evaluate after the pause: the connection may have dropped (or the
                // view model been disposed) in the meantime.
                if (!_disposed && ActiveProfile is { IsConnected: true } && AnyMonitorEnabled())
                {
                    StartPolling();
                }
            });
        }


        private void QueueDueAreaReads(DateTime now)
        {
            QueueAreaReadIfDue(PlcArea.HoldingRegister, HoldingMonitorEnabled, HoldingMonitorPeriodMs, _lastHoldingReadUtc, now, value => _lastHoldingReadUtc = value);
            QueueAreaReadIfDue(PlcArea.InputRegister, InputRegistersMonitorEnabled, InputRegistersMonitorPeriodMs, _lastInputRegReadUtc, now, value => _lastInputRegReadUtc = value);
            QueueAreaReadIfDue(PlcArea.Coil, CoilsMonitorEnabled, CoilsMonitorPeriodMs, _lastCoilsReadUtc, now, value => _lastCoilsReadUtc = value);
            QueueAreaReadIfDue(PlcArea.DiscreteInput, DiscreteInputsMonitorEnabled, DiscreteInputsMonitorPeriodMs, _lastDiscreteReadUtc, now, value => _lastDiscreteReadUtc = value);
        }


        private void QueueAreaReadIfDue(
            PlcArea area,
            bool enabled,
            int periodMs,
            DateTime lastReadUtc,
            DateTime now,
            Action<DateTime> setLastReadUtc)
        {
            if (!enabled || (now - lastReadUtc).TotalMilliseconds < Math.Max(MinimumMonitorPeriodMs, periodMs))
                return;

            lock (_pendingPollLock)
            {
                _pendingPollAreas.Add(area);
            }

            setLastReadUtc(now);
        }


        private async Task DrainPendingAreaReadsAsync(CancellationToken token)
        {
            PlcArea[] pending;
            lock (_pendingPollLock)
            {
                pending = _pendingPollAreas.ToArray();
                _pendingPollAreas.Clear();
            }

            foreach (var area in pending)
            {
                token.ThrowIfCancellationRequested();
                if (IsAreaMonitorEnabled(area))
                {
                    await ReadAreaAsync(area, token, true);
                }
            }
        }


        private async Task ReadAreaAsync(PlcArea area, CancellationToken token, bool isMonitoring = false)
        {
            await _modbusIoGate.WaitAsync(token);
            try
            {
                var service = ActiveService;
                if (service == null || ActiveProfile == null)
                {
                    await _dispatcher.InvokeAsync(() => StatusMessage = "No active service.");
                    return;
                }

                var unitId = EffectiveUnitId;
                var (start, count) = GetAreaStartCount(area);

                await _dispatcher.InvokeAsync(() => StatusMessage = $"Reading {area}...");
                token.ThrowIfCancellationRequested();

                switch (area)
                {
                    case PlcArea.HoldingRegister:
                        var holding = await service.ReadHoldingRegistersAsync(unitId, start, count)
                            ?? throw new InvalidOperationException("Read returned no response.");
                        var holdingPartial = holding.Length < count;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            if (IsRegisterGridEditing) return;
                            HoldingRegisters = ApplyRegisterValues(
                                HoldingRegisters,
                                start,
                                holding,
                                RegistersGlobalType,
                                RegistersSwapBytes,
                                RegistersSwapWords,
                                CurrentConfig.RegisterSettings.HoldingRegisterMetadata,
                                holdingPartial);
                            StatusMessage = holdingPartial
                                ? $"Partial read: {holding.Length} of {count} holding registers"
                                : $"Read {holding.Length} holding registers";
                        });
                        break;

                    case PlcArea.InputRegister:
                        var input = await service.ReadInputRegistersAsync(unitId, start, count)
                            ?? throw new InvalidOperationException("Read returned no response.");
                        var inputPartial = input.Length < count;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            if (IsRegisterGridEditing) return;
                            InputRegisters = ApplyRegisterValues(
                                InputRegisters,
                                start,
                                input,
                                InputRegistersGlobalType,
                                InputRegistersSwapBytes,
                                InputRegistersSwapWords,
                                CurrentConfig.RegisterSettings.InputRegisterMetadata,
                                inputPartial);
                            StatusMessage = inputPartial
                                ? $"Partial read: {input.Length} of {count} input registers"
                                : $"Read {input.Length} input registers";
                        });
                        break;

                    case PlcArea.Coil:
                        var coils = await service.ReadCoilsAsync(unitId, start, count)
                            ?? throw new InvalidOperationException("Read returned no response.");
                        var coilsPartial = coils.Length < count;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            if (IsRegisterGridEditing) return;
                            Coils = ApplyCoilValues(Coils, start, coils, coilsPartial);
                            StatusMessage = coilsPartial
                                ? $"Partial read: {coils.Length} of {count} coils"
                                : $"Read {coils.Length} coils";
                        });
                        break;

                    case PlcArea.DiscreteInput:
                        var discrete = await service.ReadDiscreteInputsAsync(unitId, start, count)
                            ?? throw new InvalidOperationException("Read returned no response.");
                        var discretePartial = discrete.Length < count;
                        await _dispatcher.InvokeAsync(() =>
                        {
                            if (IsRegisterGridEditing) return;
                            DiscreteInputs = ApplyCoilValues(DiscreteInputs, start, discrete, discretePartial);
                            StatusMessage = discretePartial
                                ? $"Partial read: {discrete.Length} of {count} discrete inputs"
                                : $"Read {discrete.Length} discrete inputs";
                        });
                        break;
                }

                ClearMonitorFailure(area);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await HandleAreaReadFailureAsync(area, ex, isMonitoring);
            }
            finally
            {
                _modbusIoGate.Release();
            }
        }


        private bool IsAreaMonitorEnabled(PlcArea area) => area switch
        {
            PlcArea.HoldingRegister => HoldingMonitorEnabled,
            PlcArea.InputRegister => InputRegistersMonitorEnabled,
            PlcArea.Coil => CoilsMonitorEnabled,
            PlcArea.DiscreteInput => DiscreteInputsMonitorEnabled,
            _ => false
        };


        private async Task HandleAreaReadFailureAsync(PlcArea area, Exception exception, bool isMonitoring)
        {
            var failureTime = DateTime.UtcNow;
            int failureCount;
            lock (_monitorFailureLock)
            {
                failureCount = GetMonitorFailureCountUnsafe(area) + 1;
                _monitorFailureCounts[area] = failureCount;
                _lastMonitorFailureUtc[area] = failureTime;
            }

            // Runs on the poll thread - marshal observable property changes to the UI thread.
            await _dispatcher.InvokeAsync(() =>
            {
                LastErrorTime = failureTime;
                HasConnectionError = true;
            });
            _logger.LogError(exception, "Error reading {Area} (failure {FailureCount})", area, failureCount);

            var message = $"Failed to read {area}: {exception.Message}";
            if (isMonitoring && IsAreaMonitorEnabled(area))
            {
                message += "\n\nContinuous monitoring has been paused. Fix the issue and re-enable monitoring.";
                await _dispatcher.InvokeAsync(() => SetAreaMonitorEnabled(area, false));
            }

            await _dispatcher.InvokeAsync(() => StatusMessage = message);

            if (_messageBoxService != null)
            {
                Task<DialogResult>? dialogTask = null;
                await _dispatcher.InvokeAsync(() =>
                    dialogTask = _messageBoxService.ShowAsync(message, "Read Error", DialogButton.Ok, DialogIcon.Error));
                if (dialogTask != null)
                {
                    await dialogTask;
                }
            }
        }


        private void SetAreaMonitorEnabled(PlcArea area, bool enabled)
        {
            switch (area)
            {
                case PlcArea.HoldingRegister:
                    HoldingMonitorEnabled = enabled;
                    break;
                case PlcArea.InputRegister:
                    InputRegistersMonitorEnabled = enabled;
                    break;
                case PlcArea.Coil:
                    CoilsMonitorEnabled = enabled;
                    break;
                case PlcArea.DiscreteInput:
                    DiscreteInputsMonitorEnabled = enabled;
                    break;
            }
        }

    }
}
