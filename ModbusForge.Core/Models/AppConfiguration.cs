using System.Collections.Generic;

namespace ModbusForge.Models
{
    public class AppConfiguration
    {
        public string? Mode { get; set; }
        public string? ServerAddress { get; set; }
        public int Port { get; set; }
        public byte UnitId { get; set; }

        // Project-wide connection profiles and active selection
        public List<ConnectionProfile>? Profiles { get; set; }
        public string? ActiveProfileId { get; set; }

        // Register grid state
        public int StartAddress { get; set; }
        public int RegisterCount { get; set; }
        public PlcArea SelectedArea { get; set; }
        public string? GlobalType { get; set; }
        public bool SwapBytes { get; set; }
        public bool SwapWords { get; set; }

        public List<CustomEntry>? CustomEntries { get; set; }
        public List<PlcSimulationElement>? PlcElements { get; set; }
        public List<VisualNode>? VisualNodes { get; set; }
        public List<NodeConnection>? VisualConnections { get; set; }
    }
}
