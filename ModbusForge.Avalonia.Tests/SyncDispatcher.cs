using System;
using System.Threading.Tasks;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Tests
{
    /// <summary>
    /// Synchronous dispatcher for unit tests that runs actions on the calling thread.
    /// </summary>
    public sealed class SyncDispatcher : IDispatcher
    {
        public bool CheckAccess => true;

        public void Invoke(Action action) => action();

        public T Invoke<T>(Func<T> func) => func();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            return Task.FromResult(func());
        }
    }
}
