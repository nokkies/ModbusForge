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

        public LoggingStreamResource(IStreamResource inner, ModbusFrameLogger? logger)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _logger = logger;
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
                LogFrame(_logger, FrameDirection.Rx, buffer, offset, read);

            return read;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (count > 0)
                LogFrame(_logger, FrameDirection.Tx, buffer, offset, count);

            _inner.Write(buffer, offset, count);
        }

        public void Dispose()
        {
            if (_inner is IDisposable disposable)
                disposable.Dispose();
            GC.SuppressFinalize(this);
        }

        private static void TryParseFrame(byte[] raw, out byte unitId, out byte functionCode)
        {
            unitId = 0;
            functionCode = 0;

            if (raw is null || raw.Length < 2)
                return;

            // Modbus TCP has the MBAP header. Protocol ID should be 0x00 0x00.
            if (raw.Length >= 8 && raw[2] == 0x00 && raw[3] == 0x00)
            {
                if (raw.Length > 6) unitId = raw[6];
                if (raw.Length > 7) functionCode = raw[7];
                return;
            }

            // Modbus RTU/ASCII starts with Unit ID then Function Code.
            unitId = raw[0];
            functionCode = raw[1];
        }

        private static void LogFrame(ModbusFrameLogger? logger, FrameDirection direction, byte[] buffer, int offset, int count)
        {
            if (count <= 0 || logger is null)
                return;

            var copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            TryParseFrame(copy, out var unitId, out var functionCode);
            logger.Log(direction, copy, null, unitId, functionCode);
        }
    }
}
