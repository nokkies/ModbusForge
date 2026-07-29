using System.Threading.Tasks;

namespace ModbusForge.Services;

public interface IApiServerService
{
    bool IsRunning { get; }
    Task StartAsync();
    Task StopAsync();
}
