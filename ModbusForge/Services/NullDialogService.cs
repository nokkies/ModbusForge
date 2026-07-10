using System.Windows;

namespace ModbusForge.Services
{
    /// <summary>
    /// No-op dialog service for test environments where no UI is available.
    /// Returns OK/Yes so non-interactive callers can continue.
    /// </summary>
    public class NullDialogService : IDialogService
    {
        public MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            return button switch
            {
                MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
                MessageBoxButton.OKCancel => MessageBoxResult.OK,
                _ => MessageBoxResult.OK,
            };
        }
    }
}
