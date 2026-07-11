using System.Collections.Generic;

namespace ModbusForge.Models
{
    /// <summary>
    /// A portable snapshot of the entire project workspace exchanged between MainViewModel and the persistence coordinator.
    /// </summary>
    public class ProjectWorkspaceSnapshot
    {
        public string Mode { get; set; } = "Client";
        public string ServerAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;
        public string ServerUnitId { get; set; } = "1";
        public byte ClientUnitId { get; set; } = 1;
        public byte SelectedUnitId { get; set; } = 1;
        public bool IsServerMode { get; set; }
        public Dictionary<byte, UnitIdConfiguration> UnitConfigurations { get; set; } = new();
        public List<string> VisibleTabs { get; set; } = new();
        public List<VisualNode> VisualNodes { get; set; } = new();
        public List<NodeConnection> VisualConnections { get; set; } = new();
    }

    /// <summary>
    /// Result returned by project persistence operations.
    /// </summary>
    public class ProjectPersistenceResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public ProjectWorkspaceSnapshot? Snapshot { get; set; }
        public string? FilePath { get; set; }
        public byte? ImportedUnitId { get; set; }
        public UnitIdConfiguration? ImportedConfiguration { get; set; }
    }
}
