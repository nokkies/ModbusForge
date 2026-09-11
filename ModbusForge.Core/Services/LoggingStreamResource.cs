using System;
using NModbus.IO;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Decorator for a Modbus IStreamResource that records every transmitted and received frame.
    /// </summary>
    public class LoggingStreamResource : IStreamResource, IDisposable
    {
        private readonly IStreamResource _inner;
        private readonly ModbusFrameLogger? _logger;
        private readonly TransportType _transport;

        public LoggingStreamResource(IStreamResource inner, ModbusFrameLogger? logger)
            : this(inner, logger, TransportType.Tcp)
        {
        }

        public LoggingStreamResource(IStreamResource inner, ModbusFrameLogger? logger, TransportType transport)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _logger = logger;
            _transport = transport;
        }

        public int InfiniteTimeout => _inner.InfiniteTimeout;

        public int ReadTimeout
        {
            get => _inner.ReadTimeout;
            set => _inner.ReadTimeout = value;
        }

        public int WriteTimeout
        {
            get => _inner.WriteTimeout;
            set => _inner.WriteTimeout = value;
        }

        public void DiscardInBuffer() => _inner.DiscardInBuffer();

        public int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            if (read > 0)
                LogFrame(buffer, offset, read, FrameDirection.Rx);

            return read;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (count > 0)
                LogFrame(buffer, offset, count, FrameDirection.Tx);

            _inner.Write(buffer, offset, count);
        }

        public void Dispose()
        {
            if (_inner is IDisposable disposable)
                disposable.Dispose();
            GC.SuppressFinalize(this);
        }

        private void TryParseFrame(byte[] raw, out byte unitId, out byte functionCode)
        {
            unitId = 0;
            functionCode = 0;

            if (raw is null || raw.Length < 2)
                return;

            if (_transport == TransportType.Tcp)
            {
                // MBAP: transaction(2) + protocol(2) + length(2) + unit(1) + FC(1)
                if (raw.Length >= 8 && raw[2] == 0x00 && raw[3] == 0x00)
                {
                    unitId = raw[6];
                    functionCode = raw[7];
                }
            }
            else
            {
                // RTU/ASCII: unit + FC + payload (+ CRC). Parsed deterministically -
                // the previous heuristic (any frame with raw[2..3] == 0x00 was treated as
                // MBAP) misparsed legitimate RTU frames, e.g. reads at low addresses.
                unitId = raw[0];
                functionCode = raw[1];
            }
        }

        private void LogFrame(byte[] buffer, int offset, int count, FrameDirection direction)
        {
            if (count <= 0 || _logger is null)
                return;

            var copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            TryParseFrame(copy, out var unitId, out var functionCode);
            _logger.Log(direction, copy, null, unitId, functionCode);
        }
    }
}
