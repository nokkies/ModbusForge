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
            ["AVE"] = t => new WeightedAverage(t),
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
            ["COMP_DB"] = new DeadBandComparison(),
            ["AVGMV"] = new MovingAverage(),
            ["LOOKUP_TABLE1"] = new LookupTable(),
            ["SCALING"] = new Scaling(),
            ["RAMP"] = new Ramp(),
            ["INC"] = new StepFunction(+1),
            ["DEC"] = new StepFunction(-1),
            ["EXTRACT"] = new ExtractFunction(),
            ["COMPARE"] = new CompareIntegers(),
            ["PWM"] = new PulseWidthModulation(),
            ["FGEN"] = new FunctionGenerator(),
            ["T2T"] = new TableToTable(),
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

            // Table functions: LENGTH_ARINT, SUM_ARREAL, MOVE_INT_ARINT, FIND_GT_ARREAL, ROL_ARDINT, ...
            if (TableFunction(name, "FIND_EQ_AR")) return new TableFindFunction(TableFindFunction.Kind.Eq);
            if (TableFunction(name, "FIND_GT_AR")) return new TableFindFunction(TableFindFunction.Kind.Gt);
            if (TableFunction(name, "FIND_LT_AR")) return new TableFindFunction(TableFindFunction.Kind.Lt);
            if (TableFunction(name, "ROL_AR")) return new TableRotateFunction(left: true);
            if (TableFunction(name, "ROR_AR")) return new TableRotateFunction(left: false);
            if (name.StartsWith("R2T_", StringComparison.OrdinalIgnoreCase) && TypeNames.TryGetValue(name[4..], out var r2tType))
                return new RegisterToTable(r2tType);
            if (name.StartsWith("LENGTH_AR", StringComparison.OrdinalIgnoreCase)) return new TableLengthFunction();
            if (name.StartsWith("SUM_AR", StringComparison.OrdinalIgnoreCase) && TypeNames.TryGetValue(name[6..], out var sumType))
                return new TableSumFunction(sumType);
            var fill = name.IndexOf("_AR", StringComparison.OrdinalIgnoreCase);
            if (name.StartsWith("MOVE_", StringComparison.OrdinalIgnoreCase) && fill > 5
                && TypeNames.TryGetValue(name[5..fill], out var fillType) && name[(fill + 3)..].Equals(name[5..fill], StringComparison.OrdinalIgnoreCase))
            {
                return new TableFillFunction(fillType);
            }

            // Conversions: INT_TO_REAL, REAL_TO_INT, WORD_TO_INT, TIME_TO_DINT, BOOL_TO_INT, ...
            var to = name.IndexOf("_TO_", StringComparison.OrdinalIgnoreCase);
            if (to > 0 && TypeNames.TryGetValue(name[..to], out var from) && TypeNames.TryGetValue(name[(to + 4)..], out var target))
            {
                return new ConvertFunction(from, target);
            }

            return null;
        }

        /// <summary>A table function name: the prefix followed by an element type (FIND_EQ_ARINT).</summary>
        private static bool TableFunction(string name, string prefix)
            => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && TypeNames.ContainsKey(name[prefix.Length..]);

        // ------------------------------------------------------------------
        // Functions (EF): no memory; EN = 0 drives their output links to 0.
        // ------------------------------------------------------------------

        private abstract class Function : PlcBlockBehavior
        {
            public override bool IsFunctionBlock => false;

            /// <summary>The input values in pin order.</summary>
            protected static ReadOnlySpan<PlcValue> Inputs(PlcCall call) => call.Inputs;

            /// <summary>The input values in pin order, leaving out the pin named <paramref name="except"/>.</summary>
            protected static PlcValue[] InputsExcept(PlcCall call, string except)
            {
                var skip = -1;
                for (var i = 0; i < call.InputCount && skip < 0; i++)
                {
                    if (string.Equals(except, call.InputName(i), StringComparison.OrdinalIgnoreCase)) skip = i;
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
                var inputs = InputsExcept(call, "K");
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
                    Kind.Rol => ((value << (n % width)) | (value >> ((width - (n % width)) % width))) & mask,
                    _ => ((value >> (n % width)) | (value << ((width - (n % width)) % width))) & mask
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

        /// <summary>BIT0 ... BIT31, interned so pin lookups match by reference.</summary>
        private static readonly string[] BitPinNames = Enumerable.Range(0, 32).Select(i => string.Intern("BIT" + i)).ToArray();

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
                    if (call.Input(BitPinNames[bit]).AsBool()) result |= 1L << bit;
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
                    call.SetOutput(BitPinNames[bit], PlcOps.FromBool((value & (1L << bit)) != 0));
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

        /// <summary>LENGTH_AR*: the number of elements of the table at IN.</summary>
        private sealed class TableLengthFunction : Function
        {
            public override void Execute(PlcCall call)
            {
                if (call.Input("IN").Type is not { Kind: PlcTypeKind.Array } table)
                {
                    call.Fail();
                    return;
                }
                call.SetOutput("OUT", PlcValue.FromInteger(PlcType.Int, table.Length));
            }
        }

        /// <summary>SUM_AR*: the sum of the elements of the table at IN.</summary>
        private sealed class TableSumFunction : Function
        {
            private readonly PlcType _type;
            public TableSumFunction(PlcType type) => _type = type;

            public override void Execute(PlcCall call)
            {
                var input = call.Input("IN");
                if (input.Type is not { Kind: PlcTypeKind.Array, ElementType: { } element } table)
                {
                    call.Fail();
                    return;
                }
                var bytes = input.AsBytes();
                var sum = PlcValue.DefaultOf(_type);
                for (var i = 0; i < table.Length; i++)
                {
                    sum = PlcOps.Add(_type, sum, PlcValue.Decode(element, bytes.AsSpan(i * element.Size, element.Size)));
                }
                call.SetOutput("OUT", sum);
            }
        }

        // ------------------------------------------------------------------
        // Standard library, as the Standard Block Library manual describes it:
        // Schneider Electric 33002519.20, 09/2020.
        // ------------------------------------------------------------------

        /// <summary>
        /// FIND_EQ_AR*, FIND_GT_AR*, FIND_LT_AR*: OUT is the rank of the first element of the
        /// table on IN1 equal to, greater than or less than IN2, -1 if there is none. The rank
        /// counts from 0 at the start of the table, whatever its declared bounds. An invalid
        /// REAL gives -2 and sets %S18 (pp. 52, 63-78).
        /// </summary>
        private sealed class TableFindFunction : Function
        {
            public enum Kind { Eq, Gt, Lt }

            private readonly Kind _kind;
            public TableFindFunction(Kind kind) => _kind = kind;

            public override void Execute(PlcCall call)
            {
                var input = call.Input("IN1");
                if (input.Type is not { Kind: PlcTypeKind.Array, ElementType: { } element } table)
                {
                    call.Fail();
                    return;
                }

                var value = PlcOps.Convert(call.Input("IN2"), element);
                var invalid = element.IsReal && !IsFinite(value.AsDouble());
                var bytes = input.AsBytes();
                var rank = -1;
                for (var i = 0; i < table.Length && !invalid; i++)
                {
                    var item = PlcValue.Decode(element, bytes.AsSpan(i * element.Size, element.Size));
                    if (element.IsReal && !IsFinite(item.AsDouble()))
                    {
                        invalid = true;
                        break;
                    }
                    var order = PlcOps.Compare(element, item, value);
                    if (_kind == Kind.Eq ? order == 0 : _kind == Kind.Gt ? order > 0 : order < 0)
                    {
                        rank = i;
                        break;
                    }
                }

                if (invalid)
                {
                    rank = -2;
                    call.Overflow = true;
                }
                call.SetOutput("OUT", PlcValue.FromInteger(PlcType.Int, rank));
            }
        }

        /// <summary>
        /// ROL_AR* / ROR_AR*: the elements of the table on INOUT move N places towards the
        /// higher (ROL) or lower (ROR) indexes, those pushed off one end coming back at the
        /// other; N of 0 or less leaves the table as it is (pp. 107-110).
        /// </summary>
        private sealed class TableRotateFunction : Function
        {
            private readonly bool _left;
            public TableRotateFunction(bool left) => _left = left;

            public override void Execute(PlcCall call)
            {
                var input = call.Input("INOUT");
                if (input.Type is not { Kind: PlcTypeKind.Array, ElementType: { } element } table || table.Length == 0)
                {
                    call.Fail();
                    return;
                }

                var shift = PlcOps.Convert(call.Input("N"), PlcType.Int).AsInteger();
                if (shift <= 0) return;

                var length = table.Length;
                var places = (int)(shift % length);
                if (!_left) places = (length - places) % length;
                var bytes = input.AsBytes();
                var rotated = (byte[])bytes.Clone();
                for (var i = 0; i < length; i++)
                {
                    bytes.AsSpan(i * element.Size, element.Size).CopyTo(rotated.AsSpan(((i + places) % length) * element.Size));
                }
                call.SetOutput("INOUT", PlcValue.FromBytes(table, rotated));
            }
        }

        /// <summary>
        /// INC / DEC: the integer on INOUT goes up (INC) or down (DEC) by one; on overrun it
        /// wraps (32767 + 1 = -32768 for an INT) and %S18 is set (pp. 247 and 261).
        /// </summary>
        private sealed class StepFunction : Function
        {
            private readonly int _step;
            public StepFunction(int step) => _step = step;

            public override void Execute(PlcCall call)
            {
                var value = call.Input("INOUT");
                var type = value.Type is { IsInteger: true } declared ? declared : call.OutputType("INOUT") ?? PlcType.Int;
                var current = PlcOps.Convert(value, type).AsInteger();
                var next = PlcValue.FromInteger(type, current + _step);
                if (next.AsInteger() != current + _step) call.Overflow = true;
                call.SetOutput("INOUT", next);
            }
        }

        /// <summary>
        /// AVE: the mean of X1, X2, ... weighted by K1, K2, ..., given in pairs on K_X1,
        /// K_X2, ... (K1, X1, K2, X2, ...): (K1·X1 + K2·X2 + ...) / (K1 + K2 + ...), integers
        /// dividing without a fraction. An overrun, a zero weight sum or an invalid REAL sets
        /// %S18; an odd number of inputs is an error (pp. 297-299). Integer sums are taken
        /// in 64 bits, so only the result is checked against the type's range.
        /// </summary>
        private sealed class WeightedAverage : Function
        {
            private readonly PlcType? _type;
            public WeightedAverage(PlcType? type) => _type = type;

            public override void Execute(PlcCall call)
            {
                var values = new List<(int Index, PlcValue Value)>();
                for (var i = 0; i < call.InputCount; i++)
                {
                    var name = call.InputName(i);
                    if (name.StartsWith("K_X", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(3), out var index))
                        values.Add((index, call.Input(i)));
                }
                values.Sort((a, b) => a.Index.CompareTo(b.Index));
                if (values.Count == 0 || values.Count % 2 != 0)
                {
                    call.Fail();
                    return;
                }

                var type = _type ?? PlcOps.OperatingType(values.Select(v => v.Value).ToArray());
                if (type.IsReal)
                {
                    double weighted = 0, weights = 0;
                    for (var i = 0; i < values.Count; i += 2)
                    {
                        var k = RealOf(values[i].Value);
                        var x = RealOf(values[i + 1].Value);
                        weighted += k * x;
                        weights += k;
                    }
                    var mean = weighted / weights;
                    if (weights == 0 || !IsFinite(mean))
                    {
                        call.Overflow = true;
                        call.Eno = false;
                        return;
                    }
                    call.SetOutput("Y", PlcValue.FromReal(mean));
                }
                else
                {
                    long weighted = 0, weights = 0;
                    for (var i = 0; i < values.Count; i += 2)
                    {
                        var k = PlcOps.Convert(values[i].Value, type).AsInteger();
                        weighted += k * PlcOps.Convert(values[i + 1].Value, type).AsInteger();
                        weights += k;
                    }
                    if (weights == 0)
                    {
                        call.Overflow = true;
                        call.Eno = false;
                        return;
                    }
                    var mean = weighted / weights;
                    var result = PlcValue.FromInteger(type, mean);
                    if (result.AsInteger() != mean) call.Overflow = true;
                    call.SetOutput("Y", result);
                }
            }
        }

        /// <summary>
        /// EXTRACT: copies CPY_NELEM parts of SRC, starting at part SRC_POS, into DST
        /// starting at part DST_POS; a part is SRC's memory size divided by SRC_NELEM and
        /// positions count from 0. The rest of DST keeps its value. ERR: 0 copied; -1
        /// SRC_NELEM is 0; -2 SRC's size is not a multiple of SRC_NELEM; -3 SRC_POS outside
        /// SRC; -4 DST_POS outside DST (nothing copied for any of these); 1 CPY_NELEM is 0;
        /// 2 the copy was shortened to fit (pp. 187-190).
        /// </summary>
        private sealed class ExtractFunction : Function
        {
            public override bool ReadsOutputs => true;

            public override void Execute(PlcCall call)
            {
                var source = call.Input("SRC");
                var current = call.CurrentOutput("DST");
                if (source.Type is not { } sourceType || (call.OutputType("DST") ?? current.Type) is not { } targetType)
                {
                    call.Fail();
                    return;
                }

                long Count(string pin) => PlcOps.Convert(call.Input(pin), PlcType.Udint).AsInteger();
                var parts = Count("SRC_NELEM");
                var sourcePosition = Count("SRC_POS");
                var targetPosition = Count("DST_POS");
                var copies = Count("CPY_NELEM");

                int error;
                if (parts == 0)
                {
                    error = -1;
                }
                else if (sourceType.Size % parts != 0)
                {
                    error = -2;
                }
                else
                {
                    var partSize = sourceType.Size / parts;
                    if (sourcePosition >= parts)
                    {
                        error = -3;
                    }
                    else if (targetPosition * partSize >= targetType.Size)
                    {
                        error = -4;
                    }
                    else if (copies == 0)
                    {
                        error = 1;
                    }
                    else
                    {
                        var fit = Math.Min(copies, Math.Min(parts - sourcePosition, (targetType.Size - (targetPosition * partSize)) / partSize));
                        var target = PlcValue.Encode(current.HasValue ? current : PlcValue.DefaultOf(targetType), targetType);
                        source.AsBytes().AsSpan((int)(sourcePosition * partSize), (int)(fit * partSize))
                            .CopyTo(target.AsSpan((int)(targetPosition * partSize)));
                        call.SetOutput("DST", PlcValue.Decode(targetType, target));
                        error = fit < copies ? 2 : 0;
                    }
                }
                call.SetOutput("ERR", PlcValue.FromInteger(PlcType.Int, error));
            }
        }

        // ------------------------------------------------------------------
        // Obsolete library, as the Obsolete Block Library manual describes it:
        // Schneider Electric 33002543.19, 10/2019.
        // ------------------------------------------------------------------

        /// <summary>
        /// COMPARE: with VAL = 1, SUP, EQU, INF and DIF tell whether IN1 is greater than,
        /// equal to, less than or different from IN2; with VAL = 0 nothing is compared
        /// (p. 415).
        /// </summary>
        private sealed class CompareIntegers : Function
        {
            public override void Execute(PlcCall call)
            {
                if (!call.Input("VAL").AsBool()) return;
                var order = PlcOps.Convert(call.Input("IN1"), PlcType.Int).AsInteger()
                    .CompareTo(PlcOps.Convert(call.Input("IN2"), PlcType.Int).AsInteger());
                call.SetOutput("SUP", PlcOps.FromBool(order > 0));
                call.SetOutput("EQU", PlcOps.FromBool(order == 0));
                call.SetOutput("INF", PlcOps.FromBool(order < 0));
                call.SetOutput("DIF", PlcOps.FromBool(order != 0));
            }
        }

        private sealed class TableCopyState
        {
            /// <summary>R2T wrote the last element: no more copies until R or OFF changes.</summary>
            public bool Full;
        }

        /// <summary>
        /// R2T_*: each cycle SRC goes into element OFF (from 1) of DEST, read as a table of
        /// SRC's type, and OFF moves on by one unless NOINC = 1. R = 1 sets OFF back to 1
        /// first. Once the last element is written END is 1 and nothing more is copied; an
        /// OFF outside the table also gives END = 1 (pp. 541-546).
        /// </summary>
        private sealed class RegisterToTable : FunctionBlock<TableCopyState>
        {
            private readonly PlcType _element;
            public RegisterToTable(PlcType element) => _element = element;

            public override bool ReadsOutputs => true;

            protected override void Execute(PlcCall call, TableCopyState s)
            {
                var current = call.CurrentOutput("DEST");
                if ((call.OutputType("DEST") ?? current.Type) is not { } tableType || _element.Size == 0)
                {
                    call.Fail();
                    return;
                }

                var offset = PlcOps.Convert(call.Input("OFF"), PlcType.Uint).AsInteger();
                if (call.Input("R").AsBool())
                {
                    offset = 1;
                    s.Full = false;
                }
                var length = tableType.Size / _element.Size;

                bool end;
                if (offset < 1 || offset > length || (s.Full && offset == length))
                {
                    end = true;
                }
                else
                {
                    var bytes = PlcValue.Encode(current.HasValue ? current : PlcValue.DefaultOf(tableType), tableType);
                    PlcValue.Encode(PlcOps.Convert(call.Input("SRC"), _element), _element, bytes.AsSpan((int)((offset - 1) * _element.Size), _element.Size));
                    call.SetOutput("DEST", PlcValue.Decode(tableType, bytes));
                    s.Full = offset == length;
                    end = s.Full;
                    if (!s.Full && !call.Input("NOINC").AsBool()) offset++;
                }
                if (offset != length) s.Full = false;
                call.SetOutput("OFF", PlcValue.FromInteger(PlcType.Uint, offset));
                call.SetOutput("END", PlcOps.FromBool(end));
            }
        }

        /// <summary>
        /// T2T: each cycle SIZE 16-bit words from word OFF (from 1) of SRC go to the same
        /// words of DEST, both read as plain memory, and OFF moves on by SIZE unless NOINC
        /// = 1. R = 1 sets OFF back to 1 first. OFF = 0, or words past the end of either
        /// table, give END = 1 without a copy. ENO is always 1 (pp. 589-594).
        /// </summary>
        private sealed class TableToTable : FunctionBlock<NoState>
        {
            public override bool ReadsOutputs => true;

            protected override void Execute(PlcCall call, NoState s)
            {
                var source = call.Input("SRC");
                var current = call.CurrentOutput("DEST");
                if (source.Type is not { } sourceType || (call.OutputType("DEST") ?? current.Type) is not { } tableType)
                {
                    call.Fail();
                    return;
                }

                var offset = PlcOps.Convert(call.Input("OFF"), PlcType.Uint).AsInteger();
                if (call.Input("R").AsBool()) offset = 1;
                var size = PlcOps.Convert(call.Input("SIZE"), PlcType.Uint).AsInteger();
                var words = Math.Min(sourceType.Size, tableType.Size) / 2;

                bool end;
                if (offset == 0 || offset + size - 1 > words)
                {
                    end = true;
                }
                else
                {
                    var bytes = PlcValue.Encode(current.HasValue ? current : PlcValue.DefaultOf(tableType), tableType);
                    source.AsBytes().AsSpan((int)((offset - 1) * 2), (int)(size * 2)).CopyTo(bytes.AsSpan((int)((offset - 1) * 2)));
                    call.SetOutput("DEST", PlcValue.Decode(tableType, bytes));
                    end = false;
                    if (!call.Input("NOINC").AsBool()) offset += size;
                }
                call.SetOutput("OFF", PlcValue.FromInteger(PlcType.Uint, offset));
                call.SetOutput("END", PlcOps.FromBool(end));
            }
        }

        private sealed class PwmState
        {
            public bool Started;
            public long PeriodStart;
            public long Period;
            public long On;
            public int Direction;
            public long Pause;
            public long Brake;
        }

        /// <summary>
        /// PWM: each period t_period starts with a pulse on Y_POS (X &gt; 0) or Y_NEG (X &lt; 0)
        /// of t_period · |X| / |up_pos| (or |up_neg|), so the mean output follows X; a pulse
        /// longer than t_max is cut to t_max, one shorter than t_min is left out. After the
        /// pulse and t_pause, the other output gives a brake pulse of t_brake. R = 1 resets.
        /// The pulse is worked out at the start of each period (pp. 339-347; the pulse length
        /// formulas are figures, this follows the text: "the adjusted average energy
        /// corresponds to the quotient of the switch on duration T_on and the cycle time").
        /// </summary>
        private sealed class PulseWidthModulation : FunctionBlock<PwmState>
        {
            protected override void Execute(PlcCall call, PwmState s)
            {
                var para = call.Input("PARA");
                long Time(string field) => Math.Max(0, PlcOps.Convert(Field(para, field), PlcType.Time).AsInteger());

                if (call.Input("R").AsBool() || call.ColdStart)
                {
                    s.Started = false;
                    call.SetOutput("Y_POS", PlcOps.False);
                    call.SetOutput("Y_NEG", PlcOps.False);
                    if (call.Input("R").AsBool()) return;
                }

                if (!s.Started || s.Period <= 0 || call.Now - s.PeriodStart >= s.Period)
                {
                    s.PeriodStart = s.Started && s.Period > 0 ? s.PeriodStart + ((call.Now - s.PeriodStart) / s.Period * s.Period) : call.Now;
                    s.Started = true;
                    s.Period = Time("t_period");
                    s.Pause = Time("t_pause");
                    s.Brake = Time("t_brake");

                    var x = RealInput(call, "X");
                    var limit = Math.Abs(RealOf(Field(para, x >= 0 ? "up_pos" : "up_neg")));
                    var on = limit > 0 && IsFinite(x) ? Math.Abs(x) / limit * s.Period : 0;
                    on = Math.Min(on, Math.Min(Time("t_max"), s.Period));
                    s.On = on < Time("t_min") ? 0 : (long)Math.Round(on);
                    s.Direction = s.On == 0 ? 0 : Math.Sign(x);
                }

                var elapsed = call.Now - s.PeriodStart;
                var pulse = s.Direction != 0 && elapsed < s.On;
                var brake = s.Direction != 0 && s.Brake > 0 && elapsed >= s.On + s.Pause && elapsed < s.On + s.Pause + s.Brake;
                call.SetOutput("Y_POS", PlcOps.FromBool(s.Direction > 0 ? pulse : brake));
                call.SetOutput("Y_NEG", PlcOps.FromBool(s.Direction < 0 ? pulse : brake));
            }
        }

        private sealed class FgenState
        {
            public bool PreviousStart;
            public bool Running;
            public long Start;
            public int Count;

            // The function and its parameters, taken at the start of each cycle.
            public int Function;
            public double Amplitude;
            public long HalfPeriod;
            public long Idle;
            public long Rise;
            public bool Unipolar;

            public readonly Random Random = new();
        }

        /// <summary>
        /// FGEN: a function generator; Y = YOFF + the waveform func_no of Para_FGEN (pp.
        /// 189-203). R = 1 stops it with Y = YOFF, N = 0, ACTIVE = 0. Jump (1), ramp (2) and
        /// random number (8) follow START as the text describes (p. 202). The periodic
        /// functions run while START = 1, finishing the cycle under way when it drops; N
        /// counts the cycles, and the function and its parameters change only between cycles.
        /// A cycle is two half waves of halfperiod (the second negative unless unipolar),
        /// each drawn over halfperiod - t_off and then idle for t_off: saw-tooth (3) rises
        /// over that time, delta (4) rises over half of it and falls back, square (5) holds
        /// the amplitude, trapezoid (6) rises and falls over t_rise, sine (7) is a half sine.
        /// The waveform figures (pp. 198-201) are images; the shapes above follow the text,
        /// and the S-curve smoothing of t_acc is not applied.
        /// </summary>
        private sealed class FunctionGenerator : FunctionBlock<FgenState>
        {
            private const int Jump = 1, Ramp = 2, SawTooth = 3, Delta = 4, Square = 5, Trapezoid = 6, Sine = 7, RandomNumber = 8;

            protected override void Execute(PlcCall call, FgenState s)
            {
                var offset = RealInput(call, "YOFF");
                var para = call.Input("PARA");
                var start = call.Input("START").AsBool();
                var rising = start && !s.PreviousStart;
                s.PreviousStart = start;

                if (call.Input("R").AsBool() || call.ColdStart)
                {
                    s.Running = false;
                    s.Count = 0;
                    if (call.Input("R").AsBool())
                    {
                        Output(call, offset, false, 0);
                        return;
                    }
                }

                var function = (int)PlcOps.Convert(Field(para, "func_no"), PlcType.Int).AsInteger();
                if (!s.Running && function is Jump or Ramp or RandomNumber) Latch(s, para, function);

                switch (s.Running ? s.Function : function)
                {
                    case Jump:
                        if (rising) s.Count++;
                        Output(call, offset + (start ? s.Amplitude : 0), start, s.Count);
                        return;

                    case Ramp:
                        if (rising)
                        {
                            s.Count++;
                            s.Start = call.Now;
                        }
                        var rise = s.Rise <= 0 ? 1 : Math.Min(1.0, (call.Now - s.Start) / (double)s.Rise);
                        Output(call, offset + (start ? s.Amplitude * rise : 0), start, s.Count);
                        return;

                    case RandomNumber:
                        if (rising) s.Count++;
                        var low = s.Unipolar ? 0 : -s.Amplitude;
                        Output(call, offset + (start ? low + (s.Random.NextDouble() * (s.Amplitude - low)) : 0), start, s.Count);
                        return;
                }

                if (!s.Running && start)
                {
                    if (function != s.Function) s.Count = 0;
                    Latch(s, para, function);
                    s.Running = true;
                    s.Start = call.Now;
                    s.Count++;
                }
                else if (s.Running && call.Now - s.Start >= 2 * s.HalfPeriod)
                {
                    if (start && s.HalfPeriod > 0)
                    {
                        s.Start += (call.Now - s.Start) / (2 * s.HalfPeriod) * (2 * s.HalfPeriod);
                        if (function != s.Function) s.Count = 0;
                        Latch(s, para, function);
                        s.Count++;
                    }
                    else
                    {
                        s.Running = false;
                    }
                }

                Output(call, s.Running ? offset + Wave(s, call.Now - s.Start) : offset, s.Running, s.Count);
            }

            private static void Latch(FgenState s, PlcValue para, int function)
            {
                s.Function = function;
                s.Amplitude = RealOf(Field(para, "amplitude"));
                s.HalfPeriod = Math.Max(0, PlcOps.Convert(Field(para, "halfperiod"), PlcType.Time).AsInteger());
                s.Idle = Math.Max(0, PlcOps.Convert(Field(para, "t_off"), PlcType.Time).AsInteger());
                s.Rise = Math.Max(0, PlcOps.Convert(Field(para, "t_rise"), PlcType.Time).AsInteger());
                s.Unipolar = Field(para, "unipolar").AsBool() || function is Jump or Ramp;
            }

            private static double Wave(FgenState s, long time)
            {
                if (s.HalfPeriod <= 0) return 0;
                var second = time >= s.HalfPeriod;
                var within = time - (second ? s.HalfPeriod : 0);
                var drawn = s.HalfPeriod - s.Idle;
                if (drawn <= 0 || within >= drawn) return 0;

                var u = within / (double)drawn;
                var rise = Math.Min(0.5, s.Rise / (double)drawn);
                var shape = s.Function switch
                {
                    SawTooth => u,
                    Delta => u < 0.5 ? 2 * u : 2 * (1 - u),
                    Square => 1,
                    Trapezoid => rise <= 0 ? 1 : u < rise ? u / rise : u > 1 - rise ? (1 - u) / rise : 1,
                    Sine => Math.Sin(Math.PI * u),
                    _ => 0
                };
                return shape * s.Amplitude * (second && !s.Unipolar ? -1 : 1);
            }

            private static void Output(PlcCall call, double y, bool active, int count)
            {
                call.SetOutput("Y", PlcValue.FromReal(y));
                call.SetOutput("ACTIVE", PlcOps.FromBool(active));
                call.SetOutput("N", PlcValue.FromInteger(PlcType.Int, count));
            }
        }

        /// <summary>MOVE_*_AR*: every element of the table written to OUT gets IN.</summary>
        private sealed class TableFillFunction : Function
        {
            private readonly PlcType _type;
            public TableFillFunction(PlcType type) => _type = type;

            public override void Execute(PlcCall call)
            {
                if (call.OutputType("OUT") is not { Kind: PlcTypeKind.Array, ElementType: { } element } table)
                {
                    call.Fail();
                    return;
                }
                var one = PlcValue.Encode(PlcOps.Convert(call.Input("IN"), _type), element);
                var bytes = new byte[table.Size];
                for (var i = 0; i < table.Length; i++) one.CopyTo(bytes, i * element.Size);
                call.SetOutput("OUT", PlcValue.FromBytes(table, bytes));
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

        // ------------------------------------------------------------------
        // Control library (CONT_CTL), as the Control Block Library manual describes
        // it: Schneider Electric 33002535.16, 09/2020.
        // ------------------------------------------------------------------

        // Status word bits the Control library blocks share ("Common Floating Point
        // Errors"); with these ENO is 0 and the other outputs keep their values.
        private const int StatusInvalidInput = 0x0002;

        // Block-specific status bits reported with ENO = 1.
        private const int StatusWarning = 0x0010;
        private const int StatusEmptyRange = 0x0080;

        private static double RealInput(PlcCall call, string pin) => RealOf(call.Input(pin));

        private static double RealOf(PlcValue value) => PlcOps.Convert(value, PlcType.Real).AsDouble();

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>A field of a structured value (a PARA parameter); "no value" when the type has no such field.</summary>
        private static PlcValue Field(PlcValue structure, string name)
        {
            if (structure.Type?.FindField(name) is not { } field) return default;
            var bytes = structure.AsBytes();
            return field.Offset + field.Type.Size <= bytes.Length
                ? PlcValue.Decode(field.Type, bytes.AsSpan(field.Offset, field.Type.Size))
                : default;
        }

        private static void SetStatus(PlcCall call, int status) => call.SetOutput("STATUS", PlcValue.FromInteger(PlcType.Word, status));

        private sealed class DeadBandState
        {
            /// <summary>The last result: 1 greater, 0 equal, -1 less; null before the first call.</summary>
            public int? Last;
        }

        /// <summary>
        /// COMP_DB: GREATER, EQUAL or LESS for IN1 against IN2, where a deviation within the
        /// dead zone DBAND counts as equal. Coming back from GREATER or LESS, EQUAL needs the
        /// deviation within DBAND - HYST (pp. 212-215). The boundary itself counts as inside,
        /// so DBAND = HYST = 0 is the classic comparison the manual describes.
        /// </summary>
        private sealed class DeadBandComparison : FunctionBlock<DeadBandState>
        {
            protected override void Execute(PlcCall call, DeadBandState s)
            {
                var in1 = RealInput(call, "IN1");
                var in2 = RealInput(call, "IN2");
                var band = RealInput(call, "DBAND");
                var hysteresis = RealInput(call, "HYST");
                if (!IsFinite(in1) || !IsFinite(in2) || !IsFinite(band) || !IsFinite(hysteresis))
                {
                    call.Eno = false; // the outputs keep their values
                    return;
                }

                // Warnings: a negative DBAND counts as 0; HYST is brought into [0, DBAND].
                band = Math.Max(0, band);
                hysteresis = Math.Clamp(hysteresis, 0, band);

                var deviation = in1 - in2;
                var limit = s.Last is 1 or -1 ? band - hysteresis : band;
                var result = Math.Abs(deviation) <= limit ? 0 : Math.Sign(deviation);
                s.Last = result;
                call.SetOutput("GREATER", PlcOps.FromBool(result > 0));
                call.SetOutput("EQUAL", PlcOps.FromBool(result == 0));
                call.SetOutput("LESS", PlcOps.FromBool(result < 0));
            }
        }

        private sealed class AverageState
        {
            public float[] Window = Array.Empty<float>();
            public int Count;
            public int Next;
            public bool Started;
        }

        /// <summary>
        /// AVGMV: Y is the mean of the last N values of X (N from 1 to 50) and RDY is 1 once
        /// N values are in. A cold or warm start or a new N restarts the window from X;
        /// MAN = 1 outputs YMAN and fills the window with it (pp. 232-235).
        /// </summary>
        private sealed class MovingAverage : FunctionBlock<AverageState>
        {
            private const int MaxWindow = 50;

            protected override void Execute(PlcCall call, AverageState s)
            {
                var n = PlcOps.Convert(call.Input("N"), PlcType.Int).AsInteger();
                if (n < 1 || n > MaxWindow)
                {
                    call.Eno = false; // "N=0 or N>50": an error, outputs unchanged
                    return;
                }
                var size = (int)n;

                if (call.Input("MAN").AsBool())
                {
                    var manual = (float)RealInput(call, "YMAN");
                    s.Window = Enumerable.Repeat(manual, size).ToArray();
                    s.Count = size;
                    s.Next = 0;
                    s.Started = true;
                    call.SetOutput("Y", PlcValue.FromReal(manual));
                    call.SetOutput("RDY", PlcOps.True);
                    return;
                }

                if (!s.Started || s.Window.Length != size || call.ColdStart || call.FirstTaskCycle)
                {
                    s.Window = new float[size];
                    s.Count = 0;
                    s.Next = 0;
                    s.Started = true;
                }

                s.Window[s.Next] = (float)RealInput(call, "X");
                s.Next = (s.Next + 1) % size;
                if (s.Count < size) s.Count++;

                var sum = 0f;
                for (var i = 0; i < s.Count; i++) sum += s.Window[i];
                call.SetOutput("Y", PlcValue.FromReal(sum / s.Count));
                call.SetOutput("RDY", PlcOps.FromBool(s.Count == size));
            }
        }

        /// <summary>
        /// LOOKUP_TABLE1: Y from X by linear interpolation between the support points
        /// (X1, Y1), (X2, Y2), ... given in pairs on XIYI1, XIYI2, ... in ascending X. Below
        /// X1, Y = Y1 and QXLO = 1; above the last point, Y = Ym and QXHI = 1; on a vertical
        /// step (two equal X at X) Y is the midpoint (pp. 250-253).
        /// </summary>
        private sealed class LookupTable : Function
        {
            public override void Execute(PlcCall call)
            {
                var x = RealInput(call, "X");
                var values = new List<(int Index, double Value)>();
                for (var i = 0; i < call.InputCount; i++)
                {
                    var name = call.InputName(i);
                    if (name.StartsWith("XIYI", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(name.AsSpan(4), out var index))
                    {
                        values.Add((index, RealOf(call.Input(i))));
                    }
                }
                values.Sort((a, b) => a.Index.CompareTo(b.Index));
                var count = values.Count / 2; // an odd last input has no Y
                if (count == 0)
                {
                    call.Fail();
                    return;
                }

                double X(int i) => values[2 * i].Value;
                double Y(int i) => values[(2 * i) + 1].Value;

                double y;
                var below = x < X(0);
                var above = x > X(count - 1);
                if (below)
                {
                    y = Y(0);
                }
                else if (above)
                {
                    y = Y(count - 1);
                }
                else
                {
                    y = Y(count - 1);
                    for (var i = 0; i + 1 < count; i++)
                    {
                        if (X(i) == x && X(i + 1) == x)
                        {
                            y = 0.5 * (Y(i) + Y(i + 1));
                            break;
                        }
                        if (X(i) <= x && x <= X(i + 1) && X(i) < X(i + 1))
                        {
                            y = Y(i) + ((Y(i + 1) - Y(i)) * (x - X(i)) / (X(i + 1) - X(i)));
                            // At a segment's end a step at the same X may follow; let it decide.
                            if (x < X(i + 1)) break;
                        }
                    }
                }

                call.SetOutput("Y", PlcValue.FromReal(y));
                call.SetOutput("QXHI", PlcOps.FromBool(above));
                call.SetOutput("QXLO", PlcOps.FromBool(below));
            }
        }

        /// <summary>
        /// SCALING: OUT maps IN from [in_min, in_max] onto [out_min, out_max] (out_max may be
        /// below out_min). With clip = 1, IN is held inside its range (status 16#0010). With
        /// in_min = in_max, OUT keeps its value (status 16#0080) (pp. 92-95).
        /// </summary>
        private sealed class Scaling : FunctionBlock<NoState>
        {
            protected override void Execute(PlcCall call, NoState s)
            {
                var para = call.Input("PARA");
                var input = RealInput(call, "IN");
                var inMin = RealOf(Field(para, "in_min"));
                var inMax = RealOf(Field(para, "in_max"));
                var outMin = RealOf(Field(para, "out_min"));
                var outMax = RealOf(Field(para, "out_max"));
                if (!IsFinite(input) || !IsFinite(inMin) || !IsFinite(inMax) || !IsFinite(outMin) || !IsFinite(outMax))
                {
                    SetStatus(call, StatusInvalidInput);
                    call.Eno = false;
                    return;
                }
                if (inMin == inMax)
                {
                    SetStatus(call, StatusEmptyRange);
                    return;
                }

                var status = 0;
                if (Field(para, "clip").AsBool())
                {
                    var clamped = Math.Clamp(input, Math.Min(inMin, inMax), Math.Max(inMin, inMax));
                    if (clamped != input) status |= StatusWarning;
                    input = clamped;
                }
                call.SetOutput("OUT", PlcValue.FromReal(outMin + ((input - inMin) * (outMax - outMin) / (inMax - inMin))));
                SetStatus(call, status);
            }
        }

        private sealed class RampState
        {
            public bool Started;
            public long Last;
            public double Setpoint;
            public bool Done;
        }

        /// <summary>
        /// RAMP: SP moves towards RSP at inc_rate (up) or dec_rate (down) units per second,
        /// or jumps there when that rate is 0; DONE is 1 once SP has reached RSP. TR_S = 1
        /// tracks: SP = TR_I with DONE = 0. A negative rate counts as 0 (status 16#0010)
        /// (pp. 330-334). The time step is the controller time since the previous call.
        /// </summary>
        private sealed class Ramp : FunctionBlock<RampState>
        {
            protected override void Execute(PlcCall call, RampState s)
            {
                var para = call.Input("PARA");
                var target = RealInput(call, "RSP");
                var initial = RealInput(call, "TR_I");
                var increase = RealOf(Field(para, "inc_rate"));
                var decrease = RealOf(Field(para, "dec_rate"));
                if (!IsFinite(target) || !IsFinite(initial) || !IsFinite(increase) || !IsFinite(decrease))
                {
                    SetStatus(call, StatusInvalidInput);
                    call.Eno = false; // SP and DONE keep their values
                    return;
                }

                var status = 0;
                if (increase < 0 || decrease < 0) status |= StatusWarning;
                increase = Math.Max(0, increase);
                decrease = Math.Max(0, decrease);

                var seconds = s.Started ? Math.Max(0, call.Now - s.Last) / 1000.0 : 0;
                s.Started = true;
                s.Last = call.Now;

                if (call.Input("TR_S").AsBool())
                {
                    s.Setpoint = initial;
                    s.Done = false;
                }
                else
                {
                    if (s.Setpoint < target)
                        s.Setpoint = increase == 0 ? target : Math.Min(target, s.Setpoint + (increase * seconds));
                    else if (s.Setpoint > target)
                        s.Setpoint = decrease == 0 ? target : Math.Max(target, s.Setpoint - (decrease * seconds));
                    s.Done = s.Setpoint == target;
                }

                call.SetOutput("SP", PlcValue.FromReal(s.Setpoint));
                call.SetOutput("DONE", PlcOps.FromBool(s.Done));
                SetStatus(call, status);
            }
        }
    }
}
