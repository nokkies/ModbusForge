using System;

namespace ModbusForge.Models
{
    /// <summary>
    /// A request queued to the polling engine for background execution.
    /// </summary>
    public class PollingCommand
    {
        /// <summary>
        /// Unique operation identifier used for tracing and coalescing.
        /// </summary>
        public Guid CorrelationId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The Modbus address area to read or write.
        /// </summary>
        public PlcArea Area { get; set; }

        /// <summary>
        /// Unit/slave address.
        /// </summary>
        public byte UnitId { get; set; }

        /// <summary>
        /// Start address.
        /// </summary>
        public int StartAddress { get; set; }

        /// <summary>
        /// Number of points to read or write.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// True for server mode; otherwise client mode.
        /// </summary>
        public bool IsServerMode { get; set; }

        /// <summary>
        /// When the command was queued; used for diagnostics and ordering.
        /// </summary>
        public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Optional: when set, the command is a continuous custom-entry read or write.
        /// </summary>
        public CustomEntry? CustomEntry { get; set; }

        /// <summary>
        /// Optional: when set, the command is a custom write with this value.
        /// </summary>
        public object? WriteValue { get; set; }

        /// <summary>
        /// True when the command is a write; otherwise a read.
        /// </summary>
        public bool IsWrite { get; set; }

        /// <summary>
        /// Returns a stable area/unit key for coalescing duplicate pending commands.
        /// </summary>
        public string GetCoalesceKey() => CustomEntry is null
            ? $"{Area}:{UnitId}"
            : $"{Area}:{UnitId}:custom:{CustomEntry.Name}";
    }
}
