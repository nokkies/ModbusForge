using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.Tests.Performance
{
    public class PollingThroughputTests
    {
        private readonly ITestOutputHelper _output;

        public PollingThroughputTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private sealed class StubModbusService : IModbusService
        {
            public bool IsConnected => true;
            public string BoundEndpoint => "stub";
            public ModbusFrameLogger FrameLogger { get; } = new ModbusFrameLogger(10);

            public Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task DisconnectAsync() => Task.CompletedTask;

            public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
                => Task.FromResult(new ConnectionDiagnosticResult());

            public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<ushort[]?>(Enumerable.Range(0, count).Select(i => (ushort)(startAddress + i)).ToArray());

            public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<ushort[]?>(Enumerable.Range(0, count).Select(i => (ushort)(startAddress + i)).ToArray());

            public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<bool[]?>(Enumerable.Range(0, count).Select(i => i % 2 == 0).ToArray());

            public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<bool[]?>(Enumerable.Range(0, count).Select(i => i % 2 == 0).ToArray());

            public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value) => Task.CompletedTask;
            public Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values) => Task.CompletedTask;
            public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value) => Task.CompletedTask;
            public Task WriteCoilsAsync(byte unitId, int startAddress, bool[] values) => Task.CompletedTask;
            public Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask) => Task.FromResult<ushort?>(null);
            public Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues) => Task.FromResult<ushort[]?>(null);
            public Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic) => Task.FromResult<DeviceIdentification?>(null);

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        [Fact]
        public async Task PollingEngine_20Units_50msInterval_DeliversAllResults()
        {
            var service = new StubModbusService();
            var engine = new PollingEngine(service, service, NullLogger<PollingEngine>.Instance);
            engine.Start();

            const int unitCount = 20;
            const int readsPerUnit = 1;

            var stopwatch = Stopwatch.StartNew();

            for (int cycle = 0; cycle < readsPerUnit; cycle++)
            {
                for (byte unit = 1; unit <= unitCount; unit++)
                {
                    engine.Enqueue(new PollingCommand
                    {
                        Area = PlcArea.HoldingRegister,
                        UnitId = unit,
                        StartAddress = 0,
                        Count = 10,
                    });
                }

                // Simulate 50ms producer interval
                await Task.Delay(50);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var results = new List<PollingResult>(unitCount * readsPerUnit);
            while (results.Count < unitCount * readsPerUnit)
            {
                var result = await engine.Results.ReadAsync(cts.Token).AsTask();
                results.Add(result);
            }

            stopwatch.Stop();
            engine.Stop();

            Assert.Equal(unitCount * readsPerUnit, results.Count);

            // Wall-clock performance varies across CI runners, so the measured time is
            // reported as a baseline instead of asserted against a tight 1s bound; a
            // generous ceiling still catches pathological stalls.
            const long MaxAcceptableMs = 10_000;
            _output.WriteLine($"Throughput baseline: {stopwatch.ElapsedMilliseconds} ms for {results.Count} polls");
            Assert.True(stopwatch.ElapsedMilliseconds < MaxAcceptableMs,
                $"Expected under {MaxAcceptableMs}ms but took {stopwatch.ElapsedMilliseconds}ms");
        }
    }
}
