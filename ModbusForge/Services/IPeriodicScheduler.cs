using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction for a periodic scheduler that invokes a callback at a given interval.
    /// </summary>
    public interface IPeriodicScheduler
    {
        /// <summary>
        /// Starts the scheduler with the specified interval and tick callback.
        /// </summary>
        void Start(TimeSpan interval, Func<CancellationToken, Task> tick);

        /// <summary>
        /// Stops the scheduler.
        /// </summary>
        void Stop();

        /// <summary>
        /// Disposes the scheduler and releases any resources.
        /// </summary>
        void Dispose();
    }
}
