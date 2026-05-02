using System;

namespace ModbusForge.Services
{
    public class LogMessageEventArgs : EventArgs
    {
        public string Message { get; }
        public LogMessageEventArgs(string message)
        {
            Message = message;
        }
    }
}
