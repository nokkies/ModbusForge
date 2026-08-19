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
        // Navigation index -> MainTabControl/SelectedTabIndex
        private static readonly int[] NavigationToTab =
        {
            0,  // Dashboard
            1,  // Trends
            2,  // Frame Inspector
            3,  // MQTT
            4,  // Script Editor
            5,  // Rules
            6,  // Signal Generator
            7,  // Simulation
            8,  // Holding Registers
            10, // Input Registers
            9,  // Coils
            11, // Discrete Inputs
            12, // Custom Watch
            13, // Decode
            14, // Console
            15  // Debug
        };

        private static readonly int[] TabToNavigation = new int[16];

        static NavigationIndexConverter()
        {
            for (var i = 0; i < NavigationToTab.Length; i++)
            {
                TabToNavigation[NavigationToTab[i]] = i;
            }
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int tabIndex && tabIndex >= 0 && tabIndex < TabToNavigation.Length)
                return TabToNavigation[tabIndex];

            return -1;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int listIndex && listIndex >= 0 && listIndex < NavigationToTab.Length)
                return NavigationToTab[listIndex];

            return 0;
        }
    }
}
