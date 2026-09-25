using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// An actual parameter on a pin, or an expression in Structured Text: a literal,
    /// a variable reference (with field, array and bit-extraction steps:
    /// <c>Pump.Status</c>, <c>Tab[3]</c>, <c>Flags.4</c>), or a direct address
    /// (<c>%MW100</c>, <c>%M5</c>).
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
        private readonly PlcRoot _root;
        private readonly Step[] _steps;
        private readonly bool _hasIndex;

        // The location last resolved, the runtime it belongs to and the root's binding
        // then: variable memory never moves while a runtime lives, so only array indexes
        // and in/out parameters (bound per call) need re-evaluating.
        private PlcRuntime? _resolvedFor;
        private PlcLocation _resolved;
        private int _resolvedBinding;

        internal PlcReferenceOperand(string text, PlcRoot root, IReadOnlyList<Step> steps, PlcType staticType)
            : base(text)
        {
            _root = root;
            _steps = steps.ToArray();
            _hasIndex = _steps.Any(s => s is IndexStep);
            StaticType = staticType;
        }

        public override PlcType? StaticType { get; }

        /// <summary>The root variable, when the reference starts with a declared variable.</summary>
        public PlcVariable? Variable => (_root as PlcVariableRoot)?.Variable;

        /// <summary>The direct address, when the reference is one or starts with a located variable.</summary>
        public PlcAddress? Address => (_root as PlcAddressRoot)?.Address ?? Variable?.Address;

        public override bool IsWritable => true;

        /// <summary>True when the reference has an array index, so where it points can change.</summary>
        public bool HasIndex => _hasIndex;

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
            if (!_hasIndex && ReferenceEquals(_resolvedFor, runtime) && _resolvedBinding == _root.Binding) return _resolved;

            var binding = _root.Binding;
            var location = _root.Locate(runtime);
            for (var i = 0; i < _steps.Length && location.IsValid; i++)
            {
                location = _steps[i].Apply(location, runtime);
            }

            if (!_hasIndex)
            {
                _resolved = location;
                _resolvedFor = runtime;
                _resolvedBinding = binding;
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

    /// <summary>Where a reference starts: a declared variable, a direct address, or a fixed location.</summary>
    public abstract class PlcRoot
    {
        public abstract PlcType Type { get; }

        public abstract PlcLocation Locate(PlcRuntime runtime);

        /// <summary>
        /// Changes when the root starts pointing somewhere else (an in/out parameter
        /// bound to another caller's variable), so references from it resolve again.
        /// </summary>
        internal int Binding { get; private protected set; }
    }

    /// <summary>A declared (global) variable.</summary>
    public sealed class PlcVariableRoot : PlcRoot
    {
        public PlcVariableRoot(PlcVariable variable) => Variable = variable;

        public PlcVariable Variable { get; }

        public override PlcType Type => Variable.Address?.Bit.HasValue == true ? PlcType.Bool : Variable.Type;

        public override PlcLocation Locate(PlcRuntime runtime) => runtime.Locate(Variable);
    }

    /// <summary>A direct address (%MW10, %M5, %S21).</summary>
    public sealed class PlcAddressRoot : PlcRoot
    {
        public PlcAddressRoot(PlcAddress address) => Address = address;

        public PlcAddress Address { get; }

        public override PlcType Type => Address.DefaultType;

        public override PlcLocation Locate(PlcRuntime runtime) => runtime.Memory.Locate(Address);
    }

    /// <summary>A location known when the code is compiled: a variable of one DFB instance.</summary>
    public sealed class PlcFixedRoot : PlcRoot
    {
        private readonly PlcLocation _location;

        public PlcFixedRoot(PlcLocation location) => _location = location;

        public override PlcType Type => _location.Type;

        public override PlcLocation Locate(PlcRuntime runtime) => _location;
    }

    /// <summary>
    /// An in/out parameter of one DFB instance. IEC 61131-3 passes VAR_IN_OUT by
    /// reference, so each call binds it to that call's actual variable; when the actual
    /// is not a variable, to the instance's own field, which the call copies in and out.
    /// </summary>
    public sealed class PlcInOutRoot : PlcRoot
    {
        public PlcInOutRoot(PlcLocation own)
        {
            Own = own;
            Current = own;
        }

        /// <summary>The instance's own field for the parameter.</summary>
        public PlcLocation Own { get; }

        /// <summary>Where the call in progress bound the parameter.</summary>
        public PlcLocation Current { get; private set; }

        /// <summary>True when bound to the caller's variable rather than the instance's field.</summary>
        public bool ByReference { get; private set; }

        public override PlcType Type => Own.Type;

        public override PlcLocation Locate(PlcRuntime runtime) => Current;

        /// <summary>Binds the parameter to the caller's variable for this call.</summary>
        internal void Bind(PlcLocation actual)
        {
            ByReference = true;
            Move(actual);
        }

        /// <summary>Binds the parameter to the instance's own field (the value is copied in and out).</summary>
        internal void BindOwn()
        {
            ByReference = false;
            Move(Own);
        }

        private void Move(PlcLocation location)
        {
            if (location.SameAs(Current)) return;
            Current = location;
            Binding++;
        }
    }

    /// <summary>The names code can refer to.</summary>
    public interface IPlcScope
    {
        PlcRoot? Resolve(string name);
    }

    /// <summary>The project's declared variables: what sections see.</summary>
    public sealed class PlcGlobalScope : IPlcScope
    {
        private readonly IReadOnlyDictionary<string, PlcVariable> _variables;

        public PlcGlobalScope(IReadOnlyDictionary<string, PlcVariable> variables) => _variables = variables;

        public PlcRoot? Resolve(string name) => _variables.TryGetValue(name, out var variable) ? new PlcVariableRoot(variable) : null;
    }

    /// <summary>
    /// The code of one DFB instance: its own parameters and variables first (at the
    /// instance's memory), then whatever the outer scope has.
    /// </summary>
    public sealed class PlcInstanceScope : IPlcScope
    {
        private readonly PlcLocation _instance;
        private readonly IPlcScope _outer;
        private readonly IReadOnlyDictionary<string, PlcInOutRoot>? _inOuts;

        /// <param name="inOuts">The instance's in/out parameters, which each call binds to its own variables.</param>
        public PlcInstanceScope(PlcLocation instance, IPlcScope outer, IReadOnlyDictionary<string, PlcInOutRoot>? inOuts = null)
        {
            _instance = instance;
            _outer = outer;
            _inOuts = inOuts;
        }

        public PlcRoot? Resolve(string name)
        {
            if (_inOuts != null && _inOuts.TryGetValue(name, out var inOut)) return inOut;
            return _instance.Type.FindField(name) is { } field
                ? new PlcFixedRoot(new PlcLocation(_instance.Space, _instance.Offset + field.Offset, field.Type))
                : _outer.Resolve(name);
        }
    }

    /// <summary>Builds a reference from its root and steps, checking each step against the types.</summary>
    internal sealed class PlcReferenceBuilder
    {
        private readonly string _text;
        private readonly PlcRoot _root;
        private readonly List<PlcReferenceOperand.Step> _steps = new();
        private PlcType _type;
        private string? _error;

        public PlcReferenceBuilder(string text, PlcRoot root)
        {
            _text = text;
            _root = root;
            _type = root.Type;
        }

        public void Field(string name)
        {
            if (_error != null) return;
            if (_type.FindField(name) is not { } member)
            {
                _error = _type.Kind == PlcTypeKind.Unknown ? $"type {_type} is not defined in the XEF" : $"{_type} has no field {name}";
                return;
            }
            _steps.Add(new PlcReferenceOperand.FieldStep(name));
            _type = member.Type;
        }

        public void Bit(int bit)
        {
            if (_error != null) return;
            if (!_type.IsInteger || bit >= _type.BitWidth)
            {
                _error = $"no bit {bit} in {_type}";
                return;
            }
            _steps.Add(new PlcReferenceOperand.BitStep(bit));
            _type = PlcType.Bool;
        }

        public void Index(PlcOperand index)
        {
            if (_error != null) return;
            if (_type.Kind != PlcTypeKind.Array)
            {
                _error = $"{_type} is not an array";
                return;
            }
            _steps.Add(new PlcReferenceOperand.IndexStep(index));
            _type = _type.ElementType!;
        }

        public PlcOperand Build() => _error != null
            ? new PlcUnresolvedOperand(_text, _error)
            : new PlcReferenceOperand(_text, _root, _steps, _type);
    }

    /// <summary>Turns actual-parameter text into operands against a scope.</summary>
    public static class PlcOperandParser
    {
        public static PlcOperand Parse(string text, IReadOnlyDictionary<string, PlcVariable> symbols)
            => Parse(text, new PlcGlobalScope(symbols));

        public static PlcOperand Parse(string text, IPlcScope scope)
        {
            var trimmed = text.Trim();
            if (PlcLiteral.TryParse(trimmed, out var literal)) return new PlcConstantOperand(text, literal);

            if (trimmed.StartsWith('%'))
            {
                return PlcAddress.TryParse(trimmed) is { } address
                    ? new PlcReferenceBuilder(text, new PlcAddressRoot(address)).Build()
                    : new PlcUnresolvedOperand(text, "unsupported direct address");
            }

            var reader = new Reader(trimmed);
            var name = reader.Identifier();
            if (name == null) return new PlcUnresolvedOperand(text, "not a variable reference");
            if (scope.Resolve(name) is not { } root) return new PlcUnresolvedOperand(text, $"'{name}' is not declared");

            var reference = new PlcReferenceBuilder(text, root);
            while (!reader.AtEnd)
            {
                if (reader.TryConsume('.'))
                {
                    if (reader.Number() is { } bit)
                    {
                        reference.Bit(bit);
                        continue;
                    }
                    if (reader.Identifier() is not { } field) return new PlcUnresolvedOperand(text, "bad field name");
                    reference.Field(field);
                    continue;
                }

                if (reader.TryConsume('['))
                {
                    var indexes = reader.Until(']');
                    if (indexes == null) return new PlcUnresolvedOperand(text, "unterminated index");
                    foreach (var index in indexes.Split(','))
                    {
                        var operand = Parse(index, scope);
                        if (operand is PlcUnresolvedOperand) return new PlcUnresolvedOperand(text, "unsupported index");
                        reference.Index(operand);
                    }
                    continue;
                }

                return new PlcUnresolvedOperand(text, "expression");
            }

            return reference.Build();
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
