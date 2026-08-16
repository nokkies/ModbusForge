using NModbus;
using NModbus.Device;
using ModbusForge.Models;
using ModbusForge.Services.Messages;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;

namespace ModbusForge.Services
{
    public class ModbusTcpService : IModbusService, IDisposable
    {
        private IModbusMaster? _client;
        private readonly IModbusFactory _factory = new ModbusFactory();
        private TcpClient? _tcpClient;
        private bool _disposed = false;
        private readonly ILogger<ModbusTcpService> _logger;
        private readonly IConsoleLoggerService? _consoleLoggerService;
        private readonly ModbusFrameLogger _frameLogger;
        private readonly IModbusAddressValidator _addressValidator;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private string? _lastIpAddress;
        private int _lastPort;

        private const int DisposeLockTimeoutMs = 5000;

        private static readonly TimeSpan DisconnectLockTimeout = TimeSpan.FromSeconds(10);
        private const byte DeviceIdMoreFollows = 0xFF;
        private const int MaxDeviceIdTransactions = 16;

        public ModbusTcpService(ILogger<ModbusTcpService> logger)
            : this(logger, null, null, null)
        {
        }

        public ModbusTcpService(ILogger<ModbusTcpService> logger, IConsoleLoggerService? consoleLoggerService)
            : this(logger, consoleLoggerService, null, null)
        {
        }

        public ModbusTcpService(ILogger<ModbusTcpService> logger, IConsoleLoggerService? consoleLoggerService, ModbusFrameLogger? frameLogger)
            : this(logger, consoleLoggerService, frameLogger, null)
        {
        }

        public ModbusTcpService(ILogger<ModbusTcpService> logger, IConsoleLoggerService? consoleLoggerService, ModbusFrameLogger? frameLogger, IModbusAddressValidator? addressValidator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _consoleLoggerService = consoleLoggerService;
            _frameLogger = frameLogger ?? new ModbusFrameLogger();
            _addressValidator = addressValidator ?? new ModbusAddressValidator();
            _logger.LogInformation("Modbus TCP client created");
        }

        public virtual async Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            ValidateAddressRange(unitId, startAddress, count);
            return await ModbusChunkedExecutor.ReadAsync(
                () => IsConnected,
                _ioLock,
                _client,
                _addressValidator,
                _logger,
                HandleConnectionLoss,
                ToProtocolAddress,
                unitId,
                startAddress,
                count,
                PlcArea.InputRegister,
                $"Reading {count} input registers starting at {startAddress}",
                "Error reading input registers",
                (client, protocolAddress, chunkCount) => client.ReadInputRegisters(unitId, protocolAddress, chunkCount));
        }

        public virtual async Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            ValidateSingleRequest(unitId, startAddress, count, PlcArea.DiscreteInput);
            return await ModbusChunkedExecutor.ReadAsync(
                () => IsConnected,
                _ioLock,
                _client,
                _addressValidator,
                _logger,
                HandleConnectionLoss,
                ToProtocolAddress,
                unitId,
                startAddress,
                count,
                PlcArea.DiscreteInput,
                $"Reading {count} discrete inputs starting at {startAddress}",
                "Error reading discrete inputs",
                (client, protocolAddress, chunkCount) => client.ReadInputs(unitId, protocolAddress, chunkCount));
        }

        public virtual string BoundEndpoint => string.Empty;

        public event EventHandler? ConnectionLost;

        public ModbusFrameLogger FrameLogger => _frameLogger;

        public virtual bool IsConnected
        {
            get
            {
                try
                {
                    var client = _client;
                    var tcpClient = _tcpClient;
                    return client != null && tcpClient != null && tcpClient.Connected;
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _logger.LogError(ex, "Error checking connection status");
                    return false;
                }
            }
        }

        public virtual async Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
        {
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _lastIpAddress = ipAddress;
                _lastPort = port;

                // A reconnect replaces the previous transport. Dispose it first
                // (we hold the I/O lock, so nothing can be using it) — otherwise
                // a double-connect leaks the old socket and master.
                DisposeTransport();

                var tcpClient = new TcpClient();
                try
                {
                    await tcpClient.ConnectAsync(ipAddress, port, cancellationToken).ConfigureAwait(false);
                    _tcpClient = tcpClient;
                    var streamResource = new LoggingStreamResource(ModbusStreamAdapterFactory.CreateTcpAdapter(tcpClient), _frameLogger);
                    var transport = _factory.CreateIpTransport(streamResource);
                    _client = new ModbusIpMaster(transport);
                    var message = $"Connected to Modbus server at {ipAddress}:{port}";
                    _logger.LogInformation(message);
                    _consoleLoggerService?.Log(message);
                    return true;
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    var message = $"Failed to connect to Modbus server at {ipAddress}:{port}: {ex.Message}";
                    _logger.LogError(ex, message);
                    _consoleLoggerService?.Log(message);
                    (_client as IDisposable)?.Dispose();
                    _client = null;
                    tcpClient.Dispose();
                    _tcpClient = null;
                    return false;
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public virtual async Task DisconnectAsync()
        {
            // Bound the wait: a request stuck on a half-open socket must not
            // block the disconnect forever. If we time out, tear the transport
            // down anyway — the in-flight request will fail on the closed socket
            // and release the lock on its own.
            var acquired = await _ioLock.WaitAsync(DisconnectLockTimeout).ConfigureAwait(false);
            if (!acquired)
            {
                _logger.LogWarning("Timed out waiting for an in-flight request before disconnect; closing the transport anyway.");
                try
                {
                    (_client as IDisposable)?.Dispose();
                    _client = null;
                    _tcpClient?.Close();
                    _tcpClient = null;
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _logger.LogError(ex, "Error closing the transport after a disconnect timeout.");
                }
            }
            try
            {
                if (acquired && IsConnected)
                {
                    var message = $"Disconnecting from Modbus server at {_lastIpAddress}:{_lastPort}";
                    _logger.LogInformation(message);
                    _consoleLoggerService?.Log(message);
                    (_client as IDisposable)?.Dispose();
                    _client = null;
                    _tcpClient?.Close();
                    _tcpClient = null;
                    var disconnectMessage = "Successfully disconnected from Modbus server";
                    _logger.LogInformation(disconnectMessage);
                    _consoleLoggerService?.Log(disconnectMessage);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error disconnecting from Modbus server");
                throw;
            }
            finally
            {
                if (acquired)
                {
                    _ioLock.Release();
                }
            }
        }

        public virtual async Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
        {
            ValidateAddressRange(unitId, startAddress, count);
            return await ModbusChunkedExecutor.ReadAsync(
                () => IsConnected,
                _ioLock,
                _client,
                _addressValidator,
                _logger,
                HandleConnectionLoss,
                ToProtocolAddress,
                unitId,
                startAddress,
                count,
                PlcArea.HoldingRegister,
                $"Reading {count} holding registers starting at {startAddress}",
                "Error reading holding registers",
                (client, protocolAddress, chunkCount) => client.ReadHoldingRegisters(unitId, protocolAddress, chunkCount));
        }

        public virtual async Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            ValidateSingleAddress(unitId, registerAddress);
            await ExecuteWriteAsync(
                unitId,
                registerAddress,
                $"Writing register at {registerAddress}",
                "Error writing single register",
                (client, protocolAddress) => client.WriteSingleRegister(unitId, protocolAddress, value));
        }

        public virtual async Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            ValidateAddressRange(unitId, startAddress, values.Length);
            await ModbusChunkedExecutor.WriteAsync(
                () => IsConnected,
                _ioLock,
                _client,
                _addressValidator,
                _logger,
                HandleConnectionLoss,
                ToProtocolAddress,
                unitId,
                startAddress,
                values,
                PlcArea.HoldingRegister,
                $"Writing {values.Length} registers starting at {startAddress}",
                "Error writing multiple registers",
                (client, protocolAddress, chunkValues) => client.WriteMultipleRegisters(unitId, protocolAddress, chunkValues));
        }

        public virtual async Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
        {
            ValidateSingleRequest(unitId, startAddress, count, PlcArea.Coil);
            return await ModbusChunkedExecutor.ReadAsync(
                () => IsConnected,
                _ioLock,
                _client,
                _addressValidator,
                _logger,
                HandleConnectionLoss,
                ToProtocolAddress,
                unitId,
                startAddress,
                count,
                PlcArea.Coil,
                $"Reading {count} coils starting at {startAddress}",
                "Error reading coils",
                (client, protocolAddress, chunkCount) => client.ReadCoils(unitId, protocolAddress, chunkCount));
        }

        public virtual async Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
        {
            ValidateSingleAddress(unitId, coilAddress);
            await ExecuteWriteAsync(
                unitId,
                coilAddress,
                $"Writing coil at {coilAddress}",
                "Error writing single coil",
                (client, protocolAddress) => client.WriteSingleCoil(unitId, protocolAddress, value));
        }

        public virtual async Task WriteCoilsAsync(byte unitId, int startAddress, bool[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            ValidateAddressRange(unitId, startAddress, values.Length);
            await ModbusChunkedExecutor.WriteAsync(
                () => IsConnected,
                _ioLock,
                _client,
                _addressValidator,
                _logger,
                HandleConnectionLoss,
                ToProtocolAddress,
                unitId,
                startAddress,
                values,
                PlcArea.Coil,
                $"Writing {values.Length} coils starting at {startAddress}",
                "Error writing multiple coils",
                (client, protocolAddress, chunkValues) => client.WriteMultipleCoils(unitId, protocolAddress, chunkValues));
        }

        public virtual async Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask)
        {
            ValidateSingleAddress(unitId, registerAddress);
            return await ExecuteMasterAsync<ushort?>(
                $"Mask writing register at {registerAddress} (AND 0x{andMask:X4}, OR 0x{orMask:X4})",
                "Error mask writing register",
                master =>
                {
                    ushort protocolAddress = ToProtocolAddress(registerAddress);
                    master.ExecuteCustomMessage<MaskWriteRegisterRequestResponse>(
                        new MaskWriteRegisterRequestResponse(unitId, protocolAddress, andMask, orMask));

                    // FC22 echoes the masks rather than the result, so read the register back.
                    var readBack = master.ReadHoldingRegisters(unitId, protocolAddress, 1);
                    return readBack.Length > 0 ? readBack[0] : null;
                });
        }

        public virtual async Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues)
        {
            ArgumentNullException.ThrowIfNull(writeValues);
            ValidateSingleRequest(unitId, readStartAddress, readCount, PlcArea.HoldingRegister);
            ValidateSingleRequest(unitId, writeStartAddress, writeValues.Length, PlcArea.HoldingRegister, isWrite: true);

            return await ExecuteMasterAsync<ushort[]?>(
                $"Reading {readCount} registers at {readStartAddress} and writing {writeValues.Length} registers at {writeStartAddress}",
                "Error in read/write multiple registers",
                master => master.ReadWriteMultipleRegisters(
                    unitId,
                    ToProtocolAddress(readStartAddress),
                    (ushort)readCount,
                    ToProtocolAddress(writeStartAddress),
                    writeValues));
        }

        public virtual async Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic)
        {
            return await ExecuteMasterAsync<DeviceIdentification?>(
                $"Reading device identification ({category}) from object 0x{objectId:X2}",
                "Error reading device identification",
                master =>
                {
                    var identification = new DeviceIdentification();
                    byte nextObjectId = objectId;
                    // A slave that cannot fit every object in one response sets MoreFollows to
                    // 0xFF and reports where the next transaction has to resume.
                    for (int transaction = 0; transaction < MaxDeviceIdTransactions; transaction++)
                    {
                        var response = master.ExecuteCustomMessage<ReadDeviceIdentificationResponse>(
                            new ReadDeviceIdentificationRequest(unitId, (byte)category, nextObjectId));

                        identification.ConformityLevel = response.ConformityLevel;
                        foreach (var pair in response.Objects)
                            identification.Objects[pair.Key] = pair.Value;

                        if (response.MoreFollows != DeviceIdMoreFollows)
                            break;
                        nextObjectId = response.NextObjectId;
                    }
                    return identification;
                });
        }

        private static ushort ToProtocolAddress(int uiAddress)
            => (ushort)(uiAddress > 0 ? uiAddress - 1 : 0);

        private void ValidateAddressRange(byte unitId, int startAddress, int count)
        {
            if (!_addressValidator.IsValidUnitId(unitId))
                throw new ArgumentOutOfRangeException(nameof(unitId), $"Unit ID must be between {ModbusAddressValidator.MinUnitId} and {ModbusAddressValidator.MaxUnitId}.");
            if (!_addressValidator.IsValidAddressRange(startAddress, count))
                throw new ArgumentOutOfRangeException(nameof(startAddress), $"The requested range {startAddress}..{startAddress + count - 1} is outside the Modbus address space.");
        }

        private void ValidateSingleRequest(byte unitId, int startAddress, int count, PlcArea area, bool isWrite = false)
        {
            if (!_addressValidator.IsValidUnitId(unitId))
                throw new ArgumentOutOfRangeException(nameof(unitId), $"Unit ID must be between {ModbusAddressValidator.MinUnitId} and {ModbusAddressValidator.MaxUnitId}.");
            if (!_addressValidator.IsValidRange(startAddress, count, area, isWrite))
                throw new ArgumentOutOfRangeException(nameof(startAddress), $"The requested range {startAddress}..{startAddress + count - 1} is outside the Modbus address space or exceeds the area limit.");
        }

        private void ValidateSingleAddress(byte unitId, int address)
        {
            if (!_addressValidator.IsValidUnitId(unitId))
                throw new ArgumentOutOfRangeException(nameof(unitId), $"Unit ID must be between {ModbusAddressValidator.MinUnitId} and {ModbusAddressValidator.MaxUnitId}.");
            if (!_addressValidator.IsValidStartAddress(address))
                throw new ArgumentOutOfRangeException(nameof(address), $"Address must be between {ModbusAddressValidator.MinStartAddress} and {ModbusAddressValidator.MaxStartAddress}.");
        }

        private async Task<T?> ExecuteMasterAsync<T>(
            string debugLogMessage,
            string errorLogContext,
            Func<IModbusMaster, T?> operation)
        {
            if (!IsConnected)
                return default;

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        _logger.LogDebug(debugLogMessage);
                        if (_client == null)
                            return default;

                        return operation(_client);
                    }
                    catch (NModbus.SlaveException ex)
                    {
                        _logger.LogWarning(ex, "{Context}: slave returned exception code {Code}", errorLogContext, ex.SlaveExceptionCode);
                        return default;
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        _logger.LogError(ex, errorLogContext);
                        HandleConnectionLoss();
                        return default;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task ExecuteWriteAsync(
            byte unitId,
            int address,
            string debugLogMessage,
            string errorLogContext,
            Action<IModbusMaster, ushort> writeAction)
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
                        _logger.LogDebug($"{debugLogMessage} (Unit ID: {unitId})");
                        // NModbus uses 0-based protocol addresses, convert from 1-based UI address
                        ushort protocolAddress = (ushort)(address > 0 ? address - 1 : 0);

                        if (_client != null)
                            writeAction(_client, protocolAddress);
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        _logger.LogError(ex, errorLogContext);
                        HandleConnectionLoss();
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>
        /// Disposes the current master/transport without touching the I/O lock
        /// (callers must hold it). Null-safe and exception-safe.
        /// </summary>
        private void DisposeTransport()
        {
            try
            {
                (_client as IDisposable)?.Dispose();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error disposing the previous transport during reconnect.");
            }
            _client = null;

            try
            {
                _tcpClient?.Close();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error closing the previous socket during reconnect.");
            }
            _tcpClient = null;
        }

        private void HandleConnectionLoss()
        {
            _logger.LogInformation("Client is disconnected. Cleaning up connection.");
            bool wasConnected = _client != null;
            try
            {
                (_client as IDisposable)?.Dispose();
                _client = null;
                _tcpClient?.Close();
                _tcpClient = null;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error during explicit disconnect after connection loss.");
            }

            if (wasConnected)
            {
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
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
                // Use async wait to avoid blocking the calling thread
                await _ioLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    (_client as IDisposable)?.Dispose();
                    _tcpClient?.Close();
                }
                finally
                {
                    _ioLock.Release();
                    _ioLock.Dispose();
                }
                _disposed = true;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose the underlying connection first so any in-flight I/O is
                // aborted, allowing the operation holding the lock to release it.
                (_client as IDisposable)?.Dispose();
                _client = null;
                _tcpClient?.Close();
                _tcpClient = null;

                // Acquire the lock on the calling thread (bounded wait) before
                // disposing the semaphore. Disposing while still holding it means
                // no in-flight operation can call Release on a disposed semaphore
                // (use-after-dispose). If the lock cannot be acquired in time we
                // deliberately leave the semaphore undisposed for the same reason.
                if (_ioLock.Wait(DisposeLockTimeoutMs))
                {
                    _ioLock.Dispose();
                }
                else
                {
                    _logger.LogWarning(
                        "Timed out waiting for I/O lock during Dispose; leaving semaphore undisposed to avoid use-after-dispose.");
                }
            }

            _disposed = true;
        }

        ~ModbusTcpService()
        {
            Dispose(false);
        }

        public virtual async Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
        {
            var result = new ConnectionDiagnosticResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Step 1: Test raw TCP connection
            using var testClient = new TcpClient();
            try
            {
                _logger.LogInformation($"Diagnostics: Testing TCP connection to {ipAddress}:{port}");
                
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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
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

                var master = _factory.CreateMaster(testClient);
                try
                {
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
                catch (NModbus.SlaveException slaveEx)
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
                }
                finally
                {
                    (master as IDisposable)?.Dispose();
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                result.ModbusResponding = false;
                result.ModbusError = ex.Message;
                _logger.LogWarning($"Diagnostics: Modbus test failed - {ex.Message}");
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