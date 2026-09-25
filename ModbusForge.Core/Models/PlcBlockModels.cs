using System;
using System.Collections.Generic;

namespace ModbusForge.Models
{
    /// <summary>
    /// What Control Expert draws for an FFB imported from a Unity Pro XEF (its type,
    /// instance name, pins and the actual parameters attached to them), or the text
    /// of a section text box. Null on hand-built Simulation nodes. Geometry is in
    /// canvas pixels relative to the node.
    /// </summary>
    public sealed record PlcBlockInfo
    {
        /// <summary>Unity type name as Control Expert shows it (MOVE, TON, xDIG101).</summary>
        public string TypeName { get; init; } = "";

        /// <summary>
        /// Instance name drawn above function blocks; null for elementary functions,
        /// whose ".n" names Unity generates internally.
        /// </summary>
        public string? InstanceName { get; init; }

        /// <summary>The block's own comment (FFBBlock &lt;comment&gt;).</summary>
        public string? Comment { get; init; }

        /// <summary>Text of a section text box (<see cref="PlcElementType.PlcComment"/> nodes).</summary>
        public string? Text { get; init; }

        /// <summary>Size of one Control Expert grid cell, in pixels, the geometry uses.</summary>
        public double CellSize { get; init; }

        /// <summary>
        /// Distance from the node's left/right edge to the block frame: pins sit in the
        /// block's outer cell columns, and links meet the frame at those cells' centres.
        /// </summary>
        public double FrameInset => CellSize / 2;

        /// <summary>Pins in Control Expert order: inputs top to bottom, then outputs.</summary>
        public IReadOnlyList<PlcPin> Pins { get; init; } = Array.Empty<PlcPin>();
    }

    /// <summary>One formal parameter of an imported FFB.</summary>
    public sealed record PlcPin
    {
        /// <summary>Formal parameter name (IN, PT, Q, EN...).</summary>
        public string Name { get; init; } = "";

        /// <summary>True for pins on the block's left edge.</summary>
        public bool IsInput { get; init; }

        /// <summary>Vertical centre of the pin's grid row, in pixels from the node's top edge.</summary>
        public double CenterY { get; init; }

        /// <summary>Variable, literal or expression wired to the pin; null when unconnected or linked only.</summary>
        public string? ActualParameter { get; init; }

        /// <summary>True when the pin is negated (drawn with a circle).</summary>
        public bool Inverted { get; init; }
    }

    /// <summary>A bend point of a Control Expert link, in canvas pixels.</summary>
    public sealed record PlcRoutePoint(double X, double Y);

    /// <summary>
    /// A block's values in the PLC runtime's last scan, as the canvas shows them
    /// while the PLC tab runs: pin name to display text (TRUE, 12, 1.5, T#2S), per side.
    /// </summary>
    /// <param name="Executed">False when EN or the section's activation condition held the block off.</param>
    /// <param name="Simulated">False for blocks the runtime does not compute (DFBs, unsupported types).</param>
    public sealed record PlcLiveState(
        IReadOnlyDictionary<string, string> Inputs,
        IReadOnlyDictionary<string, string> Outputs,
        bool Executed,
        bool Simulated)
    {
        /// <summary>True when both states show the same values (records compare dictionaries by reference).</summary>
        public bool SameAs(PlcLiveState? other)
            => other != null && Executed == other.Executed && Simulated == other.Simulated
               && SameValues(Inputs, other.Inputs) && SameValues(Outputs, other.Outputs);

        private static bool SameValues(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var (key, value) in a)
            {
                if (!b.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}
