using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Documents;
using ModbusForge.Helpers;

namespace ModbusForge.Converters
{
    /// <summary>
    /// Converts a Markdown string into a WPF <see cref="FlowDocument"/>.
    /// </summary>
    [ValueConversion(typeof(string), typeof(FlowDocument))]
    public class MarkdownToFlowDocumentConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string markdown
                ? MarkdownToFlowDocumentBuilder.Build(markdown)
                : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
