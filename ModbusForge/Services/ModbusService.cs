using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Modbus.Device;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    public class ModbusService : IModbusService, IDisposable
    {
        private readonly ILogger<ModbusService> _logger;
        private IModbusMaster? _client;
        private TcpClient? _tcpClient;
        private bool _disposed = false;

        public ModbusService(ILogger<ModbusService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("Modbus TCP client created");
        }

        public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            try
            {
                _logger.LogDebug($"Reading {count} input registers starting at {startAddress} (Unit ID: {unitId})");
                return Task.Run(() =>
                {
                    var registers = _client?.ReadInputRegisters(unitId, (ushort)startAddress, (ushort)count);
                    if (registers == null) return null;
                    _logger.LogDebug($"Successfully read {registers.Length} input registers");
                    return registers;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading input registers");
                throw;
            }
        }

        public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            try
            {
                _logger.LogDebug($"Reading {count} discrete inputs starting at {startAddress} (Unit ID: {unitId})");
                return Task.Run(() =>
                {
                    var inputs = _client?.ReadInputs(unitId, (ushort)startAddress, (ushort)count);
                    if (inputs == null) return null;
                    _logger.LogDebug($"Successfully read {inputs.Length} discrete inputs");
                    return inputs;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading discrete inputs");
                throw;
            }
        }

        public bool IsConnected => _client != null && _tcpClient != null && _tcpClient.Connected;

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (IsConnected)
                    {
                        _client?.Dispose();
                        _tcpClient?.Close();
                    }

                    _logger.LogInformation($"Connecting to Modbus server at {ipAddress}:{port}");
                    _tcpClient = new TcpClient();
                    _tcpClient.Connect(ipAddress, port);
                    _client = ModbusIpMaster.CreateIp(_tcpClient);
                    _logger.LogInformation($"Connected to Modbus server: {IsConnected}");
                    return true;
                }
                catch (Exception ex) when (ex is SocketException || ex is FormatException)
                {
                    _logger.LogError(ex, "Failed to connect to Modbus server");
                    _client?.Dispose();
                    _client = null;
                    _tcpClient?.Close();
                    _tcpClient = null;
                    return false;
                }
            });
        }

        public async Task DisconnectAsync()
        {
            await Task.Run(() =>
            {
                _logger.LogInformation("Disconnecting from Modbus server");
                _client?.Dispose();
                _client = null;
                _tcpClient?.Close();
                _tcpClient = null;
            });
        }

        public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            try
            {
                _logger.LogDebug($"Reading {count} holding registers starting at {startAddress} (Unit ID: {unitId})");
                return Task.Run(() =>
                {
                    var registers = _client?.ReadHoldingRegisters(unitId, (ushort)startAddress, (ushort)count);
                    if (registers == null) return null;
                    _logger.LogDebug($"Successfully read {registers.Length} registers");
                    return registers;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading holding registers");
                throw;
            }
        }

        public async Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            await Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"Writing value {value} to register {registerAddress} (Unit ID: {unitId})");
                    _client?.WriteSingleRegister(unitId, (ushort)registerAddress, value);
                    _logger.LogDebug($"Successfully wrote register {registerAddress} with value {value}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error writing to register");
                    throw;
                }
            });
        }

        public async Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            try
            {
                _logger.LogDebug($"Reading {count} coils starting at {startAddress} (Unit ID: {unitId})");

                return await Task.Run(() =>
                {
                    var coils = _client?.ReadCoils(unitId, (ushort)startAddress, (ushort)count);
                    if (coils == null) return Array.Empty<bool>();
                    _logger.LogDebug($"Successfully read {coils.Length} coils");
                    return coils;
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
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            return Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"Writing coil at {coilAddress} = {value} (Unit ID: {unitId})");
                    _client?.WriteSingleCoil(unitId, (ushort)coilAddress, value);
                    _logger.LogDebug($"Successfully wrote coil {coilAddress} with value {value}");
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
                    _tcpClient?.Close();
                }
                _disposed = true;
            }
        }

        public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
        {
            // Simple implementation - just return current connection state
            var result = new ConnectionDiagnosticResult
            {
                TcpConnected = IsConnected,
                ModbusResponding = IsConnected,
                TcpError = IsConnected ? string.Empty : "Not connected"
            };
            return Task.FromResult(result);
        }
    }
}
