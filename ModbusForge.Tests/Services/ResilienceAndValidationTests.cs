using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ResilienceAndValidationTests
    {
        [Fact]
        public void ValidationService_ValidateIpAddress_CorrectFormats()
        {
            var logger = new Mock<ILogger<ValidationService>>().Object;
            var service = new ValidationService(logger);

            Assert.True(service.ValidateIpAddress("127.0.0.1").IsValid);
            Assert.True(service.ValidateIpAddress("localhost").IsValid);
            Assert.True(service.ValidateIpAddress("0.0.0.0").IsValid);
            Assert.True(service.ValidateIpAddress("2001:db8::ff00:42:8329").IsValid); // IPv6

            Assert.False(service.ValidateIpAddress("").IsValid);
            Assert.False(service.ValidateIpAddress("999.999.999.999").IsValid);
            Assert.False(service.ValidateIpAddress("invalid-ip").IsValid);
        }

        [Fact]
        public void ValidationService_ValidatePort_Boundaries()
        {
            var logger = new Mock<ILogger<ValidationService>>().Object;
            var service = new ValidationService(logger);

            Assert.True(service.ValidatePort(502).IsValid);
            Assert.True(service.ValidatePort(1502).IsValid);
            Assert.True(service.ValidatePort(1).IsValid);
            Assert.True(service.ValidatePort(65535).IsValid);

            Assert.False(service.ValidatePort(0).IsValid);
            Assert.False(service.ValidatePort(-5).IsValid);
            Assert.False(service.ValidatePort(65536).IsValid);
        }

        [Fact]
        public void ValidationService_ValidateUnitId_Boundaries()
        {
            var logger = new Mock<ILogger<ValidationService>>().Object;
            var service = new ValidationService(logger);

            Assert.True(service.ValidateUnitId(1).IsValid);
            Assert.True(service.ValidateUnitId(240).IsValid);
            Assert.True(service.ValidateUnitId(0).IsValid); // Broadcast is valid with warning

            Assert.False(service.ValidateUnitId(247).IsValid); // Reserved
            Assert.False(service.ValidateUnitId(250).IsValid); // Reserved
        }

        [Fact]
        public void ValidationService_ValidateRegisterCount_Limits()
        {
            var logger = new Mock<ILogger<ValidationService>>().Object;
            var service = new ValidationService(logger);

            Assert.True(service.ValidateRegisterCount(1).IsValid);
            Assert.True(service.ValidateRegisterCount(125).IsValid);

            Assert.False(service.ValidateRegisterCount(0).IsValid);
            Assert.False(service.ValidateRegisterCount(126).IsValid);
        }

        [Fact]
        public void ValidationService_ValidateConnectionString_Formats()
        {
            var logger = new Mock<ILogger<ValidationService>>().Object;
            var service = new ValidationService(logger);

            Assert.True(service.ValidateConnectionString("127.0.0.1:502").IsValid);
            Assert.True(service.ValidateConnectionString("localhost:1502:1").IsValid);

            Assert.False(service.ValidateConnectionString("127.0.0.1").IsValid); // missing port
            Assert.False(service.ValidateConnectionString("127.0.0.1:invalid").IsValid);
            Assert.False(service.ValidateConnectionString("127.0.0.1:502:248").IsValid); // reserved unit id
        }

        [Fact]
        public async Task RetryPolicyService_ExecutesSuccessfully_WithoutRetry()
        {
            var logger = new Mock<ILogger<RetryPolicyService>>().Object;
            var service = new RetryPolicyService(logger);
            int callCount = 0;

            var result = await service.ExecuteWithRetryAsync(async () =>
            {
                callCount++;
                return await Task.FromResult(true);
            }, "TestSuccess", maxRetries: 3, initialDelayMs: 1);

            Assert.Equal(1, callCount);
            Assert.True(result);
        }

        [Fact]
        public async Task RetryPolicyService_RetriesOnException_AndSucceeds()
        {
            var logger = new Mock<ILogger<RetryPolicyService>>().Object;
            var service = new RetryPolicyService(logger);
            int callCount = 0;

            var result = await service.ExecuteWithRetryAsync(async () =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new SocketException((int)SocketError.TimedOut);
                }
                return await Task.FromResult(true);
            }, "TestRetry", maxRetries: 3, initialDelayMs: 1);

            Assert.Equal(3, callCount);
            Assert.True(result);
        }

        [Fact]
        public async Task RetryPolicyService_ThrowsException_AfterMaxRetries()
        {
            var logger = new Mock<ILogger<RetryPolicyService>>().Object;
            var service = new RetryPolicyService(logger);
            int callCount = 0;

            await Assert.ThrowsAsync<SocketException>(() =>
                service.ExecuteWithRetryAsync(() =>
                {
                    callCount++;
                    return Task.FromException<bool>(
                        new SocketException((int)SocketError.ConnectionRefused));
                }, "TestMaxRetries", maxRetries: 2, initialDelayMs: 1));

            Assert.Equal(3, callCount); // 1 initial try + 2 retries
        }

        [Fact]
        public async Task RetryPolicyService_DoesNotRetryCancellation()
        {
            var logger = new Mock<ILogger<RetryPolicyService>>().Object;
            var service = new RetryPolicyService(logger);
            int callCount = 0;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.ExecuteWithRetryAsync<bool>(() =>
                {
                    callCount++;
                    return Task.FromCanceled<bool>(new CancellationToken(true));
                }, "TestCancellation", maxRetries: 3, initialDelayMs: 1));

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task CircuitBreakerService_TripsOpen_AfterFailureThreshold()
        {
            var logger = new Mock<ILogger<CircuitBreakerService>>().Object;
            var service = new CircuitBreakerService(logger);
            var config = new CircuitBreakerConfig
            {
                FailureThreshold = 2,
                OpenTimeout = TimeSpan.FromMilliseconds(50),
                SuccessThreshold = 1
            };

            string circuitName = "TestCircuit";

            // First failure
            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await service.ExecuteAsync(circuitName, () => throw new Exception("Fail 1"), config);
            });
            Assert.Equal(CircuitState.Closed, service.GetState(circuitName));

            // Second failure - trips open
            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await service.ExecuteAsync(circuitName, () => throw new Exception("Fail 2"), config);
            });
            Assert.Equal(CircuitState.Open, service.GetState(circuitName));

            // While open, throws CircuitBreakerOpenException immediately without invoking action
            bool actionInvoked = false;
            await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            {
                await service.ExecuteAsync(circuitName, async () =>
                {
                    actionInvoked = true;
                    await Task.CompletedTask;
                }, config);
            });
            Assert.False(actionInvoked);
        }

        [Fact]
        public async Task CircuitBreakerService_Closes_AfterSuccessInHalfOpen()
        {
            var logger = new Mock<ILogger<CircuitBreakerService>>().Object;
            var service = new CircuitBreakerService(logger);
            var config = new CircuitBreakerConfig
            {
                FailureThreshold = 1,
                OpenTimeout = TimeSpan.FromMilliseconds(20),
                SuccessThreshold = 1
            };
            string circuitName = "TestCircuitClose";

            // Trip Open
            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await service.ExecuteAsync(circuitName, () => throw new Exception("Fail"), config);
            });
            Assert.Equal(CircuitState.Open, service.GetState(circuitName));

            // Wait for OpenTimeout to expire
            await Task.Delay(30);

            // Execute success - transitions to Closed
            await service.ExecuteAsync(circuitName, () => Task.CompletedTask, config);
            Assert.Equal(CircuitState.Closed, service.GetState(circuitName));
        }

        [Fact]
        public void ErrorHandlingService_TranslatesExceptions()
        {
            var logger = new Mock<ILogger<ErrorHandlingService>>().Object;
            var service = new ErrorHandlingService(logger);

            var socketEx = new SocketException((int)SocketError.ConnectionRefused);
            var result = service.HandleError(socketEx, "TestContext");

            Assert.Contains("Connection refused", result.UserMessage);
            Assert.Contains("Verify the Modbus server is running", result.RecoverySuggestion);
            Assert.True(result.IsRecoverable);
        }

        [Fact]
        public void ConfigurationValidator_ValidatesServerAndLoggingSettings()
        {
            var logger = new Mock<ILogger<ConfigurationValidator>>().Object;
            var validator = new ConfigurationValidator(logger);

            var validServer = new ServerSettings { Mode = "Client", DefaultPort = 502, DefaultUnitId = 1, MaxConnections = 10 };
            var validLogging = new LoggingSettings { RetentionMinutes = 60, SampleRateMs = 500 };

            var result = validator.ValidateConfiguration(validServer, validLogging);
            Assert.True(result.IsValid);

            var invalidServer = new ServerSettings { Mode = "InvalidMode", DefaultPort = 0, DefaultUnitId = 255 };
            var invalidLogging = new LoggingSettings { RetentionMinutes = 0, SampleRateMs = 10 };

            var invalidResult = validator.ValidateConfiguration(invalidServer, invalidLogging);
            Assert.False(invalidResult.IsValid);
            Assert.Contains("Mode must be 'Client' or 'Server'", invalidResult.ErrorMessage);
            Assert.Contains("DefaultPort must be between 1 and 65535", invalidResult.ErrorMessage);
            Assert.Contains("DefaultUnitId must be between 1 and 247", invalidResult.ErrorMessage);
        }
    }
}
