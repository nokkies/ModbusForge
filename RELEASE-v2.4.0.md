# ModbusForge v2.4.0 Release Notes

**Release Date:** January 11, 2026  
**Type:** Major Refactoring Release

---

## 🎯 Overview

Version 2.4.0 introduces a significant architectural improvement through the **Coordinator Pattern**, delegating connection, register, and custom entry operations from MainViewModel to specialized coordinator classes. This refactoring improves code maintainability, testability, and sets the foundation for future enhancements.

---

## ✨ What's New

### 🏗️ Coordinator Pattern Implementation

**Three new coordinator classes have been created:**

#### 1. **ConnectionCoordinator**
- Handles all connection/disconnection logic
- Manages diagnostics operations
- Supports both Client and Server modes
- Provides mode-aware service selection

**Methods:**
- `ConnectAsync()` - Connect to Modbus server or start Modbus server
- `DisconnectAsync()` - Graceful disconnect with timeout
- `RunDiagnosticsAsync()` - TCP and Modbus protocol diagnostics
- `CanConnect()` / `CanDisconnect()` - Command state management

#### 2. **RegisterCoordinator**
- Manages all register and coil read/write operations
- Handles data type conversions (uint, int, real, string)
- Supports monitoring and error handling
- Mode-aware service selection for client/server operations

**Methods:**
- `ReadRegistersAsync()` - Read holding registers
- `ReadInputRegistersAsync()` - Read input registers
- `ReadCoilsAsync()` - Read coils
- `ReadDiscreteInputsAsync()` - Read discrete inputs
- `WriteRegisterAsync()` - Write single register
- `WriteCoilAsync()` - Write single coil
- `WriteRegisterAtAsync()` - Write register at specific address
- `WriteFloatAtAsync()` - Write float value (2 registers)
- `WriteStringAtAsync()` - Write string value
- `WriteCoilAtAsync()` - Write coil at specific address

#### 3. **CustomEntryCoordinator**
- Manages custom entry operations
- Delegates to RegisterCoordinator for read/write operations
- Handles save/load functionality
- Supports all data types and areas

**Methods:**
- `AddCustomEntry()` - Add new custom entry
- `WriteCustomNowAsync()` - Write custom entry value
- `SaveCustomAsync()` - Save custom entries to file
- `LoadCustomAsync()` - Load custom entries from file

---

## 🔧 Technical Improvements

### Architecture Changes

1. **Dependency Injection**
   - All coordinators registered as singletons in `App.xaml.cs`
   - Proper service lifetime management
   - Clean separation of concerns

2. **Mode-Aware Service Selection**
   - All operations now pass `IsServerMode` parameter
   - Coordinators select appropriate service (ModbusTcpService or ModbusServerService)
   - Eliminates mode-switching bugs

3. **MainViewModel Simplification**
   - Connection methods reduced from ~90 lines to ~10 lines
   - Register methods reduced from ~200 lines to ~50 lines
   - Custom entry methods reduced from ~150 lines to ~20 lines
   - All business logic delegated to coordinators

### Code Quality

- **Reduced Complexity:** MainViewModel methods are now simple delegation calls
- **Improved Testability:** Coordinators can be unit tested independently
- **Better Maintainability:** Business logic centralized in coordinator classes
- **Consistent Error Handling:** Standardized across all operations

---

## 🐛 Bug Fixes

- **Fixed:** Mode-aware service selection ensures correct service is used in all operations
- **Fixed:** Graceful shutdown timeout prevents application freeze on close
- **Improved:** Error handling and user feedback across all operations

---

## 📊 Metrics

### Code Reduction
- **Connection Logic:** ~90 lines → ~10 lines (89% reduction)
- **Register Logic:** ~200 lines → ~50 lines (75% reduction)
- **Custom Entry Logic:** ~150 lines → ~20 lines (87% reduction)

### New Files
- `ConnectionCoordinator.cs` - 224 lines
- `RegisterCoordinator.cs` - 366 lines
- `CustomEntryCoordinator.cs` - 216 lines

---

## 🔄 Breaking Changes

**None.** This is a pure refactoring release. All existing functionality is preserved and all UI bindings continue to work as before.

---

## 📝 Known Issues

- MainViewModel still contains simulation and trend logic (planned for v2.5.0)
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
- All operations pass `IsServerMode` parameter for proper service selection

---

## 🎯 What's Next

### v2.5.0 (Planned)
- **SimulationCoordinator** - Extract simulation logic
- **TrendCoordinator** - Extract trend logging logic
- **ConfigurationCoordinator** - Extract save/load config logic
- Further MainViewModel size reduction (~300 lines target)

### v2.6.0 (Planned)
- **Testing Infrastructure**
  - Unit tests for each coordinator
  - Integration tests for coordinator interactions
  - UI automation tests for critical paths
- Comprehensive test coverage

### v2.7.0 (Planned)
- **Package Modernization**
  - Update LiveCharts to stable 2.0.0+
  - Remove PrivateAssets where possible
  - Address NU1701 warnings
  - Dependency cleanup

---

## 📦 Installation

### Download
- **Installer:** `ModbusForge-2.4.0-setup.exe`
- **Portable:** `ModbusForge-2.4.0-win-x64.zip`
- **Self-Contained:** `ModbusForge-2.4.0-win-x64-sc.zip`

### Requirements
- Windows 10/11 (x64)
- .NET 8.0 Runtime (included in self-contained version)

---

## 🙏 Acknowledgments

This release represents a major step forward in ModbusForge's architecture, setting the foundation for improved maintainability, testability, and future enhancements.

---

## 📄 Full Changelog

### Added
- ConnectionCoordinator for connection management
- RegisterCoordinator for register/coil operations
- CustomEntryCoordinator for custom entry management
- Mode-aware service selection throughout application
- Coordinator registration in DI container

### Changed
- MainViewModel methods now delegate to coordinators
- All operations pass IsServerMode parameter
- Improved error handling and user feedback
- Simplified method implementations

### Fixed
- Mode-aware service selection bugs
- Graceful shutdown timeout issues
- Consistent error handling across operations

### Technical
- Version updated to 2.4.0
- Assembly version: 2.4.0.0
- File version: 2.4.0.0

---

**Previous Release:** [v2.3.0](RELEASE-v2.3.0.md)  
**GitHub:** https://github.com/nokkies/ModbusForge  
**Issues:** https://github.com/nokkies/ModbusForge/issues
