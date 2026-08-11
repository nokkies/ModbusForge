using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Converters
{
    public static class ColorConverters
    {
        public static readonly IValueConverter RgbToBrush = new FuncValueConverter<RgbColor?, IBrush?>(color =>
        {
            if (color == null) return null;
            return new SolidColorBrush(new Color(color.Value.A, color.Value.R, color.Value.G, color.Value.B));
        });

        public static readonly IValueConverter InverseBoolean = new FuncValueConverter<bool, bool>(b => !b);
    }
}
