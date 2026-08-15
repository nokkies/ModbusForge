using System;
using System.Threading.Tasks;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Avalonia UI-thread dispatcher adapter for the shared <see cref="IDispatcher"/> abstraction.
    /// </summary>
    public sealed class AvaloniaDispatcher : IDispatcher
    {
        public bool CheckAccess => global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess();

        public void Invoke(Action action)
        {
            if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action, global::Avalonia.Threading.DispatcherPriority.Normal).GetAwaiter().GetResult();
            }
        }

        public T Invoke<T>(Func<T> func)
        {
            if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                return func();
            }

            return global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(func, global::Avalonia.Threading.DispatcherPriority.Normal).GetAwaiter().GetResult();
        }

        public async Task InvokeAsync(Action action)
        {
            if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action, global::Avalonia.Threading.DispatcherPriority.Normal);
            }
        }

        public async Task<T> InvokeAsync<T>(Func<T> func)
        {
            if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                return func();
            }

            return await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(func, global::Avalonia.Threading.DispatcherPriority.Normal);
        }
    }
}
