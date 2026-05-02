using System.Collections.ObjectModel;
using ModbusForge.Models;

namespace ModbusForge.Models
{
    /// <summary>
    /// Complete configuration for a single Unit ID, ensuring complete isolation between different IDs.
    /// </summary>
    public class UnitIdConfiguration
    {
        public byte UnitId { get; set; }

        // Custom entries specific to this Unit ID
        public ObservableCollection<CustomEntry> CustomEntries { get; set; } = new();

        // Simulation settings specific to this Unit ID
        public SimulationSettings SimulationSettings { get; set; } = new();

        // Monitoring settings specific to this Unit ID
        public MonitoringSettings MonitoringSettings { get; set; } = new();

        // Register settings specific to this Unit ID
        public RegisterSettings RegisterSettings { get; set; } = new();

        public UnitIdConfiguration()
        {
            UnitId = 1; // Default
        }

        public UnitIdConfiguration(byte unitId)
        {
            UnitId = unitId;
        }

        /// <summary>
        /// Creates a deep copy of this configuration
        /// </summary>
        public UnitIdConfiguration Clone()
        {
            var clone = new UnitIdConfiguration(UnitId);

            // Clone custom entries
            foreach (var entry in CustomEntries)
            {
                clone.CustomEntries.Add(new CustomEntry
                {
                    Name = entry.Name,
                    Address = entry.Address,
                    Type = entry.Type,
                    Value = entry.Value,
                    Continuous = entry.Continuous,
                    PeriodMs = entry.PeriodMs,
                    Monitor = entry.Monitor,
                    ReadPeriodMs = entry.ReadPeriodMs,
                    Area = entry.Area,
                    Trend = entry.Trend
                });
            }

            // Clone simulation settings
            clone.SimulationSettings = SimulationSettings.Clone();

            // Clone monitoring settings
            clone.MonitoringSettings = MonitoringSettings.Clone();

            // Clone register settings
            clone.RegisterSettings = RegisterSettings.Clone();

            return clone;
        }
    }

    /// <summary>
    /// Simulation settings specific to a Unit ID
    /// </summary>
    public class SimulationSettings
    {
        public bool SimulationEnabled { get; set; } = false;
        public int SimulationPeriodMs { get; set; } = 500;
        public bool PlcSimulationEnabled { get; set; } = false;
        public int PlcSimulationPeriodMs { get; set; } = 100;
        public ObservableCollection<PlcSimulationElement> PlcElements { get; set; } = new();
        public ObservableCollection<VisualNode> VisualNodes { get; set; } = new();
        public ObservableCollection<NodeConnection> VisualConnections { get; set; } = new();

        public SimulationSettings Clone()
        {
            var clone = new SimulationSettings
            {
                SimulationEnabled = SimulationEnabled,
                SimulationPeriodMs = SimulationPeriodMs,
                PlcSimulationEnabled = PlcSimulationEnabled,
                PlcSimulationPeriodMs = PlcSimulationPeriodMs
            };

            // Clone PLC elements
            foreach (var element in PlcElements)
            {
                clone.PlcElements.Add(new PlcSimulationElement
                {
                    Id = element.Id,
                    ElementType = element.ElementType,
                    Input1 = element.Input1,
                    Input2 = element.Input2,
                    Output = element.Output,
                    TimerPresetMs = element.TimerPresetMs,
                    CounterPreset = element.CounterPreset,
                    CompareValue = element.CompareValue,
                    SetDominant = element.SetDominant
                });
            }

            // Clone visual nodes (simplified - deep cloning would need more complex logic)
            foreach (var node in VisualNodes)
            {
                clone.VisualNodes.Add(new VisualNode
                {
                    Id = node.Id,
                    Name = node.Name,
                    ElementType = node.ElementType,
                    X = node.X,
                    Y = node.Y,
                    Width = node.Width,
                    Height = node.Height,
                    Input1Address = node.Input1Address,
                    Input2Address = node.Input2Address,
                    OutputAddress = node.OutputAddress,
                    TimerPresetMs = node.TimerPresetMs,
                    SetDominant = node.SetDominant,
                    CounterPreset = node.CounterPreset,
                    CompareValue = node.CompareValue
                });
            }

            // Clone visual connections
            foreach (var connection in VisualConnections)
            {
                clone.VisualConnections.Add(new NodeConnection(connection.SourceNodeId, connection.TargetNodeId, connection.TargetConnector));
            }

            return clone;
        }
    }

    /// <summary>
    /// Monitoring settings specific to a Unit ID
    /// </summary>
    public class MonitoringSettings
    {
        // Global monitoring
        public bool GlobalMonitorEnabled { get; set; } = false;

        // Register monitoring
        public bool HoldingMonitorEnabled { get; set; } = false;
        public int HoldingMonitorPeriodMs { get; set; } = 1000;
        public bool InputRegistersMonitorEnabled { get; set; } = false;
        public int InputRegistersMonitorPeriodMs { get; set; } = 1000;
        public bool CoilsMonitorEnabled { get; set; } = false;
        public int CoilsMonitorPeriodMs { get; set; } = 1000;
        public bool DiscreteInputsMonitorEnabled { get; set; } = false;
        public int DiscreteInputsMonitorPeriodMs { get; set; } = 1000;

        // Custom monitoring
        public bool CustomMonitorEnabled { get; set; } = false;
        public bool CustomReadMonitorEnabled { get; set; } = false;

        public MonitoringSettings Clone()
        {
            return new MonitoringSettings
            {
                GlobalMonitorEnabled = GlobalMonitorEnabled,
                HoldingMonitorEnabled = HoldingMonitorEnabled,
                HoldingMonitorPeriodMs = HoldingMonitorPeriodMs,
                InputRegistersMonitorEnabled = InputRegistersMonitorEnabled,
                InputRegistersMonitorPeriodMs = InputRegistersMonitorPeriodMs,
                CoilsMonitorEnabled = CoilsMonitorEnabled,
                CoilsMonitorPeriodMs = CoilsMonitorPeriodMs,
                DiscreteInputsMonitorEnabled = DiscreteInputsMonitorEnabled,
                DiscreteInputsMonitorPeriodMs = DiscreteInputsMonitorPeriodMs,
                CustomMonitorEnabled = CustomMonitorEnabled,
                CustomReadMonitorEnabled = CustomReadMonitorEnabled
            };
        }
    }

    /// <summary>
    /// Register settings specific to a Unit ID
    /// </summary>
    public class RegisterSettings
    {
        // Holding registers
        public int RegisterStart { get; set; } = 1;
        public int RegisterCount { get; set; } = 10;
        public int WriteRegisterAddress { get; set; } = 1;
        public ushort WriteRegisterValue { get; set; } = 0;
        public string RegistersGlobalType { get; set; } = "uint";

        // Coils
        public int CoilStart { get; set; } = 1;
        public int CoilCount { get; set; } = 16;
        public int WriteCoilAddress { get; set; } = 1;
        public bool WriteCoilState { get; set; } = false;

        // Input registers
        public int InputRegisterStart { get; set; } = 1;
        public int InputRegisterCount { get; set; } = 10;
        public string InputRegistersGlobalType { get; set; } = "uint";

        // Discrete inputs
        public int DiscreteInputStart { get; set; } = 1;
        public int DiscreteInputCount { get; set; } = 16;

        public RegisterSettings Clone()
        {
            return new RegisterSettings
            {
                RegisterStart = RegisterStart,
                RegisterCount = RegisterCount,
                WriteRegisterAddress = WriteRegisterAddress,
                WriteRegisterValue = WriteRegisterValue,
                RegistersGlobalType = RegistersGlobalType,
                CoilStart = CoilStart,
                CoilCount = CoilCount,
                WriteCoilAddress = WriteCoilAddress,
                WriteCoilState = WriteCoilState,
                InputRegisterStart = InputRegisterStart,
                InputRegisterCount = InputRegisterCount,
                InputRegistersGlobalType = InputRegistersGlobalType,
                DiscreteInputStart = DiscreteInputStart,
                DiscreteInputCount = DiscreteInputCount
            };
        }
    }
}
