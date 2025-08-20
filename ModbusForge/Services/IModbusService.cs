using System;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    public interface IModbusService : IDisposable
    {
        bool IsConnected { get; }
        
        // For client compatibility, but not used in server mode
        Task<bool> ConnectAsync(string ipAddress, int port);
        Task DisconnectAsync();
        
        // Modbus operations
        Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count);
        Task<ushort[]> ReadInputRegistersAsync(byte unitId, int startAddress, int count);
        Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value);

        // Coil operations
        Task<bool[]> ReadCoilsAsync(byte unitId, int startAddress, int count);
        Task<bool[]> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count);
        Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value);
    }
}
