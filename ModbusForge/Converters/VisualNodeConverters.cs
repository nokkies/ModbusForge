using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ModbusForge.Models;

namespace ModbusForge.Converters
{
    /// <summary>
    /// Converts PlcElementType to a color for node headers
    /// </summary>
    public class PlcElementTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PlcElementType elementType)
            {
                return new SolidColorBrush(NodeDescriptors.Get(elementType).HeaderColor);
            }
            return new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts boolean to dash array for connection lines
    /// </summary>
    public class BoolToDashArrayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isConnected && isConnected)
                return new DoubleCollection { 1, 0 }; // Solid line
            return new DoubleCollection { 5, 5 }; // Dashed line
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts node ID and connector type to a tag string
    /// </summary>
    public class ConnectorTagConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string nodeId && parameter is string connectorType)
            {
                return $"{nodeId},{connectorType}";
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
