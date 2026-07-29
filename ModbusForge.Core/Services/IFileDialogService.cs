using System.Threading.Tasks;

namespace ModbusForge.Services
{
    public interface IFileDialogService
    {
        string? ShowSaveFileDialog(string title, string filter, string defaultFileName);
        string? ShowOpenFileDialog(string title, string filter);

        /// <summary>
        /// Async save-file dialog. Platforms that require an async API can override this;
        /// the default delegates to the synchronous method.
        /// </summary>
        Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName)
            => Task.FromResult(ShowSaveFileDialog(title, filter, defaultFileName));

        /// <summary>
        /// Async open-file dialog. Platforms that require an async API can override this;
        /// the default delegates to the synchronous method.
        /// </summary>
        Task<string?> ShowOpenFileDialogAsync(string title, string filter)
            => Task.FromResult(ShowOpenFileDialog(title, filter));
    }
}
