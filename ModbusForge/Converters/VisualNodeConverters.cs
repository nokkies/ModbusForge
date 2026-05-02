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
                return elementType switch
                {
                    PlcElementType.Input => new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green
                    PlcElementType.Output => new SolidColorBrush(Color.FromRgb(255, 87, 34)), // Orange
                    PlcElementType.NOT => new SolidColorBrush(Color.FromRgb(244, 67, 54)), // Red
                    PlcElementType.AND => new SolidColorBrush(Color.FromRgb(33, 150, 243)), // Blue
                    PlcElementType.OR => new SolidColorBrush(Color.FromRgb(156, 39, 176)), // Purple
                    PlcElementType.RS => new SolidColorBrush(Color.FromRgb(255, 152, 0)), // Orange
                    PlcElementType.TON => new SolidColorBrush(Color.FromRgb(0, 188, 212)), // Cyan
                    PlcElementType.TOF => new SolidColorBrush(Color.FromRgb(0, 150, 136)), // Teal
                    PlcElementType.TP => new SolidColorBrush(Color.FromRgb(96, 125, 139)), // Blue Grey
                    PlcElementType.CTU => new SolidColorBrush(Color.FromRgb(139, 195, 74)), // Light Green
                    PlcElementType.CTD => new SolidColorBrush(Color.FromRgb(205, 220, 57)), // Lime
                    PlcElementType.CTC => new SolidColorBrush(Color.FromRgb(255, 235, 59)), // Yellow
                    PlcElementType.COMPARE_EQ or PlcElementType.COMPARE_NE => new SolidColorBrush(Color.FromRgb(255, 87, 34)), // Deep Orange
                    PlcElementType.COMPARE_GT or PlcElementType.COMPARE_LT => new SolidColorBrush(Color.FromRgb(233, 30, 99)), // Pink
                    PlcElementType.COMPARE_GE or PlcElementType.COMPARE_LE => new SolidColorBrush(Color.FromRgb(156, 39, 176)), // Deep Purple
                    PlcElementType.MATH_ADD or PlcElementType.MATH_SUB => new SolidColorBrush(Color.FromRgb(63, 81, 181)), // Indigo
                    PlcElementType.MATH_MUL or PlcElementType.MATH_DIV => new SolidColorBrush(Color.FromRgb(121, 85, 72)), // Brown
                    _ => new SolidColorBrush(Color.FromRgb(158, 158, 158)) // Grey
                };
            }
            return new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts boolean to visibility
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is System.Windows.Visibility visibility)
            {
                return visibility == System.Windows.Visibility.Visible;
            }
            return false;
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
                return elementType switch
                {
                    PlcElementType.Input => "📥",
                    PlcElementType.Output => "�",
                    PlcElementType.NOT => "❌",
                    PlcElementType.AND => "∧",
                    PlcElementType.OR => "∨",
                    PlcElementType.RS => "🔄",
                    PlcElementType.TON => "⏱️",
                    PlcElementType.TOF => "⏰",
                    PlcElementType.TP => "⚡",
                    PlcElementType.CTU => "🔢",
                    PlcElementType.CTD => "🔽",
                    PlcElementType.CTC => "🔀",
                    PlcElementType.COMPARE_EQ => "==",
                    PlcElementType.COMPARE_NE => "≠",
                    PlcElementType.COMPARE_GT => ">",
                    PlcElementType.COMPARE_LT => "<",
                    PlcElementType.COMPARE_GE => "≥",
                    PlcElementType.COMPARE_LE => "≤",
                    PlcElementType.MATH_ADD => "+",
                    PlcElementType.MATH_SUB => "-",
                    PlcElementType.MATH_MUL => "×",
                    PlcElementType.MATH_DIV => "÷",
                    _ => "?"
                };
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
