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

        public async Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
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
                        var registers = _client?.ReadInputRegisters(unitId, protocolAddress, (ushort)count);
                        if (registers == null) return Array.Empty<ushort>();
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

        public async Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
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
                        var inputs = _client?.ReadInputs(unitId, protocolAddress, (ushort)count);
                        if (inputs == null) return Array.Empty<bool>();
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

        public bool IsConnected
        {
            get
            {
                if (_client == null || _tcpClient == null)
                    return false;

                try
                {
                    if (!_tcpClient.Connected)
                        return false;

                    // Use Poll to check if socket is still alive
                    // Poll with SelectMode.SelectRead returns true if:
                    // - Connection is closed/reset/terminated
                    // - Data is available for reading
                    // We check Available == 0 to distinguish between the two
                    bool pollResult = _tcpClient.Client.Poll(1, SelectMode.SelectRead);
                    bool noDataAvailable = _tcpClient.Client.Available == 0;
                    
                    if (pollResult && noDataAvailable)
                    {
                        // Socket is closed - clean up
                        _logger.LogWarning("Socket poll detected closed connection");
                        HandleConnectionLoss();
                        return false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking connection status");
                    HandleConnectionLoss();
                    return false;
                }
            }
        }

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

        public async Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
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
                        var registers = _client?.ReadHoldingRegisters(unitId, protocolAddress, (ushort)count);
                        if (registers == null) return Array.Empty<ushort>();
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
                        _client?.WriteSingleRegister(unitId, protocolAddress, value);
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

        public async Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
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
                        var coils = _client?.ReadCoils(unitId, protocolAddress, (ushort)count);
                        if (coils == null) return Array.Empty<bool>();
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
                        _client?.WriteSingleCoil(unitId, protocolAddress, value);
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

        public async Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
        {
            var result = new ConnectionDiagnosticResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Step 1: Test raw TCP connection
            TcpClient? testClient = null;
            try
            {
                _logger.LogInformation($"Diagnostics: Testing TCP connection to {ipAddress}:{port}");
                testClient = new TcpClient();
                
                // Use async connect with timeout
                var connectTask = testClient.ConnectAsync(ipAddress, port);
                if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                {
                    result.TcpConnected = false;
                    result.TcpError = "Connection timeout (5s) - host may be unreachable or port blocked by firewall";
                    return result;
                }

                await connectTask; // Propagate any exception
                result.TcpLatencyMs = (int)sw.ElapsedMilliseconds;
                result.TcpConnected = true;
                result.RemoteEndpoint = testClient.Client.RemoteEndPoint?.ToString() ?? ipAddress;
                result.LocalEndpoint = testClient.Client.LocalEndPoint?.ToString() ?? "unknown";
                _logger.LogInformation($"Diagnostics: TCP connected in {result.TcpLatencyMs}ms");
            }
            catch (SocketException sockEx)
            {
                result.TcpConnected = false;
                result.TcpError = $"Socket error ({sockEx.SocketErrorCode}): {GetSocketErrorDescription(sockEx.SocketErrorCode)}";
                _logger.LogWarning($"Diagnostics: TCP failed - {result.TcpError}");
                return result;
            }
            catch (Exception ex)
            {
                result.TcpConnected = false;
                result.TcpError = ex.Message;
                _logger.LogWarning($"Diagnostics: TCP failed - {ex.Message}");
                return result;
            }

            // Step 2: Test Modbus protocol communication
            try
            {
                sw.Restart();
                _logger.LogInformation($"Diagnostics: Testing Modbus protocol with Unit ID {unitId}");
                
                var master = ModbusIpMaster.CreateIp(testClient);
                master.Transport.ReadTimeout = 5000;
                master.Transport.WriteTimeout = 5000;

                // Try to read a single holding register - this is the most basic Modbus operation
                try
                {
                    var registers = master.ReadHoldingRegisters(unitId, 0, 1);
                    result.ModbusLatencyMs = (int)sw.ElapsedMilliseconds;
                    result.ModbusResponding = true;
                    _logger.LogInformation($"Diagnostics: Modbus responded in {result.ModbusLatencyMs}ms, read value: {registers[0]}");
                }
                catch (Modbus.SlaveException slaveEx)
                {
                    // Slave responded with an exception - this means Modbus IS working, just the request was invalid
                    result.ModbusLatencyMs = (int)sw.ElapsedMilliseconds;
                    result.ModbusResponding = true; // Device responded, even if with error
                    result.ModbusError = $"Device responded with exception code {slaveEx.SlaveExceptionCode}: {GetModbusExceptionDescription(slaveEx.SlaveExceptionCode)}";
                    _logger.LogInformation($"Diagnostics: Modbus device responded with exception - {result.ModbusError}");
                }
                catch (IOException ioEx)
                {
                    result.ModbusResponding = false;
                    if (ioEx.InnerException is SocketException innerSock)
                    {
                        result.ModbusError = $"Connection reset by device - {GetSocketErrorDescription(innerSock.SocketErrorCode)}. Device may have rejected the Modbus request or closed the connection.";
                    }
                    else
                    {
                        result.ModbusError = $"I/O error: {ioEx.Message}. Device may have closed the connection.";
                    }
                    _logger.LogWarning($"Diagnostics: Modbus I/O failed - {result.ModbusError}");
                }
                catch (TimeoutException)
                {
                    result.ModbusResponding = false;
                    result.ModbusError = "Modbus timeout - device accepted TCP but did not respond to Modbus request. Check Unit ID or device may not support Modbus TCP.";
                    _logger.LogWarning($"Diagnostics: Modbus timeout");
                }

                master.Dispose();
            }
            catch (Exception ex)
            {
                result.ModbusResponding = false;
                result.ModbusError = ex.Message;
                _logger.LogWarning($"Diagnostics: Modbus test failed - {ex.Message}");
            }
            finally
            {
                testClient?.Close();
            }

            return result;
        }

        private static string GetSocketErrorDescription(SocketError error)
        {
            return error switch
            {
                SocketError.ConnectionRefused => "Connection refused - no service listening on port or firewall blocking",
                SocketError.HostUnreachable => "Host unreachable - check IP address and network connectivity",
                SocketError.NetworkUnreachable => "Network unreachable - check network configuration",
                SocketError.TimedOut => "Connection timed out - host not responding",
                SocketError.ConnectionReset => "Connection reset by remote host",
                SocketError.ConnectionAborted => "Connection aborted by local system",
                SocketError.AddressNotAvailable => "Address not available - invalid IP address",
                SocketError.HostNotFound => "Host not found - DNS resolution failed",
                _ => error.ToString()
            };
        }

        private static string GetModbusExceptionDescription(byte exceptionCode)
        {
            return exceptionCode switch
            {
                1 => "Illegal Function - function code not supported",
                2 => "Illegal Data Address - address out of range or not mapped",
                3 => "Illegal Data Value - value out of range",
                4 => "Slave Device Failure - device internal error",
                5 => "Acknowledge - request accepted, processing",
                6 => "Slave Device Busy - device busy, retry later",
                8 => "Memory Parity Error - device memory error",
                10 => "Gateway Path Unavailable",
                11 => "Gateway Target Device Failed to Respond",
                _ => $"Unknown exception code {exceptionCode}"
            };
        }
    }
}