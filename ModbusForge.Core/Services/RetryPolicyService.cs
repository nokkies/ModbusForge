using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    /// <summary>
    /// Provides retry policies with exponential backoff for resilient operations.
    /// </summary>
    public interface IRetryPolicyService
    {
        Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            int maxRetries = 3,
            int initialDelayMs = 1000,
            int maxDelayMs = 30000);

        Task ExecuteWithRetryAsync(
            Func<Task> operation,
            string operationName,
            int maxRetries = 3,
            int initialDelayMs = 1000,
            int maxDelayMs = 30000);
    }

    public class RetryPolicyService : IRetryPolicyService
    {
        private readonly ILogger<RetryPolicyService> _logger;

        public RetryPolicyService(ILogger<RetryPolicyService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            int maxRetries = 3,
            int initialDelayMs = 1000,
            int maxDelayMs = 30000)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            if (string.IsNullOrWhiteSpace(operationName))
                throw new ArgumentException("Operation name cannot be empty", nameof(operationName));

            int attempt = 0;
            Exception? lastException = null;

            while (attempt <= maxRetries)
            {
                try
                {
                    _logger.LogDebug("Attempting {OperationName} (attempt {Attempt}/{MaxRetries})", 
                        operationName, attempt + 1, maxRetries + 1);

                    var result = await operation();
                    
                    if (attempt > 0)
                    {
                        _logger.LogInformation("Operation {OperationName} succeeded after {Attempt} attempts", 
                            operationName, attempt + 1);
                    }
                    
                    return result;
                }
                catch (Exception ex) when (IsRetryableException(ex) && attempt < maxRetries)
                {
                    lastException = ex;
                    attempt++;
                    
                    var delay = CalculateDelay(attempt, initialDelayMs, maxDelayMs);
                    
                    _logger.LogWarning(ex, 
                        "Operation {OperationName} failed (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}ms...",
                        operationName, attempt, maxRetries + 1, delay);

                    await Task.Delay(delay);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    lastException = ex;
                    _logger.LogError(ex, "Operation {OperationName} failed after {Attempt} attempts", 
                        operationName, attempt + 1);
                    throw;
                }
            }

            _logger.LogError("Operation {OperationName} failed after {MaxRetries} retries", 
                operationName, maxRetries);
            throw new InvalidOperationException(
                $"Operation '{operationName}' failed after {maxRetries} retries", lastException);
        }

        public async Task ExecuteWithRetryAsync(
            Func<Task> operation,
            string operationName,
            int maxRetries = 3,
            int initialDelayMs = 1000,
            int maxDelayMs = 30000)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return true;
            }, operationName, maxRetries, initialDelayMs, maxDelayMs);
        }

        private static bool IsRetryableException(Exception ex)
        {
            // Retry only on exception TYPES that indicate a transient network fault.
            // (The previous version also retried whenever the message text contained
            // words like "connection" or "timeout" - that re-ran non-transient failures
            // whose messages merely mentioned those words.)
            return ex is System.IO.IOException
                || ex is System.TimeoutException
                || ex is System.Net.Sockets.SocketException;
        }

        private static int CalculateDelay(int attempt, int initialDelayMs, int maxDelayMs)
        {
            // Exponential backoff with 10% jitter. The doubling is done in long and capped
            // early so a large attempt count can never overflow the int multiply, and the
            // jitter range is clamped so Random.Shared.Next(0, 0) can never throw.
            long exponentialDelay = Math.Max(0, initialDelayMs);
            for (int i = 1; i < attempt && exponentialDelay < maxDelayMs; i++)
                exponentialDelay = Math.Min(maxDelayMs, exponentialDelay * 2);

            var jitterRange = (int)Math.Max(1, exponentialDelay / 10);
            var jitter = Random.Shared.Next(0, jitterRange);

            return (int)Math.Min(maxDelayMs, exponentialDelay + jitter);
        }
    }
}