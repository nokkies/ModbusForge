using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Converters
{
    /// <summary>
    /// Canvas ZIndex per node: Control Expert text boxes often frame whole groups of
    /// logic, so they draw behind the blocks instead of covering them.
    /// </summary>
    public sealed class PlcNodeLayerConverter : IValueConverter
    {
        public static readonly PlcNodeLayerConverter Instance = new();

        private const int TextBoxLayer = -1;
        private const int BlockLayer = 0;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is PlcElementType.PlcComment ? TextBoxLayer : BlockLayer;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
