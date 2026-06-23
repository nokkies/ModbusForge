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
        private readonly Random _random = new Random();

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
                catch (Exception ex)
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

        private bool IsRetryableException(Exception ex)
        {
            // Retry on network-related exceptions
            if (ex is System.IO.IOException || 
                ex is System.TimeoutException ||
                ex is System.Net.Sockets.SocketException)
            {
                return true;
            }

            // Retry on connection-related exceptions
            if (ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private int CalculateDelay(int attempt, int initialDelayMs, int maxDelayMs)
        {
            // Exponential backoff with jitter
            var exponentialDelay = initialDelayMs * (int)Math.Pow(2, attempt - 1);
            var jitter = _random.Next(0, (int)(exponentialDelay * 0.1)); // 10% jitter
            var delay = Math.Min(exponentialDelay + jitter, maxDelayMs);
            return delay;
        }
    }
}