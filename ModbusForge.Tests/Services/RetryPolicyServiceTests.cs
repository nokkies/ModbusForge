using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class RetryPolicyServiceTests
    {
        private readonly RetryPolicyService _service = new(NullLogger<RetryPolicyService>.Instance);

        [Fact]
        public async Task NonRetryableException_ThrowsImmediately_WithoutRetrying()
        {
            int attempts = 0;
            Func<Task<int>> operation = async () =>
            {
                attempts++;
                await Task.Yield();
                throw new InvalidOperationException("permanent failure");
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ExecuteWithRetryAsync(operation, "op", maxRetries: 3, initialDelayMs: 1));

            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task ExceptionMentioningConnectionButOfPermanentType_IsNotRetried()
        {
            // Regression: the old implementation retried whenever the exception MESSAGE
            // contained words like "connection"/"network"/"timeout", which re-ran
            // non-transient failures. Retryability must be decided by exception type only.
            int attempts = 0;
            Func<Task<int>> operation = async () =>
            {
                attempts++;
                await Task.Yield();
                throw new ArgumentException("configuration rejected: connection profile is invalid");
            };

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _service.ExecuteWithRetryAsync(operation, "op", maxRetries: 3, initialDelayMs: 1));

            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task TransientIOException_IsRetried_UntilSuccess()
        {
            int attempts = 0;
            Func<Task<int>> operation = async () =>
            {
                attempts++;
                await Task.Yield();
                if (attempts < 3)
                    throw new System.IO.IOException("connection reset");
                return 42;
            };

            var result = await _service.ExecuteWithRetryAsync(operation, "op", maxRetries: 3, initialDelayMs: 1);

            Assert.Equal(42, result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task TransientIOException_ExhaustsRetries_ThrowsAggregateFailure()
        {
            int attempts = 0;
            Func<Task<int>> operation = async () =>
            {
                attempts++;
                await Task.Yield();
                throw new System.IO.IOException("still down");
            };

            // The original last transient exception is rethrown to the caller (the
            // InvalidOperationException fallback after the loop is unreachable).
            var ex = await Assert.ThrowsAsync<System.IO.IOException>(() =>
                _service.ExecuteWithRetryAsync(operation, "op", maxRetries: 2, initialDelayMs: 1));

            Assert.Equal(3, attempts); // initial attempt + 2 retries
        }
    }
}
