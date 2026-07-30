using System;
using Wpf.Ui.Appearance;

namespace ModbusForge.Services
{
    /// <summary>
    /// WPF-UI implementation of the theme service.
    /// </summary>
    public class ThemeService : IThemeService
    {
        public bool IsDarkMode => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        public void SetTheme(bool isDark)
        {
            ApplicationThemeManager.Apply(isDark ? ApplicationTheme.Dark : ApplicationTheme.Light);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleTheme()
        {
            SetTheme(!IsDarkMode);
        }

        public event EventHandler? ThemeChanged;
    }
}
