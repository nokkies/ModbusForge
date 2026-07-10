using System.Windows;

namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction over platform-specific message dialogs so ViewModels can be unit tested.
    /// </summary>
    public interface IDialogService
    {
        MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon);
    }
}
