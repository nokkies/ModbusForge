using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NModbus.Data;
using NModbus;
using System.Net.Sockets;
using ModbusForge.Helpers;

namespace ModbusForge.Services
{
    public class ModbusServerService : IModbusService, IDisposable
    {
        private IModbusSlaveNetwork? _slaveNetwork;
        private TcpListener? _listener;
        private ISlaveDataStore? _dataStore;
        private Task? _listenTask;
        private CancellationTokenSource? _cts;
        private readonly IModbusFactory _factory = new ModbusFactory();
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

        public async Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return await Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} input registers starting at {startAddress} (Unit ID: {unitId})");

                lock (_stateLock)
                {
                    if (_dataStore == null)
                        throw new InvalidOperationException("Data store not initialized");

                    var registers = _dataStore.InputRegisters;
                    // NModbus 3 uses 0-based indexing for limits. Let's just catch out of bounds or read directly.
                    try
                    {
                        var result = registers.ReadPoints((ushort)(startAddress > 0 ? startAddress - 1 : 0), (ushort)count);
                        _logger.LogDebug($"Successfully read {result.Length} input registers");
                        return result;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested range is out of bounds");
                    }
                }
            });
        }

        public async Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return await Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} discrete inputs starting at {startAddress} (Unit ID: {unitId})");

                var inputs = _dataStore?.CoilInputs;
                if (inputs == null)
                    throw new InvalidOperationException("Data store not initialized");

                try
                {
                    var result = inputs.ReadPoints((ushort)(startAddress > 0 ? startAddress - 1 : 0), (ushort)count);
                    _logger.LogDebug($"Successfully read {result.Length} discrete inputs");
                    return result;
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested discrete input range is out of bounds");
                }
            });
        }

        public bool IsConnected => _isRunning;

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            return await Task.Run(() =>
            {
                lock (_stateLock)
                {
                    try
                    {
                        if (_isRunning)
                            return true;

                        var endpoint = new IPEndPoint(IPAddress.Any, port == 0 ? DefaultPort : port);
                    
                    // Create data store
                    _dataStore = new SlaveDataStore();
                    
                    // Initialize some test data in holding registers
                    // NModbus 3 uses 0-based index for API
                    for (ushort i = 1; i <= 16; i++)
                    {
                        _dataStore.HoldingRegisters.WritePoints((ushort)(i - 1), new ushort[] { (ushort)(i * 10) });
                    }
                    
                    // Create and start TCP listener
                    _listener = new TcpListener(endpoint);
                    _listener.Start();
                    
                    // Create slave network
                    _slaveNetwork = _factory.CreateSlaveNetwork(_listener);
                    IModbusSlave slave = _factory.CreateSlave(DefaultSlaveId, _dataStore);
                    _slaveNetwork.AddSlave(slave);
                    
                    // Start listening for connections
                    _cts = new CancellationTokenSource();
                    _isRunning = true; // Set before starting listen task
                    _listenTask = Task.Run(async () =>
                    {
                        try { await _slaveNetwork.ListenAsync(_cts.Token); }
                        catch (OperationCanceledException) { }
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
                _slaveNetwork?.Dispose();
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during resource cleanup");
            }
            finally
            {
                _slaveNetwork = null;
                _listener = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // Data store access for simulation service
        public ISlaveDataStore? GetDataStore()
        {
            lock (_stateLock)
            {
                return _dataStore;
            }
        }

        public async Task DisconnectAsync()
        {
            await Task.Run(() =>
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

        public async Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return await Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} holding registers starting at {startAddress} (Unit ID: {unitId})");

                var registers = _dataStore?.HoldingRegisters;
                if (registers == null)
                    throw new InvalidOperationException("Data store not initialized");

                try
                {
                    var result = registers.ReadPoints((ushort)(startAddress > 0 ? startAddress - 1 : 0), (ushort)count);
                    _logger.LogDebug($"Successfully read {result.Length} registers");
                    return result;
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested range is out of bounds");
                }
            });
        }

        public async Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            await Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"Writing value {value} to register {registerAddress} (Unit ID: {unitId})");

                    var registers = _dataStore?.HoldingRegisters;
                    if (registers == null)
                        throw new InvalidOperationException("Data store not initialized");

                    try
                    {
                        registers.WritePoints((ushort)(registerAddress > 0 ? registerAddress - 1 : 0), new ushort[] { value });
                        _logger.LogDebug("Successfully wrote to register");
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        throw new ArgumentOutOfRangeException(nameof(registerAddress), "Register address is out of range");
                    }
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
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return await Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} coils starting at {startAddress} (Unit ID: {unitId})");

                var coils = _dataStore?.CoilDiscretes;
                if (coils == null)
                    throw new InvalidOperationException("Data store not initialized");

                try
                {
                    var result = coils.ReadPoints((ushort)(startAddress > 0 ? startAddress - 1 : 0), (ushort)count);
                    _logger.LogDebug($"Successfully read {result.Length} coils");
                    return result;
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new ArgumentOutOfRangeException(nameof(startAddress), "Requested coil range is out of bounds");
                }
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

                    var coils = _dataStore?.CoilDiscretes;
                    if (coils == null)
                        throw new InvalidOperationException("Data store not initialized");

                    try
                    {
                        coils.WritePoints((ushort)(coilAddress > 0 ? coilAddress - 1 : 0), new bool[] { value });
                        _logger.LogDebug("Successfully wrote coil");
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        throw new ArgumentOutOfRangeException(nameof(coilAddress), "Coil address is out of range");
                    }
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
                        _slaveNetwork?.Dispose();
                        _listener?.Stop();
                    }
                    catch { /* ignore on dispose */ }
                    finally
                    {
                        _slaveNetwork = null;
                        _listener = null;
                        _cts?.Dispose();
                        _cts = null;
                        _isRunning = false;
                    }
                }
                _disposed = true;
            }
        }

        public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
        {
            // Server mode diagnostics - check if server is running and listening
            var result = new ConnectionDiagnosticResult();
            
            if (_isRunning && _listener != null)
            {
                result.TcpConnected = true;
                result.ModbusResponding = true;
                result.LocalEndpoint = _listener.LocalEndpoint?.ToString() ?? $"0.0.0.0:{port}";
                result.RemoteEndpoint = "Server Mode";
            }
            else
            {
                result.TcpConnected = false;
                result.TcpError = "Server is not running";
            }
            
            return Task.FromResult(result);
        }
    }
}
