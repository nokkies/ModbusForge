using System.Collections.ObjectModel;
using System.Windows;

namespace ModbusForge.Services
{
    public class ConsoleLoggerService : IConsoleLoggerService
    {
        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        public void Log(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Add(message);
            });
        }
    }
}