using System;
using System.Globalization;
using System.Windows.Data;
using LiveChartsCore.Measure;

namespace ModbusForge.Converters
{
    public class LockToZoomModeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool lockX = values.Length > 0 && values[0] is bool bx && bx;
            bool lockY = values.Length > 1 && values[1] is bool by && by;

            if (lockX && lockY) return ZoomAndPanMode.None;
            if (lockX && !lockY) return ZoomAndPanMode.Y;   // lock X => allow Y zoom only
            if (!lockX && lockY) return ZoomAndPanMode.X;   // lock Y => allow X zoom only
            return ZoomAndPanMode.Both;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
