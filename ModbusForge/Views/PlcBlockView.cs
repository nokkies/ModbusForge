using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
                or nameof(VisualNode.Plc) or nameof(VisualNode.Width) or nameof(VisualNode.Height))
            {
                InvalidateVisual();
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
            var frame = new Rect(inset, plc.CellSize, Math.Max(width - 2 * inset, 1), Math.Max(height - plc.CellSize, 1));
            var framePen = _node.HasError ? ErrorPen : _node.IsSelected ? SelectedPen : FramePen;

            context.DrawRectangle(FrameFill, framePen, frame);

            // Instance name above the frame (function blocks only), type name inside.
            if (!string.IsNullOrEmpty(plc.InstanceName))
            {
                DrawCentered(context, plc.InstanceName!, Regular, LabelFontSize, TextBrush, width / 2, plc.CellSize / 2);
            }

            DrawCentered(context, plc.TypeName, Bold, TypeNameFontSize, TextBrush, width / 2, plc.CellSize * 2);

            foreach (var pin in plc.Pins)
            {
                DrawPin(context, pin, width, inset);
            }
        }

        private static void DrawPin(DrawingContext context, PlcPin pin, double width, double inset)
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
                if (pin.ActualParameter is { } actual)
                {
                    DrawRightAligned(context, actual, Regular, LabelFontSize, ActualBrush, -TextGap, y);
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
                if (pin.ActualParameter is { } actual)
                {
                    DrawLeftAligned(context, actual, Regular, LabelFontSize, ActualBrush, width + TextGap, y);
                }
            }
        }

        private static FormattedText Format(string text, Typeface typeface, double size, IBrush brush)
            => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);

        private static void DrawCentered(DrawingContext context, string text, Typeface typeface, double size, IBrush brush, double centreX, double centreY)
        {
            var formatted = Format(text, typeface, size, brush);
            context.DrawText(formatted, new Point(centreX - formatted.Width / 2, centreY - formatted.Height / 2));
        }

        private static void DrawLeftAligned(DrawingContext context, string text, Typeface typeface, double size, IBrush brush, double left, double centreY)
        {
            var formatted = Format(text, typeface, size, brush);
            context.DrawText(formatted, new Point(left, centreY - formatted.Height / 2));
        }

        private static void DrawRightAligned(DrawingContext context, string text, Typeface typeface, double size, IBrush brush, double right, double centreY)
        {
            var formatted = Format(text, typeface, size, brush);
            context.DrawText(formatted, new Point(right - formatted.Width, centreY - formatted.Height / 2));
        }
    }
}
