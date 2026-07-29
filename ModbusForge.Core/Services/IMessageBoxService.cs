using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Button combinations supported by a message box.
    /// </summary>
    public enum DialogButton
    {
        Ok,
        OkCancel,
        YesNo,
        YesNoCancel
    }

    /// <summary>
    /// Visual severity of a message box.
    /// </summary>
    public enum DialogIcon
    {
        None,
        Information,
        Warning,
        Error,
        Question
    }

    /// <summary>
    /// Result returned by a message box.
    /// </summary>
    public enum DialogResult
    {
        None,
        Ok,
        Yes,
        No,
        Cancel
    }

    /// <summary>
    /// Cross-platform message box service.
    /// </summary>
    public interface IMessageBoxService
    {
        /// <summary>
        /// Shows a modal message and returns the user choice.
        /// </summary>
        Task<DialogResult> ShowAsync(string message, string title, DialogButton button, DialogIcon icon);
    }
}
