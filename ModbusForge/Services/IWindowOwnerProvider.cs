using System.Windows;

namespace ModbusForge.Services
{
    /// <summary>
    /// Provides the main application window so services can set it as the
    /// owner of tool windows without reaching into Application.Current directly.
    /// </summary>
    public interface IWindowOwnerProvider
    {
        /// <summary>Returns the current main window, or <c>null</c> if none is available.</summary>
        Window? GetMainWindow();
    }
}
