using FluentModbus.Client;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    public class ModbusTcpService : IModbusService, IDisposable
    {
        private readonly ModbusTcpClient _client;
        private bool _disposed = false;

        public ModbusTcpService()
        {
            _client = new ModbusTcpClient();
            _client.ConnectTimeout = TimeSpan.FromSeconds(5);
            _client.ReadTimeout = TimeSpan.FromSeconds(5);
            _client.WriteTimeout = TimeSpan.FromSeconds(5);
        }

        public bool IsConnected => _client.IsConnected;

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            try
            {
                await _client.ConnectAsync(ipAddress, port);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_client.IsConnected)
            {
                await Task.Run(() => _client.Disconnect());
            }
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            return await _client.ReadHoldingRegistersAsync(unitId, (ushort)startAddress, (ushort)count);
        }

        public async Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            await _client.WriteSingleRegisterAsync(unitId, (ushort)registerAddress, value);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _client?.Dispose();
                }
                _disposed = true;
            }
        }

        ~ModbusTcpService()
        {
            Dispose(false);
        }
    }
}
