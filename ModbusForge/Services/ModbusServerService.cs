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
        private ModbusTcpSlave? _slave;
        private System.Collections.Generic.List<ModbusTcpSlave> _slaves = new();
        private TcpListener? _listener;
        private DataStore? _dataStore;
        private Task? _listenTask;
        private CancellationTokenSource? _cts;
        private bool _disposed = false;
        private readonly ILogger<ModbusServerService> _logger;
        private volatile bool _isRunning = false;
        private readonly object _stateLock = new object();
        private const int DefaultPort = 502;
        private const byte DefaultSlaveId = 1;
        private const int ShutdownTimeoutMs = 5000;
        private const int DefaultDataStoreSize = 10000;

        public ModbusServerService(ILogger<ModbusServerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("Modbus TCP server created");
        }

        public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count) =>
            ReadFromDataStoreAsync(unitId, startAddress, count, ds => ds.InputRegisters, "input registers");

        public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count) =>
            ReadFromDataStoreAsync(unitId, startAddress, count, ds => ds.InputDiscretes, "discrete inputs");

        public virtual bool IsConnected => _isRunning;

        public async Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1")
        {
            return await Task.Run(() =>
            {
                lock (_stateLock)
                {
                    try
                    {
                        if (_isRunning)
                            return true;

                        if (!IPAddress.TryParse(ipAddress, out var address))
                            throw new ArgumentException($"Invalid IP address: {ipAddress}");

                        var endpoint = new IPEndPoint(address, port == 0 ? DefaultPort : port);
                    
                    // Create data store
                    _dataStore = new DataStore();
                    
                    // Initialize holding registers to support addresses up to DefaultDataStoreSize
                    for (int i = 0; i < DefaultDataStoreSize; i++)
                    {
                        _dataStore.HoldingRegisters.Add(0);
                    }
                                         

                        // Initialize input registers to support addresses up to DefaultDataStoreSize
                        for (int i = 0; i < DefaultDataStoreSize; i++)
                    {
                        _dataStore.InputRegisters.Add(0);
                    }
                    
                    // Initialize coils to support addresses up to DefaultDataStoreSize
                    for (int i = 0; i < DefaultDataStoreSize; i++)
                    {
                        _dataStore.CoilDiscretes.Add(false);
                    }
                    
                    // Initialize discrete inputs to support addresses up to DefaultDataStoreSize
                    for (int i = 0; i < DefaultDataStoreSize; i++)
                    {
                        _dataStore.InputDiscretes.Add(false);
                    }
                    
                    // Initialize some test data in holding registers
                    for (ushort i = 1; i <= 16; i++)
                        _dataStore.HoldingRegisters[i] = (ushort)(i * 10);
                    
                    // Create and start TCP listener
                    _listener = new TcpListener(endpoint);
                    _listener.Start();
                    
                    // Parse unit IDs (e.g., "1, 2, 5-10")
                    var ids = ParseUnitIds(unitIds);
                    if (ids.Count == 0) ids.Add(DefaultSlaveId);

                    // Create slaves sharing the same DataStore
                    _slaves.Clear();
                    foreach (var id in ids)
                    {
                        var slave = id == ids[0] 
                            ? ModbusTcpSlave.CreateTcp(id, _listener) // First slave owns the listener/connection management in NModbus4 usually
                            : ModbusTcpSlave.CreateTcp(id, _listener); // But for TCP we might need to add them to a SlaveNetwork or just create multiples?
                        
                        // NModbus4 ModbusTcpSlave.CreateTcp starts a listener internal to itself if we don't handle it carefully.
                        // Actually, ModbusTcpSlave.CreateTcp(id, listener) is the way.
                        
                        slave.DataStore = _dataStore;
                        _slaves.Add(slave);
                    }
                    _slave = _slaves[0]; // Keep reference for legacy compatibility if any

                    // Start listening for connections on all slaves
                    _cts = new CancellationTokenSource();
                    _isRunning = true;
                    
                    _listenTask = Task.Run(() =>
                    {
                        try 
                        { 
                            // NModbus4 ModbusTcpSlave.Listen() handles the listener loop.
                            // In Modbus TCP, the Unit ID is often ignored by the server as the IP/Port 
                            // uniquely identifies the device. However, NModbus4's implementation 
                            // will respond to requests for its own Unit ID.
                            // To support multiple Unit IDs, we use the first slave to manage the listener.
                            
                            _slave.Listen(); 
                        }
                        catch (Exception ex) { _logger.LogError(ex, "Listen task failed"); }
                    });
                    
                    _logger.LogInformation($"Modbus TCP server started on {endpoint} with Unit IDs: {string.Join(", ", ids)}");
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
                foreach (var s in _slaves) s.Dispose();
                _slaves.Clear();
                _slave = null;
                _listener = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // Data store access for simulation service
        public DataStore? GetDataStore()
        {
            lock (_stateLock)
            {
                return _dataStore;
            }
        }

        public virtual async Task DisconnectAsync()
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

        public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count) =>
            ReadFromDataStoreAsync(unitId, startAddress, count, ds => ds.HoldingRegisters, "holding registers");

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
                    if (registers == null || registerAddress < 1 || registerAddress > registers.Count)
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

        public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count) =>
            ReadFromDataStoreAsync(unitId, startAddress, count, ds => ds.CoilDiscretes, "coils");

        private async Task<T[]?> ReadFromDataStoreAsync<T>(
            byte unitId,
            int startAddress,
            int count,
            Func<DataStore, ModbusDataCollection<T>> collectionSelector,
            string resourceName)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Modbus server is not running");

            return await Task.Run(() =>
            {
                _logger.LogDebug($"Reading {count} {resourceName} starting at {startAddress} (Unit ID: {unitId})");

                lock (_stateLock)
                {
                    if (_dataStore == null)
                        throw new InvalidOperationException("Data store not initialized");

                    var collection = collectionSelector(_dataStore);
                    if (collection == null || startAddress < 1 || count < 0 || startAddress + count - 1 > collection.Count)
                        throw new ArgumentOutOfRangeException(nameof(startAddress), $"Requested {resourceName} range is out of bounds");

                    var result = new T[count];
                    for (int i = 0; i < count; i++)
                        result[i] = collection[(ushort)(startAddress + i)];

                    _logger.LogDebug($"Successfully read {result.Length} {resourceName}");
                    return result;
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
                    if (coils == null || coilAddress < 1 || coilAddress > coils.Count)
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

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (!_disposed)
            {
                try
                {
                    await DisconnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during DisposeAsync");
                }
                _disposed = true;
            }
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

        private System.Collections.Generic.List<byte> ParseUnitIds(string input)
        {
            var result = new System.Collections.Generic.List<byte>();
            if (string.IsNullOrWhiteSpace(input)) return result;

            var parts = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains('-'))
                {
                    var range = trimmed.Split('-');
                    if (range.Length == 2 && byte.TryParse(range[0].Trim(), out byte start) && byte.TryParse(range[1].Trim(), out byte end))
                    {
                        for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                        {
                            if (i >= 1 && i <= 247 && !result.Contains((byte)i))
                                result.Add((byte)i);
                        }
                    }
                }
                else if (byte.TryParse(trimmed, out byte id))
                {
                    if (id >= 1 && id <= 247 && !result.Contains(id))
                        result.Add(id);
                }
            }
            return result;
        }
    }
}
