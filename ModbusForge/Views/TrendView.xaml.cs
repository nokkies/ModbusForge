using System.Windows.Controls;
using ModbusForge.ViewModels;
using System.ComponentModel;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ModbusForge.Views
{
    public partial class TrendView : UserControl
    {
        public TrendView()
        {
            InitializeComponent();
            Unloaded += TrendView_Unloaded;
        }

        private void TrendView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
                DataContext = null;
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

        private string GetDefaultExportFolder()
        {
            try
            {
                var folder = Path.GetFullPath("Exports");
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch
            {
                return Environment.CurrentDirectory;
            }
        }

        private void ExportPng_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not TrendViewModel vm) return;

            var dlg = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|All files (*.*)|*.*",
                FileName = "trend.png",
                InitialDirectory = GetDefaultExportFolder()
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    SaveChartAsPng(dlg.FileName);
                    vm.DialogService.Show("PNG export complete.", "Trend", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    vm.DialogService.Show($"PNG export failed: {ex.Message}", "Trend", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
