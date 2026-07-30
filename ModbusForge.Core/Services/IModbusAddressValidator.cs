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
    }
}
