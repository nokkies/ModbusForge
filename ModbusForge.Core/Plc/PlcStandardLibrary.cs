using System;
using System.Collections.Generic;
using System.Linq;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// The Unity Pro / Control Expert standard functions (EF) and function blocks (EFB)
    /// the PLC runtime executes. Behaviour follows the type descriptions Control Expert
    /// embeds in every exported XEF (EFSource/EFBSource "TypeDescriptiveForm") and IEC
    /// 61131-3 for the timers, edges, bistables and counters. Types not listed here stay
    /// inert: they are drawn and keep their outputs, but never compute.
    /// </summary>
    public static class PlcStandardLibrary
    {
        private static readonly Dictionary<string, PlcBlockBehavior> Exact = BuildExact();

        /// <summary>Elementary type names as they appear in conversion and typed function names.</summary>
        private static readonly Dictionary<string, PlcType> TypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BOOL"] = PlcType.Bool,
            ["BYTE"] = PlcType.Byte,
            ["WORD"] = PlcType.Word,
            ["DWORD"] = PlcType.Dword,
            ["INT"] = PlcType.Int,
            ["UINT"] = PlcType.Uint,
            ["DINT"] = PlcType.Dint,
            ["UDINT"] = PlcType.Udint,
            ["REAL"] = PlcType.Real,
            ["TIME"] = PlcType.Time,
            ["DATE"] = PlcType.Date,
            ["TOD"] = PlcType.TimeOfDay,
            ["DT"] = PlcType.DateAndTime,
            ["STRING"] = PlcType.String16,
        };

        /// <summary>Generic functions that also exist as typed variants (ADD_REAL, GT_INT, ...).</summary>
        private static readonly Dictionary<string, Func<PlcType?, PlcBlockBehavior>> Generic = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AND"] = t => new LogicFunction(LogicFunction.Kind.And, t),
            ["OR"] = t => new LogicFunction(LogicFunction.Kind.Or, t),
            ["XOR"] = t => new LogicFunction(LogicFunction.Kind.Xor, t),
            ["NOT"] = t => new NotFunction(t),
            ["MOVE"] = t => new MoveFunction(t),
            ["SEL"] = t => new SelectFunction(t),
            ["MUX"] = t => new MultiplexFunction(t),
            ["LIMIT"] = t => new LimitFunction(t, indicators: false),
            ["LIMIT_IND"] = t => new LimitFunction(t, indicators: true),
            ["MAX"] = t => new ExtremeFunction(t, max: true),
            ["MIN"] = t => new ExtremeFunction(t, max: false),
            ["EQ"] = t => new CompareFunction(CompareFunction.Kind.Eq, t),
            ["NE"] = t => new CompareFunction(CompareFunction.Kind.Ne, t),
            ["GT"] = t => new CompareFunction(CompareFunction.Kind.Gt, t),
            ["GE"] = t => new CompareFunction(CompareFunction.Kind.Ge, t),
            ["LT"] = t => new CompareFunction(CompareFunction.Kind.Lt, t),
            ["LE"] = t => new CompareFunction(CompareFunction.Kind.Le, t),
            ["ADD"] = t => new ArithmeticFunction(ArithmeticFunction.Kind.Add, t),
            ["SUB"] = t => new ArithmeticFunction(ArithmeticFunction.Kind.Sub, t),
            ["MUL"] = t => new ArithmeticFunction(ArithmeticFunction.Kind.Mul, t),
            ["DIV"] = t => new ArithmeticFunction(ArithmeticFunction.Kind.Div, t),
            ["MOD"] = t => new ArithmeticFunction(ArithmeticFunction.Kind.Mod, t),
            ["ABS"] = t => new ArithmeticFunction(ArithmeticFunction.Kind.Abs, t),
            ["DIVMOD"] = t => new DivModFunction(t),
            ["SHL"] = t => new ShiftFunction(ShiftFunction.Kind.Shl, t),
            ["SHR"] = t => new ShiftFunction(ShiftFunction.Kind.Shr, t),
            ["ROL"] = t => new ShiftFunction(ShiftFunction.Kind.Rol, t),
            ["ROR"] = t => new ShiftFunction(ShiftFunction.Kind.Ror, t),
            ["SQRT"] = _ => new RealFunction(v => v < 0 ? null : Math.Sqrt(v)),
            ["EXP"] = _ => new RealFunction(Math.Exp),
            ["LN"] = _ => new RealFunction(v => v <= 0 ? null : Math.Log(v)),
            ["LOG"] = _ => new RealFunction(v => v <= 0 ? null : Math.Log10(v)),
            ["SIN"] = _ => new RealFunction(Math.Sin),
            ["COS"] = _ => new RealFunction(Math.Cos),
            ["TAN"] = _ => new RealFunction(Math.Tan),
            ["ASIN"] = _ => new RealFunction(v => v is < -1 or > 1 ? null : Math.Asin(v)),
            ["ACOS"] = _ => new RealFunction(v => v is < -1 or > 1 ? null : Math.Acos(v)),
            ["ATAN"] = _ => new RealFunction(Math.Atan),
            ["EXPT"] = _ => new PowerFunction(),
            ["CTU"] = t => new UpCounter(t ?? PlcType.Int),
            ["CTD"] = t => new DownCounter(t ?? PlcType.Int),
            ["CTUD"] = t => new UpDownCounter(t ?? PlcType.Int),
        };

        private static Dictionary<string, PlcBlockBehavior> BuildExact() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["SET"] = new ConstantFunction(true),
            ["RESET"] = new ConstantFunction(false),
            ["DEG_TO_RAD"] = new RealFunction(v => v * Math.PI / 180),
            ["RAD_TO_DEG"] = new RealFunction(v => v * 180 / Math.PI),
            ["MULTIME"] = new TimeScaleFunction(divide: false),
            ["DIVTIME"] = new TimeScaleFunction(divide: true),
            ["BCD_TO_INT"] = new BcdFunction(toBcd: false),
            ["INT_TO_BCD"] = new BcdFunction(toBcd: true),
            ["BIT_TO_WORD"] = new BitsToWordFunction(PlcType.Word),
            ["BIT_TO_BYTE"] = new BitsToWordFunction(PlcType.Byte),
            ["WORD_TO_BIT"] = new WordToBitsFunction(PlcType.Word),
            ["BYTE_TO_BIT"] = new WordToBitsFunction(PlcType.Byte),
            ["WORD_AS_DWORD"] = new PackFunction(PlcType.Dword, ("LOW", 0, PlcType.Word), ("HIGH", 16, PlcType.Word)),
            ["WORD_AS_DINT"] = new PackFunction(PlcType.Dint, ("LOW", 0, PlcType.Word), ("HIGH", 16, PlcType.Word)),
            ["WORD_AS_REAL"] = new PackFunction(PlcType.Real, ("LOW", 0, PlcType.Word), ("HIGH", 16, PlcType.Word)),
            ["INT_AS_DINT"] = new PackFunction(PlcType.Dint, ("LOW", 0, PlcType.Int), ("HIGH", 16, PlcType.Int)),
            ["BYTE_AS_WORD"] = new PackFunction(PlcType.Word, ("LOW", 0, PlcType.Byte), ("HIGH", 8, PlcType.Byte)),
            ["BYTE_AS_INT"] = new PackFunction(PlcType.Int, ("BYTE1", 0, PlcType.Byte), ("BYTE2", 8, PlcType.Byte)),
            ["REAL_AS_WORD"] = new UnpackFunction(PlcType.Real, ("LOW", 0, PlcType.Word), ("HIGH", 16, PlcType.Word)),
            ["DWORD_AS_WORD"] = new UnpackFunction(PlcType.Dword, ("LOW", 0, PlcType.Word), ("HIGH", 16, PlcType.Word)),
            ["DINT_AS_WORD"] = new UnpackFunction(PlcType.Dint, ("LOW", 0, PlcType.Word), ("HIGH", 16, PlcType.Word)),
            ["WORD_AS_BYTE"] = new UnpackFunction(PlcType.Word, ("LOW", 0, PlcType.Byte), ("HIGH", 8, PlcType.Byte)),
            ["INT_AS_BYTE"] = new UnpackFunction(PlcType.Int, ("BYTE1", 0, PlcType.Byte), ("BYTE2", 8, PlcType.Byte)),
            ["TON"] = new OnDelayTimer(),
            ["TOF"] = new OffDelayTimer(),
            ["TP"] = new PulseTimer(),
            ["R_TRIG"] = new EdgeDetector(rising: true),
            ["F_TRIG"] = new EdgeDetector(rising: false),
            ["TRIGGER"] = new AllEdgesDetector(),
            ["SR"] = new Bistable(setDominant: true),
            ["RS"] = new Bistable(setDominant: false),
            ["SAMPLETM"] = new SampleTimer(),
            ["SYSSTATE"] = new SystemState(),
        };

        /// <summary>The behaviour of a standard type, or null when the runtime does not simulate it.</summary>
        public static PlcBlockBehavior? Find(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            var name = typeName.Trim();
            if (Exact.TryGetValue(name, out var exact)) return exact;
            if (Generic.TryGetValue(name, out var generic)) return generic(null);

            // Typed variants: ADD_REAL, GT_INT, AND_WORD, CTU_UDINT, LIMIT_IND_DINT, MAX_TIME, ...
            var split = name.LastIndexOf('_');
            if (split > 0 && TypeNames.TryGetValue(name[(split + 1)..], out var suffixType)
                && Generic.TryGetValue(name[..split], out var typed))
            {
                return typed(suffixType);
            }

            // Conversions: INT_TO_REAL, REAL_TO_INT, WORD_TO_INT, TIME_TO_DINT, BOOL_TO_INT, ...
            var to = name.IndexOf("_TO_", StringComparison.OrdinalIgnoreCase);
            if (to > 0 && TypeNames.TryGetValue(name[..to], out var from) && TypeNames.TryGetValue(name[(to + 4)..], out var target))
            {
                return new ConvertFunction(from, target);
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Functions (EF): no memory; EN = 0 drives their output links to 0.
        // ------------------------------------------------------------------

        private abstract class Function : PlcBlockBehavior
        {
            public override bool IsFunctionBlock => false;

            /// <summary>The input values in pin order, leaving out the pin named <paramref name="except"/>.</summary>
            protected static PlcValue[] Inputs(PlcCall call, string? except = null)
            {
                var skip = -1;
                if (except != null)
                {
                    for (var i = 0; i < call.InputCount && skip < 0; i++)
                    {
                        if (string.Equals(except, call.InputName(i), StringComparison.OrdinalIgnoreCase)) skip = i;
                    }
                }

                var values = new PlcValue[skip < 0 ? call.InputCount : call.InputCount - 1];
                for (int i = 0, n = 0; i < call.InputCount; i++)
                {
                    if (i != skip) values[n++] = call.Input(i);
                }
                return values;
            }

            protected static PlcType Operating(PlcType? fixedType, ReadOnlySpan<PlcValue> values)
                => fixedType ?? PlcOps.OperatingType(values);
        }

        private sealed class LogicFunction : Function
        {
            public enum Kind { And, Or, Xor }

            private readonly Kind _kind;
            private readonly PlcType? _type;

            public LogicFunction(Kind kind, PlcType? type)
            {
                _kind = kind;
                _type = type;
            }

            public override void Execute(PlcCall call)
            {
                var inputs = Inputs(call);
                var type = Operating(_type, inputs);
                if (inputs.Length == 0) return;
                var result = PlcOps.Convert(inputs[0], type);
                for (var i = 1; i < inputs.Length; i++)
                {
                    result = _kind switch
                    {
                        Kind.And => PlcOps.And(type, result, inputs[i]),
                        Kind.Or => PlcOps.Or(type, result, inputs[i]),
                        _ => PlcOps.Xor(type, result, inputs[i])
                    };
                }
                call.SetOutput("OUT", result);
            }
        }

        private sealed class NotFunction : Function
        {
            private readonly PlcType? _type;
            public NotFunction(PlcType? type) => _type = type;

            public override void Execute(PlcCall call)
            {
                var input = call.Input("IN");
                call.SetOutput("OUT", PlcOps.Not(Operating(_type, new[] { input }), input));
            }
        }

        private sealed class MoveFunction : Function
        {
            private readonly PlcType? _type;
            public MoveFunction(PlcType? type) => _type = type;

            public override void Execute(PlcCall call)
            {
                var input = call.Input("IN");
                call.SetOutput("OUT", _type != null ? PlcOps.Convert(input, _type) : input);
            }
        }

        /// <summary>SET / RESET: OUT := 1 / 0 each time they run (conditioned by EN, they latch).</summary>
        private sealed class ConstantFunction : Function
        {
            private readonly PlcValue _value;
            public ConstantFunction(bool value) => _value = PlcOps.FromBool(value);
            public override void Execute(PlcCall call) => call.SetOutput("OUT", _value);
        }

        private sealed class SelectFunction : Function
        {
            private readonly PlcType? _type;
            public SelectFunction(PlcType? type) => _type = type;

            public override void Execute(PlcCall call)
            {
                var in0 = call.Input("IN0");
                var in1 = call.Input("IN1");
                var type = Operating(_type, new[] { in0, in1 });
                call.SetOutput("OUT", PlcOps.Convert(call.Input("G").AsBool() ? in1 : in0, type));
            }
        }

        private sealed class MultiplexFunction : Function
        {
            private readonly PlcType? _type;
            public MultiplexFunction(PlcType? type) => _type = type;

            public override void Execute(PlcCall call)
            {
                var inputs = Inputs(call, "K");
                var k = call.Input("K").AsInteger();
                if (k < 0 || k >= inputs.Length)
                {
                    call.Fail();
                    return;
                }
                call.SetOutput("OUT", PlcOps.Convert(inputs[k], Operating(_type, inputs)));
            }
        }

        private sealed class LimitFunction : Function
        {
            private readonly PlcType? _type;
            private readonly bool _indicators;

            public LimitFunction(PlcType? type, bool indicators)
            {
                _type = type;
                _indicators = indicators;
            }

            public override void Execute(PlcCall call)
            {
                var mn = call.Input("MN");
                var input = call.Input("IN");
                var mx = call.Input("MX");
                var type = Operating(_type, new[] { input, mn, mx });
                var low = PlcOps.Compare(type, input, mn) < 0;
                var high = PlcOps.Compare(type, input, mx) > 0;
                call.SetOutput("OUT", PlcOps.Convert(low ? mn : high ? mx : input, type));
                if (_indicators)
                {
                    call.SetOutput("MN_IND", PlcOps.FromBool(low));
                    call.SetOutput("MX_IND", PlcOps.FromBool(high));
                }
            }
        }

        private sealed class ExtremeFunction : Function
        {
            private readonly PlcType? _type;
            private readonly bool _max;

            public ExtremeFunction(PlcType? type, bool max)
            {
                _type = type;
                _max = max;
            }

            public override void Execute(PlcCall call)
            {
                var inputs = Inputs(call);
                if (inputs.Length == 0) return;
                var type = Operating(_type, inputs);
                var best = inputs[0];
                for (var i = 1; i < inputs.Length; i++)
                {
                    var c = PlcOps.Compare(type, inputs[i], best);
                    if (_max ? c > 0 : c < 0) best = inputs[i];
                }
                call.SetOutput("OUT", PlcOps.Convert(best, type));
            }
        }

        /// <summary>EQ/GE/GT/LE/LT check successive inputs (IN1 op IN2 op IN3 ...); NE has two.</summary>
        private sealed class CompareFunction : Function
        {
            public enum Kind { Eq, Ne, Gt, Ge, Lt, Le }

            private readonly Kind _kind;
            private readonly PlcType? _type;

            public CompareFunction(Kind kind, PlcType? type)
            {
                _kind = kind;
                _type = type;
            }

            public override void Execute(PlcCall call)
            {
                var inputs = Inputs(call);
                var type = Operating(_type, inputs);
                var result = inputs.Length >= 2;
                for (var i = 0; result && i + 1 < inputs.Length; i++)
                {
                    var c = PlcOps.Compare(type, inputs[i], inputs[i + 1]);
                    result = _kind switch
                    {
                        Kind.Eq => c == 0,
                        Kind.Ne => c != 0,
                        Kind.Gt => c > 0,
                        Kind.Ge => c >= 0,
                        Kind.Lt => c < 0,
                        _ => c <= 0
                    };
                }
                call.SetOutput("OUT", PlcOps.FromBool(result));
            }
        }

        private sealed class ArithmeticFunction : Function
        {
            public enum Kind { Add, Sub, Mul, Div, Mod, Abs }

            private readonly Kind _kind;
            private readonly PlcType? _type;

            public ArithmeticFunction(Kind kind, PlcType? type)
            {
                _kind = kind;
                _type = type;
            }

            public override void Execute(PlcCall call)
            {
                if (_kind == Kind.Abs)
                {
                    var input = call.Input("IN");
                    var t = Operating(_type, new[] { input });
                    call.SetOutput("OUT", t.IsReal
                        ? PlcValue.FromReal(Math.Abs(input.AsDouble()))
                        : PlcValue.FromInteger(t, Math.Abs(PlcOps.Convert(input, t).AsInteger())));
                    return;
                }

                var inputs = Inputs(call);
                if (inputs.Length == 0) return;
                var type = Operating(_type, inputs);
                var result = PlcOps.Convert(inputs[0], type);
                for (var i = 1; i < inputs.Length; i++)
                {
                    PlcValue? next = _kind switch
                    {
                        Kind.Add => PlcOps.Add(type, result, inputs[i]),
                        Kind.Sub => PlcOps.Subtract(type, result, inputs[i]),
                        Kind.Mul => PlcOps.Multiply(type, result, inputs[i]),
                        Kind.Div => PlcOps.Divide(type, result, inputs[i]),
                        _ => PlcOps.Modulo(type, result, inputs[i])
                    };
                    if (next is not { } value)
                    {
                        // Division by zero: ENO goes to 0 and the output is not produced.
                        call.Fail();
                        return;
                    }
                    result = value;
                }
                call.SetOutput("OUT", result);
            }
        }

        private sealed class DivModFunction : Function
        {
            private readonly PlcType? _type;
            public DivModFunction(PlcType? type) => _type = type;

            public override void Execute(PlcCall call)
            {
                var a = call.Input("IN1");
                var b = call.Input("IN2");
                var type = Operating(_type, new[] { a, b });
                if (PlcOps.Divide(type, a, b) is not { } dv || PlcOps.Modulo(type, a, b) is not { } md)
                {
                    call.Fail();
                    return;
                }
                call.SetOutput("DV", dv);
                call.SetOutput("MD", md);
            }
        }

        private sealed class ShiftFunction : Function
        {
            public enum Kind { Shl, Shr, Rol, Ror }

            private readonly Kind _kind;
            private readonly PlcType? _type;

            public ShiftFunction(Kind kind, PlcType? type)
            {
                _kind = kind;
                _type = type;
            }

            public override void Execute(PlcCall call)
            {
                var input = call.Input("IN");
                var type = Operating(_type, new[] { input });
                var width = type.BitWidth is 64 ? 16 : type.BitWidth;
                var mask = width >= 64 ? -1L : (1L << width) - 1;
                var value = PlcOps.Convert(input, type).AsInteger() & mask;
                var n = (int)Math.Clamp(call.Input("N").AsInteger(), 0, 64);
                long result = _kind switch
                {
                    Kind.Shl => n >= width ? 0 : (value << n) & mask,
                    Kind.Shr => n >= width ? 0 : value >> n,
                    Kind.Rol => ((value << (n % width)) | (value >> ((width - n % width) % width))) & mask,
                    _ => ((value >> (n % width)) | (value << ((width - n % width) % width))) & mask
                };
                call.SetOutput("OUT", PlcValue.FromInteger(type, result));
            }
        }

        private sealed class RealFunction : Function
        {
            private readonly Func<double, double?> _function;

            public RealFunction(Func<double, double?> function) => _function = function;

            public RealFunction(Func<double, double> function) => _function = v => function(v);

            public override void Execute(PlcCall call)
            {
                if (_function(call.Input("IN").AsDouble()) is not { } result || double.IsNaN(result) || double.IsInfinity(result))
                {
                    call.Fail();
                    return;
                }
                call.SetOutput("OUT", PlcValue.FromReal(result));
            }
        }

        private sealed class PowerFunction : Function
        {
            public override void Execute(PlcCall call)
            {
                var result = Math.Pow(call.Input("IN1").AsDouble(), call.Input("IN2").AsDouble());
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    call.Fail();
                    return;
                }
                call.SetOutput("OUT", PlcValue.FromReal(result));
            }
        }

        /// <summary>MULTIME / DIVTIME: a TIME scaled by a number.</summary>
        private sealed class TimeScaleFunction : Function
        {
            private readonly bool _divide;
            public TimeScaleFunction(bool divide) => _divide = divide;

            public override void Execute(PlcCall call)
            {
                var time = PlcOps.Convert(call.Input("IN1"), PlcType.Time).AsInteger();
                var factor = call.Input("IN2").AsDouble();
                if (_divide && factor == 0)
                {
                    call.Fail();
                    return;
                }
                var result = _divide ? time / factor : time * factor;
                call.SetOutput("OUT", PlcValue.FromTime(PlcValue.RoundToInteger(result)));
            }
        }

        private sealed class ConvertFunction : Function
        {
            private readonly PlcType _from;
            private readonly PlcType _to;

            public ConvertFunction(PlcType from, PlcType to)
            {
                _from = from;
                _to = to;
            }

            public override void Execute(PlcCall call)
                => call.SetOutput("OUT", PlcOps.Convert(PlcOps.Convert(call.Input("IN"), _from), _to));
        }

        private sealed class BcdFunction : Function
        {
            private readonly bool _toBcd;
            public BcdFunction(bool toBcd) => _toBcd = toBcd;

            public override void Execute(PlcCall call)
            {
                var value = PlcOps.Convert(call.Input("IN"), PlcType.Int).AsInteger() & 0xFFFF;
                long result = 0;
                if (_toBcd)
                {
                    if (value > 9999)
                    {
                        call.Fail();
                        return;
                    }
                    for (var shift = 0; value > 0; shift += 4, value /= 10) result |= (value % 10) << shift;
                }
                else
                {
                    for (var digit = 1L; value > 0; digit *= 10, value >>= 4)
                    {
                        var nibble = value & 0xF;
                        if (nibble > 9)
                        {
                            call.Fail();
                            return;
                        }
                        result += nibble * digit;
                    }
                }
                call.SetOutput("OUT", PlcValue.FromInteger(PlcType.Int, result));
            }
        }

        /// <summary>BIT_TO_WORD / BIT_TO_BYTE: OUT = {BITn, ..., BIT0}.</summary>
        private sealed class BitsToWordFunction : Function
        {
            private readonly PlcType _type;
            public BitsToWordFunction(PlcType type) => _type = type;

            public override void Execute(PlcCall call)
            {
                long result = 0;
                for (var bit = 0; bit < _type.BitWidth; bit++)
                {
                    if (call.Input("BIT" + bit).AsBool()) result |= 1L << bit;
                }
                call.SetOutput("OUT", PlcValue.FromInteger(_type, result));
            }
        }

        /// <summary>WORD_TO_BIT / BYTE_TO_BIT: BITn = bit n of IN.</summary>
        private sealed class WordToBitsFunction : Function
        {
            private readonly PlcType _type;
            public WordToBitsFunction(PlcType type) => _type = type;

            public override void Execute(PlcCall call)
            {
                var value = PlcOps.Convert(call.Input("IN"), _type).AsInteger();
                for (var bit = 0; bit < _type.BitWidth; bit++)
                {
                    call.SetOutput("BIT" + bit, PlcOps.FromBool((value & (1L << bit)) != 0));
                }
            }
        }

        /// <summary>WORD_AS_DWORD, WORD_AS_REAL, BYTE_AS_WORD, ...: inputs placed at their bit positions.</summary>
        private sealed class PackFunction : Function
        {
            private readonly PlcType _result;
            private readonly (string Pin, int Shift, PlcType Type)[] _parts;

            public PackFunction(PlcType result, params (string Pin, int Shift, PlcType Type)[] parts)
            {
                _result = result;
                _parts = parts;
            }

            public override void Execute(PlcCall call)
            {
                long bits = 0;
                foreach (var (pin, shift, type) in _parts)
                {
                    var part = PlcOps.Convert(call.Input(pin), type).AsInteger() & ((1L << type.BitWidth) - 1);
                    bits |= part << shift;
                }
                call.SetOutput("OUT", _result.IsReal
                    ? PlcValue.FromReal(BitConverter.Int32BitsToSingle((int)bits))
                    : PlcValue.FromInteger(_result, bits));
            }
        }

        /// <summary>REAL_AS_WORD, DWORD_AS_WORD, INT_AS_BYTE, ...: the input's bit pattern split into outputs.</summary>
        private sealed class UnpackFunction : Function
        {
            private readonly PlcType _source;
            private readonly (string Pin, int Shift, PlcType Type)[] _parts;

            public UnpackFunction(PlcType source, params (string Pin, int Shift, PlcType Type)[] parts)
            {
                _source = source;
                _parts = parts;
            }

            public override void Execute(PlcCall call)
            {
                var value = PlcOps.Convert(call.Input("IN"), _source);
                var bits = _source.IsReal
                    ? BitConverter.SingleToInt32Bits((float)value.AsDouble()) & 0xFFFFFFFFL
                    : value.AsInteger() & ((1L << _source.BitWidth) - 1);
                foreach (var (pin, shift, type) in _parts)
                {
                    call.SetOutput(pin, PlcValue.FromInteger(type, (bits >> shift) & ((1L << type.BitWidth) - 1)));
                }
            }
        }

        // ------------------------------------------------------------------
        // Function blocks (EFB): keep state per instance; EN = 0 freezes them.
        // ------------------------------------------------------------------

        private abstract class FunctionBlock<TState> : PlcBlockBehavior where TState : class, new()
        {
            public override bool IsFunctionBlock => true;

            public override object? CreateState() => new TState();

            public override void Execute(PlcCall call)
            {
                call.State ??= new TState();
                Execute(call, (TState)call.State);
            }

            protected abstract void Execute(PlcCall call, TState state);
        }

        private sealed class TimerState
        {
            public bool PreviousIn;
            public bool Running;
            public long Start;
            public long Elapsed;
        }

        /// <summary>TON: Q rises PT after IN rises and falls with IN; ET counts up to PT.</summary>
        private sealed class OnDelayTimer : FunctionBlock<TimerState>
        {
            protected override void Execute(PlcCall call, TimerState s)
            {
                var input = call.Input("IN").AsBool();
                var preset = PlcOps.Convert(call.Input("PT"), PlcType.Time).AsInteger();
                bool q;
                if (input)
                {
                    if (!s.PreviousIn) s.Start = call.Now;
                    s.Elapsed = Math.Min(call.Now - s.Start, preset);
                    q = s.Elapsed >= preset;
                }
                else
                {
                    s.Elapsed = 0;
                    q = false;
                }
                s.PreviousIn = input;
                call.SetOutput("Q", PlcOps.FromBool(q));
                call.SetOutput("ET", PlcValue.FromTime(s.Elapsed));
            }
        }

        /// <summary>TOF: Q follows IN up and falls PT after IN falls; ET counts the off delay.</summary>
        private sealed class OffDelayTimer : FunctionBlock<TimerState>
        {
            protected override void Execute(PlcCall call, TimerState s)
            {
                var input = call.Input("IN").AsBool();
                var preset = PlcOps.Convert(call.Input("PT"), PlcType.Time).AsInteger();
                bool q;
                if (input)
                {
                    s.Running = false;
                    s.Elapsed = 0;
                    q = true;
                }
                else
                {
                    if (s.PreviousIn)
                    {
                        s.Start = call.Now;
                        s.Running = true;
                    }
                    if (s.Running)
                    {
                        s.Elapsed = Math.Min(call.Now - s.Start, preset);
                        if (s.Elapsed >= preset) s.Running = false;
                    }
                    q = s.Running;
                }
                s.PreviousIn = input;
                call.SetOutput("Q", PlcOps.FromBool(q));
                call.SetOutput("ET", PlcValue.FromTime(s.Elapsed));
            }
        }

        /// <summary>TP: a rising IN starts a PT-long pulse on Q that IN cannot cut short or retrigger.</summary>
        private sealed class PulseTimer : FunctionBlock<TimerState>
        {
            protected override void Execute(PlcCall call, TimerState s)
            {
                var input = call.Input("IN").AsBool();
                var preset = PlcOps.Convert(call.Input("PT"), PlcType.Time).AsInteger();
                if (!s.Running && input && !s.PreviousIn)
                {
                    s.Running = true;
                    s.Start = call.Now;
                }
                if (s.Running)
                {
                    s.Elapsed = Math.Min(call.Now - s.Start, preset);
                    if (s.Elapsed >= preset) s.Running = false;
                }
                else if (!input)
                {
                    s.Elapsed = 0;
                }
                s.PreviousIn = input;
                call.SetOutput("Q", PlcOps.FromBool(s.Running));
                call.SetOutput("ET", PlcValue.FromTime(s.Elapsed));
            }
        }

        private sealed class EdgeState
        {
            public bool Previous;
        }

        /// <summary>R_TRIG / F_TRIG: Q is 1 for the one execution after CLK changes 0-&gt;1 (1-&gt;0).</summary>
        private sealed class EdgeDetector : FunctionBlock<EdgeState>
        {
            private readonly bool _rising;
            public EdgeDetector(bool rising) => _rising = rising;

            protected override void Execute(PlcCall call, EdgeState s)
            {
                var clk = call.Input("CLK").AsBool();
                var q = _rising ? clk && !s.Previous : !clk && s.Previous;
                s.Previous = clk;
                call.SetOutput("Q", PlcOps.FromBool(q));
            }
        }

        private sealed class AllEdgesDetector : FunctionBlock<EdgeState>
        {
            protected override void Execute(PlcCall call, EdgeState s)
            {
                var clk = call.Input("CLK").AsBool();
                var rise = clk && !s.Previous;
                var fall = !clk && s.Previous;
                s.Previous = clk;
                call.SetOutput("RISE", PlcOps.FromBool(rise));
                call.SetOutput("FALL", PlcOps.FromBool(fall));
                call.SetOutput("EDGE", PlcOps.FromBool(rise || fall));
            }
        }

        private sealed class BistableState
        {
            public bool Q;
        }

        /// <summary>SR (set dominant: S1, R) and RS (reset dominant: S, R1); Q1 starts at 0.</summary>
        private sealed class Bistable : FunctionBlock<BistableState>
        {
            private readonly bool _setDominant;
            public Bistable(bool setDominant) => _setDominant = setDominant;

            protected override void Execute(PlcCall call, BistableState s)
            {
                if (_setDominant)
                    s.Q = call.Input("S1").AsBool() || (s.Q && !call.Input("R").AsBool());
                else
                    s.Q = !call.Input("R1").AsBool() && (s.Q || call.Input("S").AsBool());
                call.SetOutput("Q1", PlcOps.FromBool(s.Q));
            }
        }

        private sealed class CounterState
        {
            public long Value;
            public bool PreviousUp;
            public bool PreviousDown;
        }

        private static (long Min, long Max) Range(PlcType type) => type.Kind switch
        {
            PlcTypeKind.Uint => (0, ushort.MaxValue),
            PlcTypeKind.Dint => (int.MinValue, int.MaxValue),
            PlcTypeKind.Udint => (0, uint.MaxValue),
            _ => (short.MinValue, short.MaxValue)
        };

        /// <summary>CTU: R clears CV; each CU rising edge adds 1 (no overflow); Q = CV &gt;= PV.</summary>
        private sealed class UpCounter : FunctionBlock<CounterState>
        {
            private readonly PlcType _type;
            public UpCounter(PlcType type) => _type = type;

            protected override void Execute(PlcCall call, CounterState s)
            {
                var cu = call.Input("CU").AsBool();
                var (_, max) = Range(_type);
                if (call.Input("R").AsBool()) s.Value = 0;
                else if (cu && !s.PreviousUp && s.Value < max) s.Value++;
                s.PreviousUp = cu;
                call.SetOutput("Q", PlcOps.FromBool(s.Value >= PlcOps.Convert(call.Input("PV"), _type).AsInteger()));
                call.SetOutput("CV", PlcValue.FromInteger(_type, s.Value));
            }
        }

        /// <summary>CTD: LD loads PV; each CD rising edge subtracts 1 (no underflow); Q = CV &lt;= 0.</summary>
        private sealed class DownCounter : FunctionBlock<CounterState>
        {
            private readonly PlcType _type;
            public DownCounter(PlcType type) => _type = type;

            protected override void Execute(PlcCall call, CounterState s)
            {
                var cd = call.Input("CD").AsBool();
                var (min, _) = Range(_type);
                if (call.Input("LD").AsBool()) s.Value = PlcOps.Convert(call.Input("PV"), _type).AsInteger();
                else if (cd && !s.PreviousDown && s.Value > min) s.Value--;
                s.PreviousDown = cd;
                call.SetOutput("Q", PlcOps.FromBool(s.Value <= 0));
                call.SetOutput("CV", PlcValue.FromInteger(_type, s.Value));
            }
        }

        /// <summary>CTUD: R (precedence) clears, LD loads PV, CU/CD edges count; QU = CV &gt;= PV, QD = CV &lt;= 0.</summary>
        private sealed class UpDownCounter : FunctionBlock<CounterState>
        {
            private readonly PlcType _type;
            public UpDownCounter(PlcType type) => _type = type;

            protected override void Execute(PlcCall call, CounterState s)
            {
                var cu = call.Input("CU").AsBool();
                var cd = call.Input("CD").AsBool();
                var pv = PlcOps.Convert(call.Input("PV"), _type).AsInteger();
                var (min, max) = Range(_type);
                if (call.Input("R").AsBool()) s.Value = 0;
                else if (call.Input("LD").AsBool()) s.Value = pv;
                else
                {
                    if (cu && !s.PreviousUp && s.Value < max) s.Value++;
                    if (cd && !s.PreviousDown && s.Value > min) s.Value--;
                }
                s.PreviousUp = cu;
                s.PreviousDown = cd;
                call.SetOutput("QU", PlcOps.FromBool(s.Value >= pv));
                call.SetOutput("QD", PlcOps.FromBool(s.Value <= 0));
                call.SetOutput("CV", PlcValue.FromInteger(_type, s.Value));
            }
        }

        private sealed class SampleState
        {
            public bool Started;
            public long Last;
            public long DelayScans;
        }

        /// <summary>SAMPLETM: Q is 1 for one cycle each time INTERVAL elapses, after DELSCANS start-up cycles.</summary>
        private sealed class SampleTimer : FunctionBlock<SampleState>
        {
            protected override void Execute(PlcCall call, SampleState s)
            {
                if (!s.Started)
                {
                    s.Started = true;
                    s.Last = call.Now;
                    s.DelayScans = Math.Max(0, call.Input("DELSCANS").AsInteger());
                }

                var q = false;
                if (s.DelayScans > 0)
                {
                    s.DelayScans--;
                }
                else if (call.Now - s.Last >= PlcOps.Convert(call.Input("INTERVAL"), PlcType.Time).AsInteger())
                {
                    q = true;
                    s.Last = call.Now;
                }
                call.SetOutput("Q", PlcOps.FromBool(q));
            }
        }

        private sealed class NoState
        {
        }

        /// <summary>SYSSTATE: COLD in the cold start cycle, WARM in the task's first cycle (like %S21).</summary>
        private sealed class SystemState : FunctionBlock<NoState>
        {
            protected override void Execute(PlcCall call, NoState s)
            {
                call.SetOutput("COLD", PlcOps.FromBool(call.ColdStart));
                call.SetOutput("WARM", PlcOps.FromBool(call.FirstTaskCycle || call.ColdStart));
                call.SetOutput("ERROR", PlcOps.False);
            }
        }
    }
}
