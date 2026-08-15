using System;
using System.Collections.Generic;
using ModbusForge.Models;

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
        public const int MaxTotalCount = MaxStartAddress + 1; // Largest possible contiguous read across the whole address space

        // Modbus protocol per-function limits.
        private const int MaxReadCoils = 2000;
        private const int MaxWriteCoils = 1968;
        private const int MaxReadRegisters = 125;

        /// <summary>FC16 (Write Multiple Registers) allows at most 123 register values
        /// per frame - the single-write portion of FC23 is capped lower (see
        /// <see cref="MaxReadWriteWriteCount"/>).</summary>
        public const int MaxWriteRegisters = 123;

        /// <summary>FC23 (Read/Write Multiple Registers) caps the WRITE quantity at 121 -
        /// fewer than FC16's 123, per MBE 8501.</summary>
        public const int MaxReadWriteWriteCount = 121;

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

        public bool IsValidCount(int count, PlcArea area, bool isWrite = false)
        {
            if (count < MinCount)
                return false;

            var max = GetMaxCount(area, isWrite);
            return count <= max;
        }

        public bool IsValidRange(int startAddress, int count, PlcArea area, bool isWrite = false)
        {
            if (!IsValidStartAddress(startAddress) || !IsValidCount(count, area, isWrite))
                return false;

            long end = (long)startAddress + count - 1;
            return end >= MinStartAddress && end <= MaxStartAddress;
        }

        public bool IsValidAddressRange(int startAddress, int count)
        {
            if (count < MinCount)
                return false;

            if (!IsValidStartAddress(startAddress))
                return false;

            long end = (long)startAddress + count - 1;
            return end >= MinStartAddress && end <= MaxStartAddress;
        }

        public int GetMaxCountPerRequest(PlcArea area, bool isWrite = false) => GetMaxCount(area, isWrite);

        public IEnumerable<ModbusReadRange> GetReadRanges(int startAddress, int count, PlcArea area, bool isWrite = false)
        {
            if (!IsValidAddressRange(startAddress, count))
                throw new ArgumentOutOfRangeException(nameof(count), $"The requested range {startAddress}..{startAddress + count - 1} exceeds the Modbus address space.");

            int max = GetMaxCount(area, isWrite);
            int offset = 0;
            while (offset < count)
            {
                int chunk = Math.Min(max, count - offset);
                yield return new ModbusReadRange(startAddress + offset, chunk);
                offset += chunk;
            }
        }

        private static int GetMaxCount(PlcArea area, bool isWrite) => area switch
        {
            PlcArea.Coil => isWrite ? MaxWriteCoils : MaxReadCoils,
            PlcArea.DiscreteInput => MaxReadCoils,
            PlcArea.HoldingRegister or PlcArea.InputRegister => isWrite ? MaxWriteRegisters : MaxReadRegisters,
            _ => MaxCount
        };

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
