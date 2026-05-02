# 🔧 Unit ID Isolation & Save Structure Redesign

## 🎯 Problem Statement

### Current Issues
1. **Shared State**: Checkboxes (monitoring, simulation, etc.) are shared across all Unit IDs
2. **Cross-Contamination**: Changing settings for Unit ID 1 affects Unit ID 2, 3, etc.
3. **Save Structure**: Separate Custom save/load instead of unified project approach
4. **Missing Import/Export**: No way to import/export Unit ID configurations

### User Impact
- ❌ Cannot have unique monitoring setups per Unit ID
- ❌ Simulation settings affect all Unit IDs globally
- ❌ Confusing save/load workflow
- ❌ No configuration portability between projects

## 🏗️ Proposed Solution

### 1. Per-Unit ID State Management

#### New Data Structure
```csharp
public class UnitIdConfiguration
{
    public byte UnitId { get; set; }
    public ObservableCollection<CustomEntry> CustomEntries { get; set; }
    public SimulationSettings SimulationSettings { get; set; }
    public MonitoringSettings MonitoringSettings { get; set; }
    public RegisterSettings RegisterSettings { get; set; }
}

public class SimulationSettings
{
    public bool SimulationEnabled { get; set; }
    public bool PlcSimulationEnabled { get; set; }
    public int SimulationPeriodMs { get; set; }
    public ObservableCollection<PlcSimulationElement> PlcElements { get; set; }
    public List<VisualNode> VisualNodes { get; set; }
    public List<VisualConnection> VisualConnections { get; set; }
}

public class MonitoringSettings
{
    public bool HoldingMonitorEnabled { get; set; }
    public bool InputRegistersMonitorEnabled { get; set; }
    public bool CoilsMonitorEnabled { get; set; }
    public bool DiscreteInputsMonitorEnabled { get; set; }
    public bool CustomMonitorEnabled { get; set; }
    public bool CustomReadMonitorEnabled { get; set; }
    public int HoldingMonitorPeriodMs { get; set; }
    // ... other monitoring settings
}
```

#### Updated MainViewModel
```csharp
// Per-Unit ID configurations
[ObservableProperty]
private Dictionary<byte, UnitIdConfiguration> _unitConfigurations = new();

[ObservableProperty]
private byte _selectedUnitId = 1;

// Current active configuration (binds to selected Unit ID)
public UnitIdConfiguration CurrentConfig => UnitConfigurations.GetValueOrDefault(SelectedUnitId, new UnitIdConfiguration { UnitId = SelectedUnitId });

// Properties that now delegate to current config
public ObservableCollection<CustomEntry> CustomEntries => CurrentConfig.CustomEntries;
public bool SimulationEnabled => CurrentConfig.SimulationSettings.SimulationEnabled;
public bool HoldingMonitorEnabled => CurrentConfig.MonitoringSettings.HoldingMonitorEnabled;
// ... etc for all shared properties
```

### 2. Unified Save/Load Structure

#### New Project Format
```json
{
  "ProjectInfo": {
    "Version": "3.5.0",
    "Name": "My Modbus Project",
    "Created": "2026-03-10T11:30:00Z",
    "Modified": "2026-03-10T11:30:00Z"
  },
  "GlobalSettings": {
    "Mode": "Server",
    "ServerAddress": "0.0.0.0",
    "Port": 502,
    "ServerUnitId": "1,2,3"
  },
  "UnitConfigurations": {
    "1": {
      "UnitId": 1,
      "CustomEntries": [...],
      "SimulationSettings": {...},
      "MonitoringSettings": {...},
      "RegisterSettings": {...}
    },
    "2": {
      "UnitId": 2,
      "CustomEntries": [...],
      "SimulationSettings": {...},
      "MonitoringSettings": {...},
      "RegisterSettings": {...}
    }
  }
}
```

#### New Commands
- **Save Project** (`Ctrl+S`) - Save entire project with all Unit IDs
- **Load Project** (`Ctrl+O`) - Load entire project
- **Import Unit IDs** - Import Unit ID configurations from another project
- **Export Unit IDs** - Export selected Unit ID configurations to separate file

### 3. Implementation Plan

#### Phase 1: Data Structure Refactor
1. Create `UnitIdConfiguration`, `SimulationSettings`, `MonitoringSettings` classes
2. Update `MainViewModel` to use per-Unit ID configurations
3. Implement property delegation to current configuration
4. Add configuration switching logic

#### Phase 2: Save/Load Redesign
1. Remove `SaveCustomCommand`, `LoadCustomCommand`
2. Add `SaveProjectCommand`, `LoadProjectCommand`
3. Add `ImportUnitIdsCommand`, `ExportUnitIdsCommand`
4. Update configuration coordinator for new format
5. Migrate existing JSON structure

#### Phase 3: UI Updates
1. Update menu items (Save/Load → Save Project/Load Project)
2. Add Import/Export Unit IDs menu items
3. Update keyboard shortcuts
4. Add confirmation dialogs for project operations

#### Phase 4: Testing & Migration
1. Test per-Unit ID state isolation
2. Test project save/load functionality
3. Test import/export functionality
4. Create migration tool for old format

## 🔄 Migration Strategy

### Backward Compatibility
```csharp
// Detect old format and migrate
private bool IsOldFormat(string json) => json.Contains("\"CustomEntries\":") && !json.Contains("\"UnitConfigurations\":");

private ProjectConfiguration MigrateOldFormat(string oldJson)
{
    var oldConfig = JsonSerializer.Deserialize<OldConfiguration>(oldJson);
    var newConfig = new ProjectConfiguration
    {
        GlobalSettings = new GlobalSettings
        {
            Mode = oldConfig.Mode,
            ServerAddress = oldConfig.ServerAddress,
            Port = oldConfig.Port,
            ServerUnitId = oldConfig.ServerUnitId
        },
        UnitConfigurations = new Dictionary<byte, UnitIdConfiguration>
        {
            [oldConfig.UnitId] = new UnitIdConfiguration
            {
                UnitId = oldConfig.UnitId,
                CustomEntries = oldConfig.CustomEntries,
                SimulationSettings = new SimulationSettings
                {
                    PlcElements = oldConfig.PlcElements,
                    VisualNodes = oldConfig.VisualNodes
                }
            }
        }
    };
    return newConfig;
}
```

## 🎯 Benefits

### ✅ What This Fixes
- **Isolation**: Each Unit ID has its own checkboxes, settings, and data
- **No Cross-Contamination**: Changing Unit ID 2 doesn't affect Unit ID 1
- **Unified Workflow**: Single project file contains everything
- **Portability**: Import/export Unit ID configurations between projects
- **Clarity**: Clear separation between global and per-Unit ID settings

### 🚀 New Capabilities
- Multiple simulation setups running independently
- Different monitoring configurations per Unit ID
- Project templates with pre-configured Unit IDs
- Configuration sharing between projects
- Better organization and maintainability

## 📋 Implementation Checklist

### Phase 1: Data Structure (Week 1)
- [ ] Create configuration classes
- [ ] Update MainViewModel
- [ ] Implement property delegation
- [ ] Add configuration switching

### Phase 2: Save/Load (Week 2)
- [ ] Remove old save/load commands
- [ ] Add new project commands
- [ ] Update configuration coordinator
- [ ] Implement migration logic

### Phase 3: UI (Week 3)
- [ ] Update menu items
- [ ] Add import/export UI
- [ ] Update shortcuts
- [ ] Add confirmation dialogs

### Phase 4: Testing (Week 4)
- [ ] Unit tests for isolation
- [ ] Integration tests for save/load
- [ ] Migration testing
- [ ] User acceptance testing

## 🔍 Risk Assessment

### Low Risk
- Data structure changes (internal only)
- Command additions (non-breaking)

### Medium Risk
- Save/load format changes (mitigated with migration)
- UI workflow changes (mitigated with clear messaging)

### High Risk
- None identified with proper planning and testing

## 📊 Timeline Estimate
- **Total Duration**: 4 weeks
- **Development**: 3 weeks
- **Testing & Polish**: 1 week
- **Release**: v3.5.0

This plan ensures complete isolation between Unit IDs while providing a much cleaner save/load experience for users.
