using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ModbusForge.Core.Plc
{
    /// <summary>Where a located object lives.</summary>
    public enum PlcMemoryArea
    {
        /// <summary>%M: Modbus coil (0x).</summary>
        Coil,

        /// <summary>%I: Modbus discrete input (1x).</summary>
        DiscreteInput,

        /// <summary>%IW: Modbus input register (3x).</summary>
        InputRegister,

        /// <summary>%MW (and %MD/%MF over two words): Modbus holding register (4x).</summary>
        HoldingRegister,

        /// <summary>%S system bits.</summary>
        SystemBit,

        /// <summary>%SW system words.</summary>
        SystemWord,

        /// <summary>
        /// Objects outside the Modbus state RAM (topological I/O such as %I\2.2\1.7.1 or
        /// %IW1.10.5, %KW constants): kept in PLC memory only.
        /// </summary>
        Private
    }

    /// <summary>
    /// A Unity direct address. Modicon Quantum maps its flat (state RAM) addresses
    /// onto the Modbus tables one-to-one and 1-based: %M1 is coil 000001, %I1 discrete
    /// input 100001, %IW1 input register 300001 and %MW1 holding register 400001
    /// (Unity Pro Concept Application Converter manual; Quantum 800 Series I/O
    /// reference 33002455: "0x is now %M, 1x is now %I, 3x is now %IW, 4x is now %MW").
    /// ModbusForge's data store is 1-based the same way, so <see cref="Index"/> is the
    /// data store index.
    /// </summary>
    public sealed record PlcAddress(PlcMemoryArea Area, int Index, int? Bit, PlcType DefaultType, string Text)
    {
        private static readonly Regex FlatRegex = new(
            @"^%(?<area>MW|MD|MF|MX|M|IW|ID|IF|IX|I|SW|SD|S|KW|KD|KF|QW|Q)(?<index>\d+)(?:\.(?<bit>\d+))?(?::(?<count>\d+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Element count for the %MWi:n array syntax; null otherwise.</summary>
        public int? Count { get; init; }

        /// <summary>
        /// Parses a direct address, or returns null when the text is not one.
        /// Topological addresses (rack.module.channel, remote drops) become
        /// <see cref="PlcMemoryArea.Private"/> objects keyed by their text.
        /// </summary>
        public static PlcAddress? TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var trimmed = text.Trim();
            if (!trimmed.StartsWith('%')) return null;

            var match = FlatRegex.Match(trimmed);
            if (!match.Success)
            {
                return IsTopological(trimmed) ? new PlcAddress(PlcMemoryArea.Private, 0, null, TopologicalType(trimmed), Normalize(trimmed)) : null;
            }

            var area = match.Groups["area"].Value.ToUpperInvariant();
            var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
            int? bit = match.Groups["bit"].Success ? int.Parse(match.Groups["bit"].Value, CultureInfo.InvariantCulture) : null;
            int? count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture) : null;
            var normalized = trimmed.ToUpperInvariant();

            // A bit of a word (%MW10.3): BOOL. The ':n' suffix makes an INT array.
            PlcType WordType(PlcType word) => bit.HasValue ? PlcType.Bool
                : count.HasValue ? PlcType.Array(word, 0, count.Value - 1) : word;

            return area switch
            {
                "M" or "MX" => new PlcAddress(PlcMemoryArea.Coil, index, null, PlcType.Ebool, normalized),
                "I" or "IX" => new PlcAddress(PlcMemoryArea.DiscreteInput, index, null, PlcType.Ebool, normalized),
                "MW" => new PlcAddress(PlcMemoryArea.HoldingRegister, index, bit, WordType(PlcType.Int), normalized) { Count = count },
                "MD" => new PlcAddress(PlcMemoryArea.HoldingRegister, index, bit, WordType(PlcType.Dint), normalized) { Count = count },
                "MF" => new PlcAddress(PlcMemoryArea.HoldingRegister, index, null, PlcType.Real, normalized),
                "IW" => new PlcAddress(PlcMemoryArea.InputRegister, index, bit, WordType(PlcType.Int), normalized) { Count = count },
                "ID" => new PlcAddress(PlcMemoryArea.InputRegister, index, bit, WordType(PlcType.Dint), normalized) { Count = count },
                "IF" => new PlcAddress(PlcMemoryArea.InputRegister, index, null, PlcType.Real, normalized),
                "S" => new PlcAddress(PlcMemoryArea.SystemBit, index, null, PlcType.Bool, normalized),
                "SW" => new PlcAddress(PlcMemoryArea.SystemWord, index, bit, WordType(PlcType.Int), normalized),
                "SD" => new PlcAddress(PlcMemoryArea.SystemWord, index, bit, WordType(PlcType.Dint), normalized),
                // %KW constants and flat %Q/%QW are not part of the Quantum state RAM.
                "Q" => new PlcAddress(PlcMemoryArea.Private, 0, null, PlcType.Ebool, normalized),
                _ => new PlcAddress(PlcMemoryArea.Private, 0, null, bit.HasValue ? PlcType.Bool : PlcType.Int, normalized)
            };
        }

        /// <summary>True for the Modbus tables a Modbus client can reach.</summary>
        public bool IsModbus => Area is PlcMemoryArea.Coil or PlcMemoryArea.DiscreteInput or PlcMemoryArea.InputRegister or PlcMemoryArea.HoldingRegister;

        private static bool IsTopological(string text)
            => text.Contains('\\') || Regex.IsMatch(text, @"^%(I|Q|IW|QW|ID|QD|IF|QF|CH)\d+(\.\d+){1,3}$", RegexOptions.IgnoreCase);

        private static PlcType TopologicalType(string text)
        {
            var upper = text.ToUpperInvariant();
            return upper.StartsWith("%IW") || upper.StartsWith("%QW") ? PlcType.Int
                : upper.StartsWith("%ID") || upper.StartsWith("%QD") ? PlcType.Dint
                : upper.StartsWith("%IF") || upper.StartsWith("%QF") ? PlcType.Real
                : PlcType.Ebool;
        }

        private static string Normalize(string text) => text.ToUpperInvariant();
    }
}
