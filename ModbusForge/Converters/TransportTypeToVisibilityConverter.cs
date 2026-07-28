using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ModbusForge.Models;

namespace ModbusForge.Converters
{
    /// <summary>
    /// Converts a TransportType value to a Visibility based on a parameter.
    /// Supported parameters: "Tcp", "Serial", "Rtu", "Ascii".
    /// </summary>
    public sealed class TransportTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TransportType transport && parameter is string parameterString)
            {
                bool visible = parameterString switch
                {
                    "Tcp" => transport == TransportType.Tcp,
                    "Serial" => transport == TransportType.Rtu || transport == TransportType.Ascii,
                    "Rtu" => transport == TransportType.Rtu,
                    "Ascii" => transport == TransportType.Ascii,
                    _ => false
                };

                return visible ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
