using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Converts a boolean to a brush. Returns <see cref="AvaloniaProperty.UnsetValue"/>
    /// for false when no false brush is configured so the theme's default foreground
    /// is preserved.
    /// </summary>
    public sealed class BoolToBrushConverter : IValueConverter
    {
        public IBrush? TrueBrush { get; set; } = Brushes.Red;
        public IBrush? FalseBrush { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true)
                return TrueBrush;

            return FalseBrush ?? AvaloniaProperty.UnsetValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
