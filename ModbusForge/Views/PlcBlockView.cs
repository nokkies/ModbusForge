using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Views
{
    /// <summary>
    /// Draws an FFB imported from a Unity Pro XEF the way Control Expert shows it:
    /// a framed block with the type name inside and, for function blocks, the
    /// instance name above; every pin in its grid row with the formal parameter
    /// inside the frame and the actual parameter (variable, literal) outside;
    /// negated pins with a circle. Geometry comes from the node's
    /// <see cref="PlcBlockInfo"/>, so links meet the frame exactly at the pins.
    /// </summary>
    /// <remarks>
    /// One custom-drawn control per block keeps large sections (hundreds of blocks,
    /// thousands of pin labels) cheap compared with a templated element per label.
    /// Actual parameters are drawn outside the control's bounds, in the free grid
    /// cells Control Expert leaves beside blocks for them.
    /// </remarks>
    public sealed class PlcBlockView : Control
    {
        private const double TypeNameFontSize = 11;
        private const double LabelFontSize = 9.5;
        private const double TextGap = 3;
        private const double NegationRadius = 3;

        private static readonly Typeface Regular = new(FontFamily.Default);
        private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

        private static readonly IBrush FrameFill = Brushes.White;
        private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        private static readonly IBrush ActualBrush = new SolidColorBrush(Color.FromRgb(0x0D, 0x3B, 0x8C));
        private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), 1);
        private static readonly IPen SelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0x19, 0x76, 0xD2)), 2);
        private static readonly IPen ErrorPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)), 2);
        private static readonly IPen PinPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), 1);

        // Live values while the PLC tab runs: a tag beside each pin.
        private const double ValueFontSize = 8.5;
        private const double ValuePadding = 2;
        private static readonly IBrush TrueFill = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9));
        private static readonly IBrush TrueText = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
        private static readonly IBrush FalseFill = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        private static readonly IBrush FalseText = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61));
        private static readonly IBrush NumberFill = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD));
        private static readonly IBrush NumberText = new SolidColorBrush(Color.FromRgb(0x0D, 0x47, 0xA1));
        private static readonly IBrush NoteBrush = new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75));
        private static readonly IPen IdlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), 1, new DashStyle(new double[] { 3, 2 }, 0));
        private static readonly Typeface Italic = new(FontFamily.Default, FontStyle.Italic);

        private VisualNode? _node;

        public PlcBlockView()
        {
            // The node draws its own selection/error state; pointer handling stays on
            // the hosting Border (FbdCanvasInteractionBase's node handlers).
            IsHitTestVisible = false;
            ClipToBounds = false;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_node != null)
            {
                _node.PropertyChanged -= OnNodePropertyChanged;
            }

            _node = DataContext as VisualNode;
            if (_node != null)
            {
                _node.PropertyChanged += OnNodePropertyChanged;
            }

            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_node != null)
            {
                _node.PropertyChanged -= OnNodePropertyChanged;
                _node = null;
            }

            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_node == null && DataContext is VisualNode node)
            {
                _node = node;
                _node.PropertyChanged += OnNodePropertyChanged;
            }
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(VisualNode.IsSelected) or nameof(VisualNode.HasError)
                or nameof(VisualNode.Plc) or nameof(VisualNode.PlcLive) or nameof(VisualNode.Width) or nameof(VisualNode.Height))
            {
                // Live values arrive on the PLC runtime's timer thread.
                if (Dispatcher.UIThread.CheckAccess())
                    InvalidateVisual();
                else
                    Dispatcher.UIThread.Post(InvalidateVisual);
            }
        }

        public override void Render(DrawingContext context)
        {
            if (_node?.Plc is not { } plc)
            {
                return;
            }

            var width = _node.Width;
            var height = _node.Height;
            var inset = plc.FrameInset;
            var live = _node.PlcLive;
            var frame = new Rect(inset, plc.CellSize, Math.Max(width - (2 * inset), 1), Math.Max(height - plc.CellSize, 1));
            var framePen = _node.HasError ? ErrorPen : _node.IsSelected ? SelectedPen
                : live is { Executed: false } ? IdlePen : FramePen;

            context.DrawRectangle(FrameFill, framePen, frame);

            // Instance name above the frame (function blocks only), type name inside.
            if (!string.IsNullOrEmpty(plc.InstanceName))
            {
                DrawCentered(context, plc.InstanceName!, Regular, LabelFontSize, TextBrush, width / 2, plc.CellSize / 2);
            }

            DrawCentered(context, plc.TypeName, Bold, TypeNameFontSize, TextBrush, width / 2, plc.CellSize * 2);

            // While running, say so on blocks the runtime does not compute.
            if (live is { Simulated: false } && height >= plc.CellSize * 4)
            {
                DrawCentered(context, "not simulated", Italic, ValueFontSize, NoteBrush, width / 2, plc.CellSize * 3);
            }

            foreach (var pin in plc.Pins)
            {
                var value = live == null ? null
                    : (pin.IsInput ? live.Inputs : live.Outputs).TryGetValue(pin.Name, out var text) ? text : null;
                DrawPin(context, pin, width, inset, value);
            }
        }

        private static void DrawPin(DrawingContext context, PlcPin pin, double width, double inset, string? value)
        {
            var y = pin.CenterY;
            if (pin.IsInput)
            {
                context.DrawLine(PinPen, new Point(0, y), new Point(inset, y));
                if (pin.Inverted)
                {
                    context.DrawEllipse(FrameFill, PinPen, new Point(inset - NegationRadius, y), NegationRadius, NegationRadius);
                }

                DrawLeftAligned(context, pin.Name, Regular, LabelFontSize, TextBrush, inset + TextGap, y);
                var right = -TextGap;
                if (pin.ActualParameter is { } actual)
                {
                    right -= DrawRightAligned(context, actual, Regular, LabelFontSize, ActualBrush, right, y) + TextGap;
                }
                if (value != null)
                {
                    DrawValue(context, value, right, y, rightAligned: true);
                }
            }
            else
            {
                context.DrawLine(PinPen, new Point(width - inset, y), new Point(width, y));
                if (pin.Inverted)
                {
                    context.DrawEllipse(FrameFill, PinPen, new Point(width - inset + NegationRadius, y), NegationRadius, NegationRadius);
                }

                DrawRightAligned(context, pin.Name, Regular, LabelFontSize, TextBrush, width - inset - TextGap, y);
                var left = width + TextGap;
                if (pin.ActualParameter is { } actual)
                {
                    left += DrawLeftAligned(context, actual, Regular, LabelFontSize, ActualBrush, left, y) + TextGap;
                }
                if (value != null)
                {
                    DrawValue(context, value, left, y, rightAligned: false);
                }
            }
        }

        /// <summary>A live value tag: green TRUE, grey FALSE, blue for numbers and times.</summary>
        private static void DrawValue(DrawingContext context, string value, double x, double centreY, bool rightAligned)
        {
            var (fill, brush) = value switch
            {
                "TRUE" => (TrueFill, TrueText),
                "FALSE" => (FalseFill, FalseText),
                _ => (NumberFill, NumberText)
            };
            var formatted = Format(value, Bold, ValueFontSize, brush);
            var boxWidth = formatted.Width + (2 * ValuePadding);
            var left = rightAligned ? x - boxWidth : x;
            var box = new Rect(left, centreY - (formatted.Height / 2), boxWidth, formatted.Height);
            context.DrawRectangle(fill, null, box, 2, 2);
            context.DrawText(formatted, new Point(left + ValuePadding, box.Top));
        }

        private static FormattedText Format(string text, Typeface typeface, double size, IBrush brush)
            => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);

        private static void DrawCentered(DrawingContext context, string text, Typeface typeface, double size, IBrush brush, double centreX, double centreY)
        {
            var formatted = Format(text, typeface, size, brush);
            context.DrawText(formatted, new Point(centreX - (formatted.Width / 2), centreY - (formatted.Height / 2)));
        }

        /// <returns>The drawn text's width.</returns>
        private static double DrawLeftAligned(DrawingContext context, string text, Typeface typeface, double size, IBrush brush, double left, double centreY)
        {
            var formatted = Format(text, typeface, size, brush);
            context.DrawText(formatted, new Point(left, centreY - (formatted.Height / 2)));
            return formatted.Width;
        }

        /// <returns>The drawn text's width.</returns>
        private static double DrawRightAligned(DrawingContext context, string text, Typeface typeface, double size, IBrush brush, double right, double centreY)
        {
            var formatted = Format(text, typeface, size, brush);
            context.DrawText(formatted, new Point(right - formatted.Width, centreY - (formatted.Height / 2)));
            return formatted.Width;
        }
    }
}
