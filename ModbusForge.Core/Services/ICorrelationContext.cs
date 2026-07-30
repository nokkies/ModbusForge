namespace ModbusForge.Services
{
    /// <summary>
    /// Provides an ambient correlation ID for tracing operations across async boundaries.
    /// </summary>
    public interface ICorrelationContext
    {
        /// <summary>
        /// The current correlation ID, or null if none has been started.
        /// </summary>
        string? CurrentId { get; }

        /// <summary>
        /// Starts a new correlation scope and returns the generated ID.
        /// </summary>
        string StartNew();

        /// <summary>
        /// Sets the correlation ID to an explicit value.
        /// </summary>
        void Set(string correlationId);

        /// <summary>
        /// Clears the current correlation ID.
        /// </summary>
        void Clear();

        /// <summary>
        /// Runs an action inside a correlation scope, restoring the previous ID afterwards.
        /// </summary>
        void WithCorrelationId(string correlationId, System.Action action);

        /// <summary>
        /// Runs an async action inside a correlation scope, restoring the previous ID afterwards.
        /// </summary>
        System.Threading.Tasks.Task WithCorrelationIdAsync(string correlationId, System.Func<System.Threading.Tasks.Task> action);
    }
}
