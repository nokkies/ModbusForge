using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ModbusForge.Models;
using ModbusForge.Services;
using NModbus;
using NModbus.Device;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ModbusChunkedExecutorTests
    {
        private static readonly SemaphoreSlim IoLock = new(1, 1);

        [Fact]
        public async Task ReadAsync_Merges_Three_Chunks_For_Large_Holding_Register_Request()
        {
            var master = new Mock<IModbusMaster>();
            var seen = new List<(ushort Address, ushort Count)>();

            master
                .Setup(m => m.ReadHoldingRegisters(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>()))
                .Returns((byte slave, ushort start, ushort count) =>
                {
                    seen.Add((start, count));
                    return Enumerable.Range(start, count).Select(i => (ushort)i).ToArray();
                });

            var result = await ModbusChunkedExecutor.ReadAsync(
                () => true,
                IoLock,
                master.Object,
                new ModbusAddressValidator(),
                NullLogger.Instance,
                () => { },
                ui => (ushort)(ui > 0 ? ui - 1 : 0),
                1,
                startAddress: 1,
                count: 300,
                PlcArea.HoldingRegister,
                "Read",
                "Error",
                (client, protocolAddress, chunkCount) => client.ReadHoldingRegisters(1, protocolAddress, chunkCount));

            Assert.NotNull(result);
            Assert.Equal(300, result!.Length);
            Assert.Equal(3, seen.Count);
            Assert.Equal((0, 125), seen[0]);
            Assert.Equal((125, 125), seen[1]);
            Assert.Equal((250, 50), seen[2]);
        }

        [Fact]
        public async Task ReadAsync_Returns_Partial_Result_And_Stops_When_Chunk_Fails()
        {
            var master = new Mock<IModbusMaster>();
            int callCount = 0;

            master
                .Setup(m => m.ReadHoldingRegisters(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>()))
                .Returns(() =>
                {
                    callCount++;
                    if (callCount == 1)
                        return Enumerable.Range(0, 125).Select(i => (ushort)i).ToArray();
                    if (callCount == 2)
                        return Enumerable.Range(125, 125).Select(i => (ushort)i).ToArray();
                    // third chunk returns null to simulate a failure
                    return null!;
                });

            var result = await ModbusChunkedExecutor.ReadAsync(
                () => true,
                IoLock,
                master.Object,
                new ModbusAddressValidator(),
                NullLogger.Instance,
                () => { },
                ui => (ushort)(ui > 0 ? ui - 1 : 0),
                1,
                startAddress: 1,
                count: 400,
                PlcArea.HoldingRegister,
                "Read",
                "Error",
                (client, protocolAddress, chunkCount) => client.ReadHoldingRegisters(1, protocolAddress, chunkCount));

            Assert.NotNull(result);
            Assert.Equal(250, result!.Length);
        }

        [Fact]
        public async Task WriteAsync_Splits_Large_Register_Write_Into_Protocol_Sized_Chunks()
        {
            var master = new Mock<IModbusMaster>();
            var seen = new List<(ushort Address, ushort[] Values)>();

            master
                .Setup(m => m.WriteMultipleRegisters(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort[]>()))
                .Callback((byte slave, ushort start, ushort[] values) =>
                {
                    seen.Add((start, values));
                });

            var values = Enumerable.Range(1, 300).Select(i => (ushort)i).ToArray();

            await ModbusChunkedExecutor.WriteAsync(
                () => true,
                IoLock,
                master.Object,
                new ModbusAddressValidator(),
                NullLogger.Instance,
                () => { },
                ui => (ushort)(ui > 0 ? ui - 1 : 0),
                1,
                startAddress: 1,
                values,
                PlcArea.HoldingRegister,
                "Write",
                "Error",
                (client, protocolAddress, chunkValues) => client.WriteMultipleRegisters(1, protocolAddress, chunkValues));

            Assert.Equal(3, seen.Count);
            Assert.Equal((0, 123), (seen[0].Address, seen[0].Values.Length));
            Assert.Equal((123, 123), (seen[1].Address, seen[1].Values.Length));
            Assert.Equal((246, 54), (seen[2].Address, seen[2].Values.Length));
            Assert.Equal(values, seen.SelectMany(x => x.Values).ToArray());
        }
    }
}
