using System.Collections.Generic;

namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// Metadata describing an available function block type.
    /// </summary>
    public sealed class FunctionBlockDescriptor
    {
        public string TypeId { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public IReadOnlyList<IPort> Ports { get; }

        public FunctionBlockDescriptor(string typeId, string displayName, string category, IReadOnlyList<IPort> ports)
        {
            TypeId = typeId;
            DisplayName = displayName;
            Category = category;
            Ports = ports;
        }
    }
}
