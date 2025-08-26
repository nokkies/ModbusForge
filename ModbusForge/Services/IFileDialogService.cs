namespace ModbusForge.Services
{
    public interface IFileDialogService
    {
        string? ShowSaveFileDialog(string title, string filter, string defaultFileName);
        string? ShowOpenFileDialog(string title, string filter);
    }
}
