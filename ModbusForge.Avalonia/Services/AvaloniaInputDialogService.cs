using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Stub input dialog service for the Avalonia spike.
    /// </summary>
    public sealed class AvaloniaInputDialogService : IInputDialogService
    {
        public bool TryGetInput(string title, string prompt, string defaultValue, out string input)
        {
            input = defaultValue;
            return false;
        }
    }
}
