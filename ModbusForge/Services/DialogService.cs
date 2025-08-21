using System.Windows;

namespace ModbusForge.Services
{
    public class DialogService : IDialogService
    {
        public void ShowMessageBox(string message, string caption, DialogButton button, DialogImage icon)
        {
            MessageBox.Show(message, caption, GetMessageBoxButton(button), GetMessageBoxImage(icon));
        }

        private MessageBoxButton GetMessageBoxButton(DialogButton button)
        {
            return button switch
            {
                DialogButton.OK => MessageBoxButton.OK,
                DialogButton.OKCancel => MessageBoxButton.OKCancel,
                DialogButton.YesNo => MessageBoxButton.YesNo,
                DialogButton.YesNoCancel => MessageBoxButton.YesNoCancel,
                _ => MessageBoxButton.OK,
            };
        }

        private MessageBoxImage GetMessageBoxImage(DialogImage icon)
        {
            return icon switch
            {
                DialogImage.None => MessageBoxImage.None,
                DialogImage.Error => MessageBoxImage.Error,
                DialogImage.Information => MessageBoxImage.Information,
                DialogImage.Question => MessageBoxImage.Question,
                DialogImage.Warning => MessageBoxImage.Warning,
                _ => MessageBoxImage.None,
            };
        }
    }
}
