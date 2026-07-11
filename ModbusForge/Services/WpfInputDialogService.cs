using ModbusForge.Controls;

namespace ModbusForge.Services
{
    /// <summary>
    /// WPF implementation of <see cref="IInputDialogService"/> using the application's InputDialog.
    /// </summary>
    public class WpfInputDialogService : IInputDialogService
    {
        public bool TryGetInput(string title, string prompt, string defaultValue, out string input)
        {
            input = string.Empty;
            var dialog = new InputDialog(title, prompt, defaultValue);
            if (dialog.ShowDialog() != true)
                return false;

            input = dialog.InputText;
            return true;
        }
    }
}
