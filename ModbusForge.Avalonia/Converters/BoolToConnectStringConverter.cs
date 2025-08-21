using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ModbusForge.Avalonia.Converters
{
    public class BoolToConnectStringConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isConnected)
            {
                return isConnected ? "Disconnect" : "Connect";
            }
            return "Connect";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
