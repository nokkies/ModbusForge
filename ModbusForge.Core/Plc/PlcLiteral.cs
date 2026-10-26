using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// Parses IEC 61131-3 / Unity literals as they appear on FBD pins and in
    /// initial values: TRUE, 8, -5, 16#FF9F, INT#5, 1.5, 1.0E3, t#6s, T#1M30S,
    /// 'text'. Untyped integers stay <see cref="PlcType.AnyInteger"/> so they take
    /// the type of the values they meet.
    /// </summary>
    public static class PlcLiteral
    {
        private static readonly Regex TypedPrefix = new(@"^(?<type>[A-Za-z_]+)#(?<rest>.+)$", RegexOptions.Compiled);
        private static readonly Regex Integer = new(@"^[+-]?\d[\d_]*$", RegexOptions.Compiled);
        private static readonly Regex Based = new(@"^(?<sign>[+-]?)(?<base>2|8|16)#(?<digits>[0-9A-Fa-f_]+)$", RegexOptions.Compiled);
        private static readonly Regex RealNumber = new(@"^[+-]?(\d[\d_]*)?\.\d[\d_]*([eE][+-]?\d+)?$|^[+-]?\d[\d_]*[eE][+-]?\d+$", RegexOptions.Compiled);
        private static readonly Regex TimePart = new(@"(?<n>\d+(?:\.\d+)?)(?<unit>ms|d|h|m|s)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryParse(string? text, out PlcValue value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();

            if (s.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) { value = PlcOps.True; return true; }
            if (s.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) { value = PlcOps.False; return true; }

            if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
            {
                value = PlcValue.FromString(Unescape(s[1..^1]));
                return true;
            }

            if (TryParseNumber(s, out value)) return true;

            var typed = TypedPrefix.Match(s);
            if (!typed.Success) return false;

            var prefix = typed.Groups["type"].Value.ToUpperInvariant();
            var rest = typed.Groups["rest"].Value;
            switch (prefix)
            {
                case "T":
                case "TIME":
                    return TryParseTime(rest, out value);
                case "BOOL":
                    if (!TryParse(rest, out var b)) return false;
                    value = PlcOps.Convert(b, PlcType.Bool);
                    return true;
                case "D":
                case "DATE":
                case "TOD":
                case "TIME_OF_DAY":
                case "DT":
                case "DATE_AND_TIME":
                    // Dates are carried but not interpreted.
                    value = PlcValue.DefaultOf(PlcType.TryParseBuiltIn(prefix) ?? PlcType.Date);
                    return true;
            }

            var type = PlcType.TryParseBuiltIn(prefix);
            if (type == null || !type.IsElementary || !TryParseNumber(rest, out var number)) return false;
            value = PlcOps.Convert(number, type);
            return true;
        }

        private static bool TryParseNumber(string s, out PlcValue value)
        {
            value = default;
            if (Integer.IsMatch(s))
            {
                if (!long.TryParse(s.Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) return false;
                value = PlcValue.FromInteger(PlcType.AnyInteger, n);
                return true;
            }

            var based = Based.Match(s);
            if (based.Success)
            {
                var digits = based.Groups["digits"].Value.Replace("_", "");
                var radix = int.Parse(based.Groups["base"].Value, CultureInfo.InvariantCulture);
                long n;
                try
                {
                    n = System.Convert.ToInt64(digits, radix);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                {
                    return false;
                }
                value = PlcValue.FromInteger(PlcType.AnyInteger, based.Groups["sign"].Value == "-" ? -n : n);
                return true;
            }

            if (RealNumber.IsMatch(s)
                && double.TryParse(s.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
            {
                value = PlcValue.FromReal(real);
                return true;
            }

            return false;
        }

        /// <summary>The body of a TIME literal (after T#): 1d2h3m4s5ms, 1.5s, 500ms, 24h.</summary>
        public static bool TryParseTime(string body, out PlcValue value)
        {
            value = default;
            var s = body.Replace("_", "").Trim();
            var negative = s.StartsWith('-');
            if (negative) s = s[1..];
            if (s.Length == 0) return false;

            double total = 0;
            var position = 0;
            foreach (Match part in TimePart.Matches(s))
            {
                if (part.Index != position) return false;
                position = part.Index + part.Length;
                var n = double.Parse(part.Groups["n"].Value, CultureInfo.InvariantCulture);
                total += part.Groups["unit"].Value.ToLowerInvariant() switch
                {
                    "d" => n * 86_400_000,
                    "h" => n * 3_600_000,
                    "m" => n * 60_000,
                    "s" => n * 1_000,
                    _ => n
                };
            }
            if (position != s.Length) return false;

            value = PlcValue.FromTime(PlcValue.RoundToInteger(negative ? -total : total));
            return true;
        }

        private static string Unescape(string s)
        {
            if (!s.Contains('$')) return s;
            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] != '$' || i + 1 >= s.Length)
                {
                    sb.Append(s[i]);
                    continue;
                }
                var next = s[++i];
                switch (char.ToUpperInvariant(next))
                {
                    case 'L':
                    case 'N': sb.Append('\n'); break;
                    case 'P': sb.Append('\f'); break;
                    case 'R': sb.Append('\r'); break;
                    case 'T': sb.Append('\t'); break;
                    default:
                        if (i + 1 < s.Length && Uri.IsHexDigit(next) && Uri.IsHexDigit(s[i + 1]))
                        {
                            sb.Append((char)System.Convert.ToInt32(s.Substring(i, 2), 16));
                            i++;
                        }
                        else
                        {
                            sb.Append(next);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
