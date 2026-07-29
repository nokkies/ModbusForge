using System;
using System.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Direction of a captured Modbus frame.
    /// </summary>
    public enum FrameDirection
    {
        Tx,
        Rx
    }

    /// <summary>
    /// A single captured Modbus request or response frame.
    /// </summary>
    public class ModbusFrameLog : INotifyPropertyChanged
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Milliseconds since the previous captured frame.
        /// </summary>
        public double DeltaMs { get; set; }

        public FrameDirection Direction { get; set; }

        /// <summary>
        /// Raw captured bytes. For Modbus TCP this is MBAP + PDU.
        /// For Modbus RTU this includes the CRC16.
        /// </summary>
        public byte[] RawBytes { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// True when the transport checksum (CRC16 for RTU/ASCII) is valid.
        /// Null for transports without a checksum.
        /// </summary>
        public bool? IsValidCrc { get; set; }

        public byte UnitId { get; set; }

        public byte FunctionCode { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Returns the raw bytes as a space-delimited hex string.
        /// </summary>
        public string HexString => string.Join(" ", Array.ConvertAll(RawBytes, b => b.ToString("X2")));
    }
}
