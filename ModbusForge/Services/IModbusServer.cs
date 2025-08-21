using System;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    public interface IModbusServer : IDisposable
    {
        bool IsRunning { get; }
        Task StartAsync(int port);
        Task StopAsync();
    }
}
