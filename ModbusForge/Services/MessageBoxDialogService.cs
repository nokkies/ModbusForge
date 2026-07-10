using System.Windows;

namespace ModbusForge.Services
{
    /// <summary>
    /// WPF implementation of <see cref="IDialogService"/>.
    /// </summary>
    public class MessageBoxDialogService : IDialogService
    {
        public MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            return MessageBox.Show(messageBoxText, caption, button, icon);
        }
    }
}
