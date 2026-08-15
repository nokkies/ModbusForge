using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Services
{
    /// <summary>
    /// Background polling engine that executes Modbus I/O off the UI thread.
    /// Duplicate commands for the same area/unit are coalesced so the worker always
    /// processes the most recent request and back-pressure is bounded.
    /// </summary>
    public class PollingEngine : IPollingEngine
    {
        private readonly IModbusService _clientService;
        private readonly IModbusService _serverService;
        private readonly ILogger<PollingEngine> _logger;
        private readonly ConcurrentDictionary<string, PollingCommand> _pending = new();
        private readonly Channel<PollingResult> _resultChannel;
        private readonly CancellationTokenSource _cts = new();

        private Task? _worker;
        private bool _disposed;

        /// <summary>
        /// How long Stop() waits for the worker after canceling. The worker's worst-case
        /// in-flight time is one Modbus I/O at the transport timeout (5000 ms, no app-level
        /// retries), so the margin must exceed that or a cancel arriving mid-read times out
        /// the wait while the worker is still (briefly) alive.
        /// </summary>
        private const int StopWorkerWaitMs = 6000;

        public PollingEngine(
            IModbusService clientService,
            IModbusService serverService,
            ILogger<PollingEngine> logger)
        {
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resultChannel = Channel.CreateUnbounded<PollingResult>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });
        }

        public ChannelReader<PollingResult> Results => _resultChannel.Reader;

        public event EventHandler<PollingErrorEventArgs>? Error;

        public void Start(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_worker is not null)
                return;

            _cts.TryReset();
            _worker = Task.Run(() => WorkerLoopAsync(cancellationToken), cancellationToken);
            _logger.LogInformation("Polling engine started");
        }

        public void Stop()
        {
            _cts.Cancel();
            var worker = _worker;
            if (worker != null && !worker.Wait(TimeSpan.FromMilliseconds(StopWorkerWaitMs)))
            {
                // Cannot happen in steady state (the worker is always within one bounded I/O
                // of checking the token); log loudly if it ever does so the leak is visible.
                _logger.LogWarning(
                    "Polling engine worker did not exit within {WaitMs} ms after cancel; it will finish on its own",
                    StopWorkerWaitMs);
            }
            _worker = null;
            _logger.LogInformation("Polling engine stopped");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            _cts.Dispose();
            _resultChannel.Writer.TryComplete();
            _disposed = true;
        }

        public void Enqueue(PollingCommand command)
        {
            if (command is null)
                throw new ArgumentNullException(nameof(command));

            _pending[command.GetCoalesceKey()] = command;
        }

        private async Task WorkerLoopAsync(CancellationToken externalToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, externalToken);
            var token = linkedCts.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Wait for work. A short delay keeps the loop responsive without busy spinning.
                    await Task.Delay(10, token).ConfigureAwait(false);

                    if (_pending.IsEmpty)
                        continue;

                    var snapshot = _pending.Values.ToArray();
                    _pending.Clear();

                    foreach (var command in snapshot)
                    {
                        token.ThrowIfCancellationRequested();

                        // If a newer command for the same area/unit was enqueued while we were
                        // snapshotting, skip this one. The newer command will be processed in the
                        // next iteration.
                        if (_pending.TryGetValue(command.GetCoalesceKey(), out var newer) && newer != command)
                        {
                            _logger.LogDebug("Skipping stale {CommandKey} because a newer command is already pending", command.GetCoalesceKey());
                            continue;
                        }

                        var result = await ExecuteAsync(command, token).ConfigureAwait(false);

                        if (!result.IsError)
                        {
                            await _resultChannel.Writer.WriteAsync(result, token).ConfigureAwait(false);
                            continue;
                        }

                        // Surface terminal errors to the coordinator/view model.
                        Error?.Invoke(this, new PollingErrorEventArgs(command, result));

                        // Still write the result so the UI can clear stale state or show an error.
                        await _resultChannel.Writer.WriteAsync(result, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Polling engine worker canceled");
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogError(ex, "Unhandled error in polling engine worker");
                }
            }

            _resultChannel.Writer.Complete();
        }

        private async Task<PollingResult> ExecuteAsync(PollingCommand command, CancellationToken token)
        {
            var result = new PollingResult
            {
                CorrelationId = command.CorrelationId,
                Area = command.Area,
                UnitId = command.UnitId,
                StartAddress = command.StartAddress,
                CustomEntryName = command.CustomEntry?.Name,
            };

            var service = command.IsServerMode ? _serverService : _clientService;

            try
            {
                if (command.IsWrite)
                {
                    switch (command.Area)
                    {
                        case PlcArea.HoldingRegister:
                            if (command.WriteValue is ushort us)
                                await service.WriteSingleRegisterAsync(command.UnitId, command.StartAddress, us).ConfigureAwait(false);
                            else if (command.WriteValue is ushort[] usArray)
                                await service.WriteRegistersAsync(command.UnitId, command.StartAddress, usArray).ConfigureAwait(false);
                            break;

                        case PlcArea.Coil:
                            if (command.WriteValue is bool b)
                                await service.WriteSingleCoilAsync(command.UnitId, command.StartAddress, b).ConfigureAwait(false);
                            break;

                        default:
                            throw new NotSupportedException($"Write not supported for {command.Area}");
                    }
                }
                else
                {
                    // A null result means the device did not respond (disconnected, slave
                    // exception, or timeout). Surface it as an error instead of reporting a
                    // "successful" empty read.
                    switch (command.Area)
                    {
                        case PlcArea.HoldingRegister:
                            var holdingValues = await service.ReadHoldingRegistersAsync(command.UnitId, command.StartAddress, command.Count).ConfigureAwait(false);
                            if (holdingValues is null)
                                return FailResult(result, "No response from device");
                            result.Values = holdingValues;
                            break;

                        case PlcArea.InputRegister:
                            var inputValues = await service.ReadInputRegistersAsync(command.UnitId, command.StartAddress, command.Count).ConfigureAwait(false);
                            if (inputValues is null)
                                return FailResult(result, "No response from device");
                            result.Values = inputValues;
                            break;

                        case PlcArea.Coil:
                            var coilStates = await service.ReadCoilsAsync(command.UnitId, command.StartAddress, command.Count).ConfigureAwait(false);
                            if (coilStates is null)
                                return FailResult(result, "No response from device");
                            result.States = coilStates;
                            break;

                        case PlcArea.DiscreteInput:
                            var discreteStates = await service.ReadDiscreteInputsAsync(command.UnitId, command.StartAddress, command.Count).ConfigureAwait(false);
                            if (discreteStates is null)
                                return FailResult(result, "No response from device");
                            result.States = discreteStates;
                            break;

                        default:
                            throw new NotSupportedException($"Read not supported for {command.Area}");
                    }
                }

                result.IsError = false;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                result.IsError = true;
                result.Exception = ex;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static PollingResult FailResult(PollingResult result, string message)
        {
            result.IsError = true;
            result.ErrorMessage = message;
            if (result.Area is PlcArea.HoldingRegister or PlcArea.InputRegister)
                result.Values = Array.Empty<ushort>();
            else
                result.States = Array.Empty<bool>();
            return result;
        }
    }
}
