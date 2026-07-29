using System;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Synchronous dispatcher that executes actions immediately on the calling thread.
    /// Useful for unit tests that have no WPF message loop.
    /// </summary>
    public class ImmediateDispatcher : IDispatcher
    {
        public void Invoke(Action action) => action();

        public T Invoke<T>(Func<T> func) => func();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }
}
