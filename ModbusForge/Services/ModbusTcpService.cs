using Modbus.Device;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;
using ModbusForge.Helpers;

namespace ModbusForge.Services
{
    public class ModbusTcpService : IModbusService, IDisposable
    {
        private IModbusMaster? _client;
        private TcpClient? _tcpClient;
        private bool _disposed = false;
        private readonly ILogger<ModbusTcpService> _logger;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private string? _lastIpAddress;
        private int _lastPort;

        public ModbusTcpService(ILogger<ModbusTcpService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("Modbus TCP client created");
        }

        public async Task<ushort[]> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!IsConnected)
                return null;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        _logger.LogDebug($"Reading {count} input registers starting at {startAddress} (Unit ID: {unitId})");
                        // NModbus uses 0-based protocol addresses, convert from 1-based UI address
                        ushort protocolAddress = (ushort)(startAddress > 0 ? startAddress - 1 : 0);
                        var registers = _client.ReadInputRegisters(unitId, protocolAddress, (ushort)count);
                        return registers;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading input registers");
                        HandleConnectionLoss();
                        return null;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<bool[]> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            if (!IsConnected)
                return null;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        _logger.LogDebug($"Reading {count} discrete inputs starting at {startAddress} (Unit ID: {unitId})");
                        // NModbus uses 0-based protocol addresses, convert from 1-based UI address
                        ushort protocolAddress = (ushort)(startAddress > 0 ? startAddress - 1 : 0);
                        var inputs = _client.ReadInputs(unitId, protocolAddress, (ushort)count);
                        return inputs;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading discrete inputs");
                        HandleConnectionLoss();
                        return null;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public bool IsConnected => _client != null && _tcpClient != null && _tcpClient.Connected;

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        _lastIpAddress = ipAddress;
                        _lastPort = port;
                        _tcpClient = new TcpClient();
                        _tcpClient.Connect(ipAddress, port);
                        _client = ModbusIpMaster.CreateIp(_tcpClient);
                        _logger.LogInformation($"Connected to Modbus server at {ipAddress}:{port}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to connect to Modbus server");
                        _client?.Dispose();
                        _client = null;
                        _tcpClient?.Close();
                        _tcpClient = null;
                        return false;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsConnected)
                {
                    _logger.LogInformation("Disconnecting from Modbus server");
                    _client?.Dispose();
                    _client = null;
                    _tcpClient?.Close();
                    _tcpClient = null;
                    _logger.LogInformation("Successfully disconnected from Modbus server");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from Modbus server");
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!IsConnected)
                return null;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        _logger.LogDebug($"Reading {count} holding registers starting at {startAddress} (Unit ID: {unitId})");
                        // NModbus uses 0-based protocol addresses, convert from 1-based UI address
                        ushort protocolAddress = (ushort)(startAddress > 0 ? startAddress - 1 : 0);
                        var registers = _client.ReadHoldingRegisters(unitId, protocolAddress, (ushort)count);
                        return registers;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading holding registers");
                        HandleConnectionLoss();
                        return null;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            if (!IsConnected)
                return;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        // NModbus uses 0-based protocol addresses, convert from 1-based UI address
                        ushort protocolAddress = (ushort)(registerAddress > 0 ? registerAddress - 1 : 0);
                        _client.WriteSingleRegister(unitId, protocolAddress, value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error writing single register");
                        HandleConnectionLoss();
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<bool[]> ReadCoilsAsync(byte unitId, int startAddress, int count)
        {
            if (!IsConnected)
                return null;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        _logger.LogDebug($"Reading {count} coils starting at {startAddress} (Unit ID: {unitId})");
                        // NModbus uses 0-based protocol addresses, convert from 1-based UI address
                        ushort protocolAddress = (ushort)(startAddress > 0 ? startAddress - 1 : 0);
                        var coils = _client.ReadCoils(unitId, protocolAddress, (ushort)count);
                        return coils;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading coils");
                        HandleConnectionLoss();
                        return null;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
        {
            if (!IsConnected)
                return;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        _logger.LogDebug($"Writing coil at {coilAddress} = {value} (Unit ID: {unitId})");
                        // NModbus uses 0-based protocol addresses, convert from 1-based UI address
                        ushort protocolAddress = (ushort)(coilAddress > 0 ? coilAddress - 1 : 0);
                        _client.WriteSingleCoil(unitId, protocolAddress, value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error writing single coil");
                        HandleConnectionLoss();
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private void HandleConnectionLoss()
        {
            _logger.LogInformation("Client is disconnected. Cleaning up connection.");
            try
            {
                _client?.Dispose();
                _client = null;
                _tcpClient?.Close();
                _tcpClient = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during explicit disconnect after connection loss.");
            }
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
                    _ioLock.Wait();
                    try
                    {
                        _client?.Dispose();
                        _tcpClient?.Close();
                    }
                    finally
                    {
                        _ioLock.Release();
                        _ioLock.Dispose();
                    }
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