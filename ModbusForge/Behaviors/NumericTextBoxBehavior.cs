using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModbusForge.Behaviors
{
    /// <summary>
    /// Attached behavior that restricts TextBox input to numeric values.
    /// </summary>
    public static class NumericTextBoxBehavior
    {
        public enum NumericFormat
        {
            UInteger,
            Integer,
            Decimal
        }

        public static readonly DependencyProperty IsNumericProperty =
            DependencyProperty.RegisterAttached(
                "IsNumeric",
                typeof(bool),
                typeof(NumericTextBoxBehavior),
                new PropertyMetadata(false, OnIsNumericChanged));

        public static readonly DependencyProperty FormatProperty =
            DependencyProperty.RegisterAttached(
                "Format",
                typeof(NumericFormat),
                typeof(NumericTextBoxBehavior),
                new PropertyMetadata(NumericFormat.Integer));

        public static bool GetIsNumeric(DependencyObject obj) => (bool)obj.GetValue(IsNumericProperty);
        public static void SetIsNumeric(DependencyObject obj, bool value) => obj.SetValue(IsNumericProperty, value);

        public static NumericFormat GetFormat(DependencyObject obj) => (NumericFormat)obj.GetValue(FormatProperty);
        public static void SetFormat(DependencyObject obj, NumericFormat value) => obj.SetValue(FormatProperty, value);

        private static void OnIsNumericChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox textBox) return;

            if ((bool)e.NewValue)
            {
                textBox.PreviewTextInput += OnPreviewTextInput;
                DataObject.AddPastingHandler(textBox, OnPaste);
                DataObject.AddCopyingHandler(textBox, OnCopyOrCut);
                CommandManager.AddPreviewExecutedHandler(textBox, OnPreviewExecuted);
            }
            else
            {
                textBox.PreviewTextInput -= OnPreviewTextInput;
                DataObject.RemovePastingHandler(textBox, OnPaste);
                DataObject.RemoveCopyingHandler(textBox, OnCopyOrCut);
                CommandManager.RemovePreviewExecutedHandler(textBox, OnPreviewExecuted);
            }
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            e.Handled = !IsValidInput(textBox, e.Text);
        }

        private static void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
                return;

            var text = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            if (!IsValidInput(textBox, text, replaceSelection: true))
            {
                e.CancelCommand();
                e.Handled = true;
            }
        }

        private static void OnCopyOrCut(object sender, DataObjectCopyingEventArgs e)
        {
            // Allow copy/cut; this handler is only registered so we can cleanly remove it later.
        }

        private static void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (e.Command == ApplicationCommands.Paste)
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!IsValidInput(textBox, text, replaceSelection: true))
                    {
                        e.Handled = true;
                    }
                }
            }
        }

        private static bool IsValidInput(TextBox textBox, string input, bool replaceSelection = false)
        {
            if (string.IsNullOrEmpty(input)) return true;

            var format = GetFormat(textBox);
            var start = textBox.SelectionStart;
            var length = replaceSelection ? textBox.SelectionLength : 0;

            var pre = textBox.Text.Substring(0, start);
            var after = textBox.Text.Substring(start + length);
            var candidate = pre + input + after;

            if (string.IsNullOrEmpty(candidate)) return true;

            // Allow a single leading/trailing negative sign for Integer/Decimal.
            // The caret may be at the beginning or the sign may already be present.
            return format switch
            {
                NumericFormat.UInteger => candidate.All(char.IsDigit),
                NumericFormat.Integer => IsInteger(candidate),
                NumericFormat.Decimal => IsDecimal(candidate),
                _ => true
            };
        }

        private static bool IsInteger(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            // Allow a leading minus and digits only.
            var trimmed = text.Trim();
            if (trimmed == "-") return true; // still typing
            if (trimmed.StartsWith('-'))
            {
                if (trimmed.Length == 1) return true;
                trimmed = trimmed.Substring(1);
            }
            return trimmed.All(char.IsDigit) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        private static bool IsDecimal(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            var trimmed = text.Trim();
            if (trimmed == "-" || trimmed == ".") return true; // still typing
            // Allow one decimal point and one leading minus.
            var decimalSeparator = CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator;
            var parts = trimmed.Split(new[] { decimalSeparator }, StringSplitOptions.None);
            if (parts.Length > 2) return false;

            // Handle optional leading minus.
            var firstPart = parts[0];
            if (firstPart.StartsWith('-'))
            {
                if (firstPart.Length == 1 && parts.Length == 1) return true;
                firstPart = firstPart.Substring(1);
            }

            if (!firstPart.All(char.IsDigit)) return false;
            if (parts.Length > 1 && !parts[1].All(char.IsDigit)) return false;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
    }
}
