using System;
using System.Collections.Generic;
using System.Text;

namespace ModbusForge.Core.Plc.St
{
    public enum StTokenKind
    {
        Identifier,
        Literal,
        Address,
        Symbol,
        End
    }

    public readonly record struct StToken(StTokenKind Kind, string Text, int Line)
    {
        public bool Is(string symbolOrKeyword) => string.Equals(Text, symbolOrKeyword, StringComparison.OrdinalIgnoreCase)
                                                  && Kind is StTokenKind.Symbol or StTokenKind.Identifier;

        public override string ToString() => Kind == StTokenKind.End ? "end of text" : $"'{Text}' (line {Line})";
    }

    /// <summary>
    /// Splits IEC 61131-3 Structured Text into tokens: identifiers and keywords
    /// (case-insensitive), literals (8, 16#FF, 1.5E3, INT#5, T#1S500MS, 'text',
    /// TRUE), direct addresses (%MW10, %MW10.3, %S21, %I\2.2\1.7.1) and symbols.
    /// Comments (* ... *) do not nest, as IEC 61131-3 specifies.
    /// </summary>
    public static class StLexer
    {
        private static readonly string[] Symbols = { ":=", "=>", "<>", "<=", ">=", "**", "..", "(", ")", "[", "]", ",", ";", ":", ".", "+", "-", "*", "/", "<", ">", "=", "&", "#" };

        public static List<StToken> Tokenize(string source)
        {
            var tokens = new List<StToken>();
            var i = 0;
            var line = 1;
            while (i < source.Length)
            {
                var c = source[i];
                if (c == '\n')
                {
                    line++;
                    i++;
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                // (* comment *)
                if (c == '(' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    var end = source.IndexOf("*)", i + 2, StringComparison.Ordinal);
                    var stop = end < 0 ? source.Length : end + 2;
                    line += Count(source, i, stop, '\n');
                    i = stop;
                    continue;
                }

                // // comment (accepted by newer Control Expert versions)
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }

                if (c == '\'')
                {
                    var start = i++;
                    while (i < source.Length)
                    {
                        if (source[i] == '$' && i + 1 < source.Length) { i += 2; continue; }
                        if (source[i++] == '\'') break;
                    }
                    tokens.Add(new StToken(StTokenKind.Literal, source[start..i], line));
                    continue;
                }

                if (c == '%')
                {
                    var start = i++;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] is '\\' or '_'
                           || (source[i] == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))))
                    {
                        i++;
                    }
                    tokens.Add(new StToken(StTokenKind.Address, source[start..i], line));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    tokens.Add(new StToken(StTokenKind.Literal, ReadNumber(source, ref i), line));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;

                    // Typed literals: INT#5, T#5S, TIME#1H, DWORD#16#FFFF, BOOL#1.
                    if (i < source.Length && source[i] == '#')
                    {
                        i++;
                        var body = new StringBuilder(source[start..i]);
                        if (i < source.Length && char.IsDigit(source[i]))
                        {
                            var number = ReadNumber(source, ref i);
                            body.Append(number);
                        }
                        while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] is '_' or '.' or '-' or ':' or '#'))
                        {
                            body.Append(source[i++]);
                        }
                        tokens.Add(new StToken(StTokenKind.Literal, body.ToString(), line));
                        continue;
                    }

                    var word = source[start..i];
                    tokens.Add(word.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || word.Equals("FALSE", StringComparison.OrdinalIgnoreCase)
                        ? new StToken(StTokenKind.Literal, word, line)
                        : new StToken(StTokenKind.Identifier, word, line));
                    continue;
                }

                var symbol = Array.Find(Symbols, s => string.CompareOrdinal(source, i, s, 0, s.Length) == 0);
                if (symbol == null) throw new StSyntaxException($"unexpected character '{c}'", line);
                tokens.Add(new StToken(StTokenKind.Symbol, symbol, line));
                i += symbol.Length;
            }

            tokens.Add(new StToken(StTokenKind.End, "", line));
            return tokens;
        }

        /// <summary>
        /// Digits, a based number (16#FF_FF), or a real (1.5, 1.5E-3). A ".." after the
        /// integer part is a range, not a decimal point.
        /// </summary>
        private static string ReadNumber(string source, ref int i)
        {
            var start = i;
            while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '_')) i++;

            if (i < source.Length && source[i] == '#')
            {
                i++;
                while (i < source.Length && (Uri.IsHexDigit(source[i]) || source[i] == '_')) i++;
                return source[start..i];
            }

            if (i + 1 < source.Length && source[i] == '.' && char.IsDigit(source[i + 1]))
            {
                i++;
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '_')) i++;
            }

            if (i < source.Length && (source[i] == 'e' || source[i] == 'E'))
            {
                var mark = i++;
                if (i < source.Length && (source[i] == '+' || source[i] == '-')) i++;
                if (i < source.Length && char.IsDigit(source[i]))
                {
                    while (i < source.Length && char.IsDigit(source[i])) i++;
                }
                else
                {
                    i = mark;
                }
            }

            return source[start..i];
        }

        private static int Count(string text, int from, int to, char c)
        {
            var n = 0;
            for (var k = from; k < to && k < text.Length; k++)
            {
                if (text[k] == c) n++;
            }
            return n;
        }
    }

    /// <summary>ST source the runtime cannot parse.</summary>
    public sealed class StSyntaxException : Exception
    {
        public StSyntaxException(string message, int line) : base($"line {line}: {message}") => Line = line;

        public int Line { get; }
    }
}
