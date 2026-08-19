using System;

namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction over desktop theme management so the rest of the app can be tested.
    /// </summary>
    public interface IThemeService
    {
        bool IsDarkMode { get; }
        void SetTheme(bool isDark);
        void ToggleTheme();
        event EventHandler? ThemeChanged;
    }
}
