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
            _isDarkMode = isDark;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleTheme()
        {
            _isDarkMode = !_isDarkMode;
            SetTheme(_isDarkMode);
        }

        private bool _isDarkMode = false;

        public event EventHandler? ThemeChanged;
    }
}
