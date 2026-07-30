using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.ViewModels;

namespace ModbusForge.Avalonia.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            DataContext = new AboutViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LinkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string url })
            {
                try
                {
                    if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Avalonia has no direct API; try opening with shell
                    }
                    else
                    {
                        using var process = new System.Diagnostics.Process();
                        process.StartInfo.UseShellExecute = true;
                        process.StartInfo.FileName = url;
                        process.Start();
                    }
                }
                catch (Exception)
                {
                    // ignore
                }
            }
        }
    }
}
