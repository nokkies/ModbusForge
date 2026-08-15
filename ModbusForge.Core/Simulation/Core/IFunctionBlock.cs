using System.Collections.Generic;

namespace ModbusForge.Core.Simulation.Core
{
    /// <summary>
    /// A simulation function block that can be instantiated and executed in a graph.
    /// </summary>
    public interface IFunctionBlock
    {
        /// <summary>
        /// Unique type identifier used for serialization and discovery.
        /// </summary>
        string TypeId { get; }

        /// <summary>
        /// Human-readable display name shown in the palette and node header.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Category used for palette grouping (e.g., "Logic", "Timer", "Math").
        /// </summary>
        string Category { get; }

        /// <summary>
        /// Input and output port definitions.
        /// </summary>
        IReadOnlyList<IPort> Ports { get; }

        /// <summary>
        /// Configurable parameters exposed for this block type. Empty for blocks without settings.
        /// Editors and the graph mapper use this list instead of per-type hard-coded switches.
        /// </summary>
        IReadOnlyList<BlockParameterDescriptor> Parameters { get; }

        /// <summary>
        /// Executes the block using the provided context.
        /// </summary>
        void Execute(IExecutionContext context);
    }
}
