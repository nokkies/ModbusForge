using System.Collections.Generic;
using System.Linq;

namespace ModbusForge.Core.Simulation.Core
{
    /// <summary>
    /// Interprets a block's declared port list. The visual editor exposes three
    /// generic connector slots ("Input1", "Input2", "Output") that map onto a
    /// block's declared ports positionally; the primary output is a port literally
    /// named "Output" when the block declares one, otherwise the first declared
    /// output port. Both the execution engine and the editor rely on these rules.
    /// </summary>
    public static class BlockPorts
    {
        /// <summary>Declared input ports in declaration order.</summary>
        public static IReadOnlyList<IPort> Inputs(IEnumerable<IPort> ports) =>
            ports.Where(p => p.Direction == PortDirection.Input).ToList();

        /// <summary>Declared output ports in declaration order.</summary>
        public static IReadOnlyList<IPort> Outputs(IEnumerable<IPort> ports) =>
            ports.Where(p => p.Direction == PortDirection.Output).ToList();

        /// <summary>
        /// The port the editor's generic "Output" connector and the node's main
        /// output address refer to: a port literally named "Output" when declared,
        /// otherwise the first declared output port (e.g. the VSD's "Running").
        /// Null when the block declares no output ports.
        /// </summary>
        public static string? PrimaryOutput(IEnumerable<IPort> ports)
        {
            var outputs = Outputs(ports);
            return outputs.FirstOrDefault(p => p.Name == "Output")?.Name
                   ?? outputs.FirstOrDefault()?.Name;
        }
    }
}
