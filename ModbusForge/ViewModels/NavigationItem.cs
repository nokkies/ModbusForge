using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// One entry in the main window's left navigation list. Its visibility mirrors the
    /// corresponding MainTabControl tab, so tabs the user has hidden never appear in
    /// the navigation list (and cannot be selected from it).
    /// </summary>
    public sealed class NavigationItem : ObservableObject
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

        /// <summary>Raises the IsVisible change after the underlying tab flag changed.</summary>
        internal void RaiseVisibilityChanged() => OnPropertyChanged(nameof(IsVisible));
    }
}
