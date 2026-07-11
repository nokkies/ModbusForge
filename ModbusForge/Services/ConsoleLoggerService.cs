using System;
using System.Collections.ObjectModel;

namespace ModbusForge.Services
{
    public class ConsoleLoggerService : IConsoleLoggerService, IDisposable
    {
        private readonly ISettingsService? _settingsService;
        private readonly IDispatcher _dispatcher;
        private const int Headroom = 50;

        public event EventHandler<LogMessageEventArgs>? LogMessageReceived;

        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        // Default constructor for backwards compatibility / tests without settings
        public ConsoleLoggerService(IDispatcher? dispatcher = null)
        {
            _dispatcher = dispatcher ?? new WpfDispatcher();
        }

        public ConsoleLoggerService(ISettingsService settingsService, IDispatcher? dispatcher = null)
        {
            _settingsService = settingsService;
            _dispatcher = dispatcher ?? new WpfDispatcher();
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

            _dispatcher.Invoke(() => AddAndTrim(message));
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
            _dispatcher.Invoke(TrimMessagesInternal);
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
