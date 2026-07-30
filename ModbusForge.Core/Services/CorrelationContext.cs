using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Async-local correlation ID storage. Each async flow gets its own correlation ID
    /// so concurrent Modbus operations can be traced independently.
    /// </summary>
    public sealed class CorrelationContext : ICorrelationContext
    {
        private readonly AsyncLocal<string?> _currentId = new();

        public string? CurrentId => _currentId.Value;

        public string StartNew()
        {
            var id = Guid.NewGuid().ToString("N");
            _currentId.Value = id;
            return id;
        }

        public void Set(string correlationId)
        {
            _currentId.Value = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        }

        public void Clear()
        {
            _currentId.Value = null;
        }

        public void WithCorrelationId(string correlationId, Action action)
        {
            var previous = _currentId.Value;
            try
            {
                _currentId.Value = correlationId;
                action();
            }
            finally
            {
                _currentId.Value = previous;
            }
        }

        public async Task WithCorrelationIdAsync(string correlationId, Func<Task> action)
        {
            var previous = _currentId.Value;
            try
            {
                _currentId.Value = correlationId;
                await action().ConfigureAwait(false);
            }
            finally
            {
                _currentId.Value = previous;
            }
        }
    }
}
