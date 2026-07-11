using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Extensions.Logging;
using ModbusForge.Helpers;
using ModbusForge.Services;

namespace ModbusForge
{
    public partial class AboutWindow : Window
    {
        private readonly IDialogService _dialogService;

        public AboutWindow(ILogger<AboutWindow> logger, IDialogService? dialogService = null)
        {
            InitializeComponent();
            _dialogService = dialogService ?? new NullDialogService();
            string? ver = null;
            try
            {
                var asm = Application.ResourceAssembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                #if !NET5_0_OR_GREATER || !SINGLE_FILE
                var asmPath = asm?.Location;
                #else
                var asmPath = null;
                #endif
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
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to retrieve application version information.");
            }
            VersionText.Text = !string.IsNullOrWhiteSpace(ver) ? $"ModbusForge v{ver}" : "ModbusForge v2.1.3";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                UrlHelper.OpenUrl(e.Uri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                _dialogService.Show($"Failed to open link: {ex.Message}", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
