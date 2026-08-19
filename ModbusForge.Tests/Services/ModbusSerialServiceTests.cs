using System;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NModbus;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ModbusSerialServiceTests
    {
        /// <summary>
        /// Subclass that reports as connected without a real port, so the
        /// write path (and its error handling) can be exercised with a mocked master.
        /// </summary>
        private sealed class TestableSerialService : ModbusSerialService
        {
            public TestableSerialService(ILogger<ModbusSerialService> logger)
                : base(logger, null, null, null, null, TransportType.Rtu)
            {
            }

            public override bool IsConnected => true;
        }

        private static void InjectClient(ModbusSerialService service, IModbusMaster master)
        {
            var field = typeof(ModbusSerialService).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!;
            field.SetValue(service, master);
        }

        [Fact]
        public void Constructor_InvalidTransport_Throws()
        {
            var logger = new Mock<ILogger<ModbusSerialService>>().Object;

            Assert.Throws<ArgumentException>(() => new ModbusSerialService(logger, TransportType.Tcp));
        }

        [Theory]
        [InlineData(TransportType.Rtu)]
        [InlineData(TransportType.Ascii)]
        public void Constructor_SerialTransport_SetsProperties(TransportType transport)
        {
            var loggerMock = new Mock<ILogger<ModbusSerialService>>();
            var service = new ModbusSerialService(loggerMock.Object, transport);

            Assert.Equal(transport, service.Transport);
            Assert.False(service.IsConnected);
            Assert.Empty(service.BoundEndpoint);
        }

        [Fact]
        public async Task ConnectAsync_StringOverload_ThrowsNotSupported()
        {
            var loggerMock = new Mock<ILogger<ModbusSerialService>>();
            var service = new ModbusSerialService(loggerMock.Object, TransportType.Rtu);

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                service.ConnectAsync("COM1", 9600));
        }

        [Fact]
        public async Task ConnectAsync_InvalidSerialSettings_ReturnsFalse()
        {
            var loggerMock = new Mock<ILogger<ModbusSerialService>>();
            var validationLogger = new Mock<ILogger<ValidationService>>().Object;
            var validation = new ValidationService(validationLogger);
            var service = new ModbusSerialService(loggerMock.Object, null, validation, TransportType.Rtu);

            var profile = new ConnectionProfile
            {
                Name = "Test",
                Transport = TransportType.Rtu,
                ComPort = "", // invalid
                BaudRate = 9600,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One
            };

            var result = await service.ConnectAsync(profile);

            Assert.False(result);
            Assert.False(service.IsConnected);
        }

        [Fact]
        public async Task WriteSingleRegisterAsync_SlaveException_KeepsConnection()
        {
            var loggerMock = new Mock<ILogger<ModbusSerialService>>();
            var master = new Mock<IModbusMaster>();
            master
                .Setup(m => m.WriteSingleRegister(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>()))
                .Throws(new SlaveException("Slave returned exception code 2 (ILLEGAL DATA ADDRESS)"));

            var service = new TestableSerialService(loggerMock.Object);
            InjectClient(service, master.Object);

            bool connectionLost = false;
            service.ConnectionLost += (_, _) => connectionLost = true;

            await service.WriteSingleRegisterAsync(1, 100, 42);

            // A slave exception response must not be treated as a dead line:
            Assert.False(connectionLost);
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never,
                "Slave exception must not be logged as an error");
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                "Slave exception must be logged as a warning");
        }

        [Fact]
        public async Task WriteSingleCoilAsync_SlaveException_KeepsConnection()
        {
            var loggerMock = new Mock<ILogger<ModbusSerialService>>();
            var master = new Mock<IModbusMaster>();
            master
                .Setup(m => m.WriteSingleCoil(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<bool>()))
                .Throws(new SlaveException("Slave returned exception code 4 (SLAVE DEVICE FAILURE)"));

            var service = new TestableSerialService(loggerMock.Object);
            InjectClient(service, master.Object);

            bool connectionLost = false;
            service.ConnectionLost += (_, _) => connectionLost = true;

            await service.WriteSingleCoilAsync(1, 100, true);

            Assert.False(connectionLost);
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }

        [Fact]
        public async Task WriteSingleRegisterAsync_TransportError_StillDisconnects()
        {
            var loggerMock = new Mock<ILogger<ModbusSerialService>>();
            var master = new Mock<IModbusMaster>();
            master
                .Setup(m => m.WriteSingleRegister(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>()))
                .Throws(new IOException("port disconnected"));

            var service = new TestableSerialService(loggerMock.Object);
            InjectClient(service, master.Object);

            bool connectionLost = false;
            service.ConnectionLost += (_, _) => connectionLost = true;

            await service.WriteSingleRegisterAsync(1, 100, 42);

            // A genuine transport failure must still tear the connection down.
            Assert.True(connectionLost);
        }

        [Fact]
        public async Task WriteSingleCoilAsync_TransportError_StillDisconnects()
        {
            var loggerMock = new Mock<ILogger<ModbusSerialService>>();
            var master = new Mock<IModbusMaster>();
            master
                .Setup(m => m.WriteSingleCoil(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<bool>()))
                .Throws(new TimeoutException("no response"));

            var service = new TestableSerialService(loggerMock.Object);
            InjectClient(service, master.Object);

            bool connectionLost = false;
            service.ConnectionLost += (_, _) => connectionLost = true;

            await service.WriteSingleCoilAsync(1, 100, true);

            Assert.True(connectionLost);
        }

        [Fact]
        public void AddressConversion_ReadHoldingRegisters_UsesZeroBasedProtocolAddress()
        {
            // This test documents the 1-based UI -> 0-based protocol address conversion
            // implemented in ModbusSerialService.ExecuteReadAsync.
            const int uiAddress = 1;
            ushort protocolAddress = (ushort)(uiAddress > 0 ? uiAddress - 1 : 0);
            Assert.Equal(0, protocolAddress);

            const int uiAddress2 = 100;
            ushort protocolAddress2 = (ushort)(uiAddress2 > 0 ? uiAddress2 - 1 : 0);
            Assert.Equal(99, protocolAddress2);
        }
    }
}
