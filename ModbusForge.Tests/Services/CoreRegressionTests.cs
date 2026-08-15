using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Tests.Services
{
    public class PollingEngineErrorReportingTests
    {
        private sealed class ConfigurableStubService : IModbusService
        {
            public ushort[]? NextHoldingRead { get; set; } = new ushort[] { 1, 2, 3 };

            public bool IsConnected => true;
            public string BoundEndpoint => "stub";
            public ModbusFrameLogger FrameLogger { get; } = new ModbusFrameLogger(10);

            public Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task DisconnectAsync() => Task.CompletedTask;

            public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
                => Task.FromResult(new ConnectionDiagnosticResult());

            public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
                => Task.FromResult(NextHoldingRead);

            public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<ushort[]?>(null);

            public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<bool[]?>(null);

            public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<bool[]?>(null);

            public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value) => Task.CompletedTask;
            public Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values) => Task.CompletedTask;
            public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value) => Task.CompletedTask;
            public Task WriteCoilsAsync(byte unitId, int startAddress, bool[] values) => Task.CompletedTask;
            public Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask)
                => Task.FromResult<ushort?>(null);
            public Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues)
                => Task.FromResult<ushort[]?>(null);
            public Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic)
                => Task.FromResult<DeviceIdentification?>(null);

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        [Fact]
        public async Task NullRead_IsReportedAsError_InsteadOfEmptySuccess()
        {
            var service = new ConfigurableStubService { NextHoldingRead = null };
            using var engine = new PollingEngine(service, service, NullLogger<PollingEngine>.Instance);
            engine.Start();

            engine.Enqueue(new PollingCommand
            {
                Area = PlcArea.HoldingRegister,
                UnitId = 1,
                StartAddress = 0,
                Count = 3,
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await engine.Results.ReadAsync(cts.Token).AsTask();

            Assert.True(result.IsError, "a null read (no device response) must be surfaced as an error");
            Assert.Contains("No response", result.ErrorMessage);
        }

        [Fact]
        public async Task SuccessfulRead_IsReportedWithValues()
        {
            var service = new ConfigurableStubService { NextHoldingRead = new ushort[] { 7, 8, 9 } };
            using var engine = new PollingEngine(service, service, NullLogger<PollingEngine>.Instance);
            engine.Start();

            engine.Enqueue(new PollingCommand
            {
                Area = PlcArea.HoldingRegister,
                UnitId = 1,
                StartAddress = 0,
                Count = 3,
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await engine.Results.ReadAsync(cts.Token).AsTask();

            Assert.False(result.IsError);
            Assert.Equal(new ushort[] { 7, 8, 9 }, result.Values);
        }
    }

    public class ScriptRuleServiceComparisonTests
    {
        [Theory]
        [InlineData((ushort)5, "5", true)]
        [InlineData((ushort)5, 5.0, true)]
        [InlineData((ushort)5, 6.0, false)]
        [InlineData((ushort)65535, "65535", true)]
        public void ValuesEqual_RegisterValues_CompareNumerically(object registerValue, object triggerValue, bool expected)
        {
            Assert.Equal(expected, ScriptRuleService.ValuesEqual(registerValue, triggerValue));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, "true", false)]
        public void ValuesEqual_BoolValues_CompareAsBools(bool a, object b, bool expected)
        {
            Assert.Equal(expected, ScriptRuleService.ValuesEqual(a, b));
        }

        [Fact]
        public void ValuesEqual_NonNumericStrings_FallBackToObjectEquals()
        {
            Assert.True(ScriptRuleService.ValuesEqual("hello", "hello"));
            Assert.False(ScriptRuleService.ValuesEqual("hello", "world"));
        }
    }

    /// <summary>
    /// Guards the critical fix: a TCP device that accepts the connection but never answers
    /// must time out (~5 s I/O timeout) instead of hanging the read forever.
    /// </summary>
    public class ModbusTcpTimeoutTests
    {
        [Fact]
        public async Task Read_Completes_WhenDeviceAcceptsTcpButNeverResponds()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Accept the TCP connection, then hold it open and never send anything.
            var accepted = Task.Run(async () =>
            {
                var client = await listener.AcceptTcpClientAsync();
                await Task.Delay(TimeSpan.FromSeconds(60));
                client.Dispose();
            });

            var service = new ModbusTcpService(NullLogger<ModbusTcpService>.Instance);
            var connected = await service.ConnectAsync("127.0.0.1", port, "1");
            Assert.True(connected, "the TCP handshake itself should succeed");

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await service.ReadHoldingRegistersAsync(1, 0, 1);
                stopwatch.Stop();

                Assert.True(result is null, "a device that never answers must produce a null read result");
                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                    $"read should fail with the ~5s I/O timeout, but took {stopwatch.Elapsed}");
            }
            finally
            {
                await service.DisposeAsync();
                listener.Stop();
            }
        }
    }
}
