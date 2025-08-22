using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using FluentModbus;
using System.Net.Sockets;

namespace ModbusForge.Services
{
    public class ModbusServerService : IModbusService, IDisposable
    {
        private readonly ModbusTcpServer _server;
        private bool _disposed = false;
        private readonly ILogger<ModbusServerService> _logger;
        private bool _isRunning = false;
        private const int DefaultPort = 502;

        public ModbusServerService(ILogger<ModbusServerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _server = new ModbusTcpServer();
            _logger.LogInformation("Modbus TCP server created");
        }

        public Task<ushort[]> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} input registers starting at {startAddress} (Unit ID: {unitId})");

                var src = _server.GetInputRegisterBuffer<short>();
                if (startAddress < 0 || count < 0 || startAddress + count > src.Length)
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested range is out of bounds");

                var result = new ushort[count];
                for (int i = 0; i < count; i++)
                    result[i] = unchecked((ushort)src[startAddress + i]);

                _logger.LogDebug($"Successfully read {result.Length} input registers");
                return result;
            });
        }

        public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} discrete inputs starting at {startAddress} (Unit ID: {unitId})");

                var buf = _server.GetDiscreteInputBuffer<byte>();
                var capacity = checked(buf.Length * 8);
                if (startAddress < 0 || count < 0 || startAddress + count > capacity)
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested discrete input range is out of bounds");

                var result = new bool[count];
                int remaining = count;
                int srcBitIndex = startAddress;
                int dstIndex = 0;

                while (remaining > 0)
                {
                    int byteIndex = srcBitIndex / 8;
                    int bitOffset = srcBitIndex % 8;
                    byte b = buf[byteIndex];
                    bool bit = (b & (1 << bitOffset)) != 0;
                    result[dstIndex++] = bit;
                    srcBitIndex++;
                    remaining--;
                }

                _logger.LogDebug($"Successfully read {result.Length} discrete inputs");
                return result;
            });
        }

        public bool IsConnected => _isRunning;

        public Task<bool> ConnectAsync(string ipAddress, int port)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (_isRunning)
                        return true;

                    var endpoint = new IPEndPoint(IPAddress.Any, port == 0 ? DefaultPort : port);
                    _server.Start(endpoint);
                    _isRunning = true;
                    _logger.LogInformation($"Modbus TCP server started on {endpoint}");

                    // Initialize some test data in the server buffer (first few registers)
                    var buf = _server.GetHoldingRegisterBuffer<short>();
                    var initCount = Math.Min(16, buf.Length);
                    for (int i = 0; i < initCount; i++)
                        buf[i] = (short)(i * 10);

                    return true;
                }
                catch (SocketException sockEx) when (sockEx.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    _isRunning = false;
                    int p = port == 0 ? DefaultPort : port;
                    _logger.LogWarning(sockEx, "Port {Port} is already in use. Server could not start.", p);
                    // Return false so the UI can present a friendly message and suggestions
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start Modbus TCP server");
                    _isRunning = false;
                    return false;
                }
            });
        }

        public Task DisconnectAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    if (_isRunning)
                    {
                        _server.Stop();
                        _isRunning = false;
                        _logger.LogInformation("Modbus TCP server stopped");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping Modbus TCP server");
                    throw;
                }
            });
        }

        public Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} holding registers starting at {startAddress} (Unit ID: {unitId})");

                var src = _server.GetHoldingRegisterBuffer<short>();
                if (startAddress < 0 || count < 0 || startAddress + count > src.Length)
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested range is out of bounds");

                var result = new ushort[count];
                for (int i = 0; i < count; i++)
                    result[i] = unchecked((ushort)src[startAddress + i]);

                _logger.LogDebug($"Successfully read {result.Length} registers");
                return result;
            });
        }

        public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"Writing value {value} to register {registerAddress} (Unit ID: {unitId})");

                    var buf = _server.GetHoldingRegisterBuffer<short>();
                    if (registerAddress < 0 || registerAddress >= buf.Length)
                        throw new ArgumentOutOfRangeException(nameof(registerAddress), "Register address is out of range");

                    buf[registerAddress] = unchecked((short)value);
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
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} coils starting at {startAddress} (Unit ID: {unitId})");

                var buf = _server.GetCoilBuffer<byte>(); // packed bits, LSB first
                var capacity = checked(buf.Length * 8);
                if (startAddress < 0 || count < 0 || startAddress + count > capacity)
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested coil range is out of bounds");

                var result = new bool[count];
                int remaining = count;
                int srcBitIndex = startAddress;
                int dstIndex = 0;

                while (remaining > 0)
                {
                    int byteIndex = srcBitIndex / 8;
                    int bitOffset = srcBitIndex % 8;
                    byte b = buf[byteIndex];
                    bool bit = (b & (1 << bitOffset)) != 0;
                    result[dstIndex++] = bit;
                    srcBitIndex++;
                    remaining--;
                }

                _logger.LogDebug($"Successfully read {result.Length} coils");
                return result;
            });
        }

        public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"Writing coil at {coilAddress} = {value} (Unit ID: {unitId})");

                    var buf = _server.GetCoilBuffer<byte>();
                    var capacity = checked(buf.Length * 8);
                    if (coilAddress < 0 || coilAddress >= capacity)
                        throw new ArgumentOutOfRangeException(nameof(coilAddress), "Coil address is out of range");

                    int byteIndex = coilAddress / 8;
                    int bitOffset = coilAddress % 8;
                    if (value)
                        buf[byteIndex] = (byte)(buf[byteIndex] | (1 << bitOffset));
                    else
                        buf[byteIndex] = (byte)(buf[byteIndex] & ~(1 << bitOffset));

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
                    try
                    {
                        if (_isRunning)
                            _server.Stop();
                    }
                    catch { /* ignore on dispose */ }
                    finally
                    {
                        _server?.Dispose();
                        _isRunning = false;
                    }
                }
                _disposed = true;
            }
        }
    }
}
