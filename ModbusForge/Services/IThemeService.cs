using System;

namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction over WPF-UI theme management so the rest of the app can be tested.
    /// </summary>
    public interface IThemeService
    {
        bool IsDarkMode { get; }
        void SetTheme(bool isDark);
        event EventHandler? ThemeChanged;
    }
}
