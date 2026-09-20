using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Converts between the left navigation ListBox index and the MainTabControl SelectedTabIndex.
    /// The navigation list follows the user-facing order (Holding, Input, Coils, Discrete),
    /// while the tab control keeps its original item order (Holding, Coils, Input, Discrete).
    /// </summary>
    public sealed class NavigationIndexConverter : IValueConverter
    {
        // Navigation index -> MainTabControl/SelectedTabIndex. The PLC tab is the
        // 8th TabItem in MainView.axaml (after Signal Generator); everything from
        // Simulation onward is shifted by one in tab space but not in nav space.
        private static readonly int[] NavToTab =
        {
            0,  // Dashboard
            1,  // Trends
            2,  // Frame Inspector
            3,  // MQTT
            4,  // Script Editor
            5,  // Rules
            6,  // Signal Generator
            7,  // PLC
            8,  // Simulation
            9,  // Holding Registers
            11, // Input Registers
            10, // Coils
            12, // Discrete Inputs
            13, // Custom Watch
            14, // Decode
            15, // Console
            16  // Debug
        };

        private static readonly int[] TabToNav = new int[17];

        static NavigationIndexConverter()
        {
            for (var i = 0; i < NavToTab.Length; i++)
            {
                TabToNav[NavToTab[i]] = i;
            }
        }

        /// <summary>Navigation-list index -> tab-control index (used by MainView sync).</summary>
        public static int NavigationToTab(int navigationIndex) =>
            navigationIndex >= 0 && navigationIndex < NavToTab.Length ? NavToTab[navigationIndex] : 0;

        /// <summary>Tab-control index -> navigation-list index, or -1 when unmapped.</summary>
        public static int TabToNavigation(int tabIndex) =>
            tabIndex >= 0 && tabIndex < TabToNav.Length ? TabToNav[tabIndex] : -1;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int tabIndex && tabIndex >= 0 && tabIndex < TabToNav.Length)
                return TabToNav[tabIndex];

            return -1;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int listIndex && listIndex >= 0 && listIndex < NavToTab.Length)
                return NavToTab[listIndex];

            return 0;
        }
    }
}
