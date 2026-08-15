using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using ModbusForge.Data;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Custom raw Modbus TCP dispatcher that supports multiple Unit IDs on the same port.
    /// Each Unit ID gets its own independent DataStore.
    /// Implements FC01-FC06, FC15, FC16, FC22, FC23 and FC43/MEI 14.
    /// </summary>
    public class ModbusMultiUnitServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly ConcurrentDictionary<byte, DataStore> _dataStores = new();
        private readonly ILogger _logger;
        private readonly IConsoleLoggerService? _consoleLoggerService;
        private bool _disposed;
        private int _activeClients;

        private const int DefaultDataStoreSize = ModbusAddressValidator.MaxTotalCount;
        private const ushort MbapProtocolId = 0x0000;
        private const byte MeiTypeDeviceIdentification = 0x0E;
        private const byte DeviceIdMoreFollows = 0xFF;
        private const byte DeviceIdNoMoreFollows = 0x00;

        /// <summary>
        /// Max bytes of FC43 object data per response: the Modbus TCP PDU is capped at
        /// 253 bytes (MBAP length 254 includes the unit ID byte), and the FC43 header
        /// itself is 7 bytes - so 246, not 250 (a 250-byte body would produce an
        /// oversize PDU that violates the protocol frame limit).
        /// </summary>
        private const int MaxDeviceIdResponseBytes = 246;

        /// <summary>Strict FC05 coil values; any other 16-bit value is an illegal data value.</summary>
        private const ushort CoilValueOn = 0xFF00;
        private const ushort CoilValueOff = 0x0000;

        /// <summary>Upper bound on simultaneous client connections.</summary>
        private const int MaxClients = 10;

        /// <summary>Clients that go quiet for this long are disconnected (frees the slot).</summary>
        private const int ClientIdleTimeoutMs = 10 * 60 * 1000;

        /// <summary>
        /// Identity reported by FC43 (Read Device Identification).
        /// </summary>
        public DeviceIdentification DeviceIdentification { get; set; } =
            DeviceIdentification.CreateDefault(
                typeof(ModbusMultiUnitServer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

        public ModbusMultiUnitServer(ILogger logger)
            : this(logger, null)
        {
        }

        public ModbusMultiUnitServer(ILogger logger, IConsoleLoggerService? consoleLoggerService)
        {
            _logger = logger;
            _consoleLoggerService = consoleLoggerService;
        }

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public System.Net.EndPoint? LocalEndpoint => _listener?.LocalEndpoint;

        public DataStore GetOrCreateDataStore(byte unitId)
        {
            return _dataStores.GetOrAdd(unitId, id =>
            {
                var ds = new DataStore();
                for (int i = 0; i < DefaultDataStoreSize; i++) ds.HoldingRegisters.Add(0);
                for (int i = 0; i < DefaultDataStoreSize; i++) ds.InputRegisters.Add(0);
                for (int i = 0; i < DefaultDataStoreSize; i++) ds.CoilDiscretes.Add(false);
                for (int i = 0; i < DefaultDataStoreSize; i++) ds.InputDiscretes.Add(false);
                // Seed test data
                for (ushort i = 1; i <= 16; i++)
                    ds.HoldingRegisters[i] = (ushort)(i * 10);
                _logger.LogInformation("Created DataStore for Unit ID {UnitId}", id);
                return ds;
            });
        }

        public DataStore? TryGetDataStore(byte unitId)
            => _dataStores.TryGetValue(unitId, out var ds) ? ds : null;

        public IEnumerable<byte> UnitIds => _dataStores.Keys;

        public void Start(IPEndPoint endpoint, IEnumerable<byte> unitIds)
        {
            if (IsRunning) return;

            _listener = new TcpListener(endpoint);
            _listener.Start();

            // Pre-create data stores for configured unit IDs
            foreach (var id in unitIds)
                GetOrCreateDataStore(id);

            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));

            _logger.LogInformation("ModbusMultiUnitServer started on {Endpoint}", endpoint);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
            _cts?.Dispose();
            _cts = null;
            _listener = null;
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener!.AcceptTcpClientAsync(ct);

                    if (Interlocked.Increment(ref _activeClients) > MaxClients)
                    {
                        Interlocked.Decrement(ref _activeClients);
                        _logger.LogWarning(
                            "Modbus server max clients ({MaxClients}) reached; rejecting connection from {Remote}",
                            MaxClients, client.Client.RemoteEndPoint);
                        try { client.Dispose(); } catch { /* already closed */ }
                        continue;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClientAsync(client, ct);
                        }
                        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                        {
                            _logger.LogError(ex, "Unhandled error handling Modbus client connection");
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _activeClients);
                        }
                    }, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (!ct.IsCancellationRequested)
                        _logger.LogError(ex, "Error accepting TCP connection");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            _consoleLoggerService?.Log($"Client connected from {remoteEndpoint}");
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var header = new byte[7];

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // Per-request idle timeout: a client that goes quiet is dropped after
                        // ClientIdleTimeoutMs, freeing its slot instead of holding it forever.
                        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        readCts.CancelAfter(ClientIdleTimeoutMs);

                        // Read 7-byte MBAP header
                        if (!await ReadExactAsync(stream, header, 7, readCts.Token)) break;

                        ushort transactionId = (ushort)((header[0] << 8) | header[1]);
                        ushort protocolId = (ushort)((header[2] << 8) | header[3]);
                        ushort length = (ushort)((header[4] << 8) | header[5]);
                        byte unitId = header[6];

                        if (protocolId != MbapProtocolId)
                        {
                            _logger.LogWarning(
                                "Invalid Modbus MBAP protocol ID {ProtocolId:X4} from {Remote}. Closing connection.",
                                protocolId, remoteEndpoint);
                            break;
                        }

                        // Max Modbus TCP frame size is 260 bytes (7-byte MBAP + 253-byte PDU)
                        // MBAP length includes UnitID (1 byte) + PDU (up to 253 bytes)
                        if (length < 1 || length > 254)
                        {
                            _logger.LogWarning("Invalid Modbus MBAP length: {Length} for Unit ID {UnitId}. Closing connection.", length, unitId);
                            break;
                        }

                        // Read PDU (length - 1 because length includes unit ID byte)
                        var pdu = new byte[length - 1];
                        if (!await ReadExactAsync(stream, pdu, pdu.Length, readCts.Token)) break;

                        var details = FormatRequestDetails(pdu);
                        _consoleLoggerService?.Log($"Request from {remoteEndpoint} Unit ID {unitId} FC {(pdu.Length > 0 ? pdu[0] : 0)}{details}");

                        var responseData = ProcessPdu(unitId, pdu);
                        if (responseData == null) continue;

                        // Build response: MBAP (7 bytes) + PDU
                        ushort respLen = (ushort)(responseData.Length + 1); // +1 for unit ID
                        var response = new byte[6 + 1 + responseData.Length];
                        response[0] = (byte)(transactionId >> 8);
                        response[1] = (byte)(transactionId & 0xFF);
                        response[2] = 0; response[3] = 0; // Protocol ID
                        response[4] = (byte)(respLen >> 8);
                        response[5] = (byte)(respLen & 0xFF);
                        response[6] = unitId;
                        Buffer.BlockCopy(responseData, 0, response, 7, responseData.Length);

                        await stream.WriteAsync(response, ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogDebug("Client {Remote} connection ended by server shutdown", remoteEndpoint);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (!ct.IsCancellationRequested)
                        _logger.LogDebug(ex, "Client {Remote} connection closed", remoteEndpoint);
                }
            }
        }

        private async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
        {
            int offset = 0;
            while (offset < count)
            {
                int read;
                try { read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogDebug(ex, "Error reading from stream");
                    return false;
                }
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        private byte[]? ProcessPdu(byte unitId, byte[] pdu)
        {
            if (pdu.Length == 0) return null;

            // Broadcast address: per the Modbus specification, writes are applied to every
            // unit and NO response is sent (a response to a broadcast would corrupt the
            // transaction space for other clients). Previously this created a data store
            // for Unit ID 0 and answered, which is not spec-compliant.
            if (unitId == 0)
            {
                ApplyBroadcastWrite(pdu);
                return null;
            }

            var ds = GetOrCreateDataStore(unitId);
            byte fc = pdu[0];

            try
            {
                byte[]? response;
                if (fc == 0x2B)
                {
                    response = ReadDeviceIdentification(pdu);
                }
                else
                {
                    lock (ds)
                    {
                        response = fc switch
                        {
                            1 => ReadBits(pdu, ds.CoilDiscretes, fc),
                            2 => ReadBits(pdu, ds.InputDiscretes, fc),
                            3 => ReadRegisters(pdu, ds.HoldingRegisters, fc),
                            4 => ReadRegisters(pdu, ds.InputRegisters, fc),
                            5 => WriteSingleCoil(pdu, ds),
                            6 => WriteSingleRegister(pdu, ds),
                            15 => WriteMultipleCoils(pdu, ds),
                            16 => WriteMultipleRegisters(pdu, ds),
                            22 => MaskWriteRegister(pdu, ds),
                            23 => ReadWriteMultipleRegisters(pdu, ds),
                            _ => ExceptionResponse(fc, 1) // Illegal function
                        };
                    }
                }

                return response;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Error processing FC{FC} for Unit ID {UnitId}", fc, unitId);
                return ExceptionResponse(fc, 4); // Slave device failure
            }
        }

        /// <summary>
        /// Applies a broadcast (Unit ID 0) write PDU to every configured unit's data store.
        /// Reads addressed to Unit ID 0 are ignored (no response is ever sent for broadcasts).
        /// </summary>
        private void ApplyBroadcastWrite(byte[] pdu)
        {
            byte fc = pdu[0];
            if (fc is not (5 or 6 or 15 or 16 or 22 or 23))
            {
                _logger.LogDebug("Broadcast (Unit ID 0) read FC{FC} ignored - broadcasts receive no response", fc);
                return;
            }

            foreach (var ds in _dataStores.Values)
            {
                lock (ds)
                {
                    try
                    {
                        switch (fc)
                        {
                            case 5: WriteSingleCoil(pdu, ds); break;
                            case 6: WriteSingleRegister(pdu, ds); break;
                            case 15: WriteMultipleCoils(pdu, ds); break;
                            case 16: WriteMultipleRegisters(pdu, ds); break;
                            case 22: MaskWriteRegister(pdu, ds); break;
                            case 23: ReadWriteMultipleRegisters(pdu, ds); break;
                        }
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        _logger.LogWarning(ex, "Broadcast FC{FC} failed for one of the unit data stores", fc);
                    }
                }
            }
        }

        private string FormatRequestDetails(byte[] pdu)
        {
            if (pdu.Length < 5) return string.Empty;
            byte fc = pdu[0];
            int address = (pdu[1] << 8) | pdu[2];

            return fc switch
            {
                1 or 2 or 3 or 4 => pdu.Length >= 5 ? $" addr {address} count {((pdu[3] << 8) | pdu[4])}" : string.Empty,
                5 => pdu.Length >= 5 ? $" addr {address} = {(pdu[3] == 0xFF ? 1 : 0)}" : string.Empty,
                6 => pdu.Length >= 5 ? $" addr {address} = {((pdu[3] << 8) | pdu[4])}" : string.Empty,
                15 or 16 => pdu.Length >= 6 ? $" addr {address} count {((pdu[3] << 8) | pdu[4])}" : string.Empty,
                22 => pdu.Length >= 7 ? $" addr {address}" : string.Empty,
                23 => pdu.Length >= 10 ? $" read {address}/{((pdu[3] << 8) | pdu[4])} write {((pdu[5] << 8) | pdu[6])}/{((pdu[7] << 8) | pdu[8])}" : string.Empty,
                _ => string.Empty
            };
        }

        // FC01/FC02: Read Coils / Read Discrete Inputs
        // PDU address is 0-based; DataStore is 1-based (index 0 is unused placeholder), so add 1.
        private static byte[] ReadBits(byte[] pdu, ModbusDataCollection<bool> collection, byte fc)
        {
            if (pdu.Length < 5) return ExceptionResponse(fc, 3);
            int start = ((pdu[1] << 8) | pdu[2]) + 1; // +1: PDU→DataStore index
            int count = (pdu[3] << 8) | pdu[4];
            if (count < 1 || count > 2000) return ExceptionResponse(fc, 3);
            if (start + count > collection.Count) return ExceptionResponse(fc, 2);

            int byteCount = (count + 7) / 8;
            var resp = new byte[2 + byteCount];
            resp[0] = fc;
            resp[1] = (byte)byteCount;
            for (int i = 0; i < count; i++)
            {
                if (collection[start + i])
                    resp[2 + i / 8] |= (byte)(1 << (i % 8));
            }
            return resp;
        }

        // FC03/FC04: Read Holding / Input Registers
        // PDU address is 0-based; DataStore is 1-based, so add 1.
        private static byte[] ReadRegisters(byte[] pdu, ModbusDataCollection<ushort> collection, byte fc)
        {
            if (pdu.Length < 5) return ExceptionResponse(fc, 3);
            int start = ((pdu[1] << 8) | pdu[2]) + 1; // +1: PDU→DataStore index
            int count = (pdu[3] << 8) | pdu[4];
            if (count < 1 || count > 125) return ExceptionResponse(fc, 3);
            if (start + count > collection.Count) return ExceptionResponse(fc, 2);

            var resp = new byte[2 + count * 2];
            resp[0] = fc;
            resp[1] = (byte)(count * 2);
            for (int i = 0; i < count; i++)
            {
                ushort val = collection[start + i];
                resp[2 + i * 2] = (byte)(val >> 8);
                resp[3 + i * 2] = (byte)(val & 0xFF);
            }
            return resp;
        }

        // FC05: Write Single Coil
        private static byte[] WriteSingleCoil(byte[] pdu, DataStore ds)
        {
            if (pdu.Length < 5) return ExceptionResponse(5, 3);
            int addr = ((pdu[1] << 8) | pdu[2]) + 1; // +1: PDU→DataStore index
            ushort coilValue = (ushort)((pdu[3] << 8) | pdu[4]);

            // The spec only allows 0xFF00 (ON) or 0x0000 (OFF); anything else is an
            // illegal data value. (Previously any non-0xFF high byte silently read as OFF.)
            if (coilValue != CoilValueOn && coilValue != CoilValueOff)
                return ExceptionResponse(5, 3);

            if (addr >= ds.CoilDiscretes.Count) return ExceptionResponse(5, 2);
            ds.CoilDiscretes[addr] = coilValue == CoilValueOn;
            return pdu[..5]; // Echo request
        }

        // FC06: Write Single Register
        private static byte[] WriteSingleRegister(byte[] pdu, DataStore ds)
        {
            if (pdu.Length < 5) return ExceptionResponse(6, 3);
            int addr = ((pdu[1] << 8) | pdu[2]) + 1; // +1: PDU→DataStore index
            ushort value = (ushort)((pdu[3] << 8) | pdu[4]);
            if (addr >= ds.HoldingRegisters.Count) return ExceptionResponse(6, 2);
            ds.HoldingRegisters[addr] = value;
            return pdu[..5]; // Echo request
        }

        // FC15: Write Multiple Coils
        private static byte[] WriteMultipleCoils(byte[] pdu, DataStore ds)
        {
            if (pdu.Length < 6) return ExceptionResponse(15, 3);
            int start = ((pdu[1] << 8) | pdu[2]) + 1; // +1: PDU→DataStore index
            int count = (pdu[3] << 8) | pdu[4];
            int byteCount = pdu[5];
            if (pdu.Length < 6 + byteCount) return ExceptionResponse(15, 3);
            if (start + count > ds.CoilDiscretes.Count) return ExceptionResponse(15, 2);
            for (int i = 0; i < count; i++)
                ds.CoilDiscretes[start + i] = (pdu[6 + i / 8] & (1 << (i % 8))) != 0;
            return new byte[] { 15, pdu[1], pdu[2], pdu[3], pdu[4] };
        }

        // FC16: Write Multiple Registers
        private static byte[] WriteMultipleRegisters(byte[] pdu, DataStore ds)
        {
            if (pdu.Length < 6) return ExceptionResponse(16, 3);
            int start = ((pdu[1] << 8) | pdu[2]) + 1; // +1: PDU→DataStore index
            int count = (pdu[3] << 8) | pdu[4];
            int byteCount = pdu[5];
            if (pdu.Length < 6 + byteCount) return ExceptionResponse(16, 3);
            if (start + count > ds.HoldingRegisters.Count) return ExceptionResponse(16, 2);
            for (int i = 0; i < count; i++)
                ds.HoldingRegisters[start + i] = (ushort)((pdu[6 + i * 2] << 8) | pdu[7 + i * 2]);
            return new byte[] { 16, pdu[1], pdu[2], pdu[3], pdu[4] };
        }

        // FC22: Mask Write Register — result = (current AND andMask) OR (orMask AND NOT andMask)
        private static byte[] MaskWriteRegister(byte[] pdu, DataStore ds)
        {
            if (pdu.Length < 7) return ExceptionResponse(22, 3);
            int addr = ((pdu[1] << 8) | pdu[2]) + 1; // +1: PDU→DataStore index
            ushort andMask = (ushort)((pdu[3] << 8) | pdu[4]);
            ushort orMask = (ushort)((pdu[5] << 8) | pdu[6]);
            if (addr >= ds.HoldingRegisters.Count) return ExceptionResponse(22, 2);

            ushort current = ds.HoldingRegisters[addr];
            ds.HoldingRegisters[addr] = (ushort)((current & andMask) | (orMask & ~andMask));
            return pdu[..7]; // Echo request
        }

        // FC23: Read/Write Multiple Registers — the write is performed before the read
        private static byte[] ReadWriteMultipleRegisters(byte[] pdu, DataStore ds)
        {
            if (pdu.Length < 10) return ExceptionResponse(23, 3);
            int readStart = ((pdu[1] << 8) | pdu[2]) + 1; // +1: PDU→DataStore index
            int readCount = (pdu[3] << 8) | pdu[4];
            int writeStart = ((pdu[5] << 8) | pdu[6]) + 1;
            int writeCount = (pdu[7] << 8) | pdu[8];
            int writeByteCount = pdu[9];

            if (readCount < 1 || readCount > 125 || writeCount < 1 || writeCount > 121
                || writeByteCount != writeCount * 2 || pdu.Length < 10 + writeByteCount)
                return ExceptionResponse(23, 3);
            if (readStart + readCount > ds.HoldingRegisters.Count || writeStart + writeCount > ds.HoldingRegisters.Count)
                return ExceptionResponse(23, 2);

            for (int i = 0; i < writeCount; i++)
                ds.HoldingRegisters[writeStart + i] = (ushort)((pdu[10 + (i * 2)] << 8) | pdu[11 + (i * 2)]);

            var resp = new byte[2 + (readCount * 2)];
            resp[0] = 23;
            resp[1] = (byte)(readCount * 2);
            for (int i = 0; i < readCount; i++)
            {
                ushort val = ds.HoldingRegisters[readStart + i];
                resp[2 + (i * 2)] = (byte)(val >> 8);
                resp[3 + (i * 2)] = (byte)(val & 0xFF);
            }
            return resp;
        }

        // FC43/MEI 14: Read Device Identification
        private byte[] ReadDeviceIdentification(byte[] pdu)
        {
            if (pdu.Length < 4) return ExceptionResponse(0x2B, 3);
            if (pdu[1] != MeiTypeDeviceIdentification) return ExceptionResponse(0x2B, 1);

            byte readDeviceIdCode = pdu[2];
            byte startObjectId = pdu[3];
            if (readDeviceIdCode < 1 || readDeviceIdCode > 4) return ExceptionResponse(0x2B, 3);

            var identification = DeviceIdentification;
            var category = (DeviceIdCategory)readDeviceIdCode;
            var objectIds = new List<byte>(identification.ObjectIdsFor(category));

            if (category == DeviceIdCategory.Individual)
            {
                if (!identification.Objects.ContainsKey(startObjectId))
                    return ExceptionResponse(0x2B, 2);
                objectIds = new List<byte> { startObjectId };
            }
            else
            {
                objectIds.RemoveAll(id => id < startObjectId);
                if (objectIds.Count == 0) return ExceptionResponse(0x2B, 2);
            }

            var body = new List<byte>();
            byte moreFollows = DeviceIdNoMoreFollows;
            byte nextObjectId = 0;
            byte objectCount = 0;

            foreach (var id in objectIds)
            {
                var value = Encoding.ASCII.GetBytes(identification.GetObject(id));
                if (body.Count + value.Length + 2 > MaxDeviceIdResponseBytes)
                {
                    moreFollows = DeviceIdMoreFollows;
                    nextObjectId = id;
                    break;
                }
                body.Add(id);
                body.Add((byte)value.Length);
                body.AddRange(value);
                objectCount++;
            }

            var resp = new List<byte>
            {
                0x2B,
                MeiTypeDeviceIdentification,
                readDeviceIdCode,
                identification.ConformityLevel,
                moreFollows,
                nextObjectId,
                objectCount
            };
            resp.AddRange(body);
            return resp.ToArray();
        }

        private static byte[] ExceptionResponse(byte fc, byte exceptionCode)
            => new byte[] { (byte)(fc | 0x80), exceptionCode };

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
