using System;
using System.Globalization;
using System.Windows.Data;

namespace ModbusForge.Converters
{
    /// <summary>
    /// Converts a boolean value to a connected/disconnected string representation.
    /// </summary>
    public sealed class BooleanToConnectedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isConnected)
            {
                return isConnected ? "Connected" : "Disconnected";
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("BooleanToConnectedConverter can only be used for one-way conversion");
        }
    }
}
