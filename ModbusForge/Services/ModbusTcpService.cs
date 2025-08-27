using FluentModbus;
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
        private readonly ModbusTcpClient _client;
        private bool _disposed = false;
        private readonly ILogger<ModbusTcpService> _logger;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);

        public ModbusTcpService(ILogger<ModbusTcpService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _client = new ModbusTcpClient();
            _logger.LogInformation("Modbus TCP client created");
        }

        public async Task<ushort[]> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _logger.LogDebug($"Reading {count} input registers starting at {startAddress} (Unit ID: {unitId})");
                return await Task.Run(() =>
                {
                    var span = _client.ReadInputRegisters<ushort>(unitId, (ushort)startAddress, (ushort)count);
                    return span.ToArray();
                }).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error reading input registers");
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error reading input registers");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading input registers");
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<bool[]> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            await _ioLock.WaitAsync();
            try
            {
                _logger.LogDebug($"Reading {count} discrete inputs starting at {startAddress} (Unit ID: {unitId})");
                return await Task.Run(() =>
                {
                    var bytes = _client.ReadDiscreteInputs(unitId, startAddress, count);
                    return BitConverterHelper.ToBooleanArray(bytes, count);
                });
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error reading discrete inputs");
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error reading discrete inputs");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading discrete inputs");
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public bool IsConnected => _client.IsConnected;

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
                        _client.Connect(endpoint, ModbusEndianness.BigEndian);
                        _logger.LogInformation($"Connected to Modbus server at {ipAddress}:{port}");
                        return _client.IsConnected;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to connect to Modbus server");
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
                if (_client.IsConnected)
                {
                    _logger.LogInformation("Disconnecting from Modbus server");
                    _client.Disconnect();
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
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _logger.LogDebug($"Reading {count} holding registers starting at {startAddress} (Unit ID: {unitId})");
                
                return await Task.Run(() => 
                {
                    var registers = _client.ReadHoldingRegisters<ushort>(unitId, (ushort)startAddress, (ushort)count);
                    return registers.ToArray();
                }).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error reading holding registers");
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error reading holding registers");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading holding registers");
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    // Convert ushort to short for FluentModbus
                    short signedValue = (short)value;
                    _client.WriteSingleRegister(unitId, (ushort)registerAddress, signedValue);
                }).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error writing single register");
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error writing single register");
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<bool[]> ReadCoilsAsync(byte unitId, int startAddress, int count)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _logger.LogDebug($"Reading {count} coils starting at {startAddress} (Unit ID: {unitId})");

                return await Task.Run(() =>
                {
                    var bytes = _client.ReadCoils(unitId, startAddress, count);
                    return BitConverterHelper.ToBooleanArray(bytes, count);
                }).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error reading coils");
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error reading coils");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading coils");
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
        {
            if (!_client.IsConnected)
                throw new InvalidOperationException("Not connected to Modbus server");

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _logger.LogDebug($"Writing coil at {coilAddress} = {value} (Unit ID: {unitId})");
                await Task.Run(() =>
                {
                    _client.WriteSingleCoil(unitId, coilAddress, value);
                }).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error writing single coil");
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error writing single coil");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing single coil");
                throw;
            }
            finally
            {
                _ioLock.Release();
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
                    try { _ioLock.Wait(); } catch { }
                    _client?.Dispose();
                    try { _ioLock.Release(); } catch { }
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
