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
            try
            {
                var ver = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location)?.ProductVersion ?? string.Empty;
                VersionText.Text = !string.IsNullOrWhiteSpace(ver) ? $"ModbusForge v{ver}" : "ModbusForge v1.1.2";
            }
            catch
            {
                VersionText.Text = "ModbusForge v1.1.2";
            }
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
