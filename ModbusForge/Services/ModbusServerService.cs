using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Modbus.Data;
using System.Net.Sockets;
using ModbusForge.Helpers;

namespace ModbusForge.Services
{
    public class ModbusServerService : IModbusService, IDisposable
    {
        private ModbusMultiUnitServer? _multiServer;
        private byte _primaryUnitId = 1;
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
                        if (_isRunning) return true;

                        if (!IPAddress.TryParse(ipAddress, out var address))
                            throw new ArgumentException($"Invalid IP address: {ipAddress}");

                        // Loopback or 0.0.0.0 → bind to all interfaces so external clients can connect
                        if (address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.Any))
                            address = IPAddress.Any;

                        var endpoint = new IPEndPoint(address, port == 0 ? DefaultPort : port);
                        var ids = ParseUnitIds(unitIds);
                        if (ids.Count == 0) ids.Add(DefaultSlaveId);
                        _primaryUnitId = ids[0];

                        _multiServer = new ModbusMultiUnitServer(_logger);
                        _multiServer.Start(endpoint, ids);

                        _isRunning = true;
                        _logger.LogInformation("Modbus TCP server started on {Endpoint} Unit IDs: {Ids}", endpoint, string.Join(",", ids));
                        return true;
                    }
                    catch (SocketException sockEx) when (sockEx.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        _isRunning = false;
                        int p = port == 0 ? DefaultPort : port;
                        _logger.LogWarning(sockEx, "Port {Port} is already in use.", p);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start Modbus TCP server");
                        _isRunning = false;
                        return false;
                    }
                }
            });
        }

        private void CleanupResources()
        {
            try { _multiServer?.Stop(); } catch { }
            _multiServer?.Dispose();
            _multiServer = null;
        }

        public string BoundEndpoint
        {
            get
            {
                lock (_stateLock)
                {
                    if (_multiServer?.LocalEndpoint is System.Net.IPEndPoint ep)
                    {
                        var host = ep.Address.Equals(System.Net.IPAddress.Any)
                            ? GetLocalNetworkIp()
                            : ep.Address.ToString();
                        return $"{host}:{ep.Port}";
                    }
                    return string.Empty;
                }
            }
        }

        private static string GetLocalNetworkIp()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                var ips = host.AddressList
                    .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(ip => ip.ToString())
                    .ToList();
                if (ips.Count > 0)
                    return string.Join(", ", ips);
            }
            catch { }
            return "0.0.0.0";
        }

        // Returns the primary (first) unit ID's DataStore — used by SimulationService
        public DataStore? GetDataStore() => GetDataStore(_primaryUnitId);

        public DataStore? GetDataStore(byte unitId)
        {
            lock (_stateLock)
            {
                return _multiServer?.TryGetDataStore(unitId);
            }
        }

        public IEnumerable<byte> GetUnitIds()
        {
            lock (_stateLock)
            {
                if (_multiServer == null) return System.Array.Empty<byte>();
                return new System.Collections.Generic.List<byte>(_multiServer.UnitIds);
            }
        }

        public virtual async Task DisconnectAsync()
        {
            await Task.Run(() =>
            {
                lock (_stateLock)
                {
                    if (!_isRunning) return;
                    _isRunning = false;
                    CleanupResources();
                    _logger.LogInformation("Modbus TCP server stopped");
                }
            });
        }

        public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count) =>
            ReadFromDataStoreAsync(unitId, startAddress, count, ds => ds.HoldingRegisters, "holding registers");

        public async Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            if (!_isRunning) throw new InvalidOperationException("Modbus server is not running");
            await Task.Run(() =>
            {
                var ds = GetDataStore(unitId);
                if (ds == null || registerAddress < 1 || registerAddress >= ds.HoldingRegisters.Count)
                    throw new ArgumentOutOfRangeException(nameof(registerAddress));
                ds.HoldingRegisters[(ushort)registerAddress] = value;
            });
        }

        public async Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values)
        {
            if (!_isRunning) throw new InvalidOperationException("Modbus server is not running");
            await Task.Run(() =>
            {
                var ds = GetDataStore(unitId);
                if (ds == null || startAddress < 1 || startAddress + values.Length - 1 >= ds.HoldingRegisters.Count)
                    throw new ArgumentOutOfRangeException(nameof(startAddress));

                for (int i = 0; i < values.Length; i++)
                {
                    ds.HoldingRegisters[(ushort)(startAddress + i)] = values[i];
                }
            });
        }

        public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count) =>
            ReadFromDataStoreAsync(unitId, startAddress, count, ds => ds.CoilDiscretes, "coils");

        private async Task<T[]?> ReadFromDataStoreAsync<T>(
            byte unitId, int startAddress, int count,
            Func<DataStore, ModbusDataCollection<T>> collectionSelector,
            string resourceName)
        {
            if (!_isRunning) throw new InvalidOperationException("Modbus server is not running");
            return await Task.Run(() =>
            {
                var ds = GetDataStore(unitId) ?? GetDataStore(_primaryUnitId);
                if (ds == null) throw new InvalidOperationException("Data store not initialized");
                var collection = collectionSelector(ds);
                if (startAddress < 1 || count < 0 || startAddress + count - 1 > collection.Count)
                    throw new ArgumentOutOfRangeException(nameof(startAddress), $"{resourceName} range out of bounds");
                var result = new T[count];
                for (int i = 0; i < count; i++)
                    result[i] = collection[(ushort)(startAddress + i)];
                return result;
            });
        }

        public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
        {
            if (!_isRunning) throw new InvalidOperationException("Modbus server is not running");
            return Task.Run(() =>
            {
                var ds = GetDataStore(unitId);
                if (ds == null || coilAddress < 1 || coilAddress >= ds.CoilDiscretes.Count)
                    throw new ArgumentOutOfRangeException(nameof(coilAddress));
                ds.CoilDiscretes[(ushort)coilAddress] = value;
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
                    try { CleanupResources(); } catch { }
                    _isRunning = false;
                }
                _disposed = true;
            }
        }

        public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
        {
            var result = new ConnectionDiagnosticResult();
            if (_isRunning && _multiServer != null)
            {
                result.TcpConnected = true;
                result.ModbusResponding = true;
                result.LocalEndpoint = $"{ipAddress}:{(port == 0 ? DefaultPort : port)}";
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
