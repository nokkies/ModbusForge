using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    /// <summary>
    /// Circuit breaker states.
    /// </summary>
    public enum CircuitState
    {
        Closed,   // Normal operation
        Open,     // Circuit is open, blocking requests
        HalfOpen  // Testing if the service has recovered
    }

    /// <summary>
    /// Circuit breaker configuration.
    /// </summary>
    public class CircuitBreakerConfig
    {
        public int FailureThreshold { get; set; } = 5;           // Failures before opening
        public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(60);  // How long to stay open
        public TimeSpan HalfOpenTimeout { get; set; } = TimeSpan.FromSeconds(30);  // How long to test in half-open
        public int SuccessThreshold { get; set; } = 2;            // Successes needed to close circuit
    }

    /// <summary>
    /// Circuit breaker service for preventing cascading failures.
    /// </summary>
    public interface ICircuitBreakerService
    {
        Task<T> ExecuteAsync<T>(string circuitName, Func<Task<T>> action, CircuitBreakerConfig? config = null);
        Task ExecuteAsync(string circuitName, Func<Task> action, CircuitBreakerConfig? config = null);
        CircuitState GetState(string circuitName);
        void Reset(string circuitName);
        void ResetAll();
    }

    public class CircuitBreakerService : ICircuitBreakerService
    {
        private readonly ILogger<CircuitBreakerService> _logger;
        private readonly Dictionary<string, CircuitBreakerState> _circuits = new();
        private readonly object _lock = new();

        public CircuitBreakerService(ILogger<CircuitBreakerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T> ExecuteAsync<T>(string circuitName, Func<Task<T>> action, CircuitBreakerConfig? config = null)
        {
            var state = GetOrCreateCircuitState(circuitName, config);
            AcquireExecutionPermission(state, circuitName);

            try
            {
                var result = await action();
                RecordSuccess(state, circuitName);
                return result;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                RecordFailure(state, circuitName, ex);
                throw;
            }
        }

        public async Task ExecuteAsync(string circuitName, Func<Task> action, CircuitBreakerConfig? config = null)
        {
            var state = GetOrCreateCircuitState(circuitName, config);
            AcquireExecutionPermission(state, circuitName);

            try
            {
                await action();
                RecordSuccess(state, circuitName);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                RecordFailure(state, circuitName, ex);
                throw;
            }
        }

        /// <summary>
        /// Atomically decides whether this call may proceed. When the open period has
        /// elapsed, exactly one caller becomes the half-open recovery probe; concurrent
        /// callers are still blocked until that probe decides the circuit's fate
        /// (the previous code let every concurrent caller transition to HalfOpen at once,
        /// hammering a recovering dependency with parallel test requests).
        /// </summary>
        private void AcquireExecutionPermission(CircuitBreakerState state, string circuitName)
        {
            lock (_lock)
            {
                if (state.State != CircuitState.Open)
                    return;

                if (state.OpenUntil is null || DateTime.UtcNow < state.OpenUntil)
                {
                    _logger.LogWarning("Circuit {CircuitName} is OPEN, blocking request until {OpenUntil}",
                        circuitName, state.OpenUntil);
                    throw new CircuitBreakerOpenException($"Circuit '{circuitName}' is open until {state.OpenUntil}");
                }

                if (state.HalfOpenProbeInFlight)
                {
                    _logger.LogWarning("Circuit {CircuitName} is HALF-OPEN with a recovery probe in flight, blocking request",
                        circuitName);
                    throw new CircuitBreakerOpenException($"Circuit '{circuitName}' is half-open; waiting for the recovery probe to complete");
                }

                _logger.LogInformation("Circuit {CircuitName} transitioning to Half-Open state", circuitName);
                state.State = CircuitState.HalfOpen;
                state.HalfOpenProbeInFlight = true;
                state.ConsecutiveSuccesses = 0;
            }
        }

        public CircuitState GetState(string circuitName)
        {
            lock (_lock)
            {
                return _circuits.TryGetValue(circuitName, out var state) ? state.State : CircuitState.Closed;
            }
        }

        public void Reset(string circuitName)
        {
            lock (_lock)
            {
                if (_circuits.TryGetValue(circuitName, out var state))
                {
                    _logger.LogInformation("Resetting circuit {CircuitName}", circuitName);
                    state.State = CircuitState.Closed;
                    state.FailureCount = 0;
                    state.ConsecutiveSuccesses = 0;
                    state.LastFailureTime = null;
                    state.OpenUntil = null;
                    state.HalfOpenProbeInFlight = false;
                }
            }
        }

        public void ResetAll()
        {
            lock (_lock)
            {
                _logger.LogInformation("Resetting all circuits");
                foreach (var circuit in _circuits.Values)
                {
                    circuit.State = CircuitState.Closed;
                    circuit.FailureCount = 0;
                    circuit.ConsecutiveSuccesses = 0;
                    circuit.LastFailureTime = null;
                    circuit.OpenUntil = null;
                    circuit.HalfOpenProbeInFlight = false;
                }
            }
        }

        private CircuitBreakerState GetOrCreateCircuitState(string circuitName, CircuitBreakerConfig? config)
        {
            lock (_lock)
            {
                if (!_circuits.TryGetValue(circuitName, out var state))
                {
                    state = new CircuitBreakerState(config ?? new CircuitBreakerConfig());
                    _circuits[circuitName] = state;
                    _logger.LogDebug("Created new circuit breaker: {CircuitName}", circuitName);
                }
                return state;
            }
        }

        private void RecordSuccess(CircuitBreakerState state, string circuitName)
        {
            lock (_lock)
            {
                state.ConsecutiveSuccesses++;
                _logger.LogDebug("Circuit {CircuitName} success recorded. Consecutive successes: {Count}",
                    circuitName, state.ConsecutiveSuccesses);

                if (state.State == CircuitState.HalfOpen)
                {
                    // This probe finished; further probes may start if more successes
                    // are still required to close the circuit.
                    state.HalfOpenProbeInFlight = false;

                    if (state.ConsecutiveSuccesses >= state.Config.SuccessThreshold)
                    {
                        _logger.LogInformation("Circuit {CircuitName} transitioning to CLOSED state", circuitName);
                        state.State = CircuitState.Closed;
                        state.FailureCount = 0;
                        state.ConsecutiveSuccesses = 0;
                        state.LastFailureTime = null;
                        state.OpenUntil = null;
                    }
                }
                else if (state.State == CircuitState.Closed)
                {
                    // Reset failure count on success in closed state
                    state.FailureCount = 0;
                }
            }
        }

        private void RecordFailure(CircuitBreakerState state, string circuitName, Exception exception)
        {
            lock (_lock)
            {
                state.FailureCount++;
                state.LastFailureTime = DateTime.UtcNow;
                state.ConsecutiveSuccesses = 0;

                _logger.LogWarning(exception, "Circuit {CircuitName} failure recorded. Total failures: {Count}",
                    circuitName, state.FailureCount);

                if (state.State == CircuitState.HalfOpen)
                {
                    // The recovery probe failed: reopen for a fresh open period.
                    _logger.LogError("Circuit {CircuitName} recovery probe failed, reopening circuit", circuitName);
                    state.State = CircuitState.Open;
                    state.OpenUntil = DateTime.UtcNow.Add(state.Config.OpenTimeout);
                    state.HalfOpenProbeInFlight = false;
                    state.FailureCount = 0;
                }
                else if (state.FailureCount >= state.Config.FailureThreshold)
                {
                    _logger.LogError("Circuit {CircuitName} opening due to {FailureCount} failures",
                        circuitName, state.FailureCount);
                    state.State = CircuitState.Open;
                    state.OpenUntil = DateTime.UtcNow.Add(state.Config.OpenTimeout);
                }
            }
        }

        private class CircuitBreakerState
        {
            public CircuitBreakerState(CircuitBreakerConfig config)
            {
                Config = config;
            }

            public CircuitBreakerConfig Config { get; }
            public CircuitState State { get; set; } = CircuitState.Closed;
            public int FailureCount { get; set; }
            public int ConsecutiveSuccesses { get; set; }
            public DateTime? LastFailureTime { get; set; }
            public DateTime? OpenUntil { get; set; }

            /// <summary>True while the single allowed half-open recovery probe is running.</summary>
            public bool HalfOpenProbeInFlight { get; set; }
        }
    }

    /// <summary>
    /// Exception thrown when circuit breaker is open.
    /// </summary>
    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
        public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
    }
}