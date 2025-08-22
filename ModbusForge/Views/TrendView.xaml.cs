using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.ViewModels;
using System.ComponentModel;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModbusForge.Views
{
    public partial class TrendView : UserControl
    {
        public TrendView()
        {
            InitializeComponent();
            // Avoid resolving services during design-time to keep the XAML designer happy
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = App.ServiceProvider.GetRequiredService<TrendViewModel>();
            }
        }

        public void SaveChartAsPng(string path, int? width = null, int? height = null)
        {
            if (TrendChart == null) return;
            // Ensure layout is up to date
            TrendChart.UpdateLayout();
            int w = width ?? (int)Math.Max(1, Math.Ceiling(double.IsNaN(TrendChart.ActualWidth) || TrendChart.ActualWidth == 0 ? 800 : TrendChart.ActualWidth));
            int h = height ?? (int)Math.Max(1, Math.Ceiling(double.IsNaN(TrendChart.ActualHeight) || TrendChart.ActualHeight == 0 ? 400 : TrendChart.ActualHeight));

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);

            // Draw a white background, then the chart visual on top to avoid black/transparent backgrounds in PNG viewers
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                var vb = new VisualBrush(TrendChart)
                {
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
            }
            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }
    }
}
