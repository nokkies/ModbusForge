using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.Services;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class TrendView : UserControl
    {
        public TrendView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void SaveChartAsPng(string path, int? width = null, int? height = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A destination path is required.", nameof(path));

            TrendChart.UpdateLayout();
            var bounds = TrendChart.Bounds;
            var pixelWidth = Math.Max(1, width ?? (int)Math.Ceiling(bounds.Width > 0 ? bounds.Width : 800));
            var pixelHeight = Math.Max(1, height ?? (int)Math.Ceiling(bounds.Height > 0 ? bounds.Height : 400));

            using var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
            bitmap.Render(TrendChart);
            bitmap.Save(path);
        }

        private async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            var fileDialogService = GetFileDialogService();
            if (fileDialogService is null)
            {
                SetStatus("File dialog service not available.");
                return;
            }

            try
            {
                var path = await fileDialogService.ShowSaveFileDialogAsync(
                    "Export PNG",
                    "PNG Image (*.png)|*.png|All files (*.*)|*.*",
                    "trend.png");

                if (string.IsNullOrWhiteSpace(path)) return;

                SaveChartAsPng(path);
                SetStatus($"Chart exported to {System.IO.Path.GetFileName(path)}.");
            }
            catch (Exception ex)
            {
                SetStatus($"Export PNG failed: {ex.Message}");
            }
        }

        private static IFileDialogService? GetFileDialogService()
        {
            return (global::Avalonia.Application.Current as global::ModbusForge.Avalonia.App)?.Services?.GetService<IFileDialogService>();
        }

        private void SetStatus(string message)
        {
            if (DataContext is TrendViewModel viewModel)
            {
                viewModel.StatusMessage = message;
            }
        }
    }
}
