using ModbusForge.Models;

namespace ModbusForge.Services
{
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
    }
}
