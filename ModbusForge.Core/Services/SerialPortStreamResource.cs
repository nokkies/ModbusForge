using System;
using System.IO.Ports;
using NModbus.IO;

namespace ModbusForge.Services
{
    /// <summary>
    /// Adapts a <see cref="SerialPort"/> to NModbus v3's <see cref="IStreamResource"/>.
    /// </summary>
    internal sealed class SerialPortStreamResource : IStreamResource, IDisposable
    {
        private readonly SerialPort _serialPort;

        public SerialPortStreamResource(SerialPort serialPort)
        {
            _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
        }

        // NModbus uses InfiniteTimeout as a sentinel value to decide whether a timeout
        // is active at all; it must be the platform constant (255), not the current
        // ReadTimeout value.
        public int InfiniteTimeout => SerialPort.InfiniteTimeout;

        public int ReadTimeout
        {
            get => _serialPort.ReadTimeout;
            set => _serialPort.ReadTimeout = value;
        }

        public int WriteTimeout
        {
            get => _serialPort.WriteTimeout;
            set => _serialPort.WriteTimeout = value;
        }

        public void DiscardInBuffer() => _serialPort.DiscardInBuffer();

        public int Read(byte[] buffer, int offset, int count)
            => _serialPort.BaseStream.Read(buffer, offset, count);

        public void Write(byte[] buffer, int offset, int count)
            => _serialPort.BaseStream.Write(buffer, offset, count);

        public void Dispose()
        {
            // The SerialPort is owned by the caller; we do not dispose it here.
        }
    }
}
