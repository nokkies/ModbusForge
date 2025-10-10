using System.Collections.ObjectModel;

namespace ModbusForge.Services
{
    public interface IConsoleLoggerService
    {
        ObservableCollection<string> LogMessages { get; }
        void Log(string message);
    }
}