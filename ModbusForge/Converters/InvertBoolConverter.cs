using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Inverts a boolean. Used where a panel and its restore strip show one when the
    /// other is hidden. Avalonia's own <c>ObjectConverters</c> has no <c>IsNot</c>.
    /// </summary>
    public sealed class InvertBoolConverter : IValueConverter
    {
        public static readonly InvertBoolConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool flag ? !flag : BindingOperations.DoNothing;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool flag ? !flag : BindingOperations.DoNothing;
    }
}
