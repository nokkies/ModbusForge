using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ErrorHandlingServiceTests
    {
        private readonly Mock<ILogger<ErrorHandlingService>> _mockLogger;
        private readonly ErrorHandlingService _service;

        public ErrorHandlingServiceTests()
        {
            _mockLogger = new Mock<ILogger<ErrorHandlingService>>();
            _service = new ErrorHandlingService(_mockLogger.Object);
        }

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ErrorHandlingService(null!));
        }

        [Fact]
        public void GetRecoverySuggestion_NullException_ReturnsRestartSuggestion()
        {
            var result = _service.GetRecoverySuggestion(null!);
            Assert.Equal("Please restart the application and try again.", result);
        }

        [Theory]
        [InlineData(SocketError.ConnectionRefused, "Verify the Modbus server is running")]
        [InlineData(SocketError.TimedOut, "Check network connectivity to the server")]
        [InlineData(SocketError.HostNotFound, "Verify the server IP address is correct")]
        [InlineData(SocketError.NetworkUnreachable, "Check your network connection")]
        [InlineData(SocketError.ConnectionReset, "The server may have restarted")]
        [InlineData(SocketError.AddressAlreadyInUse, "Use a different port")]
        [InlineData(SocketError.AccessDenied, "Run the application as administrator")]
        [InlineData(SocketError.Shutdown, "Check your network configuration and try again.")]
        public void GetRecoverySuggestion_SocketExceptionCodes_ReturnsExpectedRecoverySuggestion(SocketError socketError, string expectedSubstring)
        {
            var ex = new SocketException((int)socketError);
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Contains(expectedSubstring, result);
        }

        [Fact]
        public void GetRecoverySuggestion_IOException_ReturnsNetworkCableSuggestion()
        {
            var ex = new IOException("IO failure");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("Check your network cable, Wi-Fi connection, and ensure the server is reachable.", result);
        }

        [Fact]
        public void GetRecoverySuggestion_TimeoutException_ReturnsTimeoutSuggestion()
        {
            var ex = new TimeoutException("Operation timed out");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("Try increasing the timeout period or check if the server is overloaded.", result);
        }

        [Fact]
        public void GetRecoverySuggestion_UnauthorizedAccessException_ReturnsAdminSuggestion()
        {
            var ex = new UnauthorizedAccessException("Access denied");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("Run the application as administrator or check file/folder permissions.", result);
        }

        [Fact]
        public void GetRecoverySuggestion_ArgumentException_ReturnsInputParameterSuggestion()
        {
            var ex = new ArgumentException("Invalid argument", "testParam");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("Review your input parameters and ensure they are within valid ranges.", result);
        }

        [Fact]
        public void GetRecoverySuggestion_ArgumentNullException_ReturnsInputParameterSuggestion()
        {
            var ex = new ArgumentNullException("testParam");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("Review your input parameters and ensure they are within valid ranges.", result);
        }

        [Fact]
        public void GetRecoverySuggestion_InvalidOperationException_ReturnsStateSuggestion()
        {
            var ex = new InvalidOperationException("Not connected");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("Ensure you are in the correct state for this operation (e.g., connected before reading).", result);
        }

        [Fact]
        public void GetRecoverySuggestion_OverflowException_ReturnsOverflowSuggestion()
        {
            var ex = new OverflowException("Value too large");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("Use smaller numbers or check your calculations for overflow conditions.", result);
        }

        [Fact]
        public void GetRecoverySuggestion_FormatException_ReturnsFormatSuggestion()
        {
            var ex = new FormatException("Bad format");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("Ensure your input matches the expected format (e.g., numbers for numeric fields).", result);
        }

        [Fact]
        public void GetRecoverySuggestion_UnhandledExceptionTypes_ReturnsFallbackSuggestion()
        {
            var ex = new InvalidCastException("Invalid cast");
            var result = _service.GetRecoverySuggestion(ex);
            Assert.Equal("If the problem persists, please check the logs for more details or contact support.", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_NullException_ReturnsUnknownError()
        {
            var result = _service.GetUserFriendlyMessage(null!);
            Assert.Equal("An unknown error occurred.", result);
        }

        [Theory]
        [InlineData(SocketError.ConnectionRefused, "Connection refused")]
        [InlineData(SocketError.TimedOut, "Connection timed out")]
        [InlineData(SocketError.HostNotFound, "Host not found")]
        [InlineData(SocketError.NetworkUnreachable, "Network unreachable")]
        [InlineData(SocketError.ConnectionReset, "Connection was reset")]
        [InlineData(SocketError.AddressAlreadyInUse, "The port is already in use")]
        [InlineData(SocketError.AccessDenied, "Access denied")]
        [InlineData(SocketError.AlreadyInProgress, "Network error: AlreadyInProgress")]
        public void GetUserFriendlyMessage_SocketExceptionCodes_ReturnsExpectedMessage(SocketError socketError, string expectedSubstring)
        {
            var ex = new SocketException((int)socketError);
            var result = _service.GetUserFriendlyMessage(ex);
            Assert.Contains(expectedSubstring, result);
        }

        [Fact]
        public void GetUserFriendlyMessage_KnownExceptionTypes_ReturnsExpectedUserMessage()
        {
            Assert.Equal("Network communication error. Please check your network connection.",
                _service.GetUserFriendlyMessage(new IOException("IO error")));

            Assert.Equal("Operation timed out. The server may be busy or unreachable.",
                _service.GetUserFriendlyMessage(new TimeoutException("Timeout")));

            Assert.Equal("Access denied. Please check your permissions.",
                _service.GetUserFriendlyMessage(new UnauthorizedAccessException("Denied")));

            Assert.Equal("Invalid parameter: myParam",
                _service.GetUserFriendlyMessage(new ArgumentException("Error message", "myParam")));

            Assert.Equal("Invalid operation. Please check your current state.",
                _service.GetUserFriendlyMessage(new InvalidOperationException("Invalid state")));

            Assert.Equal("Numeric overflow occurred. Please check your input values.",
                _service.GetUserFriendlyMessage(new OverflowException("Overflow")));

            Assert.Equal("Invalid format. Please check your input format.",
                _service.GetUserFriendlyMessage(new FormatException("Format error")));

            var customEx = new InvalidProgramException("Custom exception message");
            Assert.Equal("Custom exception message", _service.GetUserFriendlyMessage(customEx));
        }

        [Fact]
        public void HandleError_ValidException_PopulatesResultAndLogsError()
        {
            var innerEx = new Exception("Inner exception detail");
            var ex = new InvalidOperationException("Outer exception detail", innerEx);
            var context = "TestOperationContext";

            var result = _service.HandleError(ex, context);

            Assert.NotNull(result);
            Assert.Equal("Invalid operation. Please check your current state.", result.UserMessage);
            Assert.Equal("Ensure you are in the correct state for this operation (e.g., connected before reading).", result.RecoverySuggestion);
            Assert.True(result.IsRecoverable);
            Assert.False(result.ShouldRetry);

            Assert.Contains($"Context: {context}", result.TechnicalDetails);
            Assert.Contains($"Exception Type: {nameof(InvalidOperationException)}", result.TechnicalDetails);
            Assert.Contains("Outer exception detail", result.TechnicalDetails);
            Assert.Contains($"Inner Exception: Exception", result.TechnicalDetails);
            Assert.Contains("Inner exception detail", result.TechnicalDetails);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("TestOperationContext")),
                    ex,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Theory]
        [InlineData(SocketError.TimedOut, true, true)]
        [InlineData(SocketError.ConnectionReset, true, true)]
        [InlineData(SocketError.ConnectionRefused, true, false)]
        public void HandleError_SocketException_SetsRecoverableAndRetryFlags(SocketError socketError, bool expectedRecoverable, bool expectedRetry)
        {
            var ex = new SocketException((int)socketError);
            var result = _service.HandleError(ex, "SocketTestContext");

            Assert.Equal(expectedRecoverable, result.IsRecoverable);
            Assert.Equal(expectedRetry, result.ShouldRetry);
        }
    }
}
