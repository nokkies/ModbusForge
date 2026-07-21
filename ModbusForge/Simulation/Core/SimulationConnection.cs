using System;

namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// A connection between two function block ports.
    /// </summary>
    public sealed class SimulationConnection
    {
        public string SourceNodeId { get; }
        public string SourcePortName { get; }
        public string TargetNodeId { get; }
        public string TargetPortName { get; }

        public SimulationConnection(string sourceNodeId, string sourcePortName, string targetNodeId, string targetPortName)
        {
            if (string.IsNullOrWhiteSpace(sourceNodeId))
                throw new ArgumentException("Source node id is required.", nameof(sourceNodeId));
            if (string.IsNullOrWhiteSpace(sourcePortName))
                throw new ArgumentException("Source port name is required.", nameof(sourcePortName));
            if (string.IsNullOrWhiteSpace(targetNodeId))
                throw new ArgumentException("Target node id is required.", nameof(targetNodeId));
            if (string.IsNullOrWhiteSpace(targetPortName))
                throw new ArgumentException("Target port name is required.", nameof(targetPortName));

            SourceNodeId = sourceNodeId;
            SourcePortName = sourcePortName;
            TargetNodeId = targetNodeId;
            TargetPortName = targetPortName;
        }
    }
}
