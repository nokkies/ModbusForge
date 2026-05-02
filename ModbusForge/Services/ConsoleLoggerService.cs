using System;
using System.Collections.ObjectModel;

namespace ModbusForge.Services
{
    public class ConsoleLoggerService : IConsoleLoggerService
    {
        public event EventHandler<LogMessageEventArgs>? LogMessageReceived;

        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        public void Log(string message)
        {
            LogMessageReceived?.Invoke(this, new LogMessageEventArgs(message));
            LogMessages.Add(message);
        }
    }
}
