using System;

namespace ModbusForge.Services
{
    /// <summary>
    /// Default implementation of Modbus request boundary validation.
    /// </summary>
    public sealed class ModbusAddressValidator : IModbusAddressValidator
    {
        public const int MinUnitId = 1;
        public const int MaxUnitId = 247;
        public const int MinStartAddress = 0;
        public const int MaxStartAddress = ushort.MaxValue;
        public const int MinCount = 1;
        public const int MaxCount = 125; // Modbus protocol maximum for read holding registers

        public bool IsValidUnitId(byte unitId) => unitId >= MinUnitId && unitId <= MaxUnitId;

        public bool IsValidStartAddress(int startAddress) =>
            startAddress >= MinStartAddress && startAddress <= MaxStartAddress;

        public bool IsValidCount(int count) => count >= MinCount && count <= MaxCount;

        public bool IsValidRange(int startAddress, int count)
        {
            if (!IsValidStartAddress(startAddress) || !IsValidCount(count))
                return false;

            // Prevent int overflow and stay inside the 0..65535 address space.
            long end = (long)startAddress + count - 1;
            return end >= MinStartAddress && end <= MaxStartAddress;
        }

        public static void ValidateOrThrow(byte unitId, int startAddress, int count)
        {
            var validator = new ModbusAddressValidator();
            if (!validator.IsValidUnitId(unitId))
                throw new ArgumentOutOfRangeException(nameof(unitId), $"Unit ID must be between {MinUnitId} and {MaxUnitId}.");
            if (!validator.IsValidStartAddress(startAddress))
                throw new ArgumentOutOfRangeException(nameof(startAddress), $"Start address must be between {MinStartAddress} and {MaxStartAddress}.");
            if (!validator.IsValidCount(count))
                throw new ArgumentOutOfRangeException(nameof(count), $"Count must be between {MinCount} and {MaxCount}.");
            if (!validator.IsValidRange(startAddress, count))
                throw new ArgumentOutOfRangeException(nameof(count), $"The requested range {startAddress}..{startAddress + count - 1} exceeds the Modbus address space.");
        }
    }
}
