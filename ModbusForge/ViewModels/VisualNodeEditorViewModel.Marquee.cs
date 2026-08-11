using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class VisualNodeEditorViewModel
    {
        private double _marqueeStartX;
        private double _marqueeStartY;
        private List<VisualNode> _marqueeBaseSelection = new();
        private bool _marqueeExtendSelection;

        [ObservableProperty]
        private bool _marqueeIsVisible;

        [ObservableProperty]
        private double _marqueeX;

        [ObservableProperty]
        private double _marqueeY;

        [ObservableProperty]
        private double _marqueeWidth;

        [ObservableProperty]
        private double _marqueeHeight;

        public void StartMarquee(double x, double y, bool extendSelection)
        {
            _marqueeStartX = x;
            _marqueeStartY = y;
            _marqueeExtendSelection = extendSelection;
            _marqueeBaseSelection = extendSelection ? SelectedNodes.ToList() : new List<VisualNode>();

            MarqueeX = x;
            MarqueeY = y;
            MarqueeWidth = 0;
            MarqueeHeight = 0;
            MarqueeIsVisible = true;

            if (!extendSelection)
            {
                ClearSelection();
            }
        }

        public void UpdateMarquee(double currentX, double currentY)
        {
            if (!MarqueeIsVisible)
            {
                return;
            }

            var left = Math.Min(_marqueeStartX, currentX);
            var top = Math.Min(_marqueeStartY, currentY);
            var width = Math.Abs(currentX - _marqueeStartX);
            var height = Math.Abs(currentY - _marqueeStartY);

            MarqueeX = left;
            MarqueeY = top;
            MarqueeWidth = width;
            MarqueeHeight = height;

            var intersecting = Config.Nodes
                .Where(node => NodeIntersectsMarquee(node, left, top, width, height))
                .ToList();

            var newSelection = _marqueeExtendSelection
                ? _marqueeBaseSelection.Union(intersecting).ToList()
                : intersecting;

            SetMarqueeSelection(newSelection);
        }

        public void EndMarquee()
        {
            MarqueeIsVisible = false;
            _marqueeStartX = 0;
            _marqueeStartY = 0;
            _marqueeBaseSelection = new List<VisualNode>();
            _marqueeExtendSelection = false;
        }

        private void SetMarqueeSelection(IReadOnlyList<VisualNode> nodes)
        {
            var current = SelectedNodes.ToList();
            if (current.Count == nodes.Count && current.All(nodes.Contains) && nodes.All(current.Contains))
            {
                return;
            }

            SetSelection(nodes, nodes.LastOrDefault());
        }

        private static bool NodeIntersectsMarquee(VisualNode node, double left, double top, double width, double height)
        {
            if (node is null
                || !double.IsFinite(node.X)
                || !double.IsFinite(node.Y)
                || !double.IsFinite(node.Width)
                || !double.IsFinite(node.Height))
            {
                return false;
            }

            var nodeLeft = node.X;
            var nodeTop = node.Y;
            var nodeRight = node.X + Math.Max(0, node.Width);
            var nodeBottom = node.Y + Math.Max(0, node.Height);

            var selRight = left + Math.Max(0, width);
            var selBottom = top + Math.Max(0, height);

            return !(nodeLeft > selRight || nodeRight < left || nodeTop > selBottom || nodeBottom < top);
        }
    }
}
