using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace ModbusForge.ViewModels
{
    public partial class MainViewModel
    {
        // Debug Messages Collection for Debug Tab
        [ObservableProperty]
        private ObservableCollection<string> _debugMessages = new ObservableCollection<string>();

        private static readonly string DebugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModbusForge",
            "Logs",
            $"debug_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        );

        // Method to add debug messages (called by reflection from VisualNodeEditor)
        public void AddDebugMessage(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var formattedMessage = $"[{timestamp}] {message}";
                
                // Add to UI collection
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DebugMessages.Insert(0, formattedMessage);
                    
                    // Keep only the last 100 messages to prevent memory issues
                    while (DebugMessages.Count > 100)
                    {
                        DebugMessages.RemoveAt(DebugMessages.Count - 1);
                    }
                });

                // Also write to log file
                WriteToDebugLog(formattedMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add debug message: {Message}", message);
            }
        }

        private void WriteToDebugLog(string message)
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(DebugLogPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Append to log file
                File.AppendAllText(DebugLogPath, message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Fallback to VS Debug if file logging fails
                System.Diagnostics.Debug.WriteLine($"Failed to write to debug log: {ex.Message}");
            }
        }

        // Method to get the current debug log path (for user reference)
        public string GetDebugLogPath()
        {
            return DebugLogPath;
        }
    }
}
