using System;

namespace ModbusForge.Models
{
    /// <summary>
    /// Result produced by the polling engine and consumed by the UI layer.
    /// </summary>
    public class PollingResult
    {
        public Guid CorrelationId { get; set; }

        public PlcArea Area { get; set; }

        public byte UnitId { get; set; }

        public int StartAddress { get; set; }

        /// <summary>
        /// Raw 16-bit values for holding and input registers.
        /// </summary>
        public ushort[]? Values { get; set; }

        /// <summary>
        /// Raw boolean states for coils and discrete inputs.
        /// </summary>
        public bool[]? States { get; set; }

        /// <summary>
        /// Optional custom entry name when the command targeted a custom tag.
        /// </summary>
        public string? CustomEntryName { get; set; }

        /// <summary>
        /// True if the operation failed; see <see cref="ErrorMessage"/> for details.
        /// </summary>
        public bool IsError { get; set; }

        public string? ErrorMessage { get; set; }

        public Exception? Exception { get; set; }

        public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
