using System.Collections.Generic;

namespace ModbusForge.Simulation.Core
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
        /// Executes the block using the provided context.
        /// </summary>
        void Execute(IExecutionContext context);
    }
}
