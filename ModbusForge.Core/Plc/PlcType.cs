using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ModbusForge.Core.Plc
{
    /// <summary>The Unity Pro / Control Expert data type families the PLC runtime models.</summary>
    public enum PlcTypeKind
    {
        Bool,
        Ebool,
        Byte,
        Word,
        Dword,
        Int,
        Uint,
        Dint,
        Udint,
        Real,
        Time,
        Date,
        TimeOfDay,
        DateAndTime,
        String,

        /// <summary>An untyped integer literal (8, 16#FF): takes the type of the values it meets.</summary>
        AnyInteger,

        Array,
        Struct,
        FunctionBlock,

        /// <summary>A type the XEF names but does not define (library structures such as Para_SCALING).</summary>
        Unknown
    }

    /// <summary>What a function block field is to its callers.</summary>
    public enum PlcParameterDirection
    {
        /// <summary>A structure (DDT) field.</summary>
        None,
        Input,
        InOut,
        Output,

        /// <summary>A public variable: readable from outside as Instance.Name.</summary>
        Public,

        /// <summary>A private variable of the block's own code.</summary>
        Private
    }

    /// <summary>A field of a structure or function block type at its byte offset.</summary>
    public sealed record PlcField(string Name, PlcType Type, int Offset)
    {
        public PlcParameterDirection Direction { get; init; }

        /// <summary>The field's declared initial values, relative to the field ("" for the field itself).</summary>
        public IReadOnlyList<PlcInitialValue> Defaults { get; init; } = System.Array.Empty<PlcInitialValue>();
    }

    /// <summary>
    /// A PLC data type with its size and layout in controller memory. Structures
    /// follow the Modicon Quantum mapping rules (Control Expert Program Languages
    /// and Structure, 35006144, "Derived Data Types (DDTs): Mapping Rules"):
    /// elements are stored in declaration order, BOOL and BYTE sit on any byte,
    /// every other elementary type sits on an even byte, and a structure or array
    /// is byte-aligned only when it holds nothing but BOOL/BYTE elements.
    /// </summary>
    public sealed class PlcType
    {
        private static readonly Regex StringRegex = new(@"^STRING(?:\s*\[\s*(\d+)\s*\])?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ArrayRegex = new(@"^ARRAY\s*\[(?<dims>[^\]]+)\]\s*OF\s+(?<elem>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Unity's default STRING length when none is declared.</summary>
        public const int DefaultStringLength = 16;

        public static readonly PlcType Bool = new("BOOL", PlcTypeKind.Bool, 1, 1);
        public static readonly PlcType Ebool = new("EBOOL", PlcTypeKind.Ebool, 1, 1);
        public static readonly PlcType Byte = new("BYTE", PlcTypeKind.Byte, 1, 1);
        public static readonly PlcType Word = new("WORD", PlcTypeKind.Word, 2, 2);
        public static readonly PlcType Int = new("INT", PlcTypeKind.Int, 2, 2);
        public static readonly PlcType Uint = new("UINT", PlcTypeKind.Uint, 2, 2);
        public static readonly PlcType Dword = new("DWORD", PlcTypeKind.Dword, 4, 2);
        public static readonly PlcType Dint = new("DINT", PlcTypeKind.Dint, 4, 2);
        public static readonly PlcType Udint = new("UDINT", PlcTypeKind.Udint, 4, 2);
        public static readonly PlcType Real = new("REAL", PlcTypeKind.Real, 4, 2);
        public static readonly PlcType Time = new("TIME", PlcTypeKind.Time, 4, 2);
        public static readonly PlcType Date = new("DATE", PlcTypeKind.Date, 4, 2);
        public static readonly PlcType TimeOfDay = new("TOD", PlcTypeKind.TimeOfDay, 4, 2);
        public static readonly PlcType DateAndTime = new("DT", PlcTypeKind.DateAndTime, 8, 2);
        public static readonly PlcType AnyInteger = new("ANY_INT literal", PlcTypeKind.AnyInteger, 4, 2);
        public static readonly PlcType String16 = String(DefaultStringLength);

        private readonly List<PlcField> _fields = new();
        private readonly Dictionary<string, PlcField> _fieldsByName = new(StringComparer.OrdinalIgnoreCase);
        private bool _sealed;

        private PlcType(string name, PlcTypeKind kind, int size, int alignment)
        {
            Name = name;
            Kind = kind;
            Size = size;
            Alignment = alignment;
        }

        public string Name { get; }
        public PlcTypeKind Kind { get; }

        /// <summary>Bytes the type occupies in controller memory.</summary>
        public int Size { get; private set; }

        /// <summary>1 for byte-aligned types (BOOL, BYTE and structures of them), otherwise 2.</summary>
        public int Alignment { get; private set; }

        public IReadOnlyList<PlcField> Fields => _fields;

        /// <summary>Element type of an array.</summary>
        public PlcType? ElementType { get; private set; }

        /// <summary>First index of an array.</summary>
        public int LowerBound { get; private set; }

        /// <summary>Element count of an array; character count of a STRING.</summary>
        public int Length { get; private set; }

        public bool IsBit => Kind is PlcTypeKind.Bool or PlcTypeKind.Ebool;

        public bool IsReal => Kind == PlcTypeKind.Real;

        /// <summary>Types carried as an integer bit pattern (bit strings, integers, TIME, DATE, TOD).</summary>
        public bool IsInteger => Kind is PlcTypeKind.Byte or PlcTypeKind.Word or PlcTypeKind.Dword
            or PlcTypeKind.Int or PlcTypeKind.Uint or PlcTypeKind.Dint or PlcTypeKind.Udint
            or PlcTypeKind.Time or PlcTypeKind.Date or PlcTypeKind.TimeOfDay or PlcTypeKind.AnyInteger;

        public bool IsSigned => Kind is PlcTypeKind.Int or PlcTypeKind.Dint or PlcTypeKind.AnyInteger;

        /// <summary>BYTE, WORD, DWORD: the types bit-wise logic works on besides BOOL.</summary>
        public bool IsBitString => Kind is PlcTypeKind.Byte or PlcTypeKind.Word or PlcTypeKind.Dword;

        public bool IsElementary => Kind <= PlcTypeKind.AnyInteger;

        public bool IsStructured => Kind is PlcTypeKind.Array or PlcTypeKind.Struct or PlcTypeKind.FunctionBlock;

        /// <summary>Value width in bits of an integer-carried type.</summary>
        public int BitWidth => Kind switch
        {
            PlcTypeKind.Bool or PlcTypeKind.Ebool => 1,
            PlcTypeKind.Byte => 8,
            PlcTypeKind.Word or PlcTypeKind.Int or PlcTypeKind.Uint => 16,
            PlcTypeKind.AnyInteger => 64,
            _ => 32
        };

        public PlcField? FindField(string name)
            => _fieldsByName.TryGetValue(name, out var field) ? field : null;

        public override string ToString() => Name;

        /// <summary>A STRING[n]: n characters plus the terminating NUL.</summary>
        public static PlcType String(int length)
        {
            var n = Math.Max(1, length);
            return new PlcType(n == DefaultStringLength ? "STRING" : $"STRING[{n}]", PlcTypeKind.String, n + 1, 1) { Length = n };
        }

        /// <summary>A one-dimensional array; multi-dimensional arrays nest (first index outermost).</summary>
        public static PlcType Array(PlcType element, int lowerBound, int upperBound, string? name = null)
        {
            var length = Math.Max(0, upperBound - lowerBound + 1);
            return new PlcType(name ?? $"ARRAY[{lowerBound}..{upperBound}] OF {element.Name}", PlcTypeKind.Array,
                length * element.Size, element.Alignment)
            {
                ElementType = element,
                LowerBound = lowerBound,
                Length = length,
                _sealed = true
            };
        }

        /// <summary>An empty structure (DDT) or function block type; add fields, then <see cref="Seal"/>.</summary>
        public static PlcType NewStructure(string name, PlcTypeKind kind = PlcTypeKind.Struct)
        {
            if (kind is not (PlcTypeKind.Struct or PlcTypeKind.FunctionBlock))
                throw new ArgumentOutOfRangeException(nameof(kind));
            return new PlcType(name, kind, 0, 1);
        }

        /// <summary>A named type the XEF references but does not define.</summary>
        public static PlcType NewUnknown(string name) => new(name, PlcTypeKind.Unknown, 0, 1) { _sealed = true };

        /// <summary>Appends a field at the next offset its alignment allows.</summary>
        public PlcField AddField(string name, PlcType type,
            PlcParameterDirection direction = PlcParameterDirection.None,
            IReadOnlyList<PlcInitialValue>? defaults = null)
        {
            if (_sealed) throw new InvalidOperationException($"Type {Name} is complete.");
            if (Kind is not (PlcTypeKind.Struct or PlcTypeKind.FunctionBlock))
                throw new InvalidOperationException($"Type {Name} has no fields.");

            var offset = Align(Size, type.Alignment);
            var field = new PlcField(name, type, offset)
            {
                Direction = direction,
                Defaults = defaults ?? System.Array.Empty<PlcInitialValue>()
            };
            _fields.Add(field);
            _fieldsByName.TryAdd(name, field);
            Size = offset + type.Size;
            Alignment = Math.Max(Alignment, type.Alignment);
            return field;
        }

        /// <summary>Completes a structure: its size is rounded up to its alignment.</summary>
        public PlcType Seal()
        {
            if (!_sealed)
            {
                Size = Align(Size, Alignment);
                HasDefaults = _fields.Any(f => f.Defaults.Count > 0 || f.Type.HasDefaults);
                _sealed = true;
            }
            return this;
        }

        /// <summary>True when the type, or a structure inside it, declares initial values.</summary>
        public bool HasDefaults { get; private set; }

        /// <summary>The user function block code behind a DFB type; null for other types and encrypted DFBs.</summary>
        public PlcDfbDefinition? Dfb { get; internal set; }

        private static int Align(int offset, int alignment) => alignment <= 1 ? offset : (offset + alignment - 1) / alignment * alignment;

        /// <summary>
        /// The elementary type, STRING or ARRAY a Unity type name denotes, or null when
        /// the name is a structure/function block (resolved by <see cref="PlcTypeRegistry"/>).
        /// </summary>
        public static PlcType? TryParseBuiltIn(string typeName, Func<string, PlcType?>? resolveNamed = null)
        {
            var name = typeName.Trim();
            var upper = name.ToUpperInvariant();
            switch (upper)
            {
                case "BOOL": return Bool;
                case "EBOOL": return Ebool;
                case "BYTE": return Byte;
                case "WORD": return Word;
                case "DWORD": return Dword;
                case "INT": return Int;
                case "UINT": return Uint;
                case "DINT": return Dint;
                case "UDINT": return Udint;
                case "REAL": return Real;
                case "TIME": return Time;
                case "DATE": return Date;
                case "TOD":
                case "TIME_OF_DAY": return TimeOfDay;
                case "DT":
                case "DATE_AND_TIME": return DateAndTime;
            }

            var stringMatch = StringRegex.Match(name);
            if (stringMatch.Success)
            {
                return stringMatch.Groups[1].Success
                    ? String(int.Parse(stringMatch.Groups[1].Value, CultureInfo.InvariantCulture))
                    : String16;
            }

            var arrayMatch = ArrayRegex.Match(name);
            if (arrayMatch.Success)
            {
                var elementName = arrayMatch.Groups["elem"].Value.Trim();
                var element = TryParseBuiltIn(elementName, resolveNamed) ?? resolveNamed?.Invoke(elementName);
                if (element == null) return null;

                // ARRAY[a..b, c..d] OF T nests as ARRAY[a..b] OF ARRAY[c..d] OF T.
                var dims = arrayMatch.Groups["dims"].Value.Split(',');
                for (var i = dims.Length - 1; i >= 0; i--)
                {
                    var bounds = dims[i].Split("..", StringSplitOptions.TrimEntries);
                    if (bounds.Length != 2
                        || !int.TryParse(bounds[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lower)
                        || !int.TryParse(bounds[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var upper2))
                    {
                        return null;
                    }
                    element = Array(element, lower, upper2, i == 0 ? name : null);
                }
                return element;
            }

            return null;
        }
    }
}
