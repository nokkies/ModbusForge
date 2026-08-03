using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// The side of a node a connection line should enter or exit.
    /// </summary>
    public enum PortSide
    {
        Left,
        Top,
        Bottom
    }

    public partial class VisualNodeEditorViewModel
    {
        private const double OrthogonalMargin = 8.0;
        private const double OrthogonalStep = 20.0;
        private const int OrthogonalMaxIterations = 60;

        partial void OnUseOrthogonalRoutingChanged(bool value) => RefreshConnectionLines();

        private static IList<Point> GetOrthogonalPoints(
            Point source,
            Point target,
            VisualNode sourceNode,
            VisualNode targetNode,
            IEnumerable<VisualNode> obstacles)
        {
            // Default L-shaped route: exit source to the right, horizontal, vertical, enter target from the left.
            double cornerX;
            if (target.X > source.X + OrthogonalMargin)
            {
                cornerX = (source.X + target.X) / 2.0;
            }
            else
            {
                cornerX = Math.Max(source.X, target.X) + OrthogonalStep * 2.0;
            }

            for (var i = 0; i < OrthogonalMaxIterations; i++)
            {
                var points = new List<Point>
                {
                    source,
                    new Point(cornerX, source.Y),
                    new Point(cornerX, target.Y),
                    target
                };

                if (!SegmentsIntersectNodes(points, obstacles, OrthogonalMargin))
                {
                    return points;
                }

                cornerX += OrthogonalStep;
            }

            return new List<Point>
            {
                source,
                new Point(cornerX, source.Y),
                new Point(cornerX, target.Y),
                target
            };
        }

        private static bool SegmentsIntersectNodes(IList<Point> points, IEnumerable<VisualNode> obstacles, double margin)
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];

                foreach (var node in obstacles)
                {
                    if (ReferenceEquals(node, null))
                        continue;

                    var rect = new Rect(
                        node.X - margin,
                        node.Y - margin,
                        node.Width + margin * 2.0,
                        node.Height + margin * 2.0);

                    if (SegmentIntersectsRect(a, b, rect))
                        return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersectsRect(Point a, Point b, Rect rect)
        {
            // Fast rejection via bounding boxes.
            var minX = Math.Min(a.X, b.X);
            var maxX = Math.Max(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxY = Math.Max(a.Y, b.Y);

            if (maxX < rect.X ||
                minX > rect.X + rect.Width ||
                maxY < rect.Y ||
                minY > rect.Y + rect.Height)
            {
                return false;
            }

            // Quick accept if an endpoint is inside the rectangle.
            if (rect.Contains(a) || rect.Contains(b))
                return true;

            // Test the segment against each edge of the rectangle.
            var topLeft = new Point(rect.X, rect.Y);
            var topRight = new Point(rect.X + rect.Width, rect.Y);
            var bottomLeft = new Point(rect.X, rect.Y + rect.Height);
            var bottomRight = new Point(rect.X + rect.Width, rect.Y + rect.Height);

            if (LineIntersectsLine(a, b, topLeft, topRight)) return true;
            if (LineIntersectsLine(a, b, topRight, bottomRight)) return true;
            if (LineIntersectsLine(a, b, bottomRight, bottomLeft)) return true;
            if (LineIntersectsLine(a, b, bottomLeft, topLeft)) return true;

            return false;
        }

        private static bool LineIntersectsLine(Point a1, Point a2, Point b1, Point b2)
        {
            var denominator = (a2.X - a1.X) * (b2.Y - b1.Y) - (a2.Y - a1.Y) * (b2.X - b1.X);
            if (Math.Abs(denominator) < 1e-9)
                return false;

            var ua = ((b1.X - a1.X) * (b2.Y - b1.Y) - (b1.Y - a1.Y) * (b2.X - b1.X)) / denominator;
            var ub = ((b1.X - a1.X) * (a2.Y - a1.Y) - (b1.Y - a1.Y) * (a2.X - a1.X)) / denominator;

            return ua >= 0.0 && ua <= 1.0 && ub >= 0.0 && ub <= 1.0;
        }
    }
}
