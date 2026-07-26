

using System.Windows;
using ModbusForge.Converters;
using ModbusForge.Views;
using Xunit;

namespace ModbusForge.Tests.Views
{
    public class BezierConnectionTests
    {
        [Fact]
        public void ComputeBezierControlPoints_ShortHorizontal_ExpectedBehavior()
        {
            var start = new Point(10, 10);
            var end = new Point(50, 20); // dx = 40 (end.X - start.X < 80)

            var (c1, c2) = VisualNodeEditor.ComputeBezierControlPoints(start, end);

            Assert.Equal(start.X + 40, c1.X);
            Assert.Equal(end.X - 40, c2.X);
        }

        [Fact]
        public void ComputeBezierControlPoints_LongHorizontal_ExpectedBehavior()
        {
            var start = new Point(10, 10);
            var end = new Point(210, 20); // dx = 100 * 0.5 = 100? No, 200 * 0.5 = 100

            var (c1, c2) = VisualNodeEditor.ComputeBezierControlPoints(start, end);

            Assert.Equal(110, c1.X); // 10 + 100
            Assert.Equal(110, c2.X); // 210 - 100
        }

        [Fact]
        public void ComputeBezierControlPoints_VerticalControlOffset_ExpectedBehavior()
        {
            var start = new Point(10, 10);
            var end = new Point(50, 100);

            var (c1, c2) = VisualNodeEditor.ComputeBezierControlPoints(start, end);

            Assert.Equal(start.Y, c1.Y);
            Assert.Equal(end.Y, c2.Y);
        }

        [Fact]
        public void ComputeBezierControlPoints_ReverseDirection_ExpectedBehavior()
        {
            var start = new Point(200, 10);
            var end = new Point(100, 20); // distance = 100, 100 * 0.5 = 50

            var (c1, c2) = VisualNodeEditor.ComputeBezierControlPoints(start, end);

            Assert.Equal(250, c1.X); // 200 + 50
            Assert.Equal(50, c2.X);  // 100 - 50
        }

        [Fact]
        public void ComputeBezierControlPoints_SamePoint_ExpectedBehavior()
        {
            var start = new Point(10, 10);
            var end = new Point(10, 10);

            // This should not throw and use the minimum 40 fallback
            var (c1, c2) = VisualNodeEditor.ComputeBezierControlPoints(start, end);

            Assert.Equal(50, c1.X);  // 10 + 40
            Assert.Equal(-30, c2.X); // 10 - 40
        }

        [Fact]
        public void BoolToDashArrayConverter_ConvertBack_ReturnsBindingDoNothing()
        {
            var converter = new BoolToDashArrayConverter();

            var result = converter.ConvertBack(true, typeof(bool), null!, System.Globalization.CultureInfo.InvariantCulture);

            Assert.Same(System.Windows.Data.Binding.DoNothing, result);
        }
    }
}
