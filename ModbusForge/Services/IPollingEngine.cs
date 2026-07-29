using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Decoupled background polling engine that executes Modbus I/O off the UI thread
    /// and surfaces results through <see cref="System.Threading.Channels"/>.
    /// </summary>
    public interface IPollingEngine
    {
        /// <summary>
        /// Starts the engine worker.
        /// </summary>
        void Start(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the engine worker.
        /// </summary>
        void Stop();

        /// <summary>
        /// Queues a read or write command. If a command for the same area/unit is
        /// already pending, the new command overwrites it (coalescing).
        /// </summary>
        void Enqueue(PollingCommand command);

        /// <summary>
        /// Channel reader for completed polling results.
        /// </summary>
        ChannelReader<PollingResult> Results { get; }

        /// <summary>
        /// Raised when a command failed and monitoring should be paused.
        /// </summary>
        event EventHandler<PollingErrorEventArgs>? Error;
    }

    public class PollingErrorEventArgs : EventArgs
    {
        public PollingErrorEventArgs(PollingCommand command, PollingResult result)
        {
            Command = command;
            Result = result;
        }

        public PollingCommand Command { get; }
        public PollingResult Result { get; }
    }
}
