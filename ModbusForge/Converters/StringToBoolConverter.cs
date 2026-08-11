using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Returns <c>true</c> when the bound string is not null or whitespace.
    /// </summary>
    public sealed class StringToBoolConverter : IValueConverter
    {
        public static readonly StringToBoolConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is string s && !string.IsNullOrWhiteSpace(s);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
