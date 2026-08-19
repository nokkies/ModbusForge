using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Negates a boolean value. Useful for "show when not" bindings, e.g. a
    /// placeholder shown only while nothing is selected.
    /// </summary>
    public sealed class BoolNegateConverter : IValueConverter
    {
        public static readonly BoolNegateConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is not true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
