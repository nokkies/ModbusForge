using System;

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
    /// A single captured Modbus request or response frame. Immutable once captured:
    /// rows are added to the log collection and never mutated in place, so per-property
    /// change notifications are not required (the parent collection raises the changes).
    /// </summary>
    public class ModbusFrameLog
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

        /// <summary>
        /// True when a checksum was present and valid (UI: green check).
        /// </summary>
        public bool IsCrcValid => IsValidCrc == true;

        /// <summary>
        /// True when a checksum was present and did not match (UI: red cross).
        /// </summary>
        public bool IsCrcInvalid => IsValidCrc == false;

        /// <summary>
        /// True when the transport carries no checksum to verify (UI: dash).
        /// </summary>
        public bool IsCrcNotApplicable => IsValidCrc == null;

        public byte UnitId { get; set; }

        public byte FunctionCode { get; set; }

        /// <summary>
        /// Returns the raw bytes as a space-delimited hex string.
        /// </summary>
        public string HexString => string.Join(" ", Array.ConvertAll(RawBytes, b => b.ToString("X2")));
    }
}
