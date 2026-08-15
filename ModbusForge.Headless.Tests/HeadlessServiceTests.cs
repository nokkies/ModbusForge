using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Headless.Tests
{
    /// <summary>
    /// Hand-written fake for <see cref="IModbusService"/> so reconnect behavior can be asserted
    /// deterministically (no reliance on mocking framework interception of interface defaults).
    /// </summary>
    internal sealed class FakeModbusService : IModbusService
    {
        private readonly Action<int>? _onConnect;
        private int _connectCallCount;

        public FakeModbusService(Action<int>? onConnect = null)
        {
            _onConnect = onConnect;
        }

        public int ConnectCallCount => _connectCallCount;

        public bool IsConnected { get; set; } = true;

        public string BoundEndpoint => "fake";

        public ModbusFrameLogger FrameLogger { get; } = new();

        public ushort[]? NextHoldingRead { get; set; } = null;

        public Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _connectCallCount);
            _onConnect?.Invoke(call);
            return Task.FromResult(IsConnected);
        }

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
            => Task.FromResult(new ConnectionDiagnosticResult());

        public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
            => Task.FromResult(NextHoldingRead);

        public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
            => Task.FromResult(NextHoldingRead);

        public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value) => Task.CompletedTask;

        public Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values) => Task.CompletedTask;

        public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
            => Task.FromResult<bool[]?>(null);

        public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
            => Task.FromResult<bool[]?>(null);

        public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value) => Task.CompletedTask;

        public Task WriteCoilsAsync(byte unitId, int startAddress, bool[] values) => Task.CompletedTask;

        public Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask)
            => Task.FromResult<ushort?>(null);

        public Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues)
            => Task.FromResult<ushort[]?>(null);

        public Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic)
            => Task.FromResult<DeviceIdentification?>(null);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class HeadlessPollingServiceTests
    {
        private static HeadlessPollingService CreateService(IModbusService modbus, IHostApplicationLifetime lifetime, IConfiguration configuration)
            => new HeadlessPollingService(modbus, null, lifetime, configuration, NullLogger<HeadlessPollingService>.Instance);

        [Fact]
        public async Task Reconnects_AfterConsecutiveReadFailures()
        {
            var secondConnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new FakeModbusService(onConnect: call =>
            {
                if (call == 2)
                {
                    secondConnect.TrySetResult();
                }
            })
            {
                // Reads keep returning null (device unresponsive); the service must reconnect.
                NextHoldingRead = null
            };

            var lifetime = new Mock<IHostApplicationLifetime>();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Connection:Host"] = "127.0.0.1",
                    ["Connection:Port"] = "15020",
                    ["Polling:IntervalMs"] = "20",
                    ["Polling:ReconnectBackoffMs"] = "10",
                })
                .Build();

            var service = CreateService(fake, lifetime.Object, config);
            using var cts = new CancellationTokenSource();
            var runTask = Task.Run(() => service.ExecuteForTest(cts.Token));

            await secondConnect.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(fake.ConnectCallCount >= 2, "expected a reconnect attempt after repeated read failures");

            cts.Cancel();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15));

            lifetime.Verify(l => l.StopApplication(), Times.AtLeastOnce);
        }

        [Fact]
        public async Task KeepsPolling_WhenReadsSucceed()
        {
            var fake = new FakeModbusService
            {
                NextHoldingRead = new ushort[] { 1, 2, 3 }
            };
            var readCount = new int[1];

            // Wrap reads to count them.
            var countingFake = new CountingHoldingReadFake(fake, readCount);

            var lifetime = new Mock<IHostApplicationLifetime>();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Polling:IntervalMs"] = "10",
                })
                .Build();

            var service = CreateService(countingFake, lifetime.Object, config);
            using var cts = new CancellationTokenSource();
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runTask = Task.Run(async () =>
            {
                await service.ExecuteForTest(cts.Token);
                completed.TrySetResult();
            });

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (readCount[0] < 3 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert.True(readCount[0] >= 3, $"expected at least 3 successful polls, got {readCount[0]}");

            cts.Cancel();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        private sealed class CountingHoldingReadFake : IModbusService
        {
            private readonly FakeModbusService _inner;
            private readonly int[] _readCount;

            public CountingHoldingReadFake(FakeModbusService inner, int[] readCount)
            {
                _inner = inner;
                _readCount = readCount;
            }

            public bool IsConnected => _inner.IsConnected;
            public string BoundEndpoint => _inner.BoundEndpoint;
            public ModbusFrameLogger FrameLogger => _inner.FrameLogger;

            public Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
                => _inner.ConnectAsync(ipAddress, port, unitIds, cancellationToken);

            public Task DisconnectAsync() => _inner.DisconnectAsync();

            public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
                => _inner.RunDiagnosticsAsync(ipAddress, port, unitId);

            public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
            {
                Interlocked.Increment(ref _readCount[0]);
                return _inner.ReadHoldingRegistersAsync(unitId, startAddress, count);
            }

            public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
                => _inner.ReadInputRegistersAsync(unitId, startAddress, count);

            public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
                => _inner.WriteSingleRegisterAsync(unitId, registerAddress, value);

            public Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values)
                => _inner.WriteRegistersAsync(unitId, startAddress, values);

            public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
                => _inner.ReadCoilsAsync(unitId, startAddress, count);

            public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
                => _inner.ReadDiscreteInputsAsync(unitId, startAddress, count);

            public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
                => _inner.WriteSingleCoilAsync(unitId, coilAddress, value);

            public Task WriteCoilsAsync(byte unitId, int startAddress, bool[] values)
                => _inner.WriteCoilsAsync(unitId, startAddress, values);

            public Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask)
                => _inner.MaskWriteRegisterAsync(unitId, registerAddress, andMask, orMask);

            public Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues)
                => _inner.ReadWriteMultipleRegistersAsync(unitId, readStartAddress, readCount, writeStartAddress, writeValues);

            public Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic)
                => _inner.ReadDeviceIdentificationAsync(unitId, objectId, category);

            public void Dispose() => _inner.Dispose();

            public ValueTask DisposeAsync() => _inner.DisposeAsync();
        }
    }

    public class HeadlessCustomServiceTests
    {
        private static HeadlessCustomService CreateService(IModbusService modbus, IHostApplicationLifetime lifetime, IConfiguration configuration)
            => new HeadlessCustomService(modbus, null, lifetime, configuration, NullLogger<HeadlessCustomService>.Instance);

        [Fact]
        public async Task MissingCustomFile_StopsApplication_InsteadOfHanging()
        {
            var modbus = new FakeModbusService();
            var lifetime = new Mock<IHostApplicationLifetime>();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Custom:Path"] = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json"),
                })
                .Build();

            var service = CreateService(modbus, lifetime.Object, config);

            var runTask = service.ExecuteForTest(CancellationToken.None);
            await runTask.WaitAsync(TimeSpan.FromSeconds(10));

            lifetime.Verify(l => l.StopApplication(), Times.Once);
        }

        [Fact]
        public async Task MalformedCustomFile_StopsApplication_WithNoRawException()
        {
            var path = Path.Combine(Path.GetTempPath(), "malformed-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(path, "{ not valid json !!");

            try
            {
                var modbus = new FakeModbusService();
                var lifetime = new Mock<IHostApplicationLifetime>();
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Custom:Path"] = path,
                    })
                    .Build();

                var service = CreateService(modbus, lifetime.Object, config);

                var runTask = service.ExecuteForTest(CancellationToken.None);
                await runTask.WaitAsync(TimeSpan.FromSeconds(10));

                lifetime.Verify(l => l.StopApplication(), Times.Once);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
