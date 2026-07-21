namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// Concrete port definition for a function block.
    /// </summary>
    public sealed class PortDefinition : IPort
    {
        public string Name { get; }
        public PortDirection Direction { get; }
        public SimulationDataType DataType { get; }
        public bool AllowMultipleConnections { get; }

        public PortDefinition(string name, PortDirection direction, SimulationDataType dataType, bool allowMultipleConnections = false)
        {
            Name = name;
            Direction = direction;
            DataType = dataType;
            AllowMultipleConnections = allowMultipleConnections;
        }
    }
}
