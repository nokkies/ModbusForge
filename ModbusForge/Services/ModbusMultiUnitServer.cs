using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Modbus.Data;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    /// <summary>
    /// Custom raw Modbus TCP dispatcher that supports multiple Unit IDs on the same port.
    /// Each Unit ID gets its own independent DataStore.
    /// Implements FC01-FC06, FC15, FC16.
    /// </summary>
    public class ModbusMultiUnitServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly ConcurrentDictionary<byte, DataStore> _dataStores = new();
        private readonly ILogger _logger;
        private bool _disposed;

        private const int DefaultDataStoreSize = 10000;
        private const ushort MbapProtocolId = 0x0000;

        public ModbusMultiUnitServer(ILogger logger)
        {
            _logger = logger;
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
                for (ushort i = 1; i <= 16 && i < DefaultDataStoreSize; i++)
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
                    _ = Task.Run(() => HandleClientAsync(client, ct), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        _logger.LogError(ex, "Error accepting TCP connection");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var header = new byte[7];

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // Read 7-byte MBAP header
                        if (!await ReadExactAsync(stream, header, 7, ct)) break;

                        ushort transactionId = (ushort)((header[0] << 8) | header[1]);
                        // header[2..3] = protocol ID (should be 0x0000)
                        ushort length = (ushort)((header[4] << 8) | header[5]);
                        byte unitId = header[6];

                        if (length < 1) continue;

                        // Read PDU (length - 1 because length includes unit ID byte)
                        var pdu = new byte[length - 1];
                        if (!await ReadExactAsync(stream, pdu, pdu.Length, ct)) break;

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
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        _logger.LogDebug(ex, "Client connection closed");
                }
            }
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
        {
            int offset = 0;
            while (offset < count)
            {
                int read;
                try { read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct); }
                catch { return false; }
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        private byte[]? ProcessPdu(byte unitId, byte[] pdu)
        {
            if (pdu.Length == 0) return null;
            var ds = GetOrCreateDataStore(unitId);
            byte fc = pdu[0];

            try
            {
                return fc switch
                {
                    1 => ReadBits(pdu, ds.CoilDiscretes, fc),
                    2 => ReadBits(pdu, ds.InputDiscretes, fc),
                    3 => ReadRegisters(pdu, ds.HoldingRegisters, fc),
                    4 => ReadRegisters(pdu, ds.InputRegisters, fc),
                    5 => WriteSingleCoil(pdu, ds),
                    6 => WriteSingleRegister(pdu, ds),
                    15 => WriteMultipleCoils(pdu, ds),
                    16 => WriteMultipleRegisters(pdu, ds),
                    _ => ExceptionResponse(fc, 1) // Illegal function
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error processing FC{FC} for Unit ID {UnitId}", fc, unitId);
                return ExceptionResponse(fc, 4); // Slave device failure
            }
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
            bool value = pdu[3] == 0xFF;
            if (addr >= ds.CoilDiscretes.Count) return ExceptionResponse(5, 2);
            ds.CoilDiscretes[addr] = value;
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
