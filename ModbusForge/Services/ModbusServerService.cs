using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Modbus.Data;
using Modbus.Device;
using System.Net.Sockets;
using ModbusForge.Helpers;

namespace ModbusForge.Services
{
    public class ModbusServerService : IModbusService, IDisposable
    {
        private ModbusTcpSlave _slave;
        private TcpListener _listener;
        private DataStore _dataStore;
        private Task _listenTask;
        private CancellationTokenSource _cts;
        private bool _disposed = false;
        private readonly ILogger<ModbusServerService> _logger;
        private volatile bool _isRunning = false;
        private readonly object _stateLock = new object();
        private const int DefaultPort = 502;
        private const byte DefaultSlaveId = 1;
        private const int ShutdownTimeoutMs = 5000;

        public ModbusServerService(ILogger<ModbusServerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("Modbus TCP server created");
        }

        public Task<ushort[]> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} input registers starting at {startAddress} (Unit ID: {unitId})");

                lock (_stateLock)
                {
                    if (_dataStore == null)
                        throw new InvalidOperationException("Data store not initialized");

                    var registers = _dataStore.InputRegisters;
                    if (startAddress < 1 || count < 0 || startAddress + count - 1 > registers.Count)
                        throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested range is out of bounds");

                    var result = new ushort[count];
                    for (int i = 0; i < count; i++)
                        result[i] = registers[(ushort)(startAddress + i)];

                    _logger.LogDebug($"Successfully read {result.Length} input registers");
                    return result;
                }
            });
        }

        public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} discrete inputs starting at {startAddress} (Unit ID: {unitId})");

                var inputs = _dataStore.InputDiscretes;
                if (startAddress < 1 || count < 0 || startAddress + count - 1 > inputs.Count)
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested discrete input range is out of bounds");

                var result = new bool[count];
                for (int i = 0; i < count; i++)
                    result[i] = inputs[(ushort)(startAddress + i)];

                _logger.LogDebug($"Successfully read {result.Length} discrete inputs");
                return result;
            });
        }

        public bool IsConnected => _isRunning;

        public Task<bool> ConnectAsync(string ipAddress, int port)
        {
            return Task.Run(() =>
            {
                lock (_stateLock)
                {
                    try
                    {
                        if (_isRunning)
                            return true;

                        var endpoint = new IPEndPoint(IPAddress.Any, port == 0 ? DefaultPort : port);
                    
                    // Create data store
                    _dataStore = new DataStore();
                    
                    // Initialize holding registers to support addresses up to 10000
                    for (int i = 0; i < 10000; i++)
                    {
                        _dataStore.HoldingRegisters.Add(0);
                    }
                    
                    // Initialize input registers to support addresses up to 10000
                    for (int i = 0; i < 10000; i++)
                    {
                        _dataStore.InputRegisters.Add(0);
                    }
                    
                    // Initialize coils to support addresses up to 10000
                    for (int i = 0; i < 10000; i++)
                    {
                        _dataStore.CoilDiscretes.Add(false);
                    }
                    
                    // Initialize discrete inputs to support addresses up to 10000
                    for (int i = 0; i < 10000; i++)
                    {
                        _dataStore.InputDiscretes.Add(false);
                    }
                    
                    // Initialize some test data in holding registers
                    for (ushort i = 1; i <= 16; i++)
                        _dataStore.HoldingRegisters[i] = (ushort)(i * 10);
                    
                    // Create and start TCP listener
                    _listener = new TcpListener(endpoint);
                    _listener.Start();
                    
                    // Create slave
                    _slave = ModbusTcpSlave.CreateTcp(DefaultSlaveId, _listener);
                    _slave.DataStore = _dataStore;
                    
                    // Start listening for connections
                    _cts = new CancellationTokenSource();
                    _isRunning = true; // Set before starting listen task
                    _listenTask = Task.Run(() =>
                    {
                        try { _slave.Listen(); }
                        catch (Exception ex) { _logger.LogError(ex, "Listen task failed"); }
                    });
                    
                    _logger.LogInformation($"Modbus TCP server started on {endpoint}");
                    return true;
                    }
                    catch (SocketException sockEx) when (sockEx.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        CleanupResources();
                        _isRunning = false;
                    int p = port == 0 ? DefaultPort : port;
                    _logger.LogWarning(sockEx, "Port {Port} is already in use. Server could not start.", p);
                    return false;
                }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start Modbus TCP server");
                        CleanupResources();
                        _isRunning = false;
                        return false;
                    }
                }
            });
        }

        private void CleanupResources()
        {
            try
            {
                _cts?.Cancel();
                _slave?.Dispose();
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during resource cleanup");
            }
            finally
            {
                _slave = null;
                _listener = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // Data store access for simulation service
        public DataStore GetDataStore()
        {
            lock (_stateLock)
            {
                return _dataStore;
            }
        }

        public Task DisconnectAsync()
        {
            return Task.Run(() =>
            {
                lock (_stateLock)
                {
                    try
                    {
                        if (!_isRunning)
                            return;

                        _isRunning = false;
                        _cts?.Cancel();
                        _listener?.Stop();

                        // Wait for listen task to complete
                        if (_listenTask != null && !_listenTask.IsCompleted)
                        {
                            if (!_listenTask.Wait(ShutdownTimeoutMs))
                                _logger.LogWarning("Listen task did not complete within timeout");
                        }

                        CleanupResources();
                        _logger.LogInformation("Modbus TCP server stopped");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error stopping Modbus TCP server");
                        throw;
                    }
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

                var registers = _dataStore.HoldingRegisters;
                if (startAddress < 1 || count < 0 || startAddress + count - 1 > registers.Count)
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested range is out of bounds");

                var result = new ushort[count];
                for (int i = 0; i < count; i++)
                    result[i] = registers[(ushort)(startAddress + i)];

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

                    var registers = _dataStore.HoldingRegisters;
                    if (registerAddress < 1 || registerAddress > registers.Count)
                        throw new ArgumentOutOfRangeException(nameof(registerAddress), "Register address is out of range");

                    registers[(ushort)registerAddress] = value;
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

                var coils = _dataStore.CoilDiscretes;
                if (startAddress < 1 || count < 0 || startAddress + count - 1 > coils.Count)
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested coil range is out of bounds");

                var result = new bool[count];
                for (int i = 0; i < count; i++)
                    result[i] = coils[(ushort)(startAddress + i)];

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

                    var coils = _dataStore.CoilDiscretes;
                    if (coilAddress < 1 || coilAddress > coils.Count)
                        throw new ArgumentOutOfRangeException(nameof(coilAddress), "Coil address is out of range");

                    coils[(ushort)coilAddress] = value;
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
                        _cts?.Cancel();
                        _slave?.Dispose();
                        _listener?.Stop();
                    }
                    catch { /* ignore on dispose */ }
                    finally
                    {
                        _slave = null;
                        _listener = null;
                        _cts?.Dispose();
                        _cts = null;
                        _isRunning = false;
                    }
                }
                _disposed = true;
            }
        }
    }
}
