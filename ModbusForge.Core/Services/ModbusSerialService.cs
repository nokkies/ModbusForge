using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Modbus.Device;
using ModbusForge.Models;
using ModbusForge.Services.Messages;

namespace ModbusForge.Services
{
    public class ModbusSerialService : IModbusService, IDisposable
    {
        private SerialPort? _serialPort;
        private IModbusMaster? _client;
        private ConnectionProfile? _connectionProfile;
        private bool _disposed;
        private readonly ILogger<ModbusSerialService> _logger;
        private readonly IConsoleLoggerService? _consoleLoggerService;
        private readonly IValidationService? _validationService;
        private readonly ModbusFrameLogger _frameLogger;
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private const int DisposeLockTimeoutMs = 5000;
        private const byte DeviceIdMoreFollows = 0xFF;
        private const int MaxDeviceIdTransactions = 16;

        public TransportType Transport { get; }

        public ModbusSerialService(ILogger<ModbusSerialService> logger, TransportType transport)
            : this(logger, null, null, null, transport)
        {
        }

        public ModbusSerialService(ILogger<ModbusSerialService> logger, IConsoleLoggerService? consoleLoggerService, IValidationService? validationService, TransportType transport)
            : this(logger, consoleLoggerService, validationService, null, transport)
        {
        }

        public ModbusSerialService(ILogger<ModbusSerialService> logger, IConsoleLoggerService? consoleLoggerService, IValidationService? validationService, ModbusFrameLogger? frameLogger, TransportType transport)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _consoleLoggerService = consoleLoggerService;
            _validationService = validationService;
            _frameLogger = frameLogger ?? new ModbusFrameLogger();

            if (transport != TransportType.Rtu && transport != TransportType.Ascii)
            {
                throw new ArgumentException("Transport must be RTU or ASCII", nameof(transport));
            }

            Transport = transport;
            _logger.LogInformation("Modbus serial {Transport} client created", transport);
        }

        public virtual string BoundEndpoint => _serialPort?.PortName ?? string.Empty;

        public ModbusFrameLogger FrameLogger => _frameLogger;

        public virtual bool IsConnected
        {
            get
            {
                try
                {
                    return _client != null && _serialPort != null && _serialPort.IsOpen;
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _logger.LogError(ex, "Error checking serial connection status");
                    return false;
                }
            }
        }

        public virtual Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Serial connections require a ConnectionProfile. Use ConnectAsync(ConnectionProfile).");
        }

        public virtual async Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(profile);

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var validation = _validationService?.ValidateSerialSettings(profile);
                if (validation is { IsValid: false })
                {
                    var message = $"Invalid serial settings: {validation.ErrorMessage}";
                    _logger.LogWarning(message);
                    _consoleLoggerService?.Log(message);
                    return false;
                }

                DisconnectCore();

                _connectionProfile = profile;

                _serialPort = new SerialPort(profile.ComPort, profile.BaudRate, profile.Parity, profile.DataBits, profile.StopBits)
                {
                    RtsEnable = profile.RtsEnable,
                    ReadTimeout = 5000,
                    WriteTimeout = 5000
                };

                try
                {
                    _serialPort.Open();

                    var adapter = ModbusStreamAdapterFactory.CreateSerialAdapter(_serialPort);
                    _client = Transport == TransportType.Rtu
                        ? ModbusSerialMaster.CreateRtu(new LoggingStreamResource(adapter, _frameLogger))
                        : ModbusSerialMaster.CreateAscii(new LoggingStreamResource(adapter, _frameLogger));

                    _client.Transport.ReadTimeout = 5000;
                    _client.Transport.WriteTimeout = 5000;

                    var message = $"Connected to Modbus {Transport} on {profile.ComPort} ({profile.BaudRate}/{profile.DataBits}{ParityChar(profile.Parity)}{StopBitsChar(profile.StopBits)})";
                    _logger.LogInformation(message);
                    _consoleLoggerService?.Log(message);
                    return true;
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    var message = $"Failed to open Modbus {Transport} connection on {profile.ComPort}: {ex.Message}";
                    _logger.LogError(ex, message);
                    _consoleLoggerService?.Log(message);
                    DisconnectCore();
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
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsConnected)
                {
                    _logger.LogInformation("Disconnecting Modbus serial connection on {Port}", BoundEndpoint);
                }

                DisconnectCore();

                var message = $"Modbus {Transport} connection closed";
                _logger.LogInformation(message);
                _consoleLoggerService?.Log(message);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error disconnecting Modbus serial connection");
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public virtual async Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
        {
            return await ExecuteReadAsync(
                unitId,
                startAddress,
                $"Reading {count} holding registers starting at {startAddress}",
                "Error reading holding registers",
                (client, protocolAddress) => client.ReadHoldingRegisters(unitId, protocolAddress, (ushort)count));
        }

        public virtual async Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
        {
            return await ExecuteReadAsync(
                unitId,
                startAddress,
                $"Reading {count} input registers starting at {startAddress}",
                "Error reading input registers",
                (client, protocolAddress) => client.ReadInputRegisters(unitId, protocolAddress, (ushort)count));
        }

        public virtual async Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
        {
            return await ExecuteReadAsync(
                unitId,
                startAddress,
                $"Reading {count} discrete inputs starting at {startAddress}",
                "Error reading discrete inputs",
                (client, protocolAddress) => client.ReadInputs(unitId, protocolAddress, (ushort)count));
        }

        public virtual async Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
        {
            return await ExecuteReadAsync(
                unitId,
                startAddress,
                $"Reading {count} coils starting at {startAddress}",
                "Error reading coils",
                (client, protocolAddress) => client.ReadCoils(unitId, protocolAddress, (ushort)count));
        }

        public virtual async Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
        {
            await ExecuteWriteAsync(
                unitId,
                registerAddress,
                $"Writing register at {registerAddress}",
                "Error writing single register",
                (client, protocolAddress) => client.WriteSingleRegister(unitId, protocolAddress, value));
        }

        public virtual async Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values)
        {
            await ExecuteWriteAsync(
                unitId,
                startAddress,
                $"Writing {values.Length} registers starting at {startAddress}",
                "Error writing multiple registers",
                (client, protocolAddress) => client.WriteMultipleRegisters(unitId, protocolAddress, values));
        }

        public virtual async Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value)
        {
            await ExecuteWriteAsync(
                unitId,
                coilAddress,
                $"Writing coil at {coilAddress}",
                "Error writing single coil",
                (client, protocolAddress) => client.WriteSingleCoil(unitId, protocolAddress, value));
        }

        public virtual async Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask)
        {
            return await ExecuteMasterAsync<ushort?>(
                $"Mask writing register at {registerAddress} (AND 0x{andMask:X4}, OR 0x{orMask:X4})",
                "Error mask writing register",
                (master, cast) =>
                {
                    ushort protocolAddress = ToProtocolAddress(registerAddress);
                    cast.ExecuteCustomMessage<MaskWriteRegisterRequestResponse>(
                        new MaskWriteRegisterRequestResponse(unitId, protocolAddress, andMask, orMask));

                    // FC22 echoes the masks rather than the result, so read the register back.
                    var readBack = master.ReadHoldingRegisters(unitId, protocolAddress, 1);
                    return readBack.Length > 0 ? readBack[0] : null;
                });
        }

        public virtual async Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues)
        {
            ArgumentNullException.ThrowIfNull(writeValues);

            return await ExecuteMasterAsync<ushort[]?>(
                $"Reading {readCount} registers at {readStartAddress} and writing {writeValues.Length} registers at {writeStartAddress}",
                "Error in read/write multiple registers",
                (master, _) => master.ReadWriteMultipleRegisters(
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
                (_, cast) =>
                {
                    var identification = new DeviceIdentification();
                    byte nextObjectId = objectId;

                    for (int transaction = 0; transaction < MaxDeviceIdTransactions; transaction++)
                    {
                        var response = cast.ExecuteCustomMessage<ReadDeviceIdentificationResponse>(
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

        private async Task<T[]?> ExecuteReadAsync<T>(
            byte unitId,
            int startAddress,
            string debugLogMessage,
            string errorLogContext,
            Func<IModbusMaster, ushort, T[]> readFunc)
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
                        _logger.LogDebug($"{debugLogMessage} (Unit ID: {unitId})");
                        // NModbus uses 0-based protocol addresses, convert from 1-based UI address
                        ushort protocolAddress = (ushort)(startAddress > 0 ? startAddress - 1 : 0);

                        if (_client == null)
                            return Array.Empty<T>();

                        T[]? result = null;
                        ApplySerialTiming(() => result = readFunc(_client!, protocolAddress));
                        return result ?? Array.Empty<T>();
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        _logger.LogError(ex, errorLogContext);
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
                            ApplySerialTiming(() => writeAction(_client, protocolAddress));
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

        private static ushort ToProtocolAddress(int uiAddress)
            => (ushort)(uiAddress > 0 ? uiAddress - 1 : 0);

        private async Task<T?> ExecuteMasterAsync<T>(
            string debugLogMessage,
            string errorLogContext,
            Func<IModbusMaster, ModbusMaster, T?> operation)
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
                        if (_client is not ModbusMaster master)
                            return default;

                        T? result = default;
                        ApplySerialTiming(() => result = operation(_client, master));
                        return result;
                    }
                    catch (Modbus.SlaveException ex)
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

        private void ApplySerialTiming(Action operation)
        {
            if (_connectionProfile is null || _serialPort is null)
            {
                operation();
                return;
            }

            if (_connectionProfile.PreTxDelayMs > 0)
                Thread.Sleep(_connectionProfile.PreTxDelayMs);

            if (_connectionProfile.EnableRtsToggle)
                _serialPort.RtsEnable = true;

            try
            {
                operation();

                if (_connectionProfile.EnableRtsToggle)
                {
                    _serialPort.BaseStream.Flush();
                    _serialPort.RtsEnable = false;
                }
            }
            catch
            {
                if (_connectionProfile.EnableRtsToggle)
                    _serialPort.RtsEnable = false;
                throw;
            }

            if (_connectionProfile.PostTxDelayMs > 0)
                Thread.Sleep(_connectionProfile.PostTxDelayMs);
        }

        private void HandleConnectionLoss()
        {
            _logger.LogInformation("Serial client is disconnected. Cleaning up connection.");
            DisconnectCore();
        }

        private void DisconnectCore()
        {
            try { _client?.Dispose(); } catch { }
            _client = null;

            try
            {
                _serialPort?.Close();
            }
            catch { }

            try
            {
                _serialPort?.Dispose();
            }
            catch { }

            _serialPort = null;
            _connectionProfile = null;
        }

        public virtual Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
        {
            var result = new ConnectionDiagnosticResult { IsSerialConnection = true, RemoteEndpoint = ipAddress };

            // For diagnostics through the legacy IP/port signature, treat ipAddress as the COM port
            // and port as the baud rate. All other serial settings use defaults.
            var profile = new ConnectionProfile("Diagnostics", "127.0.0.1", 502, unitId)
            {
                Transport = Transport,
                ComPort = ipAddress,
                BaudRate = port > 0 ? port : 9600
            };

            var validation = _validationService?.ValidateSerialSettings(profile);
            if (validation is { IsValid: false })
            {
                result.TcpError = validation.ErrorMessage;
                return Task.FromResult(result);
            }

            var testPort = TryOpenDiagnosticPort(profile, result);
            if (testPort == null)
            {
                return Task.FromResult(result);
            }

            try
            {
                RunModbusDiagnostic(testPort, unitId, result);
            }
            finally
            {
                try { testPort.Close(); } catch { }
                try { testPort.Dispose(); } catch { }
            }

            return Task.FromResult(result);
        }

        private SerialPort? TryOpenDiagnosticPort(ConnectionProfile profile, ConnectionDiagnosticResult result)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Diagnostics: Opening serial port {ComPort} at {BaudRate}", profile.ComPort, profile.BaudRate);
                var port = new SerialPort(profile.ComPort, profile.BaudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 5000,
                    WriteTimeout = 5000
                };
                port.Open();

                result.TcpConnected = true;
                result.TcpLatencyMs = (int)sw.ElapsedMilliseconds;
                _logger.LogInformation("Diagnostics: Serial port opened in {Latency}ms", result.TcpLatencyMs);
                return port;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                result.TcpConnected = false;
                result.TcpError = $"Could not open serial port: {ex.Message}";
                _logger.LogWarning(ex, "Diagnostics: Failed to open serial port {ComPort}", profile.ComPort);
                return null;
            }
        }

        private void RunModbusDiagnostic(SerialPort port, byte unitId, ConnectionDiagnosticResult result)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var adapter = ModbusStreamAdapterFactory.CreateSerialAdapter(port);
                using var master = Transport == TransportType.Rtu
                    ? ModbusSerialMaster.CreateRtu(new LoggingStreamResource(adapter, _frameLogger))
                    : ModbusSerialMaster.CreateAscii(new LoggingStreamResource(adapter, _frameLogger));
                master.Transport.ReadTimeout = 1000;
                master.Transport.WriteTimeout = 1000;

                sw.Restart();
                EvaluateModbusRead(master.ReadHoldingRegisters(unitId, 0, 1), sw, result);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                ApplyModbusDiagnosticError(ex, result);
            }
        }

        private static void EvaluateModbusRead(ushort[] registers, System.Diagnostics.Stopwatch sw, ConnectionDiagnosticResult result)
        {
            result.ModbusResponding = true;
            result.ModbusLatencyMs = (int)sw.ElapsedMilliseconds;
        }

        private void ApplyModbusDiagnosticError(Exception ex, ConnectionDiagnosticResult result)
        {
            switch (ex)
            {
                case Modbus.SlaveException slaveEx:
                    result.ModbusResponding = true;
                    result.ModbusError = $"Device responded with exception code {slaveEx.SlaveExceptionCode}";
                    _logger.LogInformation("Diagnostics: Modbus serial device responded with exception - {Error}", result.ModbusError);
                    break;

                case IOException ioEx:
                    result.ModbusResponding = false;
                    result.ModbusError = $"I/O error: {ioEx.Message}. Ensure the device is connected and powered.";
                    _logger.LogWarning("Diagnostics: Modbus serial I/O failed - {Error}", result.ModbusError);
                    break;

                case TimeoutException:
                    result.ModbusResponding = false;
                    result.ModbusError = "Modbus serial timeout - device did not respond. Check wiring and Unit ID.";
                    _logger.LogWarning("Diagnostics: Modbus serial timeout");
                    break;

                default:
                    result.ModbusResponding = false;
                    result.ModbusError = ex.Message;
                    _logger.LogWarning(ex, "Diagnostics: Modbus serial test failed");
                    break;
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
                await _ioLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    DisconnectCore();
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
                DisconnectCore();

                if (_ioLock.Wait(DisposeLockTimeoutMs))
                {
                    _ioLock.Dispose();
                }
                else
                {
                    _logger.LogWarning("Timed out waiting for I/O lock during Dispose; leaving semaphore undisposed.");
                }
            }

            _disposed = true;
        }

        ~ModbusSerialService()
        {
            Dispose(false);
        }

        private static char ParityChar(Parity parity)
        {
            return parity switch
            {
                Parity.None => 'N',
                Parity.Even => 'E',
                Parity.Odd => 'O',
                Parity.Mark => 'M',
                Parity.Space => 'S',
                _ => 'N'
            };
        }

        private static string StopBitsChar(StopBits stopBits)
        {
            return stopBits switch
            {
                StopBits.One => "1",
                StopBits.Two => "2",
                StopBits.OnePointFive => "1.5",
                _ => "1"
            };
        }
    }
}
