# ModbusForge v2.5.0 Release Notes

**Release Date:** January 11, 2026  
**Type:** Major Refactoring Release - Coordinator Pattern Completion

---

## 🎯 Overview

Version 2.5.0 completes the coordinator pattern refactoring by adding **TrendCoordinator** and **ConfigurationCoordinator**, further reducing MainViewModel complexity and improving code maintainability. This release builds on v2.4.0's foundation and achieves significant code reduction across the application.

---

## ✨ What's New

### 🏗️ New Coordinators

#### 1. **TrendCoordinator**
Extracts all trend logging and sampling logic from MainViewModel.

**Responsibilities:**
- Process trend sampling for custom entries
- Read values from Modbus for trending
- Update trend logger with sampled data
- Handle trend read errors and disable monitoring on failure
- Support all data types (uint, int, real) and areas (holding, input, coils, discrete)

**Methods:**
- `ProcessTrendSamplingAsync()` - Main trend sampling loop
- `ReadValueForTrendAsync()` - Read individual trend values
- `GetTrendKey()` - Generate trend identifier
- `GetTrendDisplayName()` - Generate display name for trends

**Code Reduction:**
- `TrendTimer_Tick()`: 60 lines → 8 lines (87% reduction)
- Removed `ReadValueForTrendAsync()` from MainViewModel (~80 lines)

#### 2. **ConfigurationCoordinator**
Manages application configuration save/load operations.

**Responsibilities:**
- Save complete application configuration to JSON files
- Load configuration from JSON files
- Apply loaded configuration to application state
- Handle file dialogs and error reporting

**Methods:**
- `SaveAllConfigAsync()` - Export config to JSON
- `LoadAllConfigAsync()` - Import config from JSON
- `ApplyConfiguration()` - Apply config to app state

**Code Reduction:**
- `SaveAllConfigAsync()`: 32 lines → 5 lines (84% reduction)
- `LoadAllConfigAsync()`: 43 lines → 13 lines (70% reduction)

---

## 🔧 Technical Improvements

### Architecture Changes

1. **Dependency Injection**
   - TrendCoordinator registered as singleton
   - ConfigurationCoordinator registered as singleton
   - Both coordinators injected into MainViewModel
   - Clean separation of concerns maintained

2. **Mode-Aware Service Selection**
   - TrendCoordinator uses `IsServerMode` parameter
   - Proper service selection (ModbusTcpService vs ModbusServerService)
   - Consistent with v2.4.0 coordinator pattern

3. **MainViewModel Simplification**
   - Removed unused `_isTrending` field
   - Trend logic fully delegated to TrendCoordinator
   - Configuration logic fully delegated to ConfigurationCoordinator
   - Further reduction in MainViewModel complexity

### Code Quality

- **Reduced Complexity:** Trend and config methods are simple delegation calls
- **Improved Testability:** New coordinators can be unit tested independently
- **Better Maintainability:** Business logic centralized in coordinator classes
- **Consistent Error Handling:** Standardized across all coordinators

---

## 📊 Metrics

### Code Reduction (v2.5.0)
- **TrendTimer_Tick:** 60 lines → 8 lines (87% reduction)
- **SaveAllConfigAsync:** 32 lines → 5 lines (84% reduction)
- **LoadAllConfigAsync:** 43 lines → 13 lines (70% reduction)
- **Total extracted:** ~230 lines moved to coordinators

### Cumulative Metrics (v2.4.0 + v2.5.0)
- **Connection Logic:** ~90 lines → ~10 lines (89% reduction)
- **Register Logic:** ~200 lines → ~50 lines (75% reduction)
- **Custom Entry Logic:** ~150 lines → ~20 lines (87% reduction)
- **Trend Logic:** ~140 lines → ~8 lines (94% reduction)
- **Configuration Logic:** ~75 lines → ~18 lines (76% reduction)

### New Files
- `TrendCoordinator.cs` - 230 lines
- `ConfigurationCoordinator.cs` - 135 lines

### Total Coordinators: 5
1. ✅ ConnectionCoordinator (v2.4.0)
2. ✅ RegisterCoordinator (v2.4.0)
3. ✅ CustomEntryCoordinator (v2.4.0)
4. ✅ TrendCoordinator (v2.5.0)
5. ✅ ConfigurationCoordinator (v2.5.0)

---

## 🐛 Bug Fixes

- **Fixed:** Removed unused `_isTrending` field that was causing compiler warnings
- **Improved:** Trend sampling now properly handles mode-aware service selection
- **Improved:** Configuration load/save with better error handling

---

## 🔄 Breaking Changes

**None.** This is a pure refactoring release. All existing functionality is preserved and all UI bindings continue to work as before.

---

## 📝 Known Issues

- MainViewModel still contains simulation properties (SimulationService already handles logic)
- No unit tests yet (planned for v2.6.0)
- LiveCharts still on RC version (planned for v2.7.0)

---

## 🚀 Upgrade Notes

### For Users
- No changes required
- All features work exactly as before
- Improved stability and performance

### For Developers
- New coordinator classes in `ViewModels/Coordinators/` namespace
- MainViewModel methods now delegate to coordinators
- Trend and configuration operations pass `IsServerMode` parameter
- All coordinators follow consistent pattern established in v2.4.0

---

## 🎯 What's Next

### v2.6.0 (Planned)
- **Testing Infrastructure**
  - Unit tests for each coordinator
  - Integration tests for coordinator interactions
  - UI automation tests for critical paths
  - Comprehensive test coverage
  - xUnit + Moq + WPF UI Automation framework

### v2.7.0 (Planned)
- **Package Modernization**
  - Update LiveCharts to stable 2.0.0+
  - Remove PrivateAssets where possible
  - Address NU1701 warnings
  - Dependency cleanup and optimization

---

## 📦 Installation

### Download
- **Installer:** `ModbusForge-2.5.0-setup.exe`
- **Portable:** `ModbusForge-2.5.0-win-x64.zip`
- **Self-Contained:** `ModbusForge-2.5.0-win-x64-sc.zip`

### Requirements
- Windows 10/11 (x64)
- .NET 8.0 Runtime (included in self-contained version)

---

## 🙏 Acknowledgments

This release continues the architectural improvements started in v2.4.0, further reducing MainViewModel complexity and setting the foundation for comprehensive testing in v2.6.0.

---

## 📄 Full Changelog

### Added
- TrendCoordinator for trend logging operations
- ConfigurationCoordinator for config save/load operations
- Mode-aware service selection in trend operations
- Coordinator registration in DI container

### Changed
- TrendTimer_Tick now delegates to TrendCoordinator
- SaveAllConfigAsync now delegates to ConfigurationCoordinator
- LoadAllConfigAsync now delegates to ConfigurationCoordinator
- Improved error handling in trend sampling

### Removed
- Unused `_isTrending` field from MainViewModel
- `ReadValueForTrendAsync()` method (moved to TrendCoordinator)
- Direct trend reading logic from MainViewModel
- Direct configuration save/load logic from MainViewModel

### Fixed
- Compiler warning about unused `_isTrending` field
- Improved trend sampling error handling
- Better configuration load/save error messages

### Technical
- Version updated to 2.5.0
- Assembly version: 2.5.0.0
- File version: 2.5.0.0

---

## 📊 Coordinator Pattern Progress

| Version | Coordinators Added | MainViewModel Lines | Status |
|---------|-------------------|---------------------|---------|
| v2.3.0 | 0 | ~1,486 lines | Baseline |
| v2.4.0 | 3 (Connection, Register, CustomEntry) | ~1,400 lines | ✅ Released |
| v2.5.0 | 2 (Trend, Configuration) | ~1,200 lines | ✅ Released |
| v2.6.0 | Testing Infrastructure | ~1,200 lines | 🔄 Planned |
| v2.7.0 | Package Modernization | ~1,200 lines | 🔄 Planned |

**Target Achieved:** MainViewModel reduced by ~286 lines through coordinator extraction!

---

**Previous Release:** [v2.4.0](RELEASE-v2.4.0.md)  
**GitHub:** https://github.com/nokkies/ModbusForge  
**Issues:** https://github.com/nokkies/ModbusForge/issues
