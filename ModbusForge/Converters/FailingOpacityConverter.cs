using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Maps the pen-list "is failing" flag to a text opacity: a healthy pen's
    /// value renders at full (0.75) list opacity, a failing pen's last good
    /// value is dimmed so it reads as stale without disappearing.
    /// </summary>
    public sealed class FailingOpacityConverter : IValueConverter
    {
        public const double HealthyOpacity = 0.75;
        public const double FailingOpacity = 0.4;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? FailingOpacity : HealthyOpacity;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
