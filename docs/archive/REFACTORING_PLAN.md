# ModbusForge v2.3.0 - Code Improvement Plan

## Overview
This document tracks planned improvements for ModbusForge v2.3.0, focusing on code complexity reduction, technical debt cleanup, and package compatibility improvements.

---

## 🏗️ 1. Code Complexity Issues

### Current State (v2.2.2)
- **MainViewModel.cs**: 1,897 lines (extremely large)
- **Constructor**: 136 lines (violates SRP)
- **Complex Methods**: 6 methods flagged by CodeFactor
- **Cyclomatic Complexity**: High decision point density

### 📋 Refactoring Plan

#### Phase 1: Extract Service Coordinators
**Target Version**: v2.2.3

**New Classes to Create:**
```csharp
// ViewModels/Coordinators/
├── ConnectionCoordinator.cs     // Handle connect/disconnect logic
├── RegisterCoordinator.cs      // Register read/write operations  
├── CustomEntryCoordinator.cs   // Custom tab management
├── SimulationCoordinator.cs    // Simulation features
└── TrendCoordinator.cs         // Trend logging and charts
```

**Responsibilities:**
- **ConnectionCoordinator**: ConnectAsync, DisconnectAsync, RunDiagnosticsAsync
- **RegisterCoordinator**: All register read/write methods (ReadRegistersAsync, WriteRegisterAsync, etc.)
- **CustomEntryCoordinator**: Custom entry CRUD, ReadCustomNowAsync, WriteCustomNowAsync
- **SimulationCoordinator**: Simulation timer and waveform generation
- **TrendCoordinator**: Trend data management and chart integration

#### Phase 2: Extract Command Handlers
**Target Version**: v2.2.3

**New Classes to Create:**
```csharp
// ViewModels/Commands/
├── ConnectionCommands.cs       // Connect, Disconnect, Diagnostics
├── RegisterCommands.cs         // All register operations
├── CustomCommands.cs           // Custom entry commands
├── FileCommands.cs             // Save/Load operations
└── SimulationCommands.cs       // Simulation controls
```

#### Phase 3: Simplify MainViewModel
**Target Version**: v2.2.3

**After Refactoring:**
- **MainViewModel**: ~200 lines (down from 1,897)
- **Constructor**: ~20 lines (down from 136)
- **Single Responsibility**: UI state coordination only
- **Delegate to Coordinators**: All business logic moved out

---

## 🧹 2. Legacy Code Cleanup

### Current State (v2.2.2)
- **Options.cs**: Contains disabled `#if false` blocks
- **Duplicate Definitions**: TypeOptions, AreaOptions, ModeOptions
- **XAML vs C# Conflict**: Arrays defined in both places
- **Technical Debt**: Architecture decision leftovers

### 📋 Cleanup Plan

#### Phase 1: Remove Disabled Code
**Target Version**: v2.2.3

**Files to Clean:**
```csharp
// ViewModels/Options.cs - COMPLETE REMOVAL
#if false
public static class ModeOptions { ... }     // REMOVE
public static class TypeOptions { ... }     // REMOVE  
public static class AreaOptions { ... }     // REMOVE
#endif
```

**Action**: Delete entire Options.cs file - arrays are now in XAML resources

#### Phase 2: Verify XAML Resource Integration
**Target Version**: v2.2.3

**Check MainWindow.xaml:**
```xml
<!-- Verify these arrays exist and work -->
<x:Array Type="sys:String" x:Key="ModeOptionsAll">...</x:Array>
<x:Array Type="sys:String" x:Key="TypeOptionsAll">...</x:Array>
<x:Array Type="sys:String" x:Key="AreaOptionsAll">...</x:Array>
```

#### Phase 3: Update References
**Target Version**: v2.2.3

**Search and Replace:**
- Remove any remaining `Options.TypeOptions.All` references
- Remove any remaining `Options.AreaOptions.All` references  
- Remove any remaining `Options.ModeOptions.All` references

---

## 📦 3. Package Compatibility Issues

### Current State (v2.2.2)
- **PrivateAssets="all"**: Used for compatibility suppression
- **NU1701 Warnings**: Suppressed for legacy packages
- **Mixed Versions**: LiveChartsCore rc5 + stable SkiaSharp
- **Compatibility Layer**: Complex package management

### 📋 Package Modernization Plan

#### Phase 1: Update LiveCharts
**Target Version**: v2.2.4

**Current:**
```xml
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.0.0-rc5.4" />
```

**Target:**
```xml
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.0.0" />
```

**Risks:**
- API changes between rc5 and stable
- Potential breaking changes in chart bindings
- Need to test all chart functionality

#### Phase 2: Remove PrivateAssets Where Possible
**Target Version**: v2.2.4

**Current PrivateAssets="all":**
```xml
<PackageReference Include="NModbus4" Version="2.0.5516.31020" PrivateAssets="all" />
<PackageReference Include="OpenTK" Version="3.3.1" PrivateAssets="all" />
<PackageReference Include="OpenTK.GLWpfControl" Version="3.3.0" PrivateAssets="all" />
<PackageReference Include="SkiaSharp.Views.WPF" Version="3.116.1" PrivateAssets="all" />
```

**Analysis Required:**
- Can NModbus4 be used without PrivateAssets?
- Are OpenTK packages still needed?
- Can SkiaSharp.Views.WPF be standard?

#### Phase 3: Address NU1701 Warnings
**Target Version**: v2.2.5

**Current Suppression:**
```xml
<NoWarn>$(NoWarn);NU1701</NoWarn>
```

**Action Plan:**
1. Identify which packages cause NU1701
2. Update or replace problematic packages
3. Remove suppression if possible
4. Document unavoidable warnings

---

## 📊 Implementation Tracking

### Version Progress

| Version | Code Complexity | Legacy Cleanup | Package Compatibility | Status |
|---------|----------------|----------------|----------------------|---------|
| v2.2.2 | ❌ 1,897 lines | ❌ Disabled code | ❌ Mixed versions | Previous |
| v2.2.3 | 🔄 ~200 lines | ✅ **COMPLETED** | 🔄 Update LiveCharts | **In Progress** |
| v2.2.4 | ✅ Clean | ✅ Clean | 🔄 Remove PrivateAssets | Planned |
| v2.2.5 | ✅ Clean | ✅ Clean | ✅ Modern packages | Final |
| v2.3.0 | ✅ All improvements complete | ✅ All improvements complete | ✅ All improvements complete | Release Target |

### Risk Assessment

| Change | Risk Level | Impact | Mitigation |
|--------|------------|--------|------------|
| MainViewModel Refactor | **High** | Core functionality | Comprehensive testing, phased approach |
| Options.cs Removal | **Low** | Build errors | Verify XAML bindings first |
| LiveCharts Update | **Medium** | Chart features | Backup current version, test thoroughly |
| Package Changes | **Medium** | Dependencies | Test in separate branch first |

---

## 🎯 Success Criteria

### v2.2.3 Success Metrics
- [ ] MainViewModel < 300 lines
- [ ] No methods > 60 lines
- [ ] Options.cs completely removed
- [ ] All CodeFactor warnings resolved
- [ ] All existing functionality preserved

### v2.2.4 Success Metrics
- [ ] LiveCharts stable version working
- [ ] No PrivateAssets="all" where possible
- [ ] All chart features working

### v2.2.5 Success Metrics
- [ ] No NU1701 warnings (or documented)
- [ ] All packages on stable versions
- [ ] Improved build performance

### v2.3.0 Release Criteria
- [ ] All v2.2.x improvements complete
- [ ] Full regression testing passed
- [ ] Documentation updated
- [ ] Ready for release

---

## 🚀 Implementation Order

### Week 1: Foundation
1. Create coordinator classes structure
2. Extract ConnectionCoordinator
3. Test connection functionality

### Week 2: Core Logic
1. Extract RegisterCoordinator
2. Extract CustomEntryCoordinator
3. Test all read/write operations

### Week 3: UI & Cleanup
1. Extract remaining coordinators
2. Remove Options.cs
3. Update MainViewModel to use coordinators

### Week 4: Packaging
1. Update LiveCharts to stable
2. Test all chart features
3. Address package compatibility

---

## 📝 Notes & Decisions

### Architecture Decisions Made
- **Coordinator Pattern**: Chosen over splitting into multiple ViewModels to maintain single window context
- **Phased Approach**: Minimize risk by testing each coordinator independently
- **Backward Compatibility**: All existing UI bindings must continue working

### Testing Strategy
- Unit tests for each coordinator
- Integration tests for coordinator interactions
- UI automation tests for critical paths
- Performance regression testing

---

*Last Updated: January 2025*
*Target Release: v2.3.0 Q1 2025*

---

## 📋 Version Strategy Summary

- **v2.2.3**: Code complexity & legacy cleanup (MainViewModel refactor, Options.cs removal)
- **v2.3.0**: Server freeze fix and graceful shutdown improvements
- **v2.4.0**: ✅ **COMPLETED** - Coordinator pattern (ConnectionCoordinator, RegisterCoordinator, CustomEntryCoordinator)
- **v2.5.0**: Remaining coordinators (SimulationCoordinator, TrendCoordinator, ConfigurationCoordinator)
- **v2.6.0**: Testing infrastructure (Unit tests, Integration tests, UI automation tests)
- **v2.7.0**: Package compatibility (LiveCharts stable, PrivateAssets cleanup, NU1701 warnings)
