using System.Collections.Generic;
using System.Windows.Media;

namespace ModbusForge.Helpers
{
    /// <summary>
    /// Caches and reuses <see cref="SolidColorBrush"/> instances to avoid allocating
    /// the same brush repeatedly.
    /// </summary>
    public static class BrushCache
    {
        private static readonly Dictionary<Color, SolidColorBrush> Brushes = new();

        public static SolidColorBrush GetBrush(Color color)
        {
            if (!Brushes.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                Brushes[color] = brush;
            }

            return brush;
        }

        public static void Clear()
        {
            Brushes.Clear();
        }
    }
}
