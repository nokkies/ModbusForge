using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using LiveChartsCore.Measure;

namespace ModbusForge.Avalonia.Converters
{
    public sealed class LockToZoomModeConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var lockX = values.Count > 0 && values[0] is bool x && x;
            var lockY = values.Count > 1 && values[1] is bool y && y;

            if (lockX && lockY) return ZoomAndPanMode.None;
            if (lockX) return ZoomAndPanMode.Y;
            if (lockY) return ZoomAndPanMode.X;
            return ZoomAndPanMode.Both;
        }
    }
}
