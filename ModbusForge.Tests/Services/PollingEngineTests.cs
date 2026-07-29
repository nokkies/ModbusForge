using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class PollingEngineTests
    {
        private static PollingEngine CreateEngine(IModbusService? client = null, IModbusService? server = null)
        {
            client ??= CreateServiceMock().Object;
            server ??= CreateServiceMock().Object;
            return new PollingEngine(client, server, NullLogger<PollingEngine>.Instance);
        }

        private static Mock<IModbusService> CreateServiceMock()
        {
            var mock = new Mock<IModbusService>();
            mock.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((byte unitId, int start, int count) => Enumerable.Range(0, count).Select(i => (ushort)(start + i)).ToArray());
            mock.Setup(s => s.ReadInputRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((byte unitId, int start, int count) => Enumerable.Range(0, count).Select(i => (ushort)(start + i)).ToArray());
            mock.Setup(s => s.ReadCoilsAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((byte unitId, int start, int count) => Enumerable.Range(0, count).Select(i => i % 2 == 0).ToArray());
            mock.Setup(s => s.ReadDiscreteInputsAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((byte unitId, int start, int count) => Enumerable.Range(0, count).Select(i => i % 2 != 0).ToArray());
            return mock;
        }

        [Fact]
        public async Task Enqueue_ExecutesReadAndPublishesResult()
        {
            var engine = CreateEngine();
            engine.Start();

            engine.Enqueue(new PollingCommand
            {
                Area = PlcArea.HoldingRegister,
                UnitId = 1,
                StartAddress = 10,
                Count = 5,
                IsServerMode = false,
            });

            var result = await engine.Results.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            engine.Stop();

            Assert.Equal(PlcArea.HoldingRegister, result.Area);
            Assert.Equal(1, result.UnitId);
            Assert.Equal(10, result.StartAddress);
            Assert.Equal(5, result.Values?.Length);
            Assert.Equal((ushort)10, result.Values?[0] ?? 0);
            Assert.False(result.IsError);
        }

        [Fact]
        public async Task Enqueue_CoalescesDuplicateAreaCommands()
        {
            var clientMock = CreateServiceMock();
            var engine = CreateEngine(clientMock.Object);

            // Enqueue both before starting so the worker snapshots once and only the latest remains.
            engine.Enqueue(new PollingCommand { Area = PlcArea.HoldingRegister, UnitId = 1, StartAddress = 0, Count = 2, IsServerMode = false });
            engine.Enqueue(new PollingCommand { Area = PlcArea.HoldingRegister, UnitId = 1, StartAddress = 0, Count = 4, IsServerMode = false });

            engine.Start();

            var result = await engine.Results.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            engine.Stop();

            Assert.Equal(4, result.Values?.Length);
            clientMock.Verify(s => s.ReadHoldingRegistersAsync(1, 0, 4), Times.Once);
            clientMock.Verify(s => s.ReadHoldingRegistersAsync(1, 0, 2), Times.Never);
        }

        [Fact]
        public async Task Execute_PublishesErrorOnFaultedRead()
        {
            var clientMock = CreateServiceMock();
            clientMock.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Unit offline"));

            var engine = CreateEngine(clientMock.Object);
            var errorFired = new TaskCompletionSource<PollingErrorEventArgs>();
            engine.Error += (s, e) => errorFired.TrySetResult(e);
            engine.Start();

            engine.Enqueue(new PollingCommand { Area = PlcArea.HoldingRegister, UnitId = 1, StartAddress = 0, Count = 1, IsServerMode = false });

            var error = await errorFired.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var result = await engine.Results.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            engine.Stop();

            Assert.True(result.IsError);
            Assert.Equal("Unit offline", result.ErrorMessage);
            Assert.Equal(PlcArea.HoldingRegister, error.Command.Area);
        }
    }
}
