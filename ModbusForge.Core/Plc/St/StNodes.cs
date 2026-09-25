using System;
using System.Collections.Generic;
using System.Linq;

namespace ModbusForge.Core.Plc.St
{
    /// <summary>How a statement finished: normally, or by EXIT (leave the loop) or RETURN (leave the code).</summary>
    public enum StFlow
    {
        Normal,
        Exit,
        Return
    }

    public abstract class StStatement
    {
        protected StStatement(int line) => Line = line;

        public int Line { get; }

        public abstract StFlow Execute(PlcRuntime runtime);
    }

    /// <summary>A compiled piece of Structured Text: an ST section or the body of an ST DFB.</summary>
    public sealed class StProgram
    {
        public StProgram(IReadOnlyList<StStatement> statements, IReadOnlyList<string> problems)
        {
            Statements = statements;
            Problems = problems;
        }

        public IReadOnlyList<StStatement> Statements { get; }

        /// <summary>What could not be compiled (those statements are left out).</summary>
        public IReadOnlyList<string> Problems { get; }

        public void Execute(PlcRuntime runtime) => StBlock.Run(Statements, runtime);
    }

    internal static class StBlock
    {
        public static StFlow Run(IReadOnlyList<StStatement> statements, PlcRuntime runtime)
        {
            for (var i = 0; i < statements.Count; i++)
            {
                var flow = statements[i].Execute(runtime);
                if (flow != StFlow.Normal) return flow;
            }
            return StFlow.Normal;
        }
    }

    internal sealed class StAssignment : StStatement
    {
        private readonly PlcOperand _target;
        private readonly PlcOperand _value;

        public StAssignment(PlcOperand target, PlcOperand value, int line) : base(line)
        {
            _target = target;
            _value = value;
        }

        public override StFlow Execute(PlcRuntime runtime)
        {
            _target.Write(runtime, _value.Read(runtime));
            return StFlow.Normal;
        }
    }

    internal sealed class StIf : StStatement
    {
        private readonly (PlcOperand Condition, IReadOnlyList<StStatement> Body)[] _branches;
        private readonly IReadOnlyList<StStatement>? _else;

        public StIf(IReadOnlyList<(PlcOperand, IReadOnlyList<StStatement>)> branches, IReadOnlyList<StStatement>? otherwise, int line) : base(line)
        {
            _branches = branches.ToArray();
            _else = otherwise;
        }

        public override StFlow Execute(PlcRuntime runtime)
        {
            for (var i = 0; i < _branches.Length; i++)
            {
                if (_branches[i].Condition.Read(runtime).AsBool()) return StBlock.Run(_branches[i].Body, runtime);
            }
            return _else != null ? StBlock.Run(_else, runtime) : StFlow.Normal;
        }
    }

    internal sealed class StCase : StStatement
    {
        private readonly PlcOperand _selector;
        private readonly ((long Low, long High)[] Labels, IReadOnlyList<StStatement> Body)[] _cases;
        private readonly IReadOnlyList<StStatement>? _else;

        public StCase(PlcOperand selector, IReadOnlyList<(IReadOnlyList<(long, long)>, IReadOnlyList<StStatement>)> cases,
            IReadOnlyList<StStatement>? otherwise, int line) : base(line)
        {
            _selector = selector;
            _cases = cases.Select(c => (c.Item1.ToArray(), c.Item2)).ToArray();
            _else = otherwise;
        }

        public override StFlow Execute(PlcRuntime runtime)
        {
            var value = _selector.Read(runtime).AsInteger();
            foreach (var (labels, body) in _cases)
            {
                foreach (var (low, high) in labels)
                {
                    if (value >= low && value <= high) return StBlock.Run(body, runtime);
                }
            }
            return _else != null ? StBlock.Run(_else, runtime) : StFlow.Normal;
        }
    }

    /// <summary>
    /// A loop is cut off after this many passes in one execution, so a loop that never
    /// ends cannot hang the scan (the controller's watchdog would stop the task).
    /// </summary>
    internal static class StLoopGuard
    {
        public const int MaxIterations = 100_000;
    }

    internal sealed class StFor : StStatement
    {
        private readonly PlcOperand _variable;
        private readonly PlcOperand _start;
        private readonly PlcOperand _end;
        private readonly PlcOperand? _step;
        private readonly IReadOnlyList<StStatement> _body;

        public StFor(PlcOperand variable, PlcOperand start, PlcOperand end, PlcOperand? step, IReadOnlyList<StStatement> body, int line)
            : base(line)
        {
            _variable = variable;
            _start = start;
            _end = end;
            _step = step;
            _body = body;
        }

        public override StFlow Execute(PlcRuntime runtime)
        {
            // IEC 61131-3: the bounds and the step are evaluated once, before the loop.
            var end = _end.Read(runtime).AsInteger();
            var step = _step?.Read(runtime).AsInteger() ?? 1;
            if (step == 0)
            {
                runtime.CountStError();
                return StFlow.Normal;
            }

            _variable.Write(runtime, _start.Read(runtime));
            for (var pass = 0; ; pass++)
            {
                var i = _variable.Read(runtime).AsInteger();
                if (step > 0 ? i > end : i < end) return StFlow.Normal;
                if (pass >= StLoopGuard.MaxIterations)
                {
                    runtime.CountStError();
                    return StFlow.Normal;
                }

                var flow = StBlock.Run(_body, runtime);
                if (flow == StFlow.Exit) return StFlow.Normal;
                if (flow == StFlow.Return) return flow;
                _variable.Write(runtime, PlcValue.FromInteger(PlcType.AnyInteger, _variable.Read(runtime).AsInteger() + step));
            }
        }
    }

    internal sealed class StWhile : StStatement
    {
        private readonly PlcOperand _condition;
        private readonly IReadOnlyList<StStatement> _body;
        private readonly bool _repeat;

        /// <summary>WHILE cond DO body; or, with <paramref name="repeat"/>, REPEAT body UNTIL cond.</summary>
        public StWhile(PlcOperand condition, IReadOnlyList<StStatement> body, bool repeat, int line) : base(line)
        {
            _condition = condition;
            _body = body;
            _repeat = repeat;
        }

        public override StFlow Execute(PlcRuntime runtime)
        {
            for (var pass = 0; ; pass++)
            {
                if (!_repeat && !_condition.Read(runtime).AsBool()) return StFlow.Normal;
                if (pass >= StLoopGuard.MaxIterations)
                {
                    runtime.CountStError();
                    return StFlow.Normal;
                }

                var flow = StBlock.Run(_body, runtime);
                if (flow == StFlow.Exit) return StFlow.Normal;
                if (flow == StFlow.Return) return flow;
                if (_repeat && _condition.Read(runtime).AsBool()) return StFlow.Normal;
            }
        }
    }

    internal sealed class StJump : StStatement
    {
        private readonly StFlow _flow;

        public StJump(StFlow flow, int line) : base(line) => _flow = flow;

        public override StFlow Execute(PlcRuntime runtime) => _flow;
    }

    internal sealed class StCallStatement : StStatement
    {
        private readonly StCall _call;

        public StCallStatement(StCall call, int line) : base(line) => _call = call;

        public override StFlow Execute(PlcRuntime runtime)
        {
            _call.Invoke(runtime);
            return StFlow.Normal;
        }
    }

    // ------------------------------------------------------------------
    // Expressions
    // ------------------------------------------------------------------

    internal enum StOperator
    {
        Or,
        Xor,
        And,
        Equal,
        NotEqual,
        Less,
        Greater,
        LessOrEqual,
        GreaterOrEqual,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        Power
    }

    internal sealed class StBinary : PlcOperand
    {
        private readonly StOperator _operator;
        private readonly PlcOperand _left;
        private readonly PlcOperand _right;

        public StBinary(StOperator op, PlcOperand left, PlcOperand right, string text) : base(text)
        {
            _operator = op;
            _left = left;
            _right = right;
        }

        public override PlcValue Read(PlcRuntime runtime)
        {
            var a = _left.Read(runtime);
            var b = _right.Read(runtime);
            var type = PlcOps.OperatingType(a, b);
            switch (_operator)
            {
                case StOperator.Or: return PlcOps.Or(type, a, b);
                case StOperator.Xor: return PlcOps.Xor(type, a, b);
                case StOperator.And: return PlcOps.And(type, a, b);
                case StOperator.Equal: return PlcOps.FromBool(PlcOps.Compare(type, a, b) == 0);
                case StOperator.NotEqual: return PlcOps.FromBool(PlcOps.Compare(type, a, b) != 0);
                case StOperator.Less: return PlcOps.FromBool(PlcOps.Compare(type, a, b) < 0);
                case StOperator.Greater: return PlcOps.FromBool(PlcOps.Compare(type, a, b) > 0);
                case StOperator.LessOrEqual: return PlcOps.FromBool(PlcOps.Compare(type, a, b) <= 0);
                case StOperator.GreaterOrEqual: return PlcOps.FromBool(PlcOps.Compare(type, a, b) >= 0);
                case StOperator.Add: return PlcOps.Add(type, a, b);
                case StOperator.Subtract: return PlcOps.Subtract(type, a, b);
                case StOperator.Multiply: return PlcOps.Multiply(type, a, b);
                case StOperator.Divide:
                    if (PlcOps.Divide(type, a, b) is { } quotient) return quotient;
                    runtime.CountStError();
                    return PlcValue.DefaultOf(type);
                case StOperator.Modulo:
                    if (PlcOps.Modulo(type, a, b) is { } remainder) return remainder;
                    runtime.CountStError();
                    return PlcValue.DefaultOf(type);
                default:
                    return PlcValue.FromReal(Math.Pow(a.AsDouble(), b.AsDouble()));
            }
        }
    }

    internal sealed class StNegate : PlcOperand
    {
        private readonly PlcOperand _operand;

        public StNegate(PlcOperand operand, string text) : base(text) => _operand = operand;

        public override PlcValue Read(PlcRuntime runtime)
        {
            var value = _operand.Read(runtime);
            var type = PlcOps.OperatingType(value);
            return PlcOps.Subtract(type, PlcValue.DefaultOf(type), value);
        }
    }

    internal sealed class StNot : PlcOperand
    {
        private readonly PlcOperand _operand;

        public StNot(PlcOperand operand, string text) : base(text) => _operand = operand;

        public override PlcValue Read(PlcRuntime runtime)
        {
            var value = _operand.Read(runtime);
            return PlcOps.Not(PlcOps.OperatingType(value), value);
        }
    }

    /// <summary>
    /// RE/FE on an EBOOL: TRUE while the value bit differs from the history bit the
    /// last write left (Control Expert keeps the previous value in the EBOOL's H bit).
    /// </summary>
    internal sealed class StEdge : PlcOperand
    {
        private readonly PlcReferenceOperand _operand;
        private readonly bool _rising;

        public StEdge(PlcReferenceOperand operand, bool rising, string text) : base(text)
        {
            _operand = operand;
            _rising = rising;
        }

        public override PlcType? StaticType => PlcType.Bool;

        public override PlcValue Read(PlcRuntime runtime)
        {
            var location = _operand.Resolve(runtime);
            if (!location.IsValid) return PlcOps.False;
            var (value, history) = runtime.Memory.ReadEdge(location);
            return PlcOps.FromBool(_rising ? value && !history : !value && history);
        }
    }

    /// <summary>A function call used as a value: the function's OUT (or first) output.</summary>
    internal sealed class StCallExpression : PlcOperand
    {
        private readonly StCall _call;

        public StCallExpression(StCall call, string text) : base(text) => _call = call;

        public override PlcValue Read(PlcRuntime runtime) => _call.Invoke(runtime);
    }
}
