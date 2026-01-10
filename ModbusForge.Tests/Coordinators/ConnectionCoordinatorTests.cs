using Moq;
using Xunit;
using ModbusForge.ViewModels.Coordinators;
using ModbusForge.Services;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Tests.Coordinators
{
    public class ConnectionCoordinatorTests
    {
        private readonly Mock<ModbusTcpService> _mockClientService;
        private readonly Mock<ModbusServerService> _mockServerService;
        private readonly Mock<IConsoleLoggerService> _mockConsoleLogger;
        private readonly Mock<ILogger<ConnectionCoordinator>> _mockLogger;
        private readonly ConnectionCoordinator _coordinator;

        public ConnectionCoordinatorTests()
        {
            _mockClientService = new Mock<ModbusTcpService>(MockBehavior.Loose);
            _mockServerService = new Mock<ModbusServerService>(MockBehavior.Loose, null!, null!);
            _mockConsoleLogger = new Mock<IConsoleLoggerService>();
            _mockLogger = new Mock<ILogger<ConnectionCoordinator>>();
            
            _coordinator = new ConnectionCoordinator(
                _mockClientService.Object,
                _mockServerService.Object,
                _mockConsoleLogger.Object,
                _mockLogger.Object);
        }

        [Fact]
        public void CanConnect_WhenNotConnected_ReturnsTrue()
        {
            // Arrange
            bool isConnected = false;

            // Act
            var result = _coordinator.CanConnect(isConnected);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CanConnect_WhenConnected_ReturnsFalse()
        {
            // Arrange
            bool isConnected = true;

            // Act
            var result = _coordinator.CanConnect(isConnected);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void CanDisconnect_WhenConnected_ReturnsTrue()
        {
            // Arrange
            bool isConnected = true;

            // Act
            var result = _coordinator.CanDisconnect(isConnected);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CanDisconnect_WhenNotConnected_ReturnsFalse()
        {
            // Arrange
            bool isConnected = false;

            // Act
            var result = _coordinator.CanDisconnect(isConnected);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ConnectAsync_ClientMode_CallsClientService()
        {
            // Arrange
            string serverAddress = "127.0.0.1";
            int port = 502;
            bool isServerMode = false;
            string? statusMessage = null;
            bool? connectedState = null;

            _mockClientService.Setup(s => s.ConnectAsync(serverAddress, port))
                .ReturnsAsync(true);

            // Act
            await _coordinator.ConnectAsync(
                serverAddress,
                port,
                isServerMode,
                msg => statusMessage = msg,
                connected => connectedState = connected);

            // Assert
            _mockClientService.Verify(s => s.ConnectAsync(serverAddress, port), Times.Once);
            Assert.NotNull(statusMessage);
            Assert.True(connectedState);
        }

        [Fact]
        public async Task DisconnectAsync_ClientMode_CallsClientService()
        {
            // Arrange
            bool isServerMode = false;
            string? statusMessage = null;
            bool? connectedState = null;

            _mockClientService.Setup(s => s.DisconnectAsync())
                .Returns(Task.CompletedTask);

            // Act
            await _coordinator.DisconnectAsync(
                isServerMode,
                msg => statusMessage = msg,
                connected => connectedState = connected);

            // Assert
            _mockClientService.Verify(s => s.DisconnectAsync(), Times.Once);
            Assert.NotNull(statusMessage);
            Assert.False(connectedState);
        }

        [Fact]
        public async Task DisconnectAsync_ServerMode_CallsServerService()
        {
            // Arrange
            bool isServerMode = true;
            string? statusMessage = null;
            bool? connectedState = null;

            _mockServerService.Setup(s => s.DisconnectAsync())
                .Returns(Task.CompletedTask);

            // Act
            await _coordinator.DisconnectAsync(
                isServerMode,
                msg => statusMessage = msg,
                connected => connectedState = connected);

            // Assert
            _mockServerService.Verify(s => s.DisconnectAsync(), Times.Once);
            Assert.NotNull(statusMessage);
            Assert.False(connectedState);
        }
    }
}
