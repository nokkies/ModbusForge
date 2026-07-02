using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Win32;
using ModbusForge.Helpers;

namespace ModbusForge.Views
{
    public partial class TroubleshootingWindow : Window
    {
        public TroubleshootingWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                UrlHelper.OpenUrl(e.Uri.AbsoluteUri);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open link: {ex.Message}", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }

        private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"ModbusForge_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var diagnostics = BuildDiagnosticsReport();
                    File.WriteAllText(saveDialog.FileName, diagnostics);
                    MessageBox.Show("Diagnostics exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export diagnostics: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildDiagnosticsReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ModbusForge Diagnostics Report");
            sb.AppendLine("==============================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // Application Info
            sb.AppendLine("Application Information");
            sb.AppendLine("------------------------");
            sb.AppendLine($"Product: ModbusForge");
            sb.AppendLine($"Version: {GetApplicationVersion() ?? "Unknown"}");
            sb.AppendLine($"Executable: {Environment.ProcessPath}");
            sb.AppendLine();

            // System Info
            sb.AppendLine("System Information");
            sb.AppendLine("------------------");
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"OS Architecture: {RuntimeInformation.OSArchitecture}");
            sb.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"Machine Name: {Environment.MachineName}");
            sb.AppendLine($"User: {Environment.UserName}");
            sb.AppendLine();

            // Application Paths
            sb.AppendLine("Application Paths");
            sb.AppendLine("-----------------");
            sb.AppendLine($"Base Directory: {AppDomain.CurrentDomain.BaseDirectory}");
            sb.AppendLine($"AppData Path: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModbusForge")}");
            sb.AppendLine();

            // Configuration Files
            sb.AppendLine("Configuration Files");
            sb.AppendLine("-------------------");
            AppendFileInfo(sb, "appsettings.json", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json"));
            AppendFileInfo(sb, "settings.json", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModbusForge", "settings.json"));
            AppendFileInfo(sb, "connection-profiles.json", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModbusForge", "connection-profiles.json"));
            sb.AppendLine();

            // Crash Log
            sb.AppendLine("Crash Log");
            sb.AppendLine("---------");
            var crashLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            AppendFileContents(sb, crashLogPath, 100);
            sb.AppendLine();

            sb.AppendLine("End of Diagnostics Report");
            return sb.ToString();
        }

        private static string? GetApplicationVersion()
        {
            try
            {
                var procPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(procPath))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(procPath);
                    if (!string.IsNullOrWhiteSpace(versionInfo.ProductVersion))
                        return versionInfo.ProductVersion;
                }
                return Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void AppendFileInfo(StringBuilder sb, string name, string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    sb.AppendLine($"{name}: {path}");
                    sb.AppendLine($"  Size: {info.Length} bytes");
                    sb.AppendLine($"  Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    sb.AppendLine($"{name}: Not found ({path})");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{name}: Error reading info - {ex.Message}");
            }
        }

        private static void AppendFileContents(StringBuilder sb, string path, int maxLines)
        {
            try
            {
                if (!File.Exists(path))
                {
                    sb.AppendLine("No crash log found.");
                    return;
                }

                var lines = File.ReadAllLines(path);
                var startIndex = Math.Max(0, lines.Length - maxLines);
                for (int i = startIndex; i < lines.Length; i++)
                {
                    sb.AppendLine(lines[i]);
                }

                if (lines.Length > maxLines)
                {
                    sb.AppendLine($"... ({lines.Length - maxLines} earlier lines omitted)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error reading crash log: {ex.Message}");
            }
        }
    }
}
