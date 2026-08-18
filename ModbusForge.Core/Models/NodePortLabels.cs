namespace ModbusForge.Models
{
    /// <summary>
    /// The function block's real port names for a node's three editor connector
    /// slots ("Input1", "Input2", "Output"), used as canvas pin labels and
    /// tooltips. The slots themselves keep their generic names in the wire
    /// format (NodeConnection); only the display names come from here. A slot
    /// without a matching declared port is null (the label is hidden).
    /// </summary>
    public sealed class NodePortLabels
    {
        /// <summary>Declared name of the block's first input port (e.g. "IN", "S", "A").</summary>
        public string? Input1 { get; init; }

        /// <summary>Declared name of the block's second input port (e.g. "R", "B"). Null when the block has one input.</summary>
        public string? Input2 { get; init; }

        /// <summary>Declared name of the block's primary output port (e.g. "Q", "Run", "Value").</summary>
        public string? Output { get; init; }
    }
}
