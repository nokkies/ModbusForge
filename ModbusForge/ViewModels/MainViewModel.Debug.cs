using System;
using System.Collections.ObjectModel;
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

        // Method to add debug messages (called by reflection from VisualNodeEditor)
        public void AddDebugMessage(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var formattedMessage = $"[{timestamp}] {message}";
                
                // Add to UI collection only (file logging handled by ILogger infrastructure)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DebugMessages.Insert(0, formattedMessage);
                    
                    // Keep only the last 100 messages to prevent memory issues
                    while (DebugMessages.Count > 100)
                    {
                        DebugMessages.RemoveAt(DebugMessages.Count - 1);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add debug message: {Message}", message);
            }
        }
    }
}
