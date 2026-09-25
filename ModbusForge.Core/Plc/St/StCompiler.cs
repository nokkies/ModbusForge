using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ModbusForge.Core.Plc.St
{
    /// <summary>
    /// Compiles IEC 61131-3 Structured Text (as Control Expert exports it) into
    /// executable statements bound to a scope: the project's variables for an ST
    /// section, or one DFB instance's variables for a DFB body. A statement that cannot
    /// be compiled is left out and reported, so the rest of the code still runs.
    /// </summary>
    public sealed class StCompiler
    {
        private static readonly HashSet<string> StatementEnds = new(StringComparer.OrdinalIgnoreCase)
        {
            "END_IF", "ELSIF", "ELSE", "END_CASE", "END_FOR", "END_WHILE", "UNTIL", "END_REPEAT"
        };

        private readonly IPlcScope _scope;
        private readonly PlcTypeRegistry? _types;
        private readonly List<string> _problems = new();
        private List<StToken> _tokens = new();
        private int _position;

        public StCompiler(IPlcScope scope, PlcTypeRegistry? types = null)
        {
            _scope = scope;
            _types = types;
        }

        public StProgram Compile(string source)
        {
            _problems.Clear();
            try
            {
                _tokens = StLexer.Tokenize(source);
            }
            catch (StSyntaxException ex)
            {
                return new StProgram(Array.Empty<StStatement>(), new[] { ex.Message });
            }

            _position = 0;
            var statements = Statements();
            while (Peek.Kind != StTokenKind.End)
            {
                // A stray END_IF or similar at the top level: report it and carry on after it.
                _problems.Add($"line {Peek.Line}: unexpected {Peek.Text}");
                _position++;
                statements.AddRange(Statements());
            }
            return new StProgram(statements, _problems.ToList());
        }

        // ------------------------------------------------------------------
        // Statements
        // ------------------------------------------------------------------

        private List<StStatement> Statements()
        {
            var statements = new List<StStatement>();
            while (true)
            {
                var token = Peek;
                if (token.Kind == StTokenKind.End) return statements;
                if (token.Kind == StTokenKind.Identifier && StatementEnds.Contains(token.Text)) return statements;
                if (token.Is(";"))
                {
                    _position++;
                    continue;
                }

                var start = _position;
                try
                {
                    statements.Add(Statement());
                }
                catch (StSyntaxException ex)
                {
                    _problems.Add(ex.Message);
                    SkipStatement(start);
                }
            }
        }

        private StStatement Statement()
        {
            var token = Peek;
            if (token.Kind == StTokenKind.Identifier)
            {
                switch (token.Text.ToUpperInvariant())
                {
                    case "IF": return If();
                    case "CASE": return Case();
                    case "FOR": return For();
                    case "WHILE": return While();
                    case "REPEAT": return Repeat();
                    case "EXIT":
                        _position++;
                        EndOfStatement();
                        return new StJump(StFlow.Exit, token.Line);
                    case "RETURN":
                        _position++;
                        EndOfStatement();
                        return new StJump(StFlow.Return, token.Line);
                }

                if (PeekAt(1).Is("("))
                {
                    var call = Call(out _) ?? throw new StSyntaxException($"{token.Text}(...) is not a statement", token.Line);
                    EndOfStatement();
                    return new StCallStatement(call, token.Line);
                }
            }

            if (token.Kind is StTokenKind.Identifier or StTokenKind.Address)
            {
                var target = Variable();
                Expect(":=");
                var value = Expression();
                EndOfStatement();
                if (target is PlcUnresolvedOperand unresolved) throw new StSyntaxException(unresolved.Reason, token.Line);
                return new StAssignment(target, value, token.Line);
            }

            throw new StSyntaxException($"unexpected {token}", token.Line);
        }

        private StStatement If()
        {
            var line = Next().Line;
            var branches = new List<(PlcOperand, IReadOnlyList<StStatement>)>();
            var condition = Expression();
            Expect("THEN");
            branches.Add((condition, Statements()));
            while (Peek.Is("ELSIF"))
            {
                _position++;
                var elseIf = Expression();
                Expect("THEN");
                branches.Add((elseIf, Statements()));
            }

            IReadOnlyList<StStatement>? otherwise = null;
            if (Peek.Is("ELSE"))
            {
                _position++;
                otherwise = Statements();
            }
            Expect("END_IF");
            EndOfStatement();
            return new StIf(branches, otherwise, line);
        }

        private StStatement Case()
        {
            var line = Next().Line;
            var selector = Expression();
            Expect("OF");
            var cases = new List<(IReadOnlyList<(long, long)>, IReadOnlyList<StStatement>)>();
            while (Peek.Kind == StTokenKind.Literal || Peek.Is("-"))
            {
                var labels = new List<(long, long)>();
                do
                {
                    var low = CaseLabel();
                    var high = low;
                    if (Peek.Is(".."))
                    {
                        _position++;
                        high = CaseLabel();
                    }
                    labels.Add((low, high));
                }
                while (TryConsume(","));
                Expect(":");
                cases.Add((labels, CaseBody()));
            }

            IReadOnlyList<StStatement>? otherwise = null;
            if (Peek.Is("ELSE"))
            {
                _position++;
                otherwise = Statements();
            }
            Expect("END_CASE");
            EndOfStatement();
            return new StCase(selector, cases, otherwise, line);
        }

        /// <summary>A case's statements run up to the next label (a literal), ELSE or END_CASE.</summary>
        private List<StStatement> CaseBody()
        {
            var statements = new List<StStatement>();
            while (!(Peek.Kind == StTokenKind.Literal || Peek.Is("-") || Peek.Is("ELSE") || Peek.Is("END_CASE") || Peek.Kind == StTokenKind.End))
            {
                if (TryConsume(";")) continue;
                var start = _position;
                try
                {
                    statements.Add(Statement());
                }
                catch (StSyntaxException ex)
                {
                    _problems.Add(ex.Message);
                    SkipStatement(start);
                }
            }
            return statements;
        }

        private long CaseLabel()
        {
            var negative = TryConsume("-");
            var token = Next();
            if (token.Kind != StTokenKind.Literal || !PlcLiteral.TryParse(token.Text, out var value))
                throw new StSyntaxException($"expected a case label, found {token}", token.Line);
            return negative ? -value.AsInteger() : value.AsInteger();
        }

        private StStatement For()
        {
            var line = Next().Line;
            var variable = Variable();
            Expect(":=");
            var start = Expression();
            Expect("TO");
            var end = Expression();
            PlcOperand? step = null;
            if (TryConsume("BY")) step = Expression();
            Expect("DO");
            var body = Statements();
            Expect("END_FOR");
            EndOfStatement();
            if (variable is PlcUnresolvedOperand unresolved) throw new StSyntaxException(unresolved.Reason, line);
            return new StFor(variable, start, end, step, body, line);
        }

        private StStatement While()
        {
            var line = Next().Line;
            var condition = Expression();
            Expect("DO");
            var body = Statements();
            Expect("END_WHILE");
            EndOfStatement();
            return new StWhile(condition, body, repeat: false, line);
        }

        private StStatement Repeat()
        {
            var line = Next().Line;
            var body = Statements();
            Expect("UNTIL");
            var condition = Expression();
            Expect("END_REPEAT");
            EndOfStatement();
            return new StWhile(condition, body, repeat: true, line);
        }

        /// <summary>A statement ends with ';' (Control Expert accepts it being left out before an END_ keyword).</summary>
        private void EndOfStatement()
        {
            if (TryConsume(";")) return;
            var token = Peek;
            if (token.Kind == StTokenKind.End || (token.Kind == StTokenKind.Identifier && StatementEnds.Contains(token.Text))) return;
            throw new StSyntaxException($"expected ';', found {token}", token.Line);
        }

        /// <summary>After an error: resume after the next ';' at this statement's nesting.</summary>
        private void SkipStatement(int start)
        {
            _position = Math.Max(_position, start + 1);
            var depth = 0;
            while (Peek.Kind != StTokenKind.End)
            {
                var token = Next();
                if (token.Is("(") || token.Is("[")) depth++;
                else if (token.Is(")") || token.Is("]")) depth--;
                else if (token.Is(";") && depth <= 0) return;
            }
        }

        // ------------------------------------------------------------------
        // Expressions (IEC 61131-3 precedence, lowest first)
        // ------------------------------------------------------------------

        private PlcOperand Expression() => Or();

        private PlcOperand Or() => LeftAssociative(Xor, ("OR", StOperator.Or));

        private PlcOperand Xor() => LeftAssociative(And, ("XOR", StOperator.Xor));

        private PlcOperand And() => LeftAssociative(Equality, ("AND", StOperator.And), ("&", StOperator.And));

        private PlcOperand Equality() => LeftAssociative(Relational, ("=", StOperator.Equal), ("<>", StOperator.NotEqual));

        private PlcOperand Relational() => LeftAssociative(Additive,
            ("<", StOperator.Less), (">", StOperator.Greater), ("<=", StOperator.LessOrEqual), (">=", StOperator.GreaterOrEqual));

        private PlcOperand Additive() => LeftAssociative(Term, ("+", StOperator.Add), ("-", StOperator.Subtract));

        private PlcOperand Term() => LeftAssociative(Power, ("*", StOperator.Multiply), ("/", StOperator.Divide), ("MOD", StOperator.Modulo));

        private PlcOperand Power() => LeftAssociative(Unary, ("**", StOperator.Power));

        private PlcOperand LeftAssociative(Func<PlcOperand> operand, params (string Token, StOperator Operator)[] operators)
        {
            var left = operand();
            while (true)
            {
                var token = Peek;
                var match = operators.FirstOrDefault(o => token.Is(o.Token));
                if (match.Token == null) return left;
                _position++;
                var right = operand();
                left = new StBinary(match.Operator, left, right, $"{left.Text} {token.Text} {right.Text}");
            }
        }

        private PlcOperand Unary()
        {
            if (TryConsume("-"))
            {
                var operand = Unary();
                if (operand is PlcConstantOperand constant)
                {
                    // A negative literal stays a literal (so -5 still adapts to the INT it meets).
                    var type = PlcOps.OperatingType(constant.Value);
                    return new PlcConstantOperand("-" + constant.Text, PlcOps.Subtract(type, PlcValue.DefaultOf(type), constant.Value));
                }
                return new StNegate(operand, "-" + operand.Text);
            }
            if (TryConsume("+")) return Unary();
            if (Peek.Is("NOT"))
            {
                _position++;
                var operand = Unary();
                return new StNot(operand, "NOT " + operand.Text);
            }
            return Primary();
        }

        private PlcOperand Primary()
        {
            var token = Peek;
            switch (token.Kind)
            {
                case StTokenKind.Literal:
                    _position++;
                    if (!PlcLiteral.TryParse(token.Text, out var value))
                        throw new StSyntaxException($"cannot read literal {token.Text}", token.Line);
                    return new PlcConstantOperand(token.Text, value);

                case StTokenKind.Address:
                    return Variable();

                case StTokenKind.Identifier when PeekAt(1).Is("("):
                {
                    var call = Call(out var edge);
                    return edge ?? new StCallExpression(call!, token.Text + "(...)");
                }

                case StTokenKind.Identifier:
                    return Variable();
            }

            if (TryConsume("("))
            {
                var inner = Expression();
                Expect(")");
                return inner;
            }

            throw new StSyntaxException($"unexpected {token}", token.Line);
        }

        /// <summary>A variable or direct address with its field, bit and index steps.</summary>
        private PlcOperand Variable()
        {
            var token = Next();
            if (token.Kind == StTokenKind.Address)
            {
                return PlcAddress.TryParse(token.Text) is { } address
                    ? new PlcReferenceBuilder(token.Text, new PlcAddressRoot(address)).Build()
                    : new PlcUnresolvedOperand(token.Text, $"unsupported direct address {token.Text}");
            }

            if (token.Kind != StTokenKind.Identifier)
                throw new StSyntaxException($"expected a variable, found {token}", token.Line);
            if (_scope.Resolve(token.Text) is not { } root)
                throw new StSyntaxException($"'{token.Text}' is not declared", token.Line);

            var text = token.Text;
            var reference = new PlcReferenceBuilder(text, root);
            while (true)
            {
                if (Peek.Is(".") && PeekAt(1).Kind == StTokenKind.Identifier)
                {
                    _position++;
                    var field = Next().Text;
                    reference.Field(field);
                    text += "." + field;
                }
                else if (Peek.Is(".") && PeekAt(1).Kind == StTokenKind.Literal
                         && int.TryParse(PeekAt(1).Text, NumberStyles.None, CultureInfo.InvariantCulture, out var bit))
                {
                    _position += 2;
                    reference.Bit(bit);
                    text += "." + bit.ToString(CultureInfo.InvariantCulture);
                }
                else if (TryConsume("["))
                {
                    do
                    {
                        var index = Expression();
                        reference.Index(index);
                        text += "[" + index.Text + "]";
                    }
                    while (TryConsume(","));
                    Expect("]");
                }
                else
                {
                    break;
                }
            }

            var built = reference.Build();
            if (built is PlcUnresolvedOperand unresolved) throw new StSyntaxException($"{text}: {unresolved.Reason}", token.Line);
            return built;
        }

        // ------------------------------------------------------------------
        // Calls
        // ------------------------------------------------------------------

        /// <summary>A call argument; an empty field (informal calls skip a parameter with it) has no value.</summary>
        private sealed record Argument(string? Name, bool IsOutput, PlcOperand? Value);

        /// <summary>
        /// NAME(...): a function block instance call, RE/FE, or a standard function.
        /// <paramref name="edge"/> is set for RE/FE, which are expressions.
        /// </summary>
        private StCall? Call(out PlcOperand? edge)
        {
            edge = null;
            var nameToken = Next();
            var name = nameToken.Text;
            Expect("(");
            var arguments = new List<Argument>();
            if (!Peek.Is(")"))
            {
                do
                {
                    arguments.Add(Arg());
                }
                while (TryConsume(","));
            }
            Expect(")");

            if (_scope.Resolve(name) is { } root && root.Type.Kind == PlcTypeKind.FunctionBlock)
            {
                return InstanceCall(name, root, arguments, nameToken.Line);
            }

            if ((name.Equals("RE", StringComparison.OrdinalIgnoreCase) || name.Equals("FE", StringComparison.OrdinalIgnoreCase))
                && arguments.Count == 1 && arguments[0] is { Name: null, Value: PlcReferenceOperand reference })
            {
                edge = new StEdge(reference, name.Equals("RE", StringComparison.OrdinalIgnoreCase), $"{name}({reference.Text})");
                return null;
            }

            var behavior = PlcStandardLibrary.Find(name);
            if (behavior == null || behavior.IsFunctionBlock)
                throw new StSyntaxException($"'{name}' is not a function the runtime can execute", nameToken.Line);
            return FunctionCall(name, behavior, arguments, nameToken.Line);
        }

        private Argument Arg()
        {
            if (Peek.Is(",") || Peek.Is(")")) return new Argument(null, false, null);
            if (Peek.Kind == StTokenKind.Identifier && PeekAt(1).Is(":="))
            {
                var name = Next().Text;
                _position++;
                return new Argument(name, false, Expression());
            }
            if (Peek.Kind == StTokenKind.Identifier && PeekAt(1).Is("=>"))
            {
                var name = Next().Text;
                _position++;
                return new Argument(name, true, Variable());
            }
            return new Argument(null, false, Expression());
        }

        /// <summary>
        /// An instance call. Informal (positional) arguments follow the declaration:
        /// inputs, then in/out parameters, then outputs (35006144, ST "Informal Call");
        /// an empty field skips a parameter.
        /// </summary>
        private StCall InstanceCall(string name, PlcRoot root, List<Argument> arguments, int line)
        {
            var type = root.Type;
            var inputs = new List<(PlcField, PlcOperand)>();
            var copyBack = new List<(PlcField, PlcOperand)>();
            var outputs = new List<(PlcField, PlcOperand)>();
            var positional = type.Fields.Where(f => f.Direction == PlcParameterDirection.Input)
                .Concat(type.Fields.Where(f => f.Direction == PlcParameterDirection.InOut))
                .Concat(type.Fields.Where(f => f.Direction == PlcParameterDirection.Output))
                .ToList();
            var next = 0;
            foreach (var argument in arguments)
            {
                PlcField? field;
                if (argument.Name == null)
                {
                    if (next >= positional.Count) throw new StSyntaxException($"too many arguments for {name}", line);
                    field = positional[next++];
                }
                else
                {
                    field = type.FindField(argument.Name) ?? throw new StSyntaxException($"{type} has no parameter {argument.Name}", line);
                }

                if (argument.Value == null) continue;
                if (argument.IsOutput || (argument.Name == null && field.Direction == PlcParameterDirection.Output))
                {
                    if (!argument.Value.IsWritable) throw new StSyntaxException($"output {field.Name} of {name} needs a variable", line);
                    outputs.Add((field, argument.Value));
                    continue;
                }

                inputs.Add((field, argument.Value));
                if (field.Direction == PlcParameterDirection.InOut && argument.Value.IsWritable) copyBack.Add((field, argument.Value));
            }

            var instance = (PlcReferenceOperand)new PlcReferenceBuilder(name, root).Build();
            return new StInstanceCall(instance, inputs, copyBack, outputs);
        }

        /// <summary>
        /// A function or procedure call. Informal arguments bind to the inputs in order
        /// (an extensible function takes them all), then to the outputs, as a procedure
        /// call lists them (WORD_AS_BYTE(w, lo, hi)).
        /// </summary>
        private StCall FunctionCall(string name, PlcBlockBehavior behavior, List<Argument> arguments, int line)
        {
            var positionalCount = arguments.Count(a => a.Name == null);
            var signature = _types?.Signature(name) ?? FallbackSignature(name, positionalCount);
            var inputCount = signature.Extensible ? positionalCount : Math.Min(positionalCount, signature.Inputs.Count);
            var positionalNames = signature.PositionalInputs(inputCount);
            var outputNames = signature.Outputs.Count > 0 ? signature.Outputs.ToList() : new List<string> { "OUT" };

            var inputNames = new List<string>();
            var inputValues = new List<PlcOperand>();
            var targets = new List<(int, PlcOperand)>();
            var position = 0;
            foreach (var argument in arguments)
            {
                var slot = argument.Name == null ? position++ : -1;
                if (argument.Value == null) continue;

                if (argument.IsOutput || (slot >= 0 && slot >= positionalNames.Count))
                {
                    var outputName = argument.Name
                                     ?? (slot - positionalNames.Count < outputNames.Count ? outputNames[slot - positionalNames.Count] : null)
                                     ?? throw new StSyntaxException($"too many arguments for {name}", line);
                    if (!argument.Value.IsWritable) throw new StSyntaxException($"output {outputName} of {name} needs a variable", line);
                    var index = outputNames.FindIndex(n => string.Equals(n, outputName, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                    {
                        outputNames.Add(outputName);
                        index = outputNames.Count - 1;
                    }
                    targets.Add((index, argument.Value));
                    continue;
                }

                inputNames.Add(argument.Name ?? positionalNames[slot]);
                inputValues.Add(argument.Value);
            }

            var result = outputNames.FindIndex(n => string.Equals(n, "OUT", StringComparison.OrdinalIgnoreCase));
            return new StFunctionCall(behavior, inputNames.ToArray(), inputValues.ToArray(), outputNames.ToArray(), targets, result >= 0 ? result : 0);
        }

        /// <summary>
        /// For functions the XEF gives no signature for: conversions and table functions
        /// take IN and give OUT; otherwise IN for one argument, IN1..INn for more.
        /// </summary>
        private static PlcSignature FallbackSignature(string name, int positional)
        {
            var single = positional <= 1
                         || name.Contains("_TO_", StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith("LENGTH_AR", StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith("SUM_AR", StringComparison.OrdinalIgnoreCase)
                         || (name.StartsWith("MOVE_", StringComparison.OrdinalIgnoreCase) && name.Contains("_AR", StringComparison.OrdinalIgnoreCase));
            return single
                ? new PlcSignature(new[] { "IN" }, new[] { "OUT" }, Array.Empty<string>(), false)
                : new PlcSignature(new[] { "IN1" }, new[] { "OUT" }, Array.Empty<string>(), true);
        }

        // ------------------------------------------------------------------
        // Tokens
        // ------------------------------------------------------------------

        private StToken Peek => _tokens[Math.Min(_position, _tokens.Count - 1)];

        private StToken PeekAt(int offset) => _tokens[Math.Min(_position + offset, _tokens.Count - 1)];

        private StToken Next() => _tokens[Math.Min(_position++, _tokens.Count - 1)];

        private bool TryConsume(string symbol)
        {
            if (!Peek.Is(symbol)) return false;
            _position++;
            return true;
        }

        private void Expect(string symbol)
        {
            var token = Peek;
            if (!token.Is(symbol)) throw new StSyntaxException($"expected '{symbol}', found {token}", token.Line);
            _position++;
        }
    }
}
