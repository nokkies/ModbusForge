using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ModbusForge.Services
{
    /// <summary>
    /// WPF dispatcher implementation that marshals work to the UI thread.
    /// </summary>
    public class WpfDispatcher : IDispatcher
    {
        private readonly Dispatcher _dispatcher;

        public WpfDispatcher()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public void Invoke(Action action) => _dispatcher.Invoke(action, DispatcherPriority.Send);

        public T Invoke<T>(Func<T> func) => _dispatcher.Invoke(func, DispatcherPriority.Send);

        public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;

        public Task<T> InvokeAsync<T>(Func<T> func) => _dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task;
    }
}
