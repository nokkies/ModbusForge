using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Returns <c>true</c> when the bound <see cref="PlcElementType"/> matches one of the
    /// comma-separated element type names supplied as the converter parameter.
    /// </summary>
    public sealed class ElementTypeToBoolConverter : IValueConverter
    {
        public static readonly ElementTypeToBoolConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not PlcElementType elementType || parameter is not string matchList)
            {
                return false;
            }

            var matches = matchList
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return matches.Contains(elementType.ToString())
                || matches.Contains("SignalGenerator") && elementType == PlcElementType.SignalGenerator;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
