using Microsoft.Win32;

namespace ModbusForge.Services
{
    public class FileDialogService : IFileDialogService
    {
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName)
        {
            var dlg = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = defaultFileName
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public string? ShowOpenFileDialog(string title, string filter)
        {
            var dlg = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}
