using System;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Stub theme service for the Avalonia spike. Avalonia theming is handled via Styles.
    /// </summary>
    public sealed class AvaloniaThemeService : IThemeService
    {
        public bool IsDarkMode => false;

        public void SetTheme(bool isDark)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? ThemeChanged;
    }
}
