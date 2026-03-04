using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Modbus.Device;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests
{
    public class ModbusServiceTests : IDisposable
    {
        private readonly Mock<ILogger<ModbusService>> _loggerMock;
        private readonly ModbusService _service;
        private readonly Mock<IModbusMaster> _modbusMasterMock;
        private readonly TcpListener _listener;
        private readonly TcpClient _tcpClient;

        public ModbusServiceTests()
        {
            _loggerMock = new Mock<ILogger<ModbusService>>();
            _service = new ModbusService(_loggerMock.Object);
            _modbusMasterMock = new Mock<IModbusMaster>();

            // Setup local TCP listener to allow connection
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // Create and connect TcpClient
            _tcpClient = new TcpClient();
            _tcpClient.Connect(IPAddress.Loopback, port);

            // Inject mocked fields using reflection
            var clientField = typeof(ModbusService).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance);
            if (clientField != null)
            {
                clientField.SetValue(_service, _modbusMasterMock.Object);
            }

            var tcpClientField = typeof(ModbusService).GetField("_tcpClient", BindingFlags.NonPublic | BindingFlags.Instance);
            if (tcpClientField != null)
            {
                tcpClientField.SetValue(_service, _tcpClient);
            }
        }

        [Fact]
        public async Task WriteSingleRegisterAsync_ShouldNotLogSensitiveValue()
        {
            // Arrange
            byte unitId = 1;
            int registerAddress = 100;
            ushort sensitiveValue = 12345;
            string sensitiveValueStr = sensitiveValue.ToString();

            // Act
            await _service.WriteSingleRegisterAsync(unitId, registerAddress, sensitiveValue);

            // Assert
            // Verify that NO log message contains the sensitive value
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(sensitiveValueStr)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never,
                $"Log message should NOT contain sensitive value '{sensitiveValueStr}'");
        }

        [Fact]
        public async Task WriteSingleCoilAsync_ShouldNotLogSensitiveValue()
        {
            // Arrange
            byte unitId = 1;
            int coilAddress = 100;
            bool sensitiveValue = true;
            string sensitiveValueStr = sensitiveValue.ToString();

            // Act
            await _service.WriteSingleCoilAsync(unitId, coilAddress, sensitiveValue);

            // Assert
            // Verify that NO log message contains the sensitive value
            // Note: "True" might be common, so this test might be flaky if "True" appears elsewhere in the log message unrelated to the value.
            // However, the current log message is: "Writing coil at {coilAddress} = {value} (Unit ID: {unitId})"
            // and "Successfully wrote coil {coilAddress} with value {value}"
            // If we remove {value}, "True" should disappear from these messages.

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(sensitiveValueStr)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never,
                $"Log message should NOT contain sensitive value '{sensitiveValueStr}'");
        }

        public void Dispose()
        {
            _service.Dispose();
            _tcpClient.Dispose();
            _listener.Stop();
        }
    }
}
