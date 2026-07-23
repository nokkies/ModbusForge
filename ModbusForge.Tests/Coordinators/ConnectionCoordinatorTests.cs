using System.Threading;
using Moq;
using Xunit;
using ModbusForge.ViewModels.Coordinators;
using ModbusForge.Services;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Tests.Coordinators
{
    public class ConnectionCoordinatorTests
    {
        private readonly Mock<IModbusService> _mockClientService;
        private readonly Mock<IModbusService> _mockServerService;
        private readonly Mock<IConsoleLoggerService> _mockConsoleLogger;
        private readonly Mock<ILogger<ConnectionCoordinator>> _mockLogger;
        private readonly Mock<IRetryPolicyService> _mockRetryPolicyService;
        private readonly Mock<IValidationService> _mockValidationService;
        private readonly Mock<IErrorHandlingService> _mockErrorHandlingService;
        private readonly Mock<ICircuitBreakerService> _mockCircuitBreakerService;
        private readonly ConnectionCoordinator _coordinator;

        public ConnectionCoordinatorTests()
        {
            _mockClientService = new Mock<IModbusService>();
            _mockServerService = new Mock<IModbusService>();
            _mockConsoleLogger = new Mock<IConsoleLoggerService>();
            _mockLogger = new Mock<ILogger<ConnectionCoordinator>>();

            _mockRetryPolicyService = new Mock<IRetryPolicyService>();
            _mockRetryPolicyService.Setup(r => r.ExecuteWithRetryAsync(It.IsAny<System.Func<System.Threading.Tasks.Task<bool>>>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async (System.Func<System.Threading.Tasks.Task<bool>> op, string name, int max, int init, int maxD) => await op());
            _mockRetryPolicyService.Setup(r => r.ExecuteWithRetryAsync(It.IsAny<System.Func<System.Threading.Tasks.Task>>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async (System.Func<System.Threading.Tasks.Task> op, string name, int max, int init, int maxD) => await op());

            _mockValidationService = new Mock<IValidationService>();
            _mockValidationService.Setup(v => v.ValidateIpAddress(It.IsAny<string>())).Returns(ValidationResult.Success);
            _mockValidationService.Setup(v => v.ValidatePort(It.IsAny<int>())).Returns(ValidationResult.Success);

            _mockErrorHandlingService = new Mock<IErrorHandlingService>();
            _mockErrorHandlingService.Setup(e => e.HandleError(It.IsAny<System.Exception>(), It.IsAny<string>()))
                .Returns(new ErrorHandlingResult { UserMessage = "Error", RecoverySuggestion = "Suggestion" });

            _mockCircuitBreakerService = new Mock<ICircuitBreakerService>();
            _mockCircuitBreakerService.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<System.Func<System.Threading.Tasks.Task<bool>>>(), It.IsAny<CircuitBreakerConfig>()))
                .Returns(async (string name, System.Func<System.Threading.Tasks.Task<bool>> op, CircuitBreakerConfig cfg) => await op());
            _mockCircuitBreakerService.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<System.Func<System.Threading.Tasks.Task>>(), It.IsAny<CircuitBreakerConfig>()))
                .Returns(async (string name, System.Func<System.Threading.Tasks.Task> op, CircuitBreakerConfig cfg) => await op());

            _coordinator = new ConnectionCoordinator(
                _mockClientService.Object,
                _mockServerService.Object,
                _mockConsoleLogger.Object,
                _mockLogger.Object,
                _mockRetryPolicyService.Object,
                _mockValidationService.Object,
                _mockErrorHandlingService.Object,
                _mockCircuitBreakerService.Object);
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

            _mockClientService.Setup(s => s.ConnectAsync(serverAddress, port, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            await _coordinator.ConnectAsync(
                serverAddress,
                port,
                isServerMode,
                msg => statusMessage = msg,
                connected => connectedState = connected);

            // Assert
            _mockClientService.Verify(s => s.ConnectAsync(serverAddress, port, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
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

        [Fact]
        public async Task RunDiagnosticsAsync_WhenTcpAndModbusOk_ReturnsTrue()
        {
            // Arrange
            string serverAddress = "127.0.0.1";
            int port = 502;
            byte unitId = 1;
            string? statusMessage = null;

            var expectedResult = new ConnectionDiagnosticResult
            {
                TcpConnected = true,
                ModbusResponding = true,
                TcpLatencyMs = 10,
                ModbusLatencyMs = 20,
                LocalEndpoint = "127.0.0.1:12345",
                RemoteEndpoint = "127.0.0.1:502"
            };

            _mockClientService.Setup(s => s.RunDiagnosticsAsync(serverAddress, port, unitId))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _coordinator.RunDiagnosticsAsync(serverAddress, port, unitId, msg => statusMessage = msg);

            // Assert
            Assert.True(result);
            Assert.Equal("TCP: OK, Modbus: OK", statusMessage);
            _mockClientService.Verify(s => s.RunDiagnosticsAsync(serverAddress, port, unitId), Times.Once);
            _mockConsoleLogger.Verify(l => l.Log(It.Is<string>(s => s.Contains("OK"))), Times.AtLeastOnce);
        }

        [Fact]
        public async Task RunDiagnosticsAsync_WhenTcpFails_ReturnsFalse()
        {
            // Arrange
            string serverAddress = "127.0.0.1";
            int port = 502;
            byte unitId = 1;
            string? statusMessage = null;

            var expectedResult = new ConnectionDiagnosticResult
            {
                TcpConnected = false,
                ModbusResponding = false,
                TcpError = "Connection refused"
            };

            _mockClientService.Setup(s => s.RunDiagnosticsAsync(serverAddress, port, unitId))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _coordinator.RunDiagnosticsAsync(serverAddress, port, unitId, msg => statusMessage = msg);

            // Assert
            Assert.False(result);
            Assert.Equal("TCP: FAIL, Modbus: FAIL", statusMessage);
            _mockConsoleLogger.Verify(l => l.Log(It.Is<string>(s => s.Contains("Error: Connection refused"))), Times.Once);
        }

        [Fact]
        public async Task RunDiagnosticsAsync_WhenModbusFails_ReturnsFalse()
        {
            // Arrange
            string serverAddress = "127.0.0.1";
            int port = 502;
            byte unitId = 1;
            string? statusMessage = null;

            var expectedResult = new ConnectionDiagnosticResult
            {
                TcpConnected = true,
                ModbusResponding = false,
                ModbusError = "Timeout",
                TcpLatencyMs = 10,
                LocalEndpoint = "127.0.0.1:12345",
                RemoteEndpoint = "127.0.0.1:502"
            };

            _mockClientService.Setup(s => s.RunDiagnosticsAsync(serverAddress, port, unitId))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _coordinator.RunDiagnosticsAsync(serverAddress, port, unitId, msg => statusMessage = msg);

            // Assert
            Assert.False(result);
            Assert.Equal("TCP: OK, Modbus: FAIL", statusMessage);
            _mockConsoleLogger.Verify(l => l.Log(It.Is<string>(s => s.Contains("Error: Timeout"))), Times.Once);
        }

        [Fact]
        public async Task RunDiagnosticsAsync_WhenServiceThrowsException_ReturnsFalse()
        {
            // Arrange
            string serverAddress = "127.0.0.1";
            int port = 502;
            byte unitId = 1;
            string? statusMessage = null;

            var exception = new Exception("Test exception");
            _mockClientService.Setup(s => s.RunDiagnosticsAsync(serverAddress, port, unitId))
                .ThrowsAsync(exception);

            // Act
            var result = await _coordinator.RunDiagnosticsAsync(serverAddress, port, unitId, msg => statusMessage = msg);

            // Assert
            Assert.False(result);
            Assert.Contains("Test exception", statusMessage);
            _mockConsoleLogger.Verify(l => l.Log(It.Is<string>(s => s.Contains("Diagnostics error: Test exception"))), Times.Once);

            // Note: Verifying ILogger is tricky due to extension methods,
            // but we know it's injected and used based on other log operations.
            // We just verify it doesn't crash here.
        }

        [Fact]
        public async Task RunDiagnosticsAsync_WhenResultIsNull_HandlesNullReferenceException()
        {
            // Arrange
            string serverAddress = "127.0.0.1";
            int port = 502;
            byte unitId = 1;
            string? statusMessage = null;

            // Simulate the service returning null
            _mockClientService.Setup(s => s.RunDiagnosticsAsync(serverAddress, port, unitId))
                .ReturnsAsync((ConnectionDiagnosticResult)null!);

            // Act
            var result = await _coordinator.RunDiagnosticsAsync(serverAddress, port, unitId, msg => statusMessage = msg);

            // Assert
            Assert.False(result);
            // It will hit the catch block when it tries to access result.TcpConnected
            Assert.Contains("Diagnostics error", statusMessage);
            _mockConsoleLogger.Verify(l => l.Log(It.Is<string>(s => s.Contains("Diagnostics error"))), Times.AtLeastOnce);
        }

        [Fact]
        public async Task RunDiagnosticsAsync_WhenFailsButErrorStringsNull_DoesNotThrow()
        {
            // Arrange
            string serverAddress = "127.0.0.1";
            int port = 502;
            byte unitId = 1;
            string? statusMessage = null;

            var expectedResult = new ConnectionDiagnosticResult
            {
                TcpConnected = false,
                ModbusResponding = false,
                TcpError = null!,
                ModbusError = null!
            };

            _mockClientService.Setup(s => s.RunDiagnosticsAsync(serverAddress, port, unitId))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _coordinator.RunDiagnosticsAsync(serverAddress, port, unitId, msg => statusMessage = msg);

            // Assert
            Assert.False(result);
            Assert.Equal("TCP: FAIL, Modbus: FAIL", statusMessage);
            // Verify we hit the end of the method successfully
            _mockConsoleLogger.Verify(l => l.Log("=== Diagnostics Complete ==="), Times.Once);
        }
    }
}
