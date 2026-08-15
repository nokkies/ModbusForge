using System;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Converts a collection count to a boolean: true when the count is greater than zero.
    /// Used to show/hide UI regions that depend on "does this node have anything to show".
    /// </summary>
    public sealed class CountToBoolConverter : IValueConverter
    {
        public static readonly CountToBoolConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return value is int count && count > 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
