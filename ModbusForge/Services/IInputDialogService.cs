namespace ModbusForge.Services
{
    /// <summary>
    /// Abstracts simple input prompts to keep coordinators testable.
    /// </summary>
    public interface IInputDialogService
    {
        /// <summary>
        /// Shows a prompt and returns whether the user submitted a value.
        /// </summary>
        /// <param name="title">Dialog title.</param>
        /// <param name="prompt">Prompt text.</param>
        /// <param name="defaultValue">Default input value.</param>
        /// <param name="input">The user supplied value when the method returns true.</param>
        bool TryGetInput(string title, string prompt, string defaultValue, out string input);
    }
}
