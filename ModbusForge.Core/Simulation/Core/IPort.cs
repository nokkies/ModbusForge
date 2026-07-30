namespace ModbusForge.Core.Simulation.Core
{
    /// <summary>
    /// Definition of a function block input or output port.
    /// </summary>
    public interface IPort
    {
        string Name { get; }
        PortDirection Direction { get; }
        SimulationDataType DataType { get; }
        bool AllowMultipleConnections { get; }
    }
}
