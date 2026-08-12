using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace ModbusForge.Avalonia.Helpers
{
    public static class MarkdownHelper
    {
        public static readonly AttachedProperty<string?> MarkdownTextProperty =
            AvaloniaProperty.RegisterAttached<StackPanel, string?>("MarkdownText", typeof(MarkdownHelper));

        static MarkdownHelper()
        {
            MarkdownTextProperty.Changed.AddClassHandler<StackPanel>(OnMarkdownTextChanged);
        }

        public static string? GetMarkdownText(StackPanel element)
        {
            return element.GetValue(MarkdownTextProperty);
        }

        public static void SetMarkdownText(StackPanel element, string? value)
        {
            element.SetValue(MarkdownTextProperty, value);
        }

        private static void OnMarkdownTextChanged(StackPanel panel, AvaloniaPropertyChangedEventArgs e)
        {
            panel.Children.Clear();
            var text = e.NewValue as string;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inCodeBlock = false;
            List<string> codeLines = new();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Code block handling
                if (trimmed.StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        panel.Children.Add(CreateCodeBlock(string.Join(Environment.NewLine, codeLines)));
                        codeLines.Clear();
                        inCodeBlock = false;
                    }
                    else
                    {
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeLines.Add(line);
                    continue;
                }

                // Header 1
                if (trimmed.StartsWith("# "))
                {
                    panel.Children.Add(CreateHeader(trimmed.Substring(2), 18, FontWeight.Bold));
                    continue;
                }

                // Header 2
                if (trimmed.StartsWith("## "))
                {
                    panel.Children.Add(CreateHeader(trimmed.Substring(3), 15, FontWeight.SemiBold));
                    continue;
                }

                // Header 3
                if (trimmed.StartsWith("### "))
                {
                    panel.Children.Add(CreateHeader(trimmed.Substring(4), 13, FontWeight.SemiBold));
                    continue;
                }

                // Bullet point
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    panel.Children.Add(CreateBulletItem(trimmed.Substring(2)));
                    continue;
                }

                // Empty line / paragraph separator
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    panel.Children.Add(new Border { Height = 4 });
                    continue;
                }

                // Normal paragraph text
                panel.Children.Add(CreateParagraph(trimmed));
            }
        }

        private static Control CreateHeader(string text, double fontSize, FontWeight fontWeight)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = fontSize,
                FontWeight = fontWeight,
                Margin = new Thickness(0, 10, 0, 4)
            };
            ParseAndAddInlines(tb, text);
            return tb;
        }

        private static Control CreateBulletItem(string text)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.Margin = new Thickness(0, 1, 0, 1);

            var bullet = new TextBlock
            {
                Text = "• ",
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top
            };
            Grid.SetColumn(bullet, 0);
            grid.Children.Add(bullet);

            var content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 18,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top
            };
            ParseAndAddInlines(content, text);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);

            return grid;
        }

        private static Control CreateParagraph(string text)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 18,
                Margin = new Thickness(0, 1, 0, 1)
            };
            ParseAndAddInlines(tb, text);
            return tb;
        }

        private static Control CreateCodeBlock(string code)
        {
            var border = new Border
            {
                Background = Brush.Parse("#F4F4F4"),
                BorderBrush = Brush.Parse("#E0E0E0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var tb = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap
            };

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = tb
            };

            border.Child = scroll;
            return border;
        }

        private static void ParseAndAddInlines(TextBlock tb, string rawText)
        {
            if (tb.Inlines == null) return;
            int index = 0;
            while (index < rawText.Length)
            {
                int nextBold = rawText.IndexOf("**", index);
                int nextCode = rawText.IndexOf('`', index);

                if (nextBold == -1 && nextCode == -1)
                {
                    tb.Inlines.Add(new Run { Text = rawText.Substring(index) });
                    break;
                }

                if (nextBold != -1 && (nextCode == -1 || nextBold < nextCode))
                {
                    if (nextBold > index)
                    {
                        tb.Inlines.Add(new Run { Text = rawText.Substring(index, nextBold - index) });
                    }

                    int closeBold = rawText.IndexOf("**", nextBold + 2);
                    if (closeBold != -1)
                    {
                        var boldText = rawText.Substring(nextBold + 2, closeBold - (nextBold + 2));
                        tb.Inlines.Add(new Run { Text = boldText, FontWeight = FontWeight.Bold });
                        index = closeBold + 2;
                    }
                    else
                    {
                        tb.Inlines.Add(new Run { Text = "**" });
                        index = nextBold + 2;
                    }
                }
                else
                {
                    if (nextCode > index)
                    {
                        tb.Inlines.Add(new Run { Text = rawText.Substring(index, nextCode - index) });
                    }

                    int closeCode = rawText.IndexOf('`', nextCode + 1);
                    if (closeCode != -1)
                    {
                        var codeText = rawText.Substring(nextCode + 1, closeCode - (nextCode + 1));
                        tb.Inlines.Add(new Run
                        {
                            Text = codeText,
                            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                            Background = Brush.Parse("#EAEAEA"),
                            Foreground = Brush.Parse("#A31515")
                        });
                        index = closeCode + 1;
                    }
                    else
                    {
                        tb.Inlines.Add(new Run { Text = "`" });
                        index = nextCode + 1;
                    }
                }
            }
        }
    }
}
