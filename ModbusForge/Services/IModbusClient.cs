using System;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    public interface IModbusClient : IDisposable
    {
        bool IsConnected { get; }
        Task<bool> ConnectAsync(string ipAddress, int port);
        Task DisconnectAsync();
        Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count);
        Task<ushort[]> ReadInputRegistersAsync(byte unitId, int startAddress, int count);
        Task<bool[]> ReadCoilsAsync(byte unitId, int startAddress, int count);
        Task<bool[]> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count);
        Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value);
        Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value);
    }
}
