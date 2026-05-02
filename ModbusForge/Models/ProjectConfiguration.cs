using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModbusForge.Models
{
    /// <summary>
    /// Complete project configuration with global settings and per-Unit ID configurations.
    /// Replaces the old flat configuration structure.
    /// </summary>
    public class ProjectConfiguration
    {
        public ProjectInfo ProjectInfo { get; set; } = new();
        public GlobalSettings GlobalSettings { get; set; } = new();
        public Dictionary<byte, UnitIdConfiguration> UnitConfigurations { get; set; } = new();
        
        // Visual Simulation Data
        public List<VisualNode> VisualNodes { get; set; } = new();
        public List<NodeConnection> VisualConnections { get; set; } = new();
        
        public ProjectConfiguration()
        {
            // Initialize with default Unit ID 1 configuration
            UnitConfigurations[1] = new UnitIdConfiguration(1);
        }
    }

    /// <summary>
    /// Project metadata
    /// </summary>
    public class ProjectInfo
    {
        public string Version { get; set; } = "3.4.3";
        public string Name { get; set; } = "ModbusForge Project";
        public DateTime Created { get; set; } = DateTime.Now;
        public DateTime Modified { get; set; } = DateTime.Now;
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// Global settings that apply to the entire project (not Unit ID specific)
    /// </summary>
    public class GlobalSettings
    {
        public string Mode { get; set; } = "Server";
        public string ServerAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;
        public string ServerUnitId { get; set; } = "1";
        public byte ClientUnitId { get; set; } = 1;
        
        // UI state (global)
        public int WindowWidth { get; set; } = 1200;
        public int WindowHeight { get; set; } = 800;
        public bool WindowMaximized { get; set; } = false;
        public string SelectedTab { get; set; } = "Connection";
    }

    /// <summary>
    /// Legacy configuration for migration purposes
    /// </summary>
    public class LegacyConfiguration
    {
        public string Mode { get; set; } = "Server";
        public string ServerAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;
        public byte UnitId { get; set; } = 1;
        public string ServerUnitId { get; set; } = "1";
        public List<CustomEntry> CustomEntries { get; set; } = new();
        public List<PlcSimulationElement> PlcElements { get; set; } = new();
        public List<VisualNode> VisualNodes { get; set; } = new();
        public List<NodeConnection> VisualConnections { get; set; } = new();
        
        // Legacy monitoring settings (will be migrated to Unit ID 1)
        public bool GlobalMonitorEnabled { get; set; } = false;
        public bool HoldingMonitorEnabled { get; set; } = false;
        public int HoldingMonitorPeriodMs { get; set; } = 1000;
        public bool InputRegistersMonitorEnabled { get; set; } = false;
        public int InputRegistersMonitorPeriodMs { get; set; } = 1000;
        public bool CoilsMonitorEnabled { get; set; } = false;
        public int CoilsMonitorPeriodMs { get; set; } = 1000;
        public bool DiscreteInputsMonitorEnabled { get; set; } = false;
        public int DiscreteInputsMonitorPeriodMs { get; set; } = 1000;
        public bool CustomMonitorEnabled { get; set; } = false;
        public bool CustomReadMonitorEnabled { get; set; } = false;
        
        // Legacy simulation settings
        public bool SimulationEnabled { get; set; } = false;
        public int SimulationPeriodMs { get; set; } = 500;
        public bool PlcSimulationEnabled { get; set; } = false;
        public int PlcSimulationPeriodMs { get; set; } = 100;
        
        // Legacy register settings
        public int RegisterStart { get; set; } = 1;
        public int RegisterCount { get; set; } = 10;
        public int WriteRegisterAddress { get; set; } = 1;
        public ushort WriteRegisterValue { get; set; } = 0;
        public string RegistersGlobalType { get; set; } = "uint";
        public int CoilStart { get; set; } = 1;
        public int CoilCount { get; set; } = 16;
        public int WriteCoilAddress { get; set; } = 1;
        public bool WriteCoilState { get; set; } = false;
        public int InputRegisterStart { get; set; } = 1;
        public int InputRegisterCount { get; set; } = 10;
        public string InputRegistersGlobalType { get; set; } = "uint";
        public int DiscreteInputStart { get; set; } = 1;
        public int DiscreteInputCount { get; set; } = 16;
    }
}
