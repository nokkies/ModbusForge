using System;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction for marshalling work to the application's UI thread.
    /// </summary>
    public interface IDispatcher
    {
        /// <summary>
        /// True when the calling thread is the dispatcher's thread.
        /// </summary>
        bool CheckAccess { get; }

        void Invoke(Action action);
        T Invoke<T>(Func<T> func);
        Task InvokeAsync(Action action);
        Task<T> InvokeAsync<T>(Func<T> func);

        /// <summary>
        /// Posts work to the dispatcher's thread without tracking its completion.
        /// Unlike <see cref="InvokeAsync"/>, a fault inside the action is not
        /// observable by the caller and surfaces through the platform's
        /// unhandled-exception mechanism instead.
        /// </summary>
        void Post(Action action);
    }
}
