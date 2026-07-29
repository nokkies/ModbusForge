using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Modbus;
using Modbus.Device;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Probes hosts over Modbus TCP using short-lived connections that are independent
    /// of the application's own client connection.
    /// </summary>
    public class ModbusDeviceProbe : IModbusDeviceProbe
    {
        private const int MaxReconnectAttempts = 1;
        private const byte IllegalFunctionExceptionCode = 0x01;

        private static readonly ScanRegisterType[] AllRegisterTypes =
        {
            ScanRegisterType.Coils,
            ScanRegisterType.DiscreteInputs,
            ScanRegisterType.HoldingRegisters,
            ScanRegisterType.InputRegisters
        };

        private readonly IDeviceIdentificationReader _identificationReader;
        private readonly ILogger<ModbusDeviceProbe> _logger;

        public ModbusDeviceProbe(IDeviceIdentificationReader identificationReader, ILogger<ModbusDeviceProbe> logger)
        {
            _identificationReader = identificationReader ?? throw new ArgumentNullException(nameof(identificationReader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<DeviceScanResult>> ProbeHostAsync(
            string ipAddress,
            int port,
            DeviceScanOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            var results = new List<DeviceScanResult>();
            var connection = await ConnectAsync(ipAddress, port, options, cancellationToken).ConfigureAwait(false);

            if (connection.Master == null)
            {
                results.Add(new DeviceScanResult
                {
                    IpAddress = ipAddress,
                    Port = port,
                    UnitId = options.StartUnitId,
                    Status = DeviceProbeStatus.NoTcpConnection,
                    Message = connection.Error
                });
                return results;
            }

            try
            {
                for (int unitId = options.StartUnitId; unitId <= options.EndUnitId; unitId++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var result = await ProbeUnitAsync(ipAddress, port, (byte)unitId, options, connection, cancellationToken).ConfigureAwait(false);
                    results.Add(result);

                    if (connection.Master == null)
                    {
                        // The host stopped accepting connections part-way through the sweep.
                        break;
                    }

                    if (!result.IsDevice)
                    {
                        continue;
                    }

                    if (options.DetectFunctionCodes)
                    {
                        await DetectFunctionCodesAsync(ipAddress, port, result, options, connection, cancellationToken).ConfigureAwait(false);
                    }

                    if (options.ReadDeviceIdentification)
                    {
                        await ReadIdentificationAsync(result, options, cancellationToken).ConfigureAwait(false);
                    }

                    if (options.ScanRegisterRange)
                    {
                        await ScanRegisterRangeAsync(ipAddress, port, result, options, connection, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                connection.Dispose();
            }

            return results;
        }

        private async Task<DeviceScanResult> ProbeUnitAsync(
            string ipAddress,
            int port,
            byte unitId,
            DeviceScanOptions options,
            ProbeConnection connection,
            CancellationToken cancellationToken)
        {
            var result = new DeviceScanResult
            {
                IpAddress = ipAddress,
                Port = port,
                UnitId = unitId
            };

            var stopwatch = Stopwatch.StartNew();
            var read = await ReadAsync(ipAddress, port, unitId, options.ProbeAddress, 1, options.RegisterType, options, connection, cancellationToken).ConfigureAwait(false);
            result.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            result.Status = read.Status;
            result.Message = read.Message;

            if (IsFunctionSupported(read))
            {
                result.SupportedFunctionCodes.Add(ScanFunctionCode.For(options.RegisterType));
            }

            return result;
        }

        /// <summary>
        /// Reads one item from every register space the discovery probe did not already
        /// cover, so the result lists each read function code the unit implements.
        /// </summary>
        private async Task DetectFunctionCodesAsync(
            string ipAddress,
            int port,
            DeviceScanResult device,
            DeviceScanOptions options,
            ProbeConnection connection,
            CancellationToken cancellationToken)
        {
            foreach (var registerType in AllRegisterTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (registerType == options.RegisterType || connection.Master == null)
                {
                    continue;
                }

                var read = await ReadAsync(ipAddress, port, device.UnitId, options.ProbeAddress, 1, registerType, options, connection, cancellationToken)
                    .ConfigureAwait(false);

                if (IsFunctionSupported(read))
                {
                    device.SupportedFunctionCodes.Add(ScanFunctionCode.For(registerType));
                }
            }

            device.SupportedFunctionCodes.Sort();
        }

        /// <summary>
        /// A function is implemented unless the unit rejects it as illegal; an "illegal data
        /// address" reply still proves the function itself is understood.
        /// </summary>
        private static bool IsFunctionSupported(ReadOutcome read) => read.Status switch
        {
            DeviceProbeStatus.Responded => true,
            DeviceProbeStatus.RespondedWithException => read.ExceptionCode != IllegalFunctionExceptionCode,
            _ => false
        };

        private async Task ReadIdentificationAsync(DeviceScanResult device, DeviceScanOptions options, CancellationToken cancellationToken)
        {
            var identification = await _identificationReader.ReadAsync(
                device.IpAddress,
                device.Port,
                device.UnitId,
                options.ConnectTimeoutMs,
                options.ResponseTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            if (identification == null)
            {
                return;
            }

            device.VendorName = identification.VendorName;
            device.ProductCode = identification.ProductCode;
            device.Revision = identification.Revision;
        }

        private async Task ScanRegisterRangeAsync(
            string ipAddress,
            int port,
            DeviceScanResult device,
            DeviceScanOptions options,
            ProbeConnection connection,
            CancellationToken cancellationToken)
        {
            var blockSize = Math.Max(1, options.RegisterScanBlockSize);
            var remaining = Math.Max(0, options.RegisterScanCount);
            var address = options.RegisterScanStartAddress;

            while (remaining > 0 && connection.Master != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var count = Math.Min(blockSize, remaining);
                var read = await ReadAsync(ipAddress, port, device.UnitId, address, count, options.RegisterType, options, connection, cancellationToken).ConfigureAwait(false);

                for (int offset = 0; offset < count; offset++)
                {
                    var entry = new RegisterScanResult { Address = address + offset };
                    if (read.Status == DeviceProbeStatus.Responded && read.Values != null && offset < read.Values.Length)
                    {
                        entry.IsReadable = true;
                        entry.Value = read.Values[offset];
                    }
                    else
                    {
                        entry.Error = read.Message;
                    }

                    device.Registers.Add(entry);
                }

                address += count;
                remaining -= count;
            }
        }

        private async Task<ReadOutcome> ReadAsync(
            string ipAddress,
            int port,
            byte unitId,
            int startAddress,
            int count,
            ScanRegisterType registerType,
            DeviceScanOptions options,
            ProbeConnection connection,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; ; attempt++)
            {
                var master = connection.Master;
                if (master == null)
                {
                    return new ReadOutcome(DeviceProbeStatus.NoTcpConnection, connection.Error, null);
                }

                try
                {
                    var values = await Task.Run(() => ReadValues(master, unitId, startAddress, count, registerType), cancellationToken)
                        .ConfigureAwait(false);
                    return new ReadOutcome(DeviceProbeStatus.Responded, string.Empty, values);
                }
                catch (SlaveException slaveException)
                {
                    // An exception response still proves a unit is listening at this address.
                    return new ReadOutcome(
                        DeviceProbeStatus.RespondedWithException,
                        $"Modbus exception {slaveException.SlaveExceptionCode}: {slaveException.Message}",
                        null,
                        slaveException.SlaveExceptionCode);
                }
                catch (TimeoutException)
                {
                    return new ReadOutcome(DeviceProbeStatus.NoModbusResponse, "No response before the timeout elapsed.", null);
                }
                catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
                {
                    _logger.LogDebug(ex, "Probe of {Ip}:{Port} unit {UnitId} lost its connection", ipAddress, port, unitId);
                    connection.Dispose();

                    if (attempt >= MaxReconnectAttempts)
                    {
                        return new ReadOutcome(DeviceProbeStatus.NoModbusResponse, $"Connection lost: {ex.Message}", null);
                    }

                    var reconnected = await ConnectAsync(ipAddress, port, options, cancellationToken).ConfigureAwait(false);
                    connection.Adopt(reconnected);

                    if (connection.Master == null)
                    {
                        return new ReadOutcome(DeviceProbeStatus.NoTcpConnection, connection.Error, null);
                    }
                }
            }
        }

        private static ushort[] ReadValues(IModbusMaster master, byte unitId, int startAddress, int count, ScanRegisterType registerType)
        {
            var address = (ushort)startAddress;
            var quantity = (ushort)count;

            return registerType switch
            {
                ScanRegisterType.HoldingRegisters => master.ReadHoldingRegisters(unitId, address, quantity),
                ScanRegisterType.InputRegisters => master.ReadInputRegisters(unitId, address, quantity),
                ScanRegisterType.Coils => ToRegisters(master.ReadCoils(unitId, address, quantity)),
                ScanRegisterType.DiscreteInputs => ToRegisters(master.ReadInputs(unitId, address, quantity)),
                _ => throw new ArgumentOutOfRangeException(nameof(registerType), registerType, "Unsupported register type")
            };
        }

        private static ushort[] ToRegisters(bool[] bits)
        {
            var values = new ushort[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                values[i] = bits[i] ? (ushort)1 : (ushort)0;
            }

            return values;
        }

        private static async Task<ProbeConnection> ConnectAsync(string ipAddress, int port, DeviceScanOptions options, CancellationToken cancellationToken)
        {
            var tcpClient = new TcpClient();
            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(Math.Max(1, options.ConnectTimeoutMs));

                await tcpClient.ConnectAsync(ipAddress, port, timeoutSource.Token).ConfigureAwait(false);

                var master = ModbusIpMaster.CreateIp(tcpClient);
                master.Transport.ReadTimeout = Math.Max(1, options.ResponseTimeoutMs);
                master.Transport.WriteTimeout = Math.Max(1, options.ResponseTimeoutMs);
                master.Transport.Retries = 0;

                return new ProbeConnection(tcpClient, master, string.Empty);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                tcpClient.Dispose();
                return new ProbeConnection(null, null, $"TCP connect timed out after {options.ConnectTimeoutMs} ms.");
            }
            catch (OperationCanceledException)
            {
                tcpClient.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
            {
                tcpClient.Dispose();
                return new ProbeConnection(null, null, ex.Message);
            }
        }

        private sealed record ReadOutcome(DeviceProbeStatus Status, string Message, ushort[]? Values, byte ExceptionCode = 0);

        private sealed class ProbeConnection : IDisposable
        {
            public ProbeConnection(TcpClient? tcpClient, IModbusMaster? master, string error)
            {
                TcpClient = tcpClient;
                Master = master;
                Error = error;
            }

            public TcpClient? TcpClient { get; private set; }
            public IModbusMaster? Master { get; private set; }
            public string Error { get; private set; }

            public void Adopt(ProbeConnection other)
            {
                TcpClient = other.TcpClient;
                Master = other.Master;
                Error = other.Error;
            }

            public void Dispose()
            {
                Master?.Dispose();
                TcpClient?.Dispose();
                Master = null;
                TcpClient = null;
            }
        }
    }
}
