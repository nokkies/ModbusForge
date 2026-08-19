using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Returns <c>true</c> when the bound string equals <see cref="Expected"/>
    /// (case-insensitive, ordinal). <see cref="Expected"/> is supplied through the
    /// binding's converter parameter, which makes one converter reusable for any
    /// "show this group of fields only when X == Y" scenario.
    /// </summary>
    public sealed class StringEqualityToBoolConverter : IValueConverter
    {
        public string Expected { get; init; } = string.Empty;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var expected = parameter is string p ? p : Expected;
            return value is string s && string.Equals(s, expected, StringComparison.OrdinalIgnoreCase);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
