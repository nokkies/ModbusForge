using System.Threading.Tasks;

namespace ModbusForge.Services
{
    public interface IModbusService
    {
        bool IsConnected { get; }
        Task<bool> ConnectAsync(string ipAddress, int port);
        Task DisconnectAsync();
        Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count);
        Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value);
    }
}
