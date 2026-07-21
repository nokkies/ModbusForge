using System;
using System.Globalization;

namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// Immutable typed value used by the simulation engine.
    /// </summary>
    public sealed class SimulationValue : ISimulationValue
    {
        public SimulationDataType DataType { get; }

        private readonly object _value;

        private SimulationValue(SimulationDataType dataType, object value)
        {
            DataType = dataType;
            _value = value;
        }

        public static ISimulationValue Bool(bool value) => new SimulationValue(SimulationDataType.Bool, value);
        public static ISimulationValue Int16(short value) => new SimulationValue(SimulationDataType.Int16, value);
        public static ISimulationValue UInt16(ushort value) => new SimulationValue(SimulationDataType.UInt16, value);
        public static ISimulationValue Int32(int value) => new SimulationValue(SimulationDataType.Int32, value);
        public static ISimulationValue UInt32(uint value) => new SimulationValue(SimulationDataType.UInt32, value);
        public static ISimulationValue Real(double value) => new SimulationValue(SimulationDataType.Real, value);
        public static ISimulationValue String(string value) => new SimulationValue(SimulationDataType.String, value ?? string.Empty);

        public static ISimulationValue FromObject(SimulationDataType dataType, object? value)
        {
            return dataType switch
            {
                SimulationDataType.Bool => Bool(Convert.ToBoolean(value)),
                SimulationDataType.Int16 => Int16(Convert.ToInt16(value)),
                SimulationDataType.UInt16 => UInt16(Convert.ToUInt16(value)),
                SimulationDataType.Int32 => Int32(Convert.ToInt32(value)),
                SimulationDataType.UInt32 => UInt32(Convert.ToUInt32(value)),
                SimulationDataType.Real => Real(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
                SimulationDataType.String => String(Convert.ToString(value) ?? string.Empty),
                _ => Bool(false)
            };
        }

        public bool AsBool() => _value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) ? parsed : s.Trim() is "1" or "yes" or "true" or "on",
            IConvertible c => c.ToInt32(CultureInfo.InvariantCulture) != 0,
            _ => _value is not null
        };

        public short AsInt16() => Convert.ToInt16(_value, CultureInfo.InvariantCulture);
        public ushort AsUInt16() => Convert.ToUInt16(_value, CultureInfo.InvariantCulture);
        public int AsInt32() => Convert.ToInt32(_value, CultureInfo.InvariantCulture);
        public uint AsUInt32() => Convert.ToUInt32(_value, CultureInfo.InvariantCulture);
        public double AsReal() => Convert.ToDouble(_value, CultureInfo.InvariantCulture);
        public string AsString() => Convert.ToString(_value, CultureInfo.InvariantCulture) ?? string.Empty;

        public override string ToString() => AsString();
    }
}
