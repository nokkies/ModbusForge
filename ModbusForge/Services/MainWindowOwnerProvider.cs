using System.Windows;

namespace ModbusForge.Services
{
    /// <summary>
    /// Production implementation of <see cref="IWindowOwnerProvider"/>.
    /// Returns the application's main window via <see cref="Application.Current"/>.
    /// This single class is the only place that accesses Application.Current for
    /// window-owner purposes, keeping all other services and view models testable.
    /// </summary>
    public class MainWindowOwnerProvider : IWindowOwnerProvider
    {
        public Window? GetMainWindow() => Application.Current?.MainWindow;
    }
}
