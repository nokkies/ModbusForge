using System.Collections.Generic;
using System.Windows.Media;

namespace ModbusForge.Helpers
{
    /// <summary>
    /// Caches frozen <see cref="SolidColorBrush"/> instances by color to avoid
    /// creating and disposing brushes repeatedly during node-editor rendering.
    /// </summary>
    public static class BrushCache
    {
        private static readonly Dictionary<Color, Brush> _brushes = new();

        public static Brush GetBrush(Color color)
        {
            if (!_brushes.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(color);
                if (brush.CanFreeze)
                {
                    brush.Freeze();
                }

                _brushes[color] = brush;
            }

            return brush;
        }
    }
}
