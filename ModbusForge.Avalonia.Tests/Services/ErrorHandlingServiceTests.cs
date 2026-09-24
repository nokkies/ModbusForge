using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.Services
{
    public class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Logs { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add((logLevel, exception, formatter(state, exception)));
        }
    }

    public class ErrorHandlingServiceTests
    {
        private readonly TestLogger<ErrorHandlingService> _testLogger;
        private readonly ErrorHandlingService _service;

        public ErrorHandlingServiceTests()
        {
            _testLogger = new TestLogger<ErrorHandlingService>();
            _service = new ErrorHandlingService(_testLogger);
        }

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ErrorHandlingService(null!));
        }

        [Fact]
        public void GetUserFriendlyMessage_NullException_ReturnsUnknownError()
        {
            var result = _service.GetUserFriendlyMessage(null!);
            Assert.Equal("An unknown error occurred.", result);
        }

        [Theory]
        [InlineData(SocketError.ConnectionRefused, "Connection refused. The server may not be running or the firewall is blocking the connection.")]
        [InlineData(SocketError.TimedOut, "Connection timed out. The server may be unreachable or too slow to respond.")]
        [InlineData(SocketError.HostNotFound, "Host not found. Please check the server address.")]
        [InlineData(SocketError.NetworkUnreachable, "Network unreachable. Please check your network connection.")]
        [InlineData(SocketError.ConnectionReset, "Connection was reset by the remote host.")]
        [InlineData(SocketError.AddressAlreadyInUse, "The port is already in use. Please choose a different port or stop the conflicting application.")]
        [InlineData(SocketError.AccessDenied, "Access denied. You may need administrator privileges to use this port.")]
        [InlineData(SocketError.Fault, "Network error: Fault")]
        public void GetUserFriendlyMessage_SocketException_ReturnsCorrectSocketErrorMessage(SocketError socketError, string expectedMessage)
        {
            var socketEx = new SocketException((int)socketError);
            var result = _service.GetUserFriendlyMessage(socketEx);
            Assert.Equal(expectedMessage, result);
        }

        [Fact]
        public void GetUserFriendlyMessage_IOException_ReturnsNetworkCommunicationError()
        {
            var ioEx = new IOException("IO stream failure");
            var result = _service.GetUserFriendlyMessage(ioEx);
            Assert.Equal("Network communication error. Please check your network connection.", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_TimeoutException_ReturnsTimedOutMessage()
        {
            var timeoutEx = new TimeoutException();
            var result = _service.GetUserFriendlyMessage(timeoutEx);
            Assert.Equal("Operation timed out. The server may be busy or unreachable.", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_UnauthorizedAccessException_ReturnsAccessDenied()
        {
            var unauthEx = new UnauthorizedAccessException();
            var result = _service.GetUserFriendlyMessage(unauthEx);
            Assert.Equal("Access denied. Please check your permissions.", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_ArgumentException_WithParamName_ReturnsFormattedMessage()
        {
            var argEx = new ArgumentException("Port out of range", "port");
            var result = _service.GetUserFriendlyMessage(argEx);
            Assert.Equal("Invalid parameter: port", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_ArgumentException_WithNullParamName_ReturnsUnknownParamNameMessage()
        {
            var argEx = new ArgumentException("Invalid parameter value");
            var result = _service.GetUserFriendlyMessage(argEx);
            Assert.Equal("Invalid parameter: unknown", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_InvalidOperationException_ReturnsInvalidOperationMessage()
        {
            var invOpEx = new InvalidOperationException();
            var result = _service.GetUserFriendlyMessage(invOpEx);
            Assert.Equal("Invalid operation. Please check your current state.", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_OverflowException_ReturnsNumericOverflowMessage()
        {
            var overflowEx = new OverflowException();
            var result = _service.GetUserFriendlyMessage(overflowEx);
            Assert.Equal("Numeric overflow occurred. Please check your input values.", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_FormatException_ReturnsInvalidFormatMessage()
        {
            var formatEx = new FormatException();
            var result = _service.GetUserFriendlyMessage(formatEx);
            Assert.Equal("Invalid format. Please check your input format.", result);
        }

        [Fact]
        public void GetUserFriendlyMessage_UnknownException_ReturnsExceptionMessage()
        {
            var genericEx = new InvalidCastException("Custom cast error message");
            var result = _service.GetUserFriendlyMessage(genericEx);
            Assert.Equal("Custom cast error message", result);
        }

        [Fact]
        public void GetRecoverySuggestion_NullException_ReturnsRestartApplicationSuggestion()
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
        [InlineData(SocketError.Fault, "Check your network configuration and try again.")]
        public void GetRecoverySuggestion_SocketException_ReturnsCorrectSocketSuggestion(SocketError socketError, string expectedSubstring)
        {
            var socketEx = new SocketException((int)socketError);
            var result = _service.GetRecoverySuggestion(socketEx);
            Assert.Contains(expectedSubstring, result);
        }

        [Fact]
        public void GetRecoverySuggestion_StandardExceptions_ReturnsExpectedSuggestions()
        {
            Assert.Equal("Check your network cable, Wi-Fi connection, and ensure the server is reachable.",
                _service.GetRecoverySuggestion(new IOException()));

            Assert.Equal("Try increasing the timeout period or check if the server is overloaded.",
                _service.GetRecoverySuggestion(new TimeoutException()));

            Assert.Equal("Run the application as administrator or check file/folder permissions.",
                _service.GetRecoverySuggestion(new UnauthorizedAccessException()));

            Assert.Equal("Review your input parameters and ensure they are within valid ranges.",
                _service.GetRecoverySuggestion(new ArgumentException()));

            Assert.Equal("Ensure you are in the correct state for this operation (e.g., connected before reading).",
                _service.GetRecoverySuggestion(new InvalidOperationException()));

            Assert.Equal("Use smaller numbers or check your calculations for overflow conditions.",
                _service.GetRecoverySuggestion(new OverflowException()));

            Assert.Equal("Ensure your input matches the expected format (e.g., numbers for numeric fields).",
                _service.GetRecoverySuggestion(new FormatException()));

            Assert.Equal("If the problem persists, please check the logs for more details or contact support.",
                _service.GetRecoverySuggestion(new Exception("Unknown exception")));
        }

        [Fact]
        public void HandleError_ValidException_PopulatesResultAndLogsError()
        {
            const string context = "ModbusReadContext";
            Exception exWithDetails;

            try
            {
                try
                {
                    throw new InvalidOperationException("Inner exception message");
                }
                catch (Exception inner)
                {
                    throw new IOException("Outer network error", inner);
                }
            }
            catch (Exception caughtEx)
            {
                exWithDetails = caughtEx;
            }

            var result = _service.HandleError(exWithDetails, context);

            Assert.NotNull(result);
            Assert.Equal("Network communication error. Please check your network connection.", result.UserMessage);
            Assert.Equal("Check your network cable, Wi-Fi connection, and ensure the server is reachable.", result.RecoverySuggestion);

            Assert.Contains($"Context: {context}", result.TechnicalDetails);
            Assert.Contains($"Exception Type: {nameof(IOException)}", result.TechnicalDetails);
            Assert.Contains("Message: Outer network error", result.TechnicalDetails);
            Assert.Contains($"Inner Exception: {nameof(InvalidOperationException)}", result.TechnicalDetails);
            Assert.Contains("Inner Message: Inner exception message", result.TechnicalDetails);
            Assert.Contains("Stack Trace:", result.TechnicalDetails);

            // Verify LogError was invoked on ILogger
            Assert.Single(_testLogger.Logs);
            var log = _testLogger.Logs[0];
            Assert.Equal(LogLevel.Error, log.Level);
            Assert.Same(exWithDetails, log.Exception);
            Assert.Contains(context, log.Message);
            Assert.Contains(exWithDetails.Message, log.Message);
        }

        [Theory]
        [InlineData(typeof(SocketException), SocketError.ConnectionRefused, true, false)]
        [InlineData(typeof(SocketException), SocketError.TimedOut, true, true)]
        [InlineData(typeof(SocketException), SocketError.ConnectionReset, true, true)]
        [InlineData(typeof(IOException), SocketError.Success, true, true)]
        [InlineData(typeof(TimeoutException), SocketError.Success, true, true)]
        [InlineData(typeof(InvalidOperationException), SocketError.Success, true, false)]
        [InlineData(typeof(ArgumentException), SocketError.Success, false, false)]
        [InlineData(typeof(Exception), SocketError.Success, false, false)]
        public void HandleError_IsRecoverableAndShouldRetry_CorrectForDifferentExceptions(Type exceptionType, SocketError socketError, bool expectedRecoverable, bool expectedRetry)
        {
            Exception ex;
            if (exceptionType == typeof(SocketException))
            {
                ex = new SocketException((int)socketError);
            }
            else
            {
                ex = (Exception)Activator.CreateInstance(exceptionType)!;
            }

            var result = _service.HandleError(ex, "TestContext");

            Assert.Equal(expectedRecoverable, result.IsRecoverable);
            Assert.Equal(expectedRetry, result.ShouldRetry);
        }
    }
}
