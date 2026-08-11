using System;
using global::Avalonia;
using global::Avalonia.Styling;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Stub theme service for the Avalonia spike. Avalonia theming is handled via Styles.
    /// </summary>
    public sealed class AvaloniaThemeService : IThemeService
    {
        public bool IsDarkMode => _isDarkMode;

        public void SetTheme(bool isDark)
        {
            _isDarkMode = isDark;

            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = isDark
                    ? ThemeVariant.Dark
                    : ThemeVariant.Light;
            }

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
