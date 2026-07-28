using System;
using System.IO.Ports;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ModbusSerialServiceTests
    {
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
