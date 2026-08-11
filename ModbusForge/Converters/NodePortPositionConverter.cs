using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Computes the Canvas.Left or Canvas.Top position for a node's input/output ports
    /// based on the node's Width and Height.
    /// </summary>
    public sealed class NodePortPositionConverter : IMultiValueConverter
    {
        public static readonly NodePortPositionConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2)
                return 0.0;

            if (!TryGetDouble(values[0], out var width) || !TryGetDouble(values[1], out var height))
                return 0.0;

            const double PortSize = 10.0;

            return parameter?.ToString() switch
            {
                "Input1Y" => height * 0.32 - PortSize / 2.0,
                "Input2Y" => height * 0.68 - PortSize / 2.0,
                "Input1X" or "Input2X" => -PortSize / 2.0,
                "OutputX" => width - PortSize / 2.0,
                "OutputY" => height / 2.0 - PortSize / 2.0,
                _ => 0.0
            };
        }

        private static bool TryGetDouble(object? value, out double result)
        {
            if (value is double d)
            {
                result = d;
                return true;
            }

            if (value is float f)
            {
                result = f;
                return true;
            }

            if (value is IConvertible convertible)
            {
                try
                {
                    result = convertible.ToDouble(CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    // fall through
                }
            }

            result = 0.0;
            return false;
        }
    }
}
