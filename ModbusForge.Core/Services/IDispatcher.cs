using System;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction for marshalling work to the application's UI thread.
    /// </summary>
    public interface IDispatcher
    {
        void Invoke(Action action);
        T Invoke<T>(Func<T> func);
        Task InvokeAsync(Action action);
        Task<T> InvokeAsync<T>(Func<T> func);
    }
}
