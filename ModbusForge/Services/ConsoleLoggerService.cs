using System;

namespace ModbusForge.Services
{
    public class ConsoleLoggerService : IConsoleLoggerService
    {
        public event EventHandler<LogMessageEventArgs>? LogMessageReceived;

        public void Log(string message)
        {
            LogMessageReceived?.Invoke(this, new LogMessageEventArgs(message));
        }
    }
}
