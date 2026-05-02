using System;
using System.Collections.ObjectModel;

namespace ModbusForge.Services
{
    public interface IConsoleLoggerService
    {
        event EventHandler<LogMessageEventArgs>? LogMessageReceived;
        ObservableCollection<string> LogMessages { get; }
        void Log(string message);
    }
}
