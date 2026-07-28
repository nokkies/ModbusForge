using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ModbusForge.Models
{
    /// <summary>
    /// How addresses in an imported register map are numbered.
    /// </summary>
    public enum AddressingConvention
    {
        /// <summary>Addresses are already protocol addresses (0-based).</summary>
        ZeroBased,
        /// <summary>Addresses are 1-based; one is subtracted to get the protocol address.</summary>
        OneBased,
        /// <summary>Modicon 5/6-digit addresses (e.g. 40001 / 400001) that encode the area.</summary>
        Modicon
    }

    /// <summary>
    /// Byte/word ordering for multi-register values.
    /// </summary>
    public enum WordOrder
    {
        /// <summary>Most significant word first (default).</summary>
        BigEndian,
        /// <summary>Least significant word first (word-swapped).</summary>
        LittleEndian
    }

    /// <summary>
    /// Read/write permission declared by a register map.
    /// </summary>
    public enum RegisterAccess
    {
        ReadOnly,
        ReadWrite
    }

    /// <summary>
    /// One row of a vendor register map.
    /// </summary>
    public class RegisterTemplateEntry
    {
        public string TagName { get; set; } = string.Empty;

        public PlcArea RegisterType { get; set; } = PlcArea.HoldingRegister;

        /// <summary>Protocol (0-based) address, after the addressing convention has been applied.</summary>
        public int Address { get; set; }

        /// <summary>Address exactly as written in the source file.</summary>
        public string RawAddress { get; set; } = string.Empty;

        /// <summary>Bit within the register, for packed status words. Null when the whole register is used.</summary>
        public int? Bit { get; set; }

        public TagDataType DataType { get; set; } = TagDataType.UInt16;

        public WordOrder WordOrder { get; set; } = WordOrder.BigEndian;

        /// <summary>Number of registers occupied (strings/arrays). Defaults to the data type width.</summary>
        public int Length { get; set; } = 1;

        public double Scale { get; set; } = 1.0;

        public double Offset { get; set; }

        public string Unit { get; set; } = string.Empty;

        public RegisterAccess Access { get; set; } = RegisterAccess.ReadWrite;

        /// <summary>Raw-value to label map parsed from an "enum" column (e.g. "0=Off;1=On").</summary>
        public Dictionary<int, string> Enum { get; set; } = new();

        public string Description { get; set; } = string.Empty;

        public double? Default { get; set; }

        public double? RangeMin { get; set; }

        public double? RangeMax { get; set; }

        public string Group { get; set; } = "Default";

        /// <summary>1-based row this entry was parsed from, used by the import preview.</summary>
        public int SourceRow { get; set; }

        /// <summary>Creates the tag this template entry maps to.</summary>
        public Tag ToTag() => new()
        {
            Name = TagName,
            Description = Description,
            Group = Group,
            Area = RegisterType,
            Address = Address,
            DataType = Bit.HasValue ? TagDataType.Bool : DataType,
            Scale = Scale,
            Offset = Offset,
            Units = Unit,
            IsReadOnly = Access == RegisterAccess.ReadOnly,
            AlarmLow = RangeMin,
            AlarmHigh = RangeMax,
            IsAlarmEnabled = RangeMin.HasValue || RangeMax.HasValue,
            ValueEnum = Enum.Count > 0 ? new Dictionary<int, string>(Enum) : null,
        };

        /// <summary>Serializes <see cref="Enum"/> back to the "0=Off;1=On" column format.</summary>
        public string FormatEnum() =>
            string.Join(";", Enum.OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key.ToString(CultureInfo.InvariantCulture)}={kvp.Value}"));
    }

    /// <summary>
    /// A named, reusable device template persisted under %AppData%\ModbusForge\templates.
    /// </summary>
    public class RegisterTemplate
    {
        public int SchemaVersion { get; set; } = 1;

        public string Name { get; set; } = string.Empty;

        public string SourceFile { get; set; } = string.Empty;

        public DateTime ImportedUtc { get; set; } = DateTime.UtcNow;

        public AddressingConvention Addressing { get; set; } = AddressingConvention.ZeroBased;

        public List<RegisterTemplateEntry> Entries { get; set; } = new();
    }
}
