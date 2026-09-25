using System;
using System.Collections.Generic;
using ModbusForge.Data;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// A resolved storage location: a typed span of bytes in one memory space, a
    /// single bit of a bit-addressed table (%M, %I, %S), or one bit of a value
    /// (bit extraction such as <c>Word.3</c>, or a BOOL located on <c>%MW10.3</c>).
    /// </summary>
    public readonly struct PlcLocation
    {
        public PlcLocation(PlcSpace space, int offset, PlcType type, int bit = -1)
        {
            Space = space;
            Offset = offset;
            Type = type;
            Bit = bit;
        }

        public PlcSpace Space { get; }

        /// <summary>Byte offset in a byte space; bit index in a bit table.</summary>
        public int Offset { get; }

        public PlcType Type { get; }

        /// <summary>Bit number within the bytes at <see cref="Offset"/>, or -1.</summary>
        public int Bit { get; }

        public bool IsValid => Space != null;
    }

    /// <summary>One memory space: private bytes, a Modbus register table viewed as bytes, or a bit table.</summary>
    public abstract class PlcSpace
    {
        /// <summary>True when locations are bits (%M, %I, %S) rather than bytes.</summary>
        public virtual bool IsBitTable => false;

        public abstract byte ReadByte(int offset);
        public abstract void WriteByte(int offset, byte value);

        public virtual void ReadBytes(int offset, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++) destination[i] = ReadByte(offset + i);
        }

        public virtual void WriteBytes(int offset, ReadOnlySpan<byte> source)
        {
            for (var i = 0; i < source.Length; i++) WriteByte(offset + i, source[i]);
        }

        /// <summary>Sets or clears bit <paramref name="bit"/> (0-7) of the byte at <paramref name="offset"/>.</summary>
        public virtual void WriteBitOfByte(int offset, int bit, bool value)
        {
            var current = ReadByte(offset);
            var mask = (byte)(1 << bit);
            var updated = value ? (byte)(current | mask) : (byte)(current & ~mask);
            if (updated != current) WriteByte(offset, updated);
        }

        public virtual bool ReadBit(int index) => (ReadByte(index / 8) & (1 << (index % 8))) != 0;

        public virtual void WriteBit(int index, bool value)
        {
            var current = ReadByte(index / 8);
            var mask = (byte)(1 << (index % 8));
            WriteByte(index / 8, value ? (byte)(current | mask) : (byte)(current & ~mask));
        }
    }

    /// <summary>Private controller memory for one unlocated variable (or a system table).</summary>
    public sealed class PlcByteSpace : PlcSpace
    {
        private byte[] _bytes;

        public PlcByteSpace(int size) => _bytes = new byte[Math.Max(size, 1)];

        public int Size => _bytes.Length;

        public override byte ReadByte(int offset) => offset >= 0 && offset < _bytes.Length ? _bytes[offset] : (byte)0;

        public override void WriteByte(int offset, byte value)
        {
            if (offset < 0) return;
            if (offset >= _bytes.Length) Array.Resize(ref _bytes, Math.Max(offset + 1, _bytes.Length * 2));
            _bytes[offset] = value;
        }
    }

    /// <summary>
    /// A Modbus register table (holding or input registers) of the data store being
    /// scanned, as bytes: byte 2n is register n's low byte and 2n+1 its high byte, so
    /// %MWn starts at byte offset 2n and a 32-bit value keeps its least significant
    /// word in the lower register.
    /// </summary>
    public sealed class PlcRegisterSpace : PlcSpace
    {
        private readonly PlcMemoryArea _area;
        private readonly PlcMemory _memory;

        internal PlcRegisterSpace(PlcMemory memory, PlcMemoryArea area)
        {
            _memory = memory;
            _area = area;
        }

        private ModbusDataCollection<ushort>? Table => _memory.Store is not { } store ? null
            : _area == PlcMemoryArea.InputRegister ? store.InputRegisters : store.HoldingRegisters;

        public override byte ReadByte(int offset)
        {
            var word = ReadRegister(Table, offset / 2);
            return (byte)(offset % 2 == 0 ? word & 0xFF : word >> 8);
        }

        public override void WriteByte(int offset, byte value)
        {
            var table = Table;
            var register = offset / 2;
            if (!InRange(table, register)) return;
            var word = table![register];
            var updated = offset % 2 == 0 ? (ushort)((word & 0xFF00) | value) : (ushort)((word & 0x00FF) | (value << 8));
            if (updated != word) table[register] = updated;
        }

        /// <summary>One register read, and a write only when the bit changes.</summary>
        public override void WriteBitOfByte(int offset, int bit, bool value)
        {
            var table = Table;
            var register = offset / 2;
            if (!InRange(table, register)) return;
            var word = table![register];
            var mask = (ushort)(1 << (bit + (offset % 2) * 8));
            var updated = value ? (ushort)(word | mask) : (ushort)(word & ~mask);
            if (updated != word) table[register] = updated;
        }

        public override void ReadBytes(int offset, Span<byte> destination)
        {
            if (destination.Length == 0) return;
            var (first, words) = ReadWords(Table, offset, destination.Length);
            for (var i = 0; i < destination.Length; i++)
            {
                var position = offset + i;
                var index = position / 2 - first;
                var word = index >= 0 && index < words.Length ? words[index] : (ushort)0;
                destination[i] = (byte)(position % 2 == 0 ? word & 0xFF : word >> 8);
            }
        }

        public override void WriteBytes(int offset, ReadOnlySpan<byte> source)
        {
            if (source.Length == 0) return;
            var table = Table;
            var (first, words) = ReadWords(table, offset, source.Length);
            if (words.Length == 0) return;

            var changed = false;
            for (var i = 0; i < source.Length; i++)
            {
                var position = offset + i;
                var index = position / 2 - first;
                if (index < 0 || index >= words.Length) continue;
                var word = words[index];
                var updated = position % 2 == 0 ? (ushort)((word & 0xFF00) | source[i]) : (ushort)((word & 0x00FF) | (source[i] << 8));
                if (updated == word) continue;
                words[index] = updated;
                changed = true;
            }

            // One write for the whole span, and none when nothing changed.
            if (changed) table!.WriteRange(first, words);
        }

        /// <summary>The registers covering a byte span, clipped to the table, in one read.</summary>
        private static (int First, ushort[] Words) ReadWords(ModbusDataCollection<ushort>? table, int offset, int length)
        {
            if (table == null) return (0, Array.Empty<ushort>());
            var first = Math.Max(offset / 2, 1);
            var last = Math.Min((offset + length - 1) / 2, table.Count - 1);
            return last < first ? (first, Array.Empty<ushort>()) : (first, table.ReadRange(first, last - first + 1));
        }

        private static bool InRange(ModbusDataCollection<ushort>? table, int register)
            => table != null && register >= 1 && register < table.Count;

        private static ushort ReadRegister(ModbusDataCollection<ushort>? table, int register)
            => InRange(table, register) ? table![register] : (ushort)0;
    }

    /// <summary>A Modbus bit table (coils or discrete inputs) of the data store being scanned.</summary>
    public sealed class PlcBitTableSpace : PlcSpace
    {
        private readonly PlcMemoryArea _area;
        private readonly PlcMemory _memory;

        internal PlcBitTableSpace(PlcMemory memory, PlcMemoryArea area)
        {
            _memory = memory;
            _area = area;
        }

        public override bool IsBitTable => true;

        private ModbusDataCollection<bool>? Table => _memory.Store is not { } store ? null
            : _area == PlcMemoryArea.DiscreteInput ? store.InputDiscretes : store.CoilDiscretes;

        public override bool ReadBit(int index)
        {
            var table = Table;
            return table != null && index >= 1 && index < table.Count && table[index];
        }

        public override void WriteBit(int index, bool value)
        {
            var table = Table;
            if (table == null || index < 1 || index >= table.Count) return;
            if (table[index] != value) table[index] = value;
        }

        public override byte ReadByte(int offset) => ReadBit(offset) ? (byte)1 : (byte)0;
        public override void WriteByte(int offset, byte value) => WriteBit(offset, (value & 1) != 0);
    }

    /// <summary>System bits (%S): one bit per index, kept by the runtime.</summary>
    public sealed class PlcSystemBitSpace : PlcSpace
    {
        private readonly bool[] _bits = new bool[PlcMemory.SystemBitCount];

        public override bool IsBitTable => true;

        public override bool ReadBit(int index) => index >= 0 && index < _bits.Length && _bits[index];

        public override void WriteBit(int index, bool value)
        {
            if (index >= 0 && index < _bits.Length) _bits[index] = value;
        }

        public override byte ReadByte(int offset) => ReadBit(offset) ? (byte)1 : (byte)0;
        public override void WriteByte(int offset, byte value) => WriteBit(offset, (value & 1) != 0);
    }

    /// <summary>
    /// The controller memory the runtime reads and writes: the Modbus tables of the
    /// data store being scanned (the Quantum state RAM), system bits and words, and
    /// private storage for objects outside the state RAM (topological I/O).
    /// </summary>
    public sealed class PlcMemory
    {
        public const int SystemBitCount = 256;
        public const int SystemWordCount = 1024;

        private readonly Dictionary<string, PlcByteSpace> _privateObjects = new(StringComparer.OrdinalIgnoreCase);

        public PlcMemory()
        {
            HoldingRegisters = new PlcRegisterSpace(this, PlcMemoryArea.HoldingRegister);
            InputRegisters = new PlcRegisterSpace(this, PlcMemoryArea.InputRegister);
            Coils = new PlcBitTableSpace(this, PlcMemoryArea.Coil);
            DiscreteInputs = new PlcBitTableSpace(this, PlcMemoryArea.DiscreteInput);
            SystemBits = new PlcSystemBitSpace();
            SystemWords = new PlcByteSpace(SystemWordCount * 2);
        }

        /// <summary>The data store of the scan in progress (the Modbus server's, or the offline store).</summary>
        public DataStore? Store { get; set; }

        public PlcRegisterSpace HoldingRegisters { get; }
        public PlcRegisterSpace InputRegisters { get; }
        public PlcBitTableSpace Coils { get; }
        public PlcBitTableSpace DiscreteInputs { get; }
        public PlcSystemBitSpace SystemBits { get; }
        public PlcByteSpace SystemWords { get; }

        /// <summary>The location a direct address (or a variable located on it) denotes.</summary>
        public PlcLocation Locate(PlcAddress address, PlcType? declaredType = null)
        {
            var type = declaredType ?? address.DefaultType;
            switch (address.Area)
            {
                case PlcMemoryArea.Coil:
                    return new PlcLocation(Coils, address.Index, type);
                case PlcMemoryArea.DiscreteInput:
                    return new PlcLocation(DiscreteInputs, address.Index, type);
                case PlcMemoryArea.SystemBit:
                    return new PlcLocation(SystemBits, address.Index, type);
                case PlcMemoryArea.HoldingRegister:
                case PlcMemoryArea.InputRegister:
                case PlcMemoryArea.SystemWord:
                {
                    PlcSpace space = address.Area switch
                    {
                        PlcMemoryArea.HoldingRegister => HoldingRegisters,
                        PlcMemoryArea.InputRegister => InputRegisters,
                        _ => SystemWords
                    };
                    return address.Bit is { } bit
                        ? new PlcLocation(space, address.Index * 2, PlcType.Bool, bit)
                        : new PlcLocation(space, address.Index * 2, type);
                }
                default:
                    if (!_privateObjects.TryGetValue(address.Text, out var storage))
                        _privateObjects[address.Text] = storage = new PlcByteSpace(Math.Max(type.Size, 2));
                    return new PlcLocation(storage, 0, type);
            }
        }

        /// <summary>Reads the value at a location; an invalid location reads as "no value".</summary>
        public PlcValue Read(PlcLocation location)
        {
            if (!location.IsValid) return default;

            var space = location.Space;
            if (location.Bit >= 0)
            {
                var bit = location.Bit;
                return PlcOps.FromBool((space.ReadByte(location.Offset + bit / 8) & (1 << (bit % 8))) != 0);
            }

            var type = location.Type;
            if (space.IsBitTable)
            {
                if (type.Kind == PlcTypeKind.Array && type.ElementType!.IsBit)
                {
                    var bits = new byte[type.Size];
                    for (var i = 0; i < type.Length; i++) bits[i] = space.ReadBit(location.Offset + i) ? (byte)1 : (byte)0;
                    return PlcValue.FromBytes(type, bits);
                }
                var on = PlcOps.FromBool(space.ReadBit(location.Offset));
                return type.IsBit ? on : PlcOps.Convert(on, type);
            }

            Span<byte> buffer = type.Size <= 64 ? stackalloc byte[type.Size] : new byte[type.Size];
            space.ReadBytes(location.Offset, buffer);
            return PlcValue.Decode(type, buffer);
        }

        /// <summary>Writes a value, converted to the location's type.</summary>
        public void Write(PlcLocation location, PlcValue value)
        {
            if (!location.IsValid) return;

            var space = location.Space;
            if (location.Bit >= 0)
            {
                space.WriteBitOfByte(location.Offset + location.Bit / 8, location.Bit % 8, value.AsBool());
                return;
            }

            var type = location.Type;
            if (space.IsBitTable)
            {
                if (type.Kind == PlcTypeKind.Array && type.ElementType!.IsBit)
                {
                    var bits = PlcOps.Convert(value, type).AsBytes();
                    for (var i = 0; i < type.Length && i < bits.Length; i++) space.WriteBit(location.Offset + i, (bits[i] & 1) != 0);
                    return;
                }
                space.WriteBit(location.Offset, PlcOps.Convert(value, PlcType.Bool).AsBool());
                return;
            }

            Span<byte> buffer = type.Size <= 64 ? stackalloc byte[type.Size] : new byte[type.Size];
            PlcValue.Encode(value, type, buffer);
            space.WriteBytes(location.Offset, buffer);
        }
    }
}
