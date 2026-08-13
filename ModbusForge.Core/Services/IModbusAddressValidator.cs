using System.Collections.Generic;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// A contiguous range to read or write in a single Modbus packet.
    /// </summary>
    public sealed record ModbusReadRange(int StartAddress, int Count);

    /// <summary>
    /// Validates Modbus request parameters before they are sent to a device.
    /// </summary>
    public interface IModbusAddressValidator
    {
        bool IsValidUnitId(byte unitId);
        bool IsValidStartAddress(int startAddress);
        bool IsValidCount(int count);
        bool IsValidRange(int startAddress, int count);

        /// <summary>
        /// Validates the count for a specific register area and operation direction.
        /// </summary>
        bool IsValidCount(int count, PlcArea area, bool isWrite = false);

        /// <summary>
        /// Validates the address range for a specific register area and operation direction.
        /// </summary>
        bool IsValidRange(int startAddress, int count, PlcArea area, bool isWrite = false);

        /// <summary>
        /// Validates that the entire start+count range fits inside the Modbus address space,
        /// regardless of the per-packet limit.
        /// </summary>
        bool IsValidAddressRange(int startAddress, int count);

        /// <summary>
        /// Returns the maximum count allowed for a single Modbus request for this area/direction.
        /// </summary>
        int GetMaxCountPerRequest(PlcArea area, bool isWrite = false);

        /// <summary>
        /// Splits a total read/write range into chunks that each fit in one Modbus request.
        /// </summary>
        IEnumerable<ModbusReadRange> GetReadRanges(int startAddress, int count, PlcArea area, bool isWrite = false);
    }
}
