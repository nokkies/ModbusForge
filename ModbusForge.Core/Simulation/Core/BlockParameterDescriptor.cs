namespace ModbusForge.Core.Simulation.Core
{
    /// <summary>
    /// Value kind of a configurable function-block parameter.
    /// </summary>
    public enum BlockParameterKind
    {
        /// <summary>32-bit integer.</summary>
        Int32,

        /// <summary>Double-precision real.</summary>
        Real,

        /// <summary>Boolean flag.</summary>
        Bool,

        /// <summary>String chosen from a fixed list of options (see <see cref="BlockParameterDescriptor.Options"/>).</summary>
        Choice
    }

    /// <summary>
    /// Declarative description of one configurable parameter of a function block.
    /// Blocks expose their parameters through <see cref="IFunctionBlock.Parameters"/> so that
    /// the mapper and every editor surface (node footer, property panel) can render and
    /// apply them without per-type hard-coded UI lists.
    /// </summary>
    public sealed record BlockParameterDescriptor
    {
        /// <summary>Stable key used to read/write the value (matches the engine parameter name).</summary>
        public required string Name { get; init; }

        /// <summary>Label shown in editors.</summary>
        public required string DisplayName { get; init; }

        /// <summary>Value kind.</summary>
        public required BlockParameterKind Kind { get; init; }

        /// <summary>Value used when the editor has not stored one.</summary>
        public object? DefaultValue { get; init; }

        /// <summary>Allowed values when <see cref="Kind"/> is <see cref="BlockParameterKind.Choice"/>.</summary>
        public IReadOnlyList<string>? Options { get; init; }

        /// <summary>Inclusive minimum for numeric kinds.</summary>
        public double? Minimum { get; init; }

        /// <summary>Inclusive maximum for numeric kinds.</summary>
        public double? Maximum { get; init; }

        /// <summary>Optional unit suffix shown next to numeric editors (e.g. "ms").</summary>
        public string? Suffix { get; init; }
    }
}
