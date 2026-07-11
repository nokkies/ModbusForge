using System.Windows;

namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction for opening shell-level dialog windows (About, Help, Preferences, etc.)
    /// so that MainWindow does not need IServiceProvider to resolve dependencies.
    /// </summary>
    public interface IShellWindowService
    {
        void ShowAbout(Window owner);
        void ShowHelp(Window owner);
        void ShowKeyboardShortcuts(Window owner);
        void ShowTroubleshooting(Window owner);
        void ShowScriptEditor(Window owner, byte unitId);
        void ShowPreferences(Window owner);
        void ShowConnectionManager(Window owner);
    }
}
