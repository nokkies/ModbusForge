using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ModbusForge.Avalonia.Behaviors
{
    /// <summary>
    /// Attached behavior that restricts <see cref="TextBox"/> input to numeric values,
    /// similar to the WPF NumericTextBoxBehavior.
    /// </summary>
    public class NumericTextBoxBehavior
    {
        public enum NumericFormat
        {
            UInteger,
            Integer,
            Decimal
        }

        public static readonly AttachedProperty<bool> IsNumericProperty =
            AvaloniaProperty.RegisterAttached<NumericTextBoxBehavior, TextBox, bool>(
                "IsNumeric",
                defaultValue: false);

        public static readonly AttachedProperty<NumericFormat> FormatProperty =
            AvaloniaProperty.RegisterAttached<NumericTextBoxBehavior, TextBox, NumericFormat>(
                "Format",
                defaultValue: NumericFormat.Decimal);

        public static readonly AttachedProperty<double> MinimumProperty =
            AvaloniaProperty.RegisterAttached<NumericTextBoxBehavior, TextBox, double>(
                "Minimum",
                defaultValue: double.MinValue);

        public static readonly AttachedProperty<double> MaximumProperty =
            AvaloniaProperty.RegisterAttached<NumericTextBoxBehavior, TextBox, double>(
                "Maximum",
                defaultValue: double.MaxValue);

        static NumericTextBoxBehavior()
        {
            IsNumericProperty.Changed.AddClassHandler<TextBox>(OnIsNumericChanged);
        }

        public static bool GetIsNumeric(TextBox textBox) => textBox.GetValue(IsNumericProperty);

        public static void SetIsNumeric(TextBox textBox, bool value) => textBox.SetValue(IsNumericProperty, value);

        public static NumericFormat GetFormat(TextBox textBox) => textBox.GetValue(FormatProperty);

        public static void SetFormat(TextBox textBox, NumericFormat value) => textBox.SetValue(FormatProperty, value);

        public static double GetMinimum(TextBox textBox) => textBox.GetValue(MinimumProperty);

        public static void SetMinimum(TextBox textBox, double value) => textBox.SetValue(MinimumProperty, value);

        public static double GetMaximum(TextBox textBox) => textBox.GetValue(MaximumProperty);

        public static void SetMaximum(TextBox textBox, double value) => textBox.SetValue(MaximumProperty, value);

        private static void OnIsNumericChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.GetNewValue<bool>())
            {
                textBox.TextInput += OnTextInput;
                textBox.LostFocus += OnLostFocus;
            }
            else
            {
                textBox.TextInput -= OnTextInput;
                textBox.LostFocus -= OnLostFocus;
            }
        }

        private static void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (!IsValidInput(textBox, e.Text))
            {
                e.Handled = true;
            }
        }

        private static void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox || !GetIsNumeric(textBox))
            {
                return;
            }

            if (!TryParseValue(textBox, textBox.Text ?? string.Empty, out var value))
            {
                return;
            }

            var minimum = GetMinimum(textBox);
            var maximum = GetMaximum(textBox);
            value = Math.Clamp(value, minimum, maximum);
            textBox.Text = FormatClampedValue(value, GetFormat(textBox));
            textBox.CaretIndex = textBox.Text.Length;
        }

        private static bool IsValidInput(TextBox textBox, string? input)
        {
            if (string.IsNullOrEmpty(input)) return true;

            var format = GetFormat(textBox);
            var selectionStart = textBox.SelectionStart;
            var selectionEnd = textBox.SelectionEnd;
            var start = Math.Min(selectionStart, selectionEnd);
            var length = Math.Abs(selectionEnd - selectionStart);
            var text = textBox.Text ?? string.Empty;

            var pre = text.Substring(0, start);
            var after = text.Substring(start + length);
            var candidate = pre + input + after;

            if (string.IsNullOrEmpty(candidate)) return true;

            bool valid = format switch
            {
                NumericFormat.UInteger => candidate.All(char.IsDigit),
                NumericFormat.Integer => IsInteger(candidate),
                NumericFormat.Decimal => IsDecimal(candidate),
                _ => true
            };

            return valid && IsWithinMaximum(textBox, candidate);
        }

        private static bool IsInteger(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (text == "-") return true;
            return text[0] == '-' ? text.Substring(1).All(char.IsDigit) : text.All(char.IsDigit);
        }

        private static bool IsDecimal(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;

            var stripped = text.Replace("-", "");
            if (stripped == string.Empty) return true;

            var parts = stripped.Split('.');
            if (parts.Length > 2) return false;

            if (text == "-.") return true;

            foreach (var part in parts)
            {
                if (!part.All(char.IsDigit)) return false;
            }

            return true;
        }

        private static bool IsWithinMaximum(TextBox textBox, string candidate)
        {
            var format = GetFormat(textBox);
            var maximum = GetMaximum(textBox);

            if (!TryParseValue(textBox, candidate, out var value))
            {
                return true;
            }

            return value <= maximum;
        }

        private static bool TryParseValue(TextBox textBox, string text, out double value)
        {
            value = 0;
            var format = GetFormat(textBox);
            return format switch
            {
                NumericFormat.UInteger => uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) && (value = u) >= 0,
                NumericFormat.Integer => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && (value = i) == i,
                NumericFormat.Decimal => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && (value = d) == d,
                _ => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
            };
        }

        private static string FormatClampedValue(double value, NumericFormat format)
        {
            return format switch
            {
                NumericFormat.UInteger => ((uint)value).ToString(CultureInfo.InvariantCulture),
                NumericFormat.Integer => ((int)value).ToString(CultureInfo.InvariantCulture),
                _ => value.ToString(CultureInfo.InvariantCulture)
            };
        }
    }
}
