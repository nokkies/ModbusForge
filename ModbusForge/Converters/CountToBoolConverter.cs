using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Converts a count to a boolean. By default the result is true when the count is
    /// greater than zero ("does this node have anything to show"); with the
    /// <c>zero</c> converter parameter it is true when the count is exactly zero,
    /// which is how empty-state placeholders are shown.
    /// </summary>
    public sealed class CountToBoolConverter : IValueConverter
    {
        public static readonly CountToBoolConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var count = value as int?;
            if (count == null && value is IEnumerable enumerable && value is not string)
            {
                count = 0;
                foreach (var _ in enumerable)
                {
                    count++;
                }
            }

            if (count == null)
            {
                return false;
            }

            var wantZero = string.Equals(parameter as string, "zero", StringComparison.OrdinalIgnoreCase);
            return wantZero ? count == 0 : count > 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
