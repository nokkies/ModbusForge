using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// An actual parameter on a pin: a literal, a variable reference (with field,
    /// array and bit-extraction steps: <c>Pump.Status</c>, <c>Tab[3]</c>,
    /// <c>Flags.4</c>), or a direct address (<c>%MW100</c>, <c>%M5</c>).
    /// </summary>
    public abstract class PlcOperand
    {
        protected PlcOperand(string text) => Text = text;

        /// <summary>The actual parameter as written in the XEF.</summary>
        public string Text { get; }

        /// <summary>The type the operand reads as, when known without running.</summary>
        public virtual PlcType? StaticType => null;

        public virtual bool IsWritable => false;

        public abstract PlcValue Read(PlcRuntime runtime);

        public virtual void Write(PlcRuntime runtime, PlcValue value)
        {
        }

        public override string ToString() => Text;
    }

    /// <summary>A literal actual parameter.</summary>
    public sealed class PlcConstantOperand : PlcOperand
    {
        public PlcConstantOperand(string text, PlcValue value) : base(text) => Value = value;

        public PlcValue Value { get; }

        public override PlcType? StaticType => Value.Type;

        public override PlcValue Read(PlcRuntime runtime) => Value;
    }

    /// <summary>An actual parameter the runtime cannot evaluate (an expression, an undeclared name).</summary>
    public sealed class PlcUnresolvedOperand : PlcOperand
    {
        public PlcUnresolvedOperand(string text, string reason) : base(text) => Reason = reason;

        public string Reason { get; }

        public override PlcValue Read(PlcRuntime runtime) => default;
    }

    /// <summary>A variable or direct-address reference, resolved against controller memory on every access.</summary>
    public sealed class PlcReferenceOperand : PlcOperand
    {
        private readonly PlcVariable? _variable;
        private readonly PlcAddress? _address;
        private readonly Step[] _steps;
        private readonly bool _hasIndex;

        // The location last resolved and the runtime it belongs to: variable memory
        // never moves while a runtime lives, so only array indexes need re-evaluating.
        private PlcRuntime? _resolvedFor;
        private PlcLocation _resolved;

        internal PlcReferenceOperand(string text, PlcVariable? variable, PlcAddress? address, IReadOnlyList<Step> steps, PlcType staticType)
            : base(text)
        {
            _variable = variable;
            _address = address;
            _steps = steps.ToArray();
            _hasIndex = _steps.Any(s => s is IndexStep);
            StaticType = staticType;
        }

        public override PlcType? StaticType { get; }

        /// <summary>The root variable, when the reference starts with a symbol.</summary>
        public PlcVariable? Variable => _variable;

        /// <summary>The direct address, when the reference is one.</summary>
        public PlcAddress? Address => _address ?? _variable?.Address;

        public override bool IsWritable => true;

        public override PlcValue Read(PlcRuntime runtime)
        {
            var location = Resolve(runtime);
            return location.IsValid ? runtime.Memory.Read(location) : default;
        }

        public override void Write(PlcRuntime runtime, PlcValue value)
        {
            var location = Resolve(runtime);
            if (location.IsValid) runtime.Memory.Write(location, value);
        }

        /// <summary>Where the reference points now (array indexes are evaluated on each access).</summary>
        public PlcLocation Resolve(PlcRuntime runtime)
        {
            if (!_hasIndex && ReferenceEquals(_resolvedFor, runtime)) return _resolved;

            var location = _variable != null ? runtime.Locate(_variable) : runtime.Memory.Locate(_address!);
            for (var i = 0; i < _steps.Length && location.IsValid; i++)
            {
                location = _steps[i].Apply(location, runtime);
            }

            if (!_hasIndex)
            {
                _resolved = location;
                _resolvedFor = runtime;
            }
            return location;
        }

        internal abstract class Step
        {
            public abstract PlcLocation Apply(PlcLocation location, PlcRuntime runtime);
        }

        internal sealed class FieldStep : Step
        {
            private readonly string _name;
            public FieldStep(string name) => _name = name;

            public override PlcLocation Apply(PlcLocation location, PlcRuntime runtime)
            {
                if (location.Bit >= 0 || location.Space.IsBitTable || location.Type.FindField(_name) is not { } field) return default;
                return new PlcLocation(location.Space, location.Offset + field.Offset, field.Type);
            }
        }

        internal sealed class IndexStep : Step
        {
            private readonly PlcOperand _index;
            public IndexStep(PlcOperand index) => _index = index;

            public override PlcLocation Apply(PlcLocation location, PlcRuntime runtime)
            {
                var type = location.Type;
                if (type.Kind != PlcTypeKind.Array || location.Bit >= 0) return default;
                var index = _index.Read(runtime).AsInteger() - type.LowerBound;
                if (index < 0 || index >= type.Length)
                {
                    runtime.CountIndexError();
                    return default;
                }
                var element = type.ElementType!;
                var offset = location.Space.IsBitTable ? location.Offset + (int)index : location.Offset + (int)index * element.Size;
                return new PlcLocation(location.Space, offset, element);
            }
        }

        /// <summary>Bit extraction: <c>Word.3</c> is bit 3 of the value.</summary>
        internal sealed class BitStep : Step
        {
            private readonly int _bit;
            public BitStep(int bit) => _bit = bit;

            public override PlcLocation Apply(PlcLocation location, PlcRuntime runtime)
            {
                var type = location.Type;
                if (location.Bit >= 0 || location.Space.IsBitTable || !type.IsInteger || _bit >= type.BitWidth) return default;
                return new PlcLocation(location.Space, location.Offset, PlcType.Bool, _bit);
            }
        }
    }

    /// <summary>Turns actual-parameter text into operands against a symbol table.</summary>
    public static class PlcOperandParser
    {
        public static PlcOperand Parse(string text, IReadOnlyDictionary<string, PlcVariable> symbols)
        {
            var trimmed = text.Trim();
            if (PlcLiteral.TryParse(trimmed, out var literal)) return new PlcConstantOperand(text, literal);

            if (trimmed.StartsWith('%'))
            {
                return PlcAddress.TryParse(trimmed) is { } address
                    ? new PlcReferenceOperand(text, null, address, Array.Empty<PlcReferenceOperand.Step>(), address.DefaultType)
                    : new PlcUnresolvedOperand(text, "unsupported direct address");
            }

            var reader = new Reader(trimmed);
            var root = reader.Identifier();
            if (root == null) return new PlcUnresolvedOperand(text, "not a variable reference");
            if (!symbols.TryGetValue(root, out var variable)) return new PlcUnresolvedOperand(text, $"'{root}' is not declared");

            // The type each step lands on, so a reference that cannot resolve is caught here.
            var type = variable.Address?.Bit.HasValue == true ? PlcType.Bool : variable.Type;
            var steps = new List<PlcReferenceOperand.Step>();
            while (!reader.AtEnd)
            {
                if (reader.TryConsume('.'))
                {
                    if (reader.Number() is { } bit)
                    {
                        if (!type.IsInteger || bit >= type.BitWidth) return new PlcUnresolvedOperand(text, $"no bit {bit} in {type}");
                        steps.Add(new PlcReferenceOperand.BitStep(bit));
                        type = PlcType.Bool;
                        continue;
                    }
                    if (reader.Identifier() is not { } field) return new PlcUnresolvedOperand(text, "bad field name");
                    if (type.FindField(field) is not { } member)
                    {
                        return new PlcUnresolvedOperand(text, type.Kind == PlcTypeKind.Unknown
                            ? $"type {type} is not defined in the XEF"
                            : $"{type} has no field {field}");
                    }
                    steps.Add(new PlcReferenceOperand.FieldStep(field));
                    type = member.Type;
                    continue;
                }

                if (reader.TryConsume('['))
                {
                    var indexes = reader.Until(']');
                    if (indexes == null) return new PlcUnresolvedOperand(text, "unterminated index");
                    foreach (var index in indexes.Split(','))
                    {
                        if (type.Kind != PlcTypeKind.Array) return new PlcUnresolvedOperand(text, $"{type} is not an array");
                        var operand = Parse(index, symbols);
                        if (operand is PlcUnresolvedOperand) return new PlcUnresolvedOperand(text, "unsupported index");
                        steps.Add(new PlcReferenceOperand.IndexStep(operand));
                        type = type.ElementType!;
                    }
                    continue;
                }

                return new PlcUnresolvedOperand(text, "expression");
            }

            return new PlcReferenceOperand(text, variable, null, steps, type);
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _position;

            public Reader(string text) => _text = text;

            public bool AtEnd
            {
                get
                {
                    SkipSpaces();
                    return _position >= _text.Length;
                }
            }

            public bool TryConsume(char c)
            {
                SkipSpaces();
                if (_position < _text.Length && _text[_position] == c)
                {
                    _position++;
                    return true;
                }
                return false;
            }

            public string? Identifier()
            {
                SkipSpaces();
                var start = _position;
                if (_position >= _text.Length || !(char.IsLetter(_text[_position]) || _text[_position] == '_')) return null;
                while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_')) _position++;
                return _text[start.._position];
            }

            public int? Number()
            {
                SkipSpaces();
                var start = _position;
                while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
                if (_position == start) return null;
                // "a.1b" is not a bit number; let the identifier rule reject it.
                if (_position < _text.Length && (char.IsLetter(_text[_position]) || _text[_position] == '_'))
                {
                    _position = start;
                    return null;
                }
                return int.Parse(_text[start.._position], CultureInfo.InvariantCulture);
            }

            public string? Until(char end)
            {
                var depth = 0;
                var sb = new StringBuilder();
                while (_position < _text.Length)
                {
                    var c = _text[_position++];
                    if (c == '[') depth++;
                    if (c == end && depth-- == 0) return sb.ToString();
                    sb.Append(c);
                }
                return null;
            }

            private void SkipSpaces()
            {
                while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
            }
        }
    }
}
