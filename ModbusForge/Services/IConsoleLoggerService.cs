using System;

namespace ModbusForge.Services
{
    public interface IConsoleLoggerService
    {
        event EventHandler<LogMessageEventArgs>? LogMessageReceived;
        void Log(string message);
    }
}
