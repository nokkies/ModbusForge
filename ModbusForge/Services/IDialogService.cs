namespace ModbusForge.Services
{
    public interface IDialogService
    {
        void ShowMessageBox(string message, string caption, DialogButton button, DialogImage icon);
    }
}
