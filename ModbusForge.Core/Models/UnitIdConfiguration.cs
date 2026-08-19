using System;
using System.Collections.ObjectModel;
using System.Linq;
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

        // Trend pens specific to this Unit ID
        public ObservableCollection<TrendPen> TrendPens { get; set; } = new();

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
                    WriteValue = entry.WriteValue,
                    Continuous = entry.Continuous,
                    PeriodMs = entry.PeriodMs,
                    Monitor = entry.Monitor,
                    ReadPeriodMs = entry.ReadPeriodMs,
                    Area = entry.Area,
                    // Legacy shim: carried so a clone made before
                    // MigrateLegacyTrendEntries() still sees the old Trend
                    // flags (see below).
                    Trend = entry.Trend
                });
            }

            // Clone trend pens (runtime LastReadUtc is not carried over, like
            // the entries' read stamps - a clone is a fresh copy, not a
            // continuation). The stable Key IS carried over: a clone of a unit
            // keeps trending the same series.
            foreach (var pen in TrendPens)
            {
                clone.TrendPens.Add(new TrendPen
                {
                    Key = pen.Key,
                    Name = pen.Name,
                    Area = pen.Area,
                    Address = pen.Address,
                    Type = pen.Type,
                    ReadPeriodMs = pen.ReadPeriodMs
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

        /// <summary>
        /// One-time upgrade: converts legacy custom watch entries that were
        /// flagged for trending (<see cref="CustomEntry.Trend"/>, pre-pen
        /// persistence) into first-class <see cref="TrendPen"/>s, clearing the
        /// flag so the entries behave as plain watch items again. Idempotent -
        /// a second call finds nothing to move.
        /// </summary>
        /// <returns>The number of entries converted into pens.</returns>
        public int MigrateLegacyTrendEntries()
        {
            TrendPens ??= new ObservableCollection<TrendPen>();

            var moved = 0;
            foreach (var entry in CustomEntries.Where(e => e.Trend).ToList())
            {
                var alreadyCovered = TrendPens.Any(p =>
                    p.Address == entry.Address &&
                    string.Equals(p.Area, entry.Area ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                if (!alreadyCovered)
                {
                    var name = MakeUniquePenName(TrendPens, string.IsNullOrWhiteSpace(entry.Name) ? $"Trend {entry.Address}" : entry.Name);
                    TrendPens.Add(new TrendPen
                    {
                        Key = name,
                        Name = name,
                        Area = entry.Area ?? "HoldingRegister",
                        Address = entry.Address,
                        Type = string.IsNullOrWhiteSpace(entry.Type) ? "int" : entry.Type,
                        ReadPeriodMs = entry.ReadPeriodMs > 0 ? entry.ReadPeriodMs : 1000
                    });
                    moved++;
                }

                entry.Trend = false;
            }

            return moved;
        }

        /// <summary>
        /// Fills <see cref="TrendPen.Key"/> for pens saved before pens had
        /// explicit stable keys, when the key was implicit in the pen's name.
        /// The backfill is the old name itself, so every existing series keeps
        /// the key its samples were published under and no chart history is
        /// orphaned by the upgrade. Idempotent - pens with a key keep it.
        /// </summary>
        public void EnsurePenKeys()
        {
            foreach (var pen in TrendPens)
            {
                if (string.IsNullOrEmpty(pen.Key))
                {
                    pen.Key = pen.Name;
                }
            }
        }

        /// <summary>
        /// Returns <paramref name="requested"/> or "<paramref name="requested"/> 2",
        /// "... 3", ... until the name is unique among <paramref name="existing"/>.
        /// </summary>
        internal static string MakeUniquePenName(IEnumerable<TrendPen> existing, string requested)
        {
            if (existing.All(p => !string.Equals(p.Name, requested, StringComparison.Ordinal)))
            {
                return requested;
            }

            for (var suffix = 2; ; suffix++)
            {
                var candidate = $"{requested} {suffix}";
                if (existing.All(p => !string.Equals(p.Name, candidate, StringComparison.Ordinal)))
                {
                    return candidate;
                }
            }
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

            // Clone visual nodes, deep-cloning address references so clones do not
            // share mutable PlcAddressReference instances.
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
                    Input1Address = node.Input1Address?.Clone() ?? new PlcAddressReference(),
                    Input2Address = node.Input2Address?.Clone() ?? new PlcAddressReference(),
                    OutputAddress = node.OutputAddress?.Clone() ?? new PlcAddressReference(),
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
    /// Per-address data type metadata for a 16-bit Modbus register.
    /// </summary>
    public class RegisterMetadata
    {
        public int Address { get; set; }
        public string Type { get; set; } = "int";
        public bool SwapBytes { get; set; } = false;
        public bool SwapWords { get; set; } = false;
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
        public string RegistersGlobalType { get; set; } = "int";
        public bool RegistersSwapBytes { get; set; } = false;
        public bool RegistersSwapWords { get; set; } = false;
        public List<RegisterMetadata> HoldingRegisterMetadata { get; set; } = new();

        // Coils
        public int CoilStart { get; set; } = 1;
        public int CoilCount { get; set; } = 16;
        public int WriteCoilAddress { get; set; } = 1;
        public bool WriteCoilState { get; set; } = false;

        // Input registers
        public int InputRegisterStart { get; set; } = 1;
        public int InputRegisterCount { get; set; } = 10;
        public string InputRegistersGlobalType { get; set; } = "int";
        public bool InputRegistersSwapBytes { get; set; } = false;
        public bool InputRegistersSwapWords { get; set; } = false;
        public List<RegisterMetadata> InputRegisterMetadata { get; set; } = new();

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
                RegistersSwapBytes = RegistersSwapBytes,
                RegistersSwapWords = RegistersSwapWords,
                HoldingRegisterMetadata = HoldingRegisterMetadata?.Select(m => new RegisterMetadata
                {
                    Address = m.Address,
                    Type = m.Type,
                    SwapBytes = m.SwapBytes,
                    SwapWords = m.SwapWords
                }).ToList() ?? new List<RegisterMetadata>(),
                CoilStart = CoilStart,
                CoilCount = CoilCount,
                WriteCoilAddress = WriteCoilAddress,
                WriteCoilState = WriteCoilState,
                InputRegisterStart = InputRegisterStart,
                InputRegisterCount = InputRegisterCount,
                InputRegistersGlobalType = InputRegistersGlobalType,
                InputRegistersSwapBytes = InputRegistersSwapBytes,
                InputRegistersSwapWords = InputRegistersSwapWords,
                InputRegisterMetadata = InputRegisterMetadata?.Select(m => new RegisterMetadata
                {
                    Address = m.Address,
                    Type = m.Type,
                    SwapBytes = m.SwapBytes,
                    SwapWords = m.SwapWords
                }).ToList() ?? new List<RegisterMetadata>(),
                DiscreteInputStart = DiscreteInputStart,
                DiscreteInputCount = DiscreteInputCount
            };
        }
    }
}
