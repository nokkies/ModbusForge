using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// A PLC value with its type. Bit strings, integers and TIME/DATE/TOD are carried
    /// as a normalized integer (wrapped to the type's width like the controller does),
    /// REAL as a single-precision number, structured values as their memory bytes.
    /// </summary>
    public readonly struct PlcValue
    {
        private readonly long _integer;
        private readonly double _real;
        private readonly object? _object;

        private PlcValue(PlcType type, long integer, double real, object? obj)
        {
            Type = type;
            _integer = integer;
            _real = real;
            _object = obj;
        }

        /// <summary>The value's type; null for "no value" (an unconnected, unassigned pin).</summary>
        public PlcType? Type { get; }

        public bool HasValue => Type != null;

        public static PlcValue FromBool(bool value) => new(PlcType.Bool, value ? 1 : 0, 0, null);

        public static PlcValue FromInteger(PlcType type, long value)
        {
            if (type.IsReal) return FromReal(value);
            if (!type.IsElementary || type.Kind == PlcTypeKind.String)
                throw new ArgumentException($"{type} is not an integer type.", nameof(type));
            return new PlcValue(type, Normalize(type, value), 0, null);
        }

        public static PlcValue FromReal(double value) => new(PlcType.Real, 0, (float)value, null);

        public static PlcValue FromTime(long milliseconds) => FromInteger(PlcType.Time, milliseconds);

        public static PlcValue FromString(string text, PlcType? type = null)
            => new(type is { Kind: PlcTypeKind.String } ? type : PlcType.String16, 0, 0, text);

        public static PlcValue FromBytes(PlcType type, byte[] bytes) => new(type, 0, 0, bytes);

        /// <summary>The zero value of a type (FALSE, 0, 0.0, T#0s, '', all-zero structure).</summary>
        public static PlcValue DefaultOf(PlcType type) => type.Kind switch
        {
            PlcTypeKind.Real => FromReal(0),
            PlcTypeKind.String => FromString("", type),
            PlcTypeKind.Array or PlcTypeKind.Struct or PlcTypeKind.FunctionBlock or PlcTypeKind.Unknown
                => FromBytes(type, new byte[type.Size]),
            PlcTypeKind.Bool or PlcTypeKind.Ebool => new PlcValue(type, 0, 0, null),
            _ => FromInteger(type, 0)
        };

        public bool AsBool() => Type?.Kind switch
        {
            null => false,
            PlcTypeKind.Real => _real != 0,
            PlcTypeKind.String or PlcTypeKind.Array or PlcTypeKind.Struct or PlcTypeKind.FunctionBlock or PlcTypeKind.Unknown => false,
            _ => _integer != 0
        };

        /// <summary>The integer payload; a REAL is rounded the IEC 559 way (half to even).</summary>
        public long AsInteger() => Type?.Kind switch
        {
            null => 0,
            PlcTypeKind.Real => RoundToInteger(_real),
            PlcTypeKind.String or PlcTypeKind.Array or PlcTypeKind.Struct or PlcTypeKind.FunctionBlock or PlcTypeKind.Unknown => 0,
            _ => _integer
        };

        public double AsDouble() => Type?.Kind switch
        {
            null => 0,
            PlcTypeKind.Real => _real,
            PlcTypeKind.String or PlcTypeKind.Array or PlcTypeKind.Struct or PlcTypeKind.FunctionBlock or PlcTypeKind.Unknown => 0,
            _ => _integer
        };

        public string AsString() => _object as string ?? (Type is { IsElementary: true } ? ToDisplayString() : "");

        /// <summary>The memory bytes of a structured value (a copy is not taken).</summary>
        public byte[] AsBytes() => _object as byte[] ?? (Type != null ? Encode(this, Type) : Array.Empty<byte>());

        /// <summary>IEC 559 rounding (half to even), clamped to the 64-bit range.</summary>
        public static long RoundToInteger(double value)
        {
            if (double.IsNaN(value)) return 0;
            var rounded = Math.Round(value, MidpointRounding.ToEven);
            if (rounded >= long.MaxValue) return long.MaxValue;
            if (rounded <= long.MinValue) return long.MinValue;
            return (long)rounded;
        }

        /// <summary>Wraps an integer to a type's width and signedness.</summary>
        public static long Normalize(PlcType type, long value) => type.Kind switch
        {
            PlcTypeKind.Bool or PlcTypeKind.Ebool => value != 0 ? 1 : 0,
            PlcTypeKind.Byte => value & 0xFF,
            PlcTypeKind.Word or PlcTypeKind.Uint => value & 0xFFFF,
            PlcTypeKind.Int => (short)value,
            PlcTypeKind.Dword or PlcTypeKind.Udint or PlcTypeKind.Time or PlcTypeKind.Date or PlcTypeKind.TimeOfDay => value & 0xFFFFFFFFL,
            PlcTypeKind.Dint => (int)value,
            _ => value
        };

        /// <summary>The value as Control Expert writes literals: TRUE, 12, 1.5, T#2S500MS, 'text'.</summary>
        public string ToDisplayString()
        {
            if (Type == null) return "";
            switch (Type.Kind)
            {
                case PlcTypeKind.Bool:
                case PlcTypeKind.Ebool:
                    return _integer != 0 ? "TRUE" : "FALSE";
                case PlcTypeKind.Real:
                    return ((float)_real).ToString("G7", CultureInfo.InvariantCulture);
                case PlcTypeKind.Time:
                    return FormatTime(_integer);
                case PlcTypeKind.String:
                    return "'" + (_object as string ?? "") + "'";
                case PlcTypeKind.Array:
                case PlcTypeKind.Struct:
                case PlcTypeKind.FunctionBlock:
                case PlcTypeKind.Unknown:
                    return "(" + Type.Name + ")";
                default:
                    return _integer.ToString(CultureInfo.InvariantCulture);
            }
        }

        public override string ToString() => ToDisplayString();

        /// <summary>T#1H2M3S4MS style, largest unit first, zero parts left out.</summary>
        public static string FormatTime(long milliseconds)
        {
            if (milliseconds == 0) return "T#0MS";
            var sb = new StringBuilder("T#");
            var rest = milliseconds;
            void Part(long unit, string suffix)
            {
                if (rest < unit) return;
                sb.Append((rest / unit).ToString(CultureInfo.InvariantCulture)).Append(suffix);
                rest %= unit;
            }
            Part(86_400_000, "D");
            Part(3_600_000, "H");
            Part(60_000, "M");
            Part(1_000, "S");
            Part(1, "MS");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Memory encoding: little-endian, as Control Expert maps data on %MW
        // ("the word first byte corresponds to the least significant 8 bits"),
        // so a 32-bit value's least significant word sits at the lower address.
        // ------------------------------------------------------------------

        /// <summary>Encodes a value as <paramref name="type"/> stores it in memory.</summary>
        public static byte[] Encode(PlcValue value, PlcType type)
        {
            var bytes = new byte[type.Size];
            Encode(value, type, bytes);
            return bytes;
        }

        public static void Encode(PlcValue value, PlcType type, Span<byte> destination)
        {
            switch (type.Kind)
            {
                case PlcTypeKind.Bool:
                case PlcTypeKind.Ebool:
                    destination[0] = (byte)(PlcOps.Convert(value, type).AsBool() ? 1 : 0);
                    return;
                case PlcTypeKind.Byte:
                    destination[0] = (byte)PlcOps.Convert(value, type).AsInteger();
                    return;
                case PlcTypeKind.Word:
                case PlcTypeKind.Int:
                case PlcTypeKind.Uint:
                    BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)PlcOps.Convert(value, type).AsInteger());
                    return;
                case PlcTypeKind.Real:
                    BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits((float)PlcOps.Convert(value, type).AsDouble()));
                    return;
                case PlcTypeKind.DateAndTime:
                    BinaryPrimitives.WriteInt64LittleEndian(destination, PlcOps.Convert(value, type).AsInteger());
                    return;
                case PlcTypeKind.String:
                {
                    destination.Clear();
                    var text = value.AsString();
                    var count = Math.Min(text.Length, type.Size - 1);
                    for (var i = 0; i < count; i++)
                        destination[i] = (byte)(text[i] <= 0xFF ? text[i] : '?');
                    return;
                }
                case PlcTypeKind.Array:
                case PlcTypeKind.Struct:
                case PlcTypeKind.FunctionBlock:
                case PlcTypeKind.Unknown:
                {
                    destination.Clear();
                    var source = value.AsBytes();
                    source.AsSpan(0, Math.Min(source.Length, destination.Length)).CopyTo(destination);
                    return;
                }
                default:
                    BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)PlcOps.Convert(value, type).AsInteger());
                    return;
            }
        }

        /// <summary>Decodes <paramref name="type"/> from its memory bytes.</summary>
        public static PlcValue Decode(PlcType type, ReadOnlySpan<byte> source) => type.Kind switch
        {
            PlcTypeKind.Bool or PlcTypeKind.Ebool => new PlcValue(type, source[0] & 1, 0, null),
            PlcTypeKind.Byte => FromInteger(type, source[0]),
            PlcTypeKind.Word or PlcTypeKind.Uint => FromInteger(type, BinaryPrimitives.ReadUInt16LittleEndian(source)),
            PlcTypeKind.Int => FromInteger(type, BinaryPrimitives.ReadInt16LittleEndian(source)),
            PlcTypeKind.Dint => FromInteger(type, BinaryPrimitives.ReadInt32LittleEndian(source)),
            PlcTypeKind.Real => FromReal(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source))),
            PlcTypeKind.DateAndTime => FromInteger(type, BinaryPrimitives.ReadInt64LittleEndian(source)),
            PlcTypeKind.String => FromString(DecodeText(source), type),
            PlcTypeKind.Array or PlcTypeKind.Struct or PlcTypeKind.FunctionBlock or PlcTypeKind.Unknown
                => FromBytes(type, source.ToArray()),
            _ => FromInteger(type, BinaryPrimitives.ReadUInt32LittleEndian(source))
        };

        private static string DecodeText(ReadOnlySpan<byte> source)
        {
            var end = source.IndexOf((byte)0);
            if (end < 0) end = source.Length;
            var chars = new char[end];
            for (var i = 0; i < end; i++) chars[i] = (char)source[i];
            return new string(chars);
        }
    }
}
