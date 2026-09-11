using System;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// One entry in the main window's left navigation list. Its visibility mirrors the
    /// corresponding MainTabControl tab; the navigation list only ever contains entries
    /// whose tab is currently visible.
    /// </summary>
    public sealed class NavigationItem
    {
        public string Title { get; }

        /// <summary>Index of the corresponding TabItem in MainTabControl.</summary>
        public int TabIndex { get; }

        private readonly Func<bool> _isVisible;

        public NavigationItem(string title, int tabIndex, Func<bool> isVisible)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            TabIndex = tabIndex;
            _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
        }

        public bool IsVisible => _isVisible();

        /// <summary>The navigation ListBox renders entries through ToString.</summary>
        public override string ToString() => Title;
    }
}
