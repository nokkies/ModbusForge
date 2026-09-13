using System;
using System.Globalization;

namespace ModbusForge.Core.Xef
{
    /// <summary>
    /// Pure helpers for interpreting XEF pin/constant values. The translator and the PLC
    /// simulation both feed raw <c>effectiveParameter</c> / <c>variableInit</c> strings through
    /// these; no DI and no Avalonia.
    /// </summary>
    public static class XefValueHelpers
    {
        private const char TimeLiteralPrefix = '#';

        private static readonly string[] TimeUnitNames =
        {
            "h", "m", "s", "ms"
        };

        private static readonly double[] TimeUnitMsFactors =
        {
            3600_000.0,   // hours
            60_000.0,     // minutes
            1000.0,       // seconds
            1.0           // milliseconds
        };

        /// <summary>
        /// Parses a numeric constant literal (INT or REAL) using invariant culture. Returns null
        /// when the text is null/empty or not a plain number. <see cref="Tuple"/>.IsReal is false
        /// for integer literals and true when a decimal point or exponent is present.
        /// </summary>
        public static (bool IsReal, double Value)? TryParseConstant(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var trimmed = text.Trim();

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                // A literal with no '.' and no exponent is an integer literal.
                var isReal = trimmed.IndexOf('.') >= 0 ||
                             trimmed.IndexOf('e', StringComparison.OrdinalIgnoreCase) >= 0;
                return (isReal, value);
            }

            return null;
        }

        /// <summary>True for IEC time literals (T#-prefixed, case-insensitive).</summary>
        public static bool IsTimeLiteral(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (trimmed.Length < 2 || char.ToUpperInvariant(trimmed[0]) != 'T')
            {
                return false;
            }

            return trimmed[1] == TimeLiteralPrefix;
        }

        /// <summary>
        /// Parses an IEC TIME literal (or a bare millisecond count) to integer milliseconds.
        /// Supported units: h, m, s, ms (case-insensitive), in compound form (e.g. "T#2m30s").
        /// A bare number with no unit is treated as milliseconds. Returns null when not parseable.
        /// </summary>
        public static int? ParseTimeToMs(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var trimmed = text.Trim();

            // Bare number = milliseconds.
            if (!IsTimeLiteral(trimmed))
            {
                return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var bareMs)
                    ? (int?)ConvertToMilliseconds(bareMs)
                    : null;
            }

            // T#<value>
            var body = trimmed.Length > 2 ? trimmed.Substring(2) : string.Empty;
            if (body.Length == 0)
            {
                return null;
            }

            // Walk the compound body, e.g. "2m30s" or "0.5s". A trailing numeric token with no
            // unit is interpreted as milliseconds (IEC default for a bare number inside a literal).
            var index = 0;
            var totalMs = 0.0;
            var matchedAny = false;
            var failed = false;

            while (index < body.Length)
            {
                // Parse a (possibly decimal) number.
                var numberStart = index;
                var sawDigits = false;
                while (index < body.Length)
                {
                    var c = body[index];
                    if (char.IsDigit(c) || c == '.' || c == ',' || c == '_' ||
                        (c == 'e' && index + 1 < body.Length &&
                         (char.IsDigit(body[index + 1]) || body[index + 1] == '+')))
                    {
                        if (char.IsDigit(c))
                        {
                            sawDigits = true;
                        }
                        index++;
                    }
                    else
                    {
                        break;
                    }
                }

                var numberText = body.Substring(numberStart, index - numberStart).Replace(",", ".").Replace("_", "");
                if (!sawDigits ||
                    !double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var numberValue))
                {
                    failed = true;
                    break;
                }

                // Parse a unit token (ms before m/s/h, or h/m/s) or none.
                var unit = ReadUnitToken(body, index, out var unitLength);
                var factor = unit switch
                {
                    "h" => TimeUnitMsFactors[0],
                    "m" => TimeUnitMsFactors[1],
                    "s" => TimeUnitMsFactors[2],
                    "ms" => TimeUnitMsFactors[3],
                    null => 1.0,
                    _ => (double?)null,
                };

                if (factor is null)
                {
                    failed = true;
                    break;
                }

                totalMs += numberValue * factor.Value;
                matchedAny = true;
                index += unitLength;
            }

            if (failed || !matchedAny)
            {
                return null;
            }

            return ConvertToMilliseconds(totalMs);
        }

        private static int? ConvertToMilliseconds(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) ||
                ms < int.MinValue || ms > int.MaxValue)
            {
                return null;
            }

            return (int)Math.Round(ms, MidpointRounding.AwayFromZero);
        }

        private static string? ReadUnitToken(string body, int start, out int length)
        {
            length = 0;
            if (start >= body.Length)
            {
                return null;
            }

            // "ms" must be matched before "m".
            if (body.Length - start >= 2 &&
                char.ToLowerInvariant(body[start]) == 'm' &&
                char.ToLowerInvariant(body[start + 1]) == 's')
            {
                length = 2;
                return "ms";
            }

            var single = char.ToLowerInvariant(body[start]);
            if (single is 'h' or 'm' or 's')
            {
                length = 1;
                return single.ToString();
            }

            return null;
        }

        /// <summary>
        /// True when the value looks like an inline equation: it contains one of the operators
        /// (+ - * / &lt; &gt; =) and is not itself a pure number or T# time literal. This mirrors
        /// the parser's heuristic for flagging a pin <c>HasEquation</c>.
        /// </summary>
        public static bool LooksLikeEquation(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (TryParseConstant(trimmed) != null || IsTimeLiteral(trimmed))
            {
                return false;
            }

            // Note: a bare '-' inside an otherwise-numeric token (e.g. "-5") is already handled
            // by TryParseConstant, so any operator reaching here marks an equation.
            return trimmed.IndexOfAny(EquationOperators) >= 0;
        }

        private static readonly char[] EquationOperators =
        {
            '+', '-', '*', '/', '<', '>', '='
        };

        /// <summary>
        /// A small, SAFE arithmetic evaluator for inline XEF equations restricted to numeric
        /// constants, the four binary operators (+ - * /), unary minus and parentheses. It uses a
        /// recursive-descent parser (no eval / JavaScript / DynamicExpresso). Comparisons,
        /// identifiers, and function calls are not supported and return false.
        /// </summary>
        public static bool TryEvaluateArithmetic(string? text, out double result)
        {
            result = 0.0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var expression = text.Trim();
            var parser = new ExpressionParser(expression);
            if (!parser.TryParse(out var value))
            {
                return false;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }

            result = value;
            return true;
        }

        /// <summary>
        /// Minimal recursive-descent parser for: expression := term ((+|-) term)*,
        /// term := factor ((*|/) factor)*, factor := ('-')? (number | '(' expression ')').
        /// </summary>
        private sealed class ExpressionParser
        {
            private readonly string _text;
            private int _pos;

            public ExpressionParser(string text)
            {
                _text = text;
            }

            public bool TryParse(out double value)
            {
                _pos = 0;
                value = 0.0;
                if (!TryParseExpression(out value))
                {
                    return false;
                }

                SkipWhitespace();
                return _pos == _text.Length;
            }

            private bool TryParseExpression(out double value)
            {
                if (!TryParseTerm(out value))
                {
                    return false;
                }

                for (;;)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length)
                    {
                        return true;
                    }

                    var op = _text[_pos];
                    if (op != '+' && op != '-')
                    {
                        return true;
                    }

                    var left = value;
                    _pos++;
                    if (!TryParseTerm(out var right))
                    {
                        return false;
                    }

                    value = op == '+' ? left + right : left - right;
                }
            }

            private bool TryParseTerm(out double value)
            {
                if (!TryParseFactor(out value))
                {
                    return false;
                }

                for (;;)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length)
                    {
                        return true;
                    }

                    var op = _text[_pos];
                    if (op != '*' && op != '/')
                    {
                        return true;
                    }

                    var left = value;
                    _pos++;
                    if (!TryParseFactor(out var right))
                    {
                        return false;
                    }

                    if (op == '*')
                    {
                        value = left * right;
                    }
                    else
                    {
                        if (right == 0.0)
                        {
                            value = 0.0;
                            return false;
                        }

                        value = left / right;
                    }
                }
            }

            private bool TryParseFactor(out double value)
            {
                SkipWhitespace();
                if (_pos >= _text.Length)
                {
                    value = 0.0;
                    return false;
                }

                var c = _text[_pos];

                // Unary minus (or a lone '+').
                if (c == '-' || c == '+')
                {
                    var sign = c == '-' ? -1.0 : 1.0;
                    _pos++;
                    if (!TryParseFactor(out var operand))
                    {
                        value = 0.0;
                        return false;
                    }

                    value = sign * operand;
                    return true;
                }

                // Parenthesised sub-expression.
                if (c == '(')
                {
                    _pos++;
                    if (!TryParseExpression(out var inner))
                    {
                        value = 0.0;
                        return false;
                    }

                    SkipWhitespace();
                    if (_pos >= _text.Length || _text[_pos] != ')')
                    {
                        value = 0.0;
                        return false;
                    }

                    _pos++;
                    value = inner;
                    return true;
                }

                // Numeric literal.
                if (char.IsDigit(c) || c == '.')
                {
                    var numberStart = _pos;
                    while (_pos < _text.Length &&
                           (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                    {
                        _pos++;
                    }

                    var numberText = _text.Substring(numberStart, _pos - numberStart);
                    if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        value = 0.0;
                        return false;
                    }

                    value = number;
                    return true;
                }

                value = 0.0;
                return false;
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                }
            }
        }
    }

    /// <summary>
    /// Maps XEF/SCL data type names to the app's Modbus areas. This is a display/heuristic
    /// mapping: the authoritative address source is the variable's <c>topologicalAddress</c>
    /// (see <see cref="XefVariable.TryParseAddress"/>), not the data type. The type only tells us
    /// how to *interpret* the value (bool vs. integer vs. real) for display and simulation.
    /// </summary>
    public static class XefTypeMapper
    {
        /// <summary>
        /// Maps a XEF/SCL data type name (case-insensitive) to
        /// (<see cref="Models.PlcArea"/>, isReal, isBool). Unknown types default to a
        /// non-real, non-bool HoldingRegister.
        /// </summary>
        public static (Models.PlcArea Area, bool IsReal, bool IsBool) MapDataType(string? xefTypeName)
        {
            var name = Normalize(xefTypeName);
            return name switch
            {
                "bool" => (Models.PlcArea.Coil, false, true),
                "byte" or "word" or "dword" or "dint" or "udint" or "int" or "uint"
                    or "sint" or "usint" or "lint" or "ulint" or "time" or "date" or "time_of_day"
                    => (Models.PlcArea.HoldingRegister, false, false),
                "real" or "lreal" => (Models.PlcArea.HoldingRegister, true, false),
                _ => (Models.PlcArea.HoldingRegister, false, false),
            };
        }

        /// <summary>True for boolean type names (BOOL).</summary>
        public static bool IsBoolType(string? xefTypeName)
        {
            return Normalize(xefTypeName) == "bool";
        }

        /// <summary>True for real type names (REAL, LREAL).</summary>
        public static bool IsRealType(string? xefTypeName)
        {
            return Normalize(xefTypeName) is "real" or "lreal";
        }

        /// <summary>True for integer type names (BYTE/WORD/DINT/UDINT/INT/UINT and friends).</summary>
        public static bool IsIntegerType(string? xefTypeName)
        {
            return Normalize(xefTypeName) switch
            {
                "byte" or "word" or "dword" or "dint" or "udint" or "int" or "uint"
                    or "sint" or "usint" or "lint" or "ulint" => true,
                _ => false,
            };
        }

        private static string? Normalize(string? xefTypeName)
        {
            if (string.IsNullOrWhiteSpace(xefTypeName))
            {
                return null;
            }

            // Strip array decorations like "ARRAY[0..9] OF INT" down to the element type for
            // heuristic purposes; XEF type names arrive as plain tokens in practice.
            var trimmed = xefTypeName.Trim();
            var ofIndex = trimmed.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
            if (ofIndex >= 0)
            {
                trimmed = trimmed.Substring(ofIndex + 4).Trim();
            }

            return trimmed.ToLowerInvariant();
        }
    }
}
