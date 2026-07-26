using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ModbusForge.Helpers
{
    /// <summary>
    /// Builds a WPF <see cref="FlowDocument"/> from a simple Markdown string.
    /// </summary>
    public static class MarkdownToFlowDocumentBuilder
    {
        public static FlowDocument Build(string markdown)
        {
            try
            {
                var document = new FlowDocument();
                document.FontFamily = new FontFamily("Segoe UI");
                document.FontSize = 14;
                document.PagePadding = new Thickness(0);

                if (string.IsNullOrWhiteSpace(markdown))
                {
                    return document;
                }

                var lines = markdown.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var currentParagraph = new Paragraph();

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        continue;
                    }

                    // Headers
                    if (trimmedLine.StartsWith("# "))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        var header = new Paragraph(new Run(trimmedLine.Substring(2).Trim()))
                        {
                            FontSize = 22,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 16, 0, 8)
                        };
                        document.Blocks.Add(header);
                    }
                    else if (trimmedLine.StartsWith("## "))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        var header = new Paragraph(new Run(trimmedLine.Substring(3).Trim()))
                        {
                            FontSize = 18,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 12, 0, 6)
                        };
                        document.Blocks.Add(header);
                    }
                    else if (trimmedLine.StartsWith("### "))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        var header = new Paragraph(new Run(trimmedLine.Substring(4).Trim()))
                        {
                            FontSize = 16,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 8, 0, 4)
                        };
                        document.Blocks.Add(header);
                    }
                    // List items
                    else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        var list = new List { Margin = new Thickness(24, 8, 0, 8), MarkerStyle = TextMarkerStyle.Disc };
                        var listItem = new ListItem();
                        var paragraph = new Paragraph();
                        AddInlineText(trimmedLine.Substring(2).Trim(), paragraph);
                        listItem.Blocks.Add(paragraph);
                        list.ListItems.Add(listItem);
                        document.Blocks.Add(list);
                    }
                    // Regular text
                    else
                    {
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            currentParagraph.Inlines.Add(new Run(" "));
                        }
                        AddInlineText(trimmedLine, currentParagraph);
                    }
                }

                AddCurrentParagraph(document, ref currentParagraph);
                return document;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                return CreateSimpleDocument("Error displaying help content");
            }
        }

        public static FlowDocument BuildErrorDocument(string text)
        {
            return CreateSimpleDocument(text);
        }

        private static FlowDocument CreateSimpleDocument(string text)
        {
            try
            {
                var document = new FlowDocument();
                document.FontFamily = new FontFamily("Segoe UI");
                document.FontSize = 14;
                document.PagePadding = new Thickness(0);

                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run(text));
                document.Blocks.Add(paragraph);

                return document;
            }
            catch
            {
                var document = new FlowDocument();
                document.Blocks.Add(new Paragraph(new Run("Help content unavailable")));
                return document;
            }
        }

        private static void AddInlineText(string text, Paragraph paragraph)
        {
            var parts = Regex.Split(text, @"(\*\*[^*]+\*\*|`[^`]+`)");

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                {
                    paragraph.Inlines.Add(new Run(part.Substring(2, part.Length - 4))
                    {
                        FontWeight = FontWeights.Bold
                    });
                }
                else if (part.StartsWith("`") && part.EndsWith("`") && part.Length > 2)
                {
                    paragraph.Inlines.Add(new Run(part.Substring(1, part.Length - 2))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = BrushCache.GetBrush(Color.FromRgb(240, 240, 240))
                    });
                }
                else
                {
                    paragraph.Inlines.Add(new Run(part));
                }
            }
        }

        private static void AddCurrentParagraph(FlowDocument document, ref Paragraph currentParagraph)
        {
            if (currentParagraph.Inlines.Count > 0)
            {
                currentParagraph.Margin = new Thickness(0, 8, 0, 8);
                document.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph();
            }
        }
    }
}
