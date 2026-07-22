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
    /// Converts boolean to inverted visibility
    /// </summary>
    public class BoolToInvertedVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            }
            return System.Windows.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is System.Windows.Visibility visibility)
            {
                return visibility != System.Windows.Visibility.Visible;
            }
            return true;
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
    /// Converts node type to icon/emoji
    /// </summary>
    public class PlcElementTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PlcElementType elementType)
            {
                return NodeDescriptors.Get(elementType).Icon;
            }
            return "?";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts zoom level to scale transform
    /// </summary>
    public class ZoomToScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double zoom)
            {
                return new ScaleTransform(zoom, zoom);
            }
            return new ScaleTransform(1.0, 1.0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ScaleTransform transform)
            {
                return transform.ScaleX;
            }
            return 1.0;
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
