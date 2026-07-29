using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Stub file dialog service for the Avalonia spike.
    /// </summary>
    public sealed class AvaloniaFileDialogService : IFileDialogService
    {
        public string? ShowOpenFileDialog(string title, string filter)
        {
            return null;
        }

        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName)
        {
            return null;
        }
    }
}
