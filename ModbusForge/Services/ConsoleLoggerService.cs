using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace ModbusForge.Services
{
    public class ConsoleLoggerService : IConsoleLoggerService, IDisposable
    {
        private readonly ISettingsService? _settingsService;
        private const int Headroom = 50;

        public event EventHandler<LogMessageEventArgs>? LogMessageReceived;

        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        // Default constructor for backwards compatibility / tests without settings
        public ConsoleLoggerService()
        {
        }

        public ConsoleLoggerService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            if (_settingsService != null)
            {
                _settingsService.SettingsChanged += OnSettingsChanged;
            }
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            TrimMessages();
        }

        public void Log(string message)
        {
            LogMessageReceived?.Invoke(this, new LogMessageEventArgs(message));

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => AddAndTrim(message));
            }
            else
            {
                AddAndTrim(message);
            }
        }

        private void AddAndTrim(string message)
        {
            LogMessages.Add(message);

            int cap = _settingsService?.MaxConsoleMessages ?? 1000;

            if (cap <= 0)
            {
                LogMessages.Clear();
                return;
            }

            if (LogMessages.Count > cap + Headroom)
            {
                while (LogMessages.Count > cap)
                {
                    LogMessages.RemoveAt(0);
                }
            }
        }

        private void TrimMessages()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(TrimMessagesInternal);
            }
            else
            {
                TrimMessagesInternal();
            }
        }

        private void TrimMessagesInternal()
        {
            int cap = _settingsService?.MaxConsoleMessages ?? 1000;

            if (cap <= 0)
            {
                LogMessages.Clear();
                return;
            }

            // Here we strictly trim down to cap immediately on settings change,
            // no headroom, to reflect the user's new preference immediately.
            while (LogMessages.Count > cap)
            {
                LogMessages.RemoveAt(0);
            }
        }

        public void Dispose()
        {
            if (_settingsService != null)
            {
                _settingsService.SettingsChanged -= OnSettingsChanged;
            }
            GC.SuppressFinalize(this);
        }
    }
}
