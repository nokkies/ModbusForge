# ModbusForge v4.5.x Release Summary

**Release Date:** March 27, 2026  
**Latest Version:** v4.5.14  
**Series:** v4.5.5 - v4.5.14

---

## Overview

The v4.5.x series focused on code quality improvements, UI refinements, and bug fixes following the v4.5.3 UI refinement release. This series addressed memory leaks, hardcoded values, silent failures, and added user experience improvements.

---

## Changes by Version

### v4.5.5 - Right-Click Delete for Nodes
- Added context menu with delete option when right-clicking nodes
- Implemented `Node_MouseRightButtonDown` handler
- Delete functionality uses existing `DeleteNodeCommand`

### v4.5.6 - Memory Leak Fix
- **Critical:** Fixed memory leak from stale UI event handlers
- Added `_nodeEventHandlers` dictionary to track attached handlers
- Implemented `CleanupNodeEventHandlers()` method for proper cleanup
- Event handlers now properly detached when nodes are removed
- Added missing `using System.ComponentModel;` directive

### v4.5.7 - Fix Empty Catch Blocks
- Replaced silent failure catch blocks with proper logging
- Files affected: `MainViewModel.cs`
- Empty `catch { }` blocks now log to `ILogger` with appropriate levels
- Changed: `catch { }` → `catch (Exception ex) { _logger.LogDebug/LogWarning(...) }`

### v4.5.8 - Extract Magic Numbers to Constants
- Added `Layout Constants` region in `VisualNodeEditor.xaml.cs`
- New constants:
  - `NodeHeaderHeight = 24`
  - `ConnectorOffset = 6`
  - `SingleInputVerticalRatio = 0.5`
  - `DualInputTopRatio = 0.333`
  - `DualInputBottomRatio = 0.667`
- Replaced hardcoded values in `CalculateConnectorPosition()`

### v4.5.9 - Fix Dialog Title
- Fixed hardcoded "TEST DIALOG" title in `ConfigureButton_Click`
- Title now correctly uses `dialogTitle` variable
- Removed unused `customEntries` variable

### v4.5.10 - Standardize Logging
- Removed mixed logging approach in `MainViewModel.Debug.cs`
- Eliminated custom file logging (`DebugLogPath`, `WriteToDebugLog()`)
- Kept UI debug messages collection only
- Logging now handled exclusively by `ILogger` infrastructure

### v4.5.11 - Refactor CreateNodeElement
- Split ~350 line `CreateNodeElement()` method into focused sub-methods
- New methods:
  - `CreateNodeHeader()` - Creates header with title and address
  - `CreateNodeContent()` - Creates content grid with connectors
  - `CreateInlineAddressEditor()` - Creates address editing controls
  - `SetupNodeEventHandlers()` - Sets up property changed handlers
  - `UpdateLiveValueDisplay()` - Updates live value visual state
  - `UpdateLiveTextForElementType()` - Formats live value text
  - `CreateNodeFooter()` - Creates footer with parameters
  - `AddFooterControls()` - Adds footer parameter controls
  - `IsIoNode()` - Helper to check if node is I/O type
  - `ValidateAndUpdateAddress()` - Validates address input

### v4.5.12 - LINQ .ToList() Review
- **Skipped:** After review, all `.ToList()` calls were necessary for:
  - Collection modification safety
  - Thread safety during async operations
  - JSON serialization requiring materialization

### v4.5.13 - Address TextBox Input Validation
- Added `PreviewTextInput` handler to block non-digit characters
- Implemented `ValidateAndUpdateAddress()` method
- Visual feedback: Red border for invalid input
- Tooltips indicate valid input range (0-65535)
- Empty input restores previous value
- Selects all text on validation failure

### v4.5.14 - Start Maximized
- Added `WindowState="Maximized"` to `MainWindow.xaml`
- Application now starts maximized on launch

---

## Technical Debt Addressed

| Issue | Resolution |
|-------|------------|
| Memory leaks | Proper event handler cleanup |
| Silent failures | All catch blocks now log errors |
| Magic numbers | Extracted to named constants |
| Long methods | `CreateNodeElement` refactored into 10+ methods |
| Mixed logging | Consolidated to `ILogger` only |
| Input validation | Added numeric validation with visual feedback |

---

## Files Modified

- `VisualNodeEditor.xaml.cs` - Most changes (refactoring, constants, validation)
- `MainWindow.xaml` - Maximized window state
- `MainViewModel.cs` - Empty catch block fixes
- `MainViewModel.Debug.cs` - Logging standardization
- `ModbusForge.csproj` - Version updates

---

## Breaking Changes

None. All changes are backward compatible.

---

## Tags

`v4.5.5` `v4.5.6` `v4.5.7` `v4.5.8` `v4.5.9` `v4.5.10` `v4.5.11` `v4.5.13` `v4.5.14`
