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
            rtb.Render(TrendChart);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }
    }
}
