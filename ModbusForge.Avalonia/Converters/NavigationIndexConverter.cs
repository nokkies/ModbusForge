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
            5,  // Signal Generator
            6,  // Simulation
            7,  // Holding Registers
            9,  // Input Registers
            8,  // Coils
            10, // Discrete Inputs
            11, // Custom Watch
            12, // Decode
            13, // Console
            14  // Debug
        };

        private static readonly int[] TabToNavigation = new int[15];

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
