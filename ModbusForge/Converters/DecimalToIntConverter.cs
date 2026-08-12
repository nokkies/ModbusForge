using System;
using System.Globalization;
using global::Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Converts between <see cref="decimal?"/> (used by Avalonia NumericUpDown) and <see cref="int"/>
    /// so two-way bindings to integer view-model properties work reliably.
    /// </summary>
    public sealed class DecimalToIntConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null)
                return null;

            try
            {
                var d = System.Convert.ToDecimal(value, culture);
                return d;
            }
            catch
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null)
                return 0;

            try
            {
                return System.Convert.ToInt32(value, culture);
            }
            catch
            {
                return 0;
            }
        }
    }
}
