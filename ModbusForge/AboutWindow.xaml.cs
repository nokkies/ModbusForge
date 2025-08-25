using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using System.Reflection;

namespace ModbusForge
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            string? ver = null;
            try
            {
                var asm = Application.ResourceAssembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var asmPath = asm?.Location;
                if (!string.IsNullOrWhiteSpace(asmPath))
                {
                    ver = FileVersionInfo.GetVersionInfo(asmPath)?.ProductVersion;
                }
                if (string.IsNullOrWhiteSpace(ver))
                {
                    ver = Process.GetCurrentProcess()?.MainModule?.FileVersionInfo?.ProductVersion;
                }
                if (string.IsNullOrWhiteSpace(ver))
                {
                    ver = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString();
                }
            }
            catch { }
            VersionText.Text = !string.IsNullOrWhiteSpace(ver) ? $"ModbusForge v{ver}" : "ModbusForge v1.2.0";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open link: {ex.Message}", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
