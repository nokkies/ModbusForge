using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests
{
    /// <summary>
    /// Verifies the TCP service's logging hygiene: write values are sent to the device
    /// (the mocked NModbus master) but never appear in log messages. Uses the internal
    /// test-seam constructor instead of reflection, so a renamed field fails the build
    /// rather than silently no-opping the injection.
    /// </summary>
    public class ModbusServiceTests : IDisposable
    {
        private readonly Mock<ILogger<ModbusTcpService>> _loggerMock;
        private readonly Mock<NModbus.IModbusMaster> _modbusMasterMock;
        private readonly ModbusTcpService _service;
        private readonly TcpListener _listener;
        private readonly TcpClient _tcpClient;

        public ModbusServiceTests()
        {
            _loggerMock = new Mock<ILogger<ModbusTcpService>>();
            _modbusMasterMock = new Mock<NModbus.IModbusMaster>();

            // Local TCP listener + connected client so IsConnected reports true.
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _tcpClient = new TcpClient();
            _tcpClient.Connect(IPAddress.Loopback, port);

            _service = new ModbusTcpService(
                _loggerMock.Object,
                consoleLoggerService: null,
                frameLogger: null,
                addressValidator: null,
                master: _modbusMasterMock.Object,
                tcpClient: _tcpClient);

            Assert.True(_service.IsConnected, "Seam-injected connection must report connected");
        }

        [Fact]
        public async Task WriteSingleRegisterAsync_SendsValueToMaster_DoesNotLogValue()
        {
            // Arrange
            const byte unitId = 1;
            const int uiAddress = 100; // 1-based UI address
            const ushort sensitiveValue = 12345;

            // Act
            await _service.WriteSingleRegisterAsync(unitId, uiAddress, sensitiveValue);

            // The write must reach the device ... (NModbus protocol address is 0-based)
            _modbusMasterMock.Verify(
                m => m.WriteSingleRegister(unitId, (ushort)(uiAddress - 1), sensitiveValue),
                Times.Once,
                "WriteSingleRegisterAsync must forward the value to the master");

            // ... and the sensitive value must NOT appear in any log message.
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains(sensitiveValue.ToString())),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never,
                "Log message should NOT contain sensitive value '12345'");

            // And a debug log actually happened (logging was not removed entirely).
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task WriteSingleCoilAsync_SendsValueToMaster_DoesNotLogValue()
        {
            // Arrange
            const byte unitId = 1;
            const int uiAddress = 100;
            const bool sensitiveValue = true;

            // Act
            await _service.WriteSingleCoilAsync(unitId, uiAddress, sensitiveValue);

            // The write must reach the device.
            _modbusMasterMock.Verify(
                m => m.WriteSingleCoil(unitId, (ushort)(uiAddress - 1), sensitiveValue),
                Times.Once,
                "WriteSingleCoilAsync must forward the value to the master");

            // "True" as a standalone token must not appear in a debug log message.
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null &&
                        System.Text.RegularExpressions.Regex.IsMatch(v.ToString()!, @"\bTrue\b")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never,
                "Log message should NOT contain sensitive value 'True'");

            // And a debug log actually happened.
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        public void Dispose()
        {
            _service?.Dispose();
            _tcpClient?.Dispose();
            _listener?.Stop();
        }
    }
}
