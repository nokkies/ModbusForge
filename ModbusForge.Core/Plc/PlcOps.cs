using System;
using System.Globalization;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// Conversions and the arithmetic, comparison and bit-wise operations the
    /// standard functions share. Integer results wrap to the operating type the way
    /// the controller's 16/32-bit arithmetic does; REAL arithmetic is single precision.
    /// </summary>
    public static class PlcOps
    {
        /// <summary>
        /// Converts a value to <paramref name="target"/> following the rules the Unity
        /// conversion functions document: bit patterns carry over between integer types
        /// (least significant bits kept), REAL to integer rounds half to even, and a
        /// missing value becomes the target's zero.
        /// </summary>
        public static PlcValue Convert(PlcValue value, PlcType target)
        {
            if (!value.HasValue) return PlcValue.DefaultOf(target);

            var source = value.Type!;
            if (ReferenceEquals(source, target)) return value;

            switch (target.Kind)
            {
                case PlcTypeKind.Bool:
                case PlcTypeKind.Ebool:
                    if (source.IsBit) return value.AsBool() ? True : False;
                    if (source.IsReal) return (value.AsInteger() & 1) != 0 ? True : False;
                    if (source.IsInteger) return (value.AsInteger() & 1) != 0 ? True : False;
                    return False;

                case PlcTypeKind.Real:
                    return PlcValue.FromReal(value.AsDouble());

                case PlcTypeKind.String:
                    return PlcValue.FromString(source.Kind == PlcTypeKind.String ? Truncate(value.AsString(), target.Length) : value.ToDisplayString(), target);

                case PlcTypeKind.Array:
                case PlcTypeKind.Struct:
                case PlcTypeKind.FunctionBlock:
                case PlcTypeKind.Unknown:
                {
                    var bytes = new byte[target.Size];
                    var sourceBytes = source.IsStructured || source.Kind == PlcTypeKind.Unknown ? value.AsBytes() : PlcValue.Encode(value, source);
                    sourceBytes.AsSpan(0, Math.Min(sourceBytes.Length, bytes.Length)).CopyTo(bytes);
                    return PlcValue.FromBytes(target, bytes);
                }

                default:
                    if (source.IsReal) return PlcValue.FromInteger(target, value.AsInteger());
                    if (source.IsBit || source.IsInteger) return PlcValue.FromInteger(target, value.AsInteger());
                    if (source.Kind == PlcTypeKind.String)
                        return PlcValue.FromInteger(target, long.TryParse(value.AsString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0);
                    return PlcValue.DefaultOf(target);
            }
        }

        public static readonly PlcValue True = PlcValue.FromBool(true);
        public static readonly PlcValue False = PlcValue.FromBool(false);

        private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

        /// <summary>
        /// The type a generic function computes in: the first input that carries a
        /// declared type (untyped literals adapt to it); REAL wins if the inputs mix.
        /// EBOOL computes as BOOL.
        /// </summary>
        public static PlcType OperatingType(ReadOnlySpan<PlcValue> values)
        {
            PlcType? chosen = null;
            var sawLiteral = false;
            foreach (var value in values) Consider(value, ref chosen, ref sawLiteral);
            return OperatingType(chosen, sawLiteral);
        }

        /// <summary>The operating type of two operands (an ST binary operator), without an array.</summary>
        public static PlcType OperatingType(PlcValue a, PlcValue b)
        {
            PlcType? chosen = null;
            var sawLiteral = false;
            Consider(a, ref chosen, ref sawLiteral);
            Consider(b, ref chosen, ref sawLiteral);
            return OperatingType(chosen, sawLiteral);
        }

        /// <summary>The operating type of one operand (an ST unary operator).</summary>
        public static PlcType OperatingType(PlcValue value) => OperatingType(value, default);

        private static void Consider(PlcValue value, ref PlcType? chosen, ref bool sawLiteral)
        {
            if (!value.HasValue) return;
            var type = value.Type!;
            if (type.Kind == PlcTypeKind.AnyInteger)
            {
                sawLiteral = true;
                return;
            }
            if (chosen == null || (type.IsReal && !chosen.IsReal)) chosen = type;
        }

        private static PlcType OperatingType(PlcType? chosen, bool sawLiteral)
        {
            if (chosen == null) return sawLiteral ? PlcType.AnyInteger : PlcType.Bool;
            return chosen.Kind == PlcTypeKind.Ebool ? PlcType.Bool : chosen;
        }

        public static PlcValue Add(PlcType type, PlcValue a, PlcValue b) => type.IsReal
            ? PlcValue.FromReal((float)a.AsDouble() + (float)b.AsDouble())
            : PlcValue.FromInteger(type, Convert(a, type).AsInteger() + Convert(b, type).AsInteger());

        public static PlcValue Subtract(PlcType type, PlcValue a, PlcValue b) => type.IsReal
            ? PlcValue.FromReal((float)a.AsDouble() - (float)b.AsDouble())
            : PlcValue.FromInteger(type, Convert(a, type).AsInteger() - Convert(b, type).AsInteger());

        public static PlcValue Multiply(PlcType type, PlcValue a, PlcValue b) => type.IsReal
            ? PlcValue.FromReal((float)a.AsDouble() * (float)b.AsDouble())
            : PlcValue.FromInteger(type, unchecked(Convert(a, type).AsInteger() * Convert(b, type).AsInteger()));

        /// <summary>Integer division truncates toward zero (7/3 = 2, -7/3 = -2). Null on division by zero.</summary>
        public static PlcValue? Divide(PlcType type, PlcValue a, PlcValue b)
        {
            if (type.IsReal)
            {
                var divisor = (float)b.AsDouble();
                return divisor == 0 ? null : PlcValue.FromReal((float)a.AsDouble() / divisor);
            }

            var d = Convert(b, type).AsInteger();
            return d == 0 ? null : PlcValue.FromInteger(type, Convert(a, type).AsInteger() / d);
        }

        public static PlcValue? Modulo(PlcType type, PlcValue a, PlcValue b)
        {
            var d = Convert(b, type).AsInteger();
            return d == 0 ? null : PlcValue.FromInteger(type, Convert(a, type).AsInteger() % d);
        }

        /// <summary>Compares two values in the operating type: negative, zero or positive.</summary>
        public static int Compare(PlcType type, PlcValue a, PlcValue b)
        {
            if (type.IsReal) return a.AsDouble().CompareTo(b.AsDouble());
            if (type.Kind == PlcTypeKind.String) return string.CompareOrdinal(a.AsString(), b.AsString());
            return Convert(a, type).AsInteger().CompareTo(Convert(b, type).AsInteger());
        }

        public static PlcValue And(PlcType type, PlcValue a, PlcValue b) => type.IsBit
            ? FromBool(a.AsBool() && b.AsBool())
            : PlcValue.FromInteger(type, Convert(a, type).AsInteger() & Convert(b, type).AsInteger());

        public static PlcValue Or(PlcType type, PlcValue a, PlcValue b) => type.IsBit
            ? FromBool(a.AsBool() || b.AsBool())
            : PlcValue.FromInteger(type, Convert(a, type).AsInteger() | Convert(b, type).AsInteger());

        public static PlcValue Xor(PlcType type, PlcValue a, PlcValue b) => type.IsBit
            ? FromBool(a.AsBool() ^ b.AsBool())
            : PlcValue.FromInteger(type, Convert(a, type).AsInteger() ^ Convert(b, type).AsInteger());

        public static PlcValue Not(PlcType type, PlcValue a) => type.IsBit
            ? FromBool(!a.AsBool())
            : PlcValue.FromInteger(type, ~Convert(a, type).AsInteger());

        public static PlcValue FromBool(bool value) => value ? True : False;

        /// <summary>The negation an inverted pin applies: NOT for bits, bit-wise complement otherwise.</summary>
        public static PlcValue Invert(PlcValue value)
        {
            if (!value.HasValue) return True;
            var type = value.Type!;
            if (type.IsBit) return FromBool(!value.AsBool());
            if (type.IsInteger) return PlcValue.FromInteger(type, ~value.AsInteger());
            return value;
        }
    }
}
