using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Two-way converter between <see cref="double"/> and invariant numeric strings.
    /// Used for the live-value TextBox so editing and simulation updates are consistent.
    /// </summary>
    public sealed class DoubleToStringConverter : IValueConverter
    {
        public static readonly DoubleToStringConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            }

            return value?.ToString() ?? string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var text = value as string ?? string.Empty;
            return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
        }
    }
}
