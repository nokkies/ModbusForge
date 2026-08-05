using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Picks the node background brush: red/green when live values are showing,
    /// otherwise the node type's header color.
    /// </summary>
    public sealed class NodeStateBrushConverter : IMultiValueConverter
    {
        public static readonly NodeStateBrushConverter Instance = new();

        private static readonly SolidColorBrush LiveTrueBrush = new(Color.FromRgb(40, 160, 40));
        private static readonly SolidColorBrush LiveFalseBrush = new(Color.FromRgb(160, 40, 40));

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 3)
                return Brushes.LightBlue;

            bool showLive = values[0] is true;
            bool currentValue = values[1] is true;
            var elementType = values[2] is PlcElementType type ? type : PlcElementType.Input;

            if (showLive)
            {
                return currentValue ? LiveTrueBrush : LiveFalseBrush;
            }

            var color = NodeDescriptors.Get(elementType).HeaderColor;
            return new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        }

        public object? ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
