using System.Collections.Generic;
using ModbusForge.Data;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Engine
{
    /// <summary>
    /// Executes a simulation graph of function blocks.
    /// </summary>
    public interface IExecutionEngine
    {
        /// <summary>
        /// Loads a graph. Must be called before Execute.
        /// </summary>
        void LoadGraph(IEnumerable<SimulationNode> nodes, IEnumerable<SimulationConnection> connections);

        /// <summary>
        /// Runs one execution cycle against the provided Modbus data store.
        /// </summary>
        void Execute(DataStore? dataStore = null);

        /// <summary>
        /// The nodes in topological execution order, or empty if no graph is loaded.
        /// </summary>
        IReadOnlyList<SimulationNode> ExecutionOrder { get; }

        /// <summary>
        /// Node ids that are part of a cycle and were excluded from execution.
        /// </summary>
        IReadOnlyList<string> CycleNodeIds { get; }

        /// <summary>
        /// Number of completed execution cycles.
        /// </summary>
        int CycleCount { get; }
    }
}
