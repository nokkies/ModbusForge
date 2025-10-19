using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;
using Microsoft.Extensions.Logging;
using ModbusForge.Helpers;

namespace ModbusForge.Services
{
    public class ModbusService : IModbusService, IDisposable
    {
        private readonly ILogger<ModbusService> _logger;
        private readonly ModbusTcpClient _client;
        private bool _disposed = false;

        public ModbusService(ILogger<ModbusService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _client = new ModbusTcpClient();
            _logger.LogInformation("Modbus TCP client created");
        }

        public Task<ushort[]> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            try
            {
                _logger.LogDebug($"Reading {count} input registers starting at {startAddress} (Unit ID: {unitId})");
                return Task.Run(() =>
                {
                    var span = _client.ReadInputRegisters<ushort>(unitId, (ushort)startAddress, (ushort)count);
                    _logger.LogDebug($"Successfully read {span.Length} input registers");
                    return span.ToArray();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading input registers");
                throw;
            }
        }

        public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            try
            {
                _logger.LogDebug($"Reading {count} discrete inputs starting at {startAddress} (Unit ID: {unitId})");

                return Task.Run(() =>
                {
                    var bytes = _client.ReadDiscreteInputs(unitId, startAddress, count);
                    return BitConverterHelper.ToBooleanArray(bytes, count);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading discrete inputs");
                throw;
            }
        }

        public bool IsConnected => _client.IsConnected;

        public Task<bool> ConnectAsync(string ipAddress, int port)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (_client.IsConnected)
                    {
                        _client.Disconnect();
                    }

                    _logger.LogInformation($"Connecting to Modbus server at {ipAddress}:{port}");
                    var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
                    _client.Connect(endpoint, ModbusEndianness.BigEndian);
                    _logger.LogInformation($"Connected to Modbus server: {_client.IsConnected}");
                    return _client.IsConnected;
                }
                catch (Exception ex) when (ex is SocketException || ex is FormatException)
                {
                    _logger.LogError(ex, "Failed to connect to Modbus server");
                    return false;
                }
            });
        }

        public Task DisconnectAsync()
        {
            return Task.Run(() =>
            {
                _logger.LogInformation("Disconnecting from Modbus server");
                _client.Disconnect();
            });
        }

        public Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            try
            {
                _logger.LogDebug($"Reading {count} holding registers starting at {startAddress} (Unit ID: {unitId})");
                
                return Task.Run(() =>
                {
                    var span = _client.ReadHoldingRegisters<ushort>(unitId, (ushort)startAddress, (ushort)count);
                    _logger.LogDebug($"Successfully read {span.Length} registers");
                    return span.ToArray();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading holding registers");
                throw;
            }
        }

        public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            return Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"Writing value {value} to register {registerAddress} (Unit ID: {unitId})");
                    short signed = unchecked((short)value);
                    _client.WriteSingleRegister(unitId, (ushort)registerAddress, signed);
                    _logger.LogDebug("Successfully wrote to register");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error writing to register");
                    throw;
                }
            });
        }

        public Task<bool[]> ReadCoilsAsync(byte unitId, int startAddress, int count)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            try
            {
                _logger.LogDebug($"Reading {count} coils starting at {startAddress} (Unit ID: {unitId})");

                return Task.Run(() =>
                {
                    var bytes = _client.ReadCoils(unitId, startAddress, count);
                    return BitConverterHelper.ToBooleanArray(bytes, count);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading coils");
                throw;
            }
        }

        public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            return Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"Writing coil at {coilAddress} = {value} (Unit ID: {unitId})");
                    _client.WriteSingleCoil(unitId, coilAddress, value);
                    _logger.LogDebug("Successfully wrote coil");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error writing single coil");
                    throw;
                }
            });
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
                    _logger.LogInformation("Disposing Modbus client");
                    _client?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
