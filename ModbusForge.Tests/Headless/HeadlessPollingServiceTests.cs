using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Headless;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Tests.Headless
{
    public class HeadlessPollingServiceTests
    {
        [Fact]
        public async Task EnsureConnected_RetriesWithBackoff_UntilSuccess()
        {
            var service = new FakeModbusService
            {
                ConnectAttemptsBeforeSuccess = 3,
            };

            var connected = await HeadlessConnection.EnsureConnectedAsync(
                service, new ConnectionProfile("t", "127.0.0.1", 502, 1),
                NullLogger<HeadlessPollingService>.Instance, CancellationToken.None,
                initialBackoffMs: 10, maxBackoffMs: 40);

            Assert.True(connected);
            Assert.Equal(3, service.ConnectCalls);
        }

        [Fact]
        public async Task EnsureConnected_ReturnsFalse_WhenCancelledDuringRetry()
        {
            using var cts = new CancellationTokenSource();
            var service = new FakeModbusService { AlwaysFails = true };

            var task = HeadlessConnection.EnsureConnectedAsync(
                service, new ConnectionProfile("t", "127.0.0.1", 502, 1),
                NullLogger<HeadlessPollingService>.Instance, cts.Token,
                initialBackoffMs: 50, maxBackoffMs: 100);

            // Give the first attempt a moment to fail, then cancel while it
            // waits out the backoff.
            await Task.Delay(10);
            cts.Cancel();

            var connected = await task;

            Assert.False(connected);
            Assert.Equal(1, service.ConnectCalls);
        }

        [Fact]
        public async Task ExecuteAsync_DeadDeviceAtStartup_RetriesUntilItComesUp()
        {
            var modbus = new FakeModbusService
            {
                ConnectAttemptsBeforeSuccess = 2,
                HoldingValues = new ushort[] { 7, 8 },
            };
            var lifetime = new FakeLifetime();

            var service = CreateService(modbus, lifetime, new Dictionary<string, string?>
            {
                ["Polling:IntervalMs"] = "50",
                ["Polling:Count"] = "2",
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            await AssertDoesNotThrowAsync(() => service.RunAsync(cts.Token));
            cts.Cancel();

            // It connected (after one failure) and polled at least once.
            Assert.Equal(2, modbus.ConnectCalls);
            Assert.True(modbus.ReadHoldingCalls >= 1, $"expected at least one read, got {modbus.ReadHoldingCalls}");
        }

        [Fact]
        public async Task ExecuteAsync_ConnectionLost_MidRun_ReconnectsAndContinuesPolling()
        {
            var modbus = new FakeModbusService
            {
                HoldingValues = new ushort[] { 1, 2 },
            };
            var lifetime = new FakeLifetime();

            var service = CreateService(modbus, lifetime, new Dictionary<string, string?>
            {
                ["Polling:IntervalMs"] = "50",
                ["Polling:Count"] = "2",
            });

            using var cts = new CancellationTokenSource();
            var task = service.RunAsync(cts.Token);

            // Let it connect and produce at least one successful poll.
            await WaitForAsync(() => modbus.ReadHoldingCalls >= 1, TimeSpan.FromSeconds(3));

            // Simulate the transport noticing a dead peer.
            modbus.RaiseConnectionLost();

            // The service must notice the loss (DisconnectAsync) and
            // re-establish the connection (a second ConnectAsync call).
            await WaitForAsync(() => modbus.ConnectCalls >= 2, TimeSpan.FromSeconds(3));

            var readsBeforeReconnect = modbus.ReadHoldingCalls;
            await WaitForAsync(() => modbus.ReadHoldingCalls > readsBeforeReconnect, TimeSpan.FromSeconds(3));

            cts.Cancel();
            await AssertDoesNotThrowAsync(async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await task.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    // ExecuteAsync surfaces cancellation through its awaits.
                }
            });
        }

        [Fact]
        public async Task ExecuteAsync_ThreeSilentPolls_TriggersReconnect()
        {
            // The device answers the first two reads, then goes silent
            // (socket alive, no responses) - no ConnectionLost event. After
            // three unanswered polls the service must drop and re-establish
            // the connection; the fake then answers again, and the cycle
            // repeats.
            var modbus = new FakeModbusService
            {
                NullResponseAfterReads = 2,
                ResetSilenceOnReconnect = true,
            };
            var lifetime = new FakeLifetime();

            var service = CreateService(modbus, lifetime, new Dictionary<string, string?>
            {
                ["Polling:IntervalMs"] = "50",
                ["Polling:Count"] = "1",
            });

            using var cts = new CancellationTokenSource();
            var task = service.RunAsync(cts.Token);

            // Two reconnect cycles give ample room under CI load.
            await WaitForAsync(() => modbus.ConnectCalls >= 3, TimeSpan.FromSeconds(10));

            cts.Cancel();
            await DrainAsync(task);
        }

        [Fact]
        public async Task ExecuteAsync_InvalidPollCount_StopsTheApplication()
        {
            var modbus = new FakeModbusService();
            var lifetime = new FakeLifetime();

            var service = CreateService(modbus, lifetime, new Dictionary<string, string?>
            {
                ["Polling:Count"] = "0",
            });

            using var cts = new CancellationTokenSource();
            await service.RunAsync(cts.Token);

            Assert.True(lifetime.Stopped);
            Assert.Equal(0, modbus.ConnectCalls); // validation happens before any connection attempt
        }

        [Fact]
        public async Task ExecuteAsync_IntervalBelowMinimum_StopsTheApplication()
        {
            var modbus = new FakeModbusService();
            var lifetime = new FakeLifetime();

            var service = CreateService(modbus, lifetime, new Dictionary<string, string?>
            {
                ["Polling:IntervalMs"] = "1",
            });

            using var cts = new CancellationTokenSource();
            await service.RunAsync(cts.Token);

            Assert.True(lifetime.Stopped);
            Assert.Equal(0, modbus.ConnectCalls);
        }

        private static TestablePollingService CreateService(
            FakeModbusService modbus, FakeLifetime lifetime, Dictionary<string, string?> settings)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            return new TestablePollingService(
                modbus,
                null,
                lifetime,
                configuration,
                NullLogger<HeadlessPollingService>.Instance);
        }

        /// <summary>
        /// Exposes the protected BackgroundService.ExecuteAsync to the tests.
        /// </summary>
        private sealed class TestablePollingService : HeadlessPollingService
        {
            public TestablePollingService(
                IModbusService modbusService,
                MqttGatewayService? mqttService,
                IHostApplicationLifetime lifetime,
                IConfiguration configuration,
                ILogger<HeadlessPollingService> logger)
                : base(modbusService, mqttService, lifetime, configuration, logger)
            {
            }

            public Task RunAsync(CancellationToken token) => ExecuteAsync(token);
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.Elapsed > timeout)
                    throw new TimeoutException("Condition was not met in time.");
                await Task.Delay(10);
            }
        }

        private static async Task DrainAsync(Task task)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // ExecuteAsync surfaces cancellation through its awaits.
            }
        }

        private static async Task AssertDoesNotThrowAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a normal exit for the service.
            }
        }

        private sealed class FakeLifetime : IHostApplicationLifetime
        {
            public bool Stopped { get; private set; }

            public CancellationToken ApplicationStarted { get; } = CancellationToken.None;
            public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
            public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

            public void StopApplication() => Stopped = true;
        }

        private sealed class FakeModbusService : IModbusService
        {
            private readonly object _gate = new();
            private bool _connected;
            private int _readsSinceConnect;

            public int ConnectAttemptsBeforeSuccess { get; init; }
            public bool AlwaysFails { get; init; }
            public int NullResponseAfterReads { get; init; } = -1; // -1 = never go silent

            /// <summary>
            /// When set, a successful ConnectAsync resets the silent-response
            /// counter - modelling a device that comes back when re-addressed.
            /// </summary>
            public bool ResetSilenceOnReconnect { get; init; }

            public ushort[]? HoldingValues { get; init; } = Array.Empty<ushort>();
            public int ReadHoldingCalls { get; private set; }
            public int ConnectCalls { get; private set; }

            public event EventHandler? ConnectionLost;

            public bool IsConnected
            {
                get
                {
                    lock (_gate)
                    {
                        return _connected;
                    }
                }
            }

            public string BoundEndpoint => "127.0.0.1:502";

            public ModbusFrameLogger FrameLogger { get; } = new();

            public Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    ConnectCalls++;

                    if (AlwaysFails || (ConnectAttemptsBeforeSuccess > 0 && ConnectCalls < ConnectAttemptsBeforeSuccess))
                    {
                        _connected = false;
                        return Task.FromResult(false);
                    }

                    _connected = true;
                    if (ResetSilenceOnReconnect)
                        _readsSinceConnect = 0;
                    return Task.FromResult(true);
                }
            }

            public Task DisconnectAsync()
            {
                lock (_gate)
                {
                    _connected = false;
                }
                return Task.CompletedTask;
            }

            public Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
                => ConnectAsync(profile.IpAddress, profile.Port, profile.UnitId.ToString(), cancellationToken);

            public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
                => throw new NotSupportedException();

            public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
            {
                ReadHoldingCalls++;
                _readsSinceConnect++;
                if (NullResponseAfterReads >= 0 && _readsSinceConnect > NullResponseAfterReads)
                    return Task.FromResult<ushort[]?>(null);
                return Task.FromResult<ushort[]?>(HoldingValues);
            }

            public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<ushort[]?>(null);

            public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
                => Task.CompletedTask;

            public Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values)
                => Task.CompletedTask;

            public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<bool[]?>(null);

            public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<bool[]?>(null);

            public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
                => Task.CompletedTask;

            public Task WriteCoilsAsync(byte unitId, int startAddress, bool[] values)
                => Task.CompletedTask;

            public Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask)
                => Task.FromResult<ushort?>(null);

            public Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues)
                => Task.FromResult<ushort[]?>(null);

            public Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic)
                => Task.FromResult<DeviceIdentification?>(null);

            public void RaiseConnectionLost() => ConnectionLost?.Invoke(this, EventArgs.Empty);

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
