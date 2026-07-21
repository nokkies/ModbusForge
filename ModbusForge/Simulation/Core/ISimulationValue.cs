namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// A typed value carried between function block ports.
    /// </summary>
    public interface ISimulationValue
    {
        SimulationDataType DataType { get; }

        bool AsBool();
        short AsInt16();
        ushort AsUInt16();
        int AsInt32();
        uint AsUInt32();
        double AsReal();
        string AsString();
    }
}
