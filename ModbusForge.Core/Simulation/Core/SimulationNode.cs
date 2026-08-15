using System;
using System.Collections.Generic;
using ModbusForge.Models;

namespace ModbusForge.Core.Simulation.Core
{
    /// <summary>
    /// Runtime instance of a function block in a simulation graph.
    /// </summary>
    public sealed class SimulationNode
    {
        public string Id { get; }
        public string Name { get; set; }
        public IFunctionBlock Block { get; }
        public IStateBag State { get; } = new StateBag();

        /// <summary>
        /// When false the engine skips re-evaluating this node; its last outputs
        /// remain visible downstream (frozen), so disabling a block does not drop the signal.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Values produced by the block for each output port during the last execution cycle.
        /// </summary>
        public Dictionary<string, ISimulationValue> OutputValues { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Optional Modbus address bindings for input ports.
        /// </summary>
        public Dictionary<string, PlcAddressReference?> InputBindings { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Optional Modbus address bindings for output ports.
        /// </summary>
        public Dictionary<string, PlcAddressReference?> OutputBindings { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Typed parameters configured for this instance (e.g., timer preset).
        /// </summary>
        public Dictionary<string, object?> Parameters { get; } = new(StringComparer.Ordinal);

        public SimulationNode(string id, string name, IFunctionBlock block)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Node id is required.", nameof(id));

            Id = id;
            Name = name;
            Block = block ?? throw new ArgumentNullException(nameof(block));
        }
    }
}
