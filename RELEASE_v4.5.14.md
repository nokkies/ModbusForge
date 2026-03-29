# ModbusForge v4.5.14 Release

**Release Date:** March 27, 2026

## Summary

The v4.5.14 release represents the culmination of the v4.5.x code quality improvement series. This release includes UI enhancements, significant code refactoring, memory leak fixes, and improved input validation.

## What's New

### 🚀 Maximized Startup
- Application now starts maximized for better usability on modern displays
- No user configuration needed - automatic maximization on launch

### 🔒 Input Validation
- Address TextBoxes in the visual node editor now validate numeric input
- Visual feedback: Red border appears when invalid input is entered
- Blocks non-digit characters during typing
- Empty input reverts to previous value

### ♻️ Code Quality Improvements

**Major Refactoring**
- `CreateNodeElement()` method refactored from ~350 lines into 10+ focused sub-methods:
  - `CreateNodeHeader()` - Header creation with title and address
  - `CreateNodeContent()` - Content grid with connectors
  - `CreateInlineAddressEditor()` - Address editing controls
  - `SetupNodeEventHandlers()` - Property change handler setup
  - `UpdateLiveValueDisplay()` - Live value visual updates
  - `UpdateLiveTextForElementType()` - Value text formatting
  - `CreateNodeFooter()` - Footer with parameters
  - `AddFooterControls()` - Parameter control creation
  - `IsIoNode()` - Node type checking helper
  - `ValidateAndUpdateAddress()` - Address validation logic

**Constants Extraction**
- Added `Layout Constants` region in `VisualNodeEditor.xaml.cs`
- `NodeHeaderHeight = 24`
- `ConnectorOffset = 6`
- `SingleInputVerticalRatio = 0.5`
- `DualInputTopRatio = 0.333`
- `DualInputBottomRatio = 0.667`

**Logging Standardization**
- Removed mixed logging approach
- Eliminated custom file logging infrastructure
- Now uses `ILogger` exclusively throughout the application

### 🛠️ Bug Fixes

**v4.5.6 - Memory Leak Fix**
- Fixed memory leak from stale UI event handlers
- Added `_nodeEventHandlers` dictionary for tracking
- Implemented `CleanupNodeEventHandlers()` for proper cleanup

**v4.5.7 - Empty Catch Blocks**
- Replaced silent failure catch blocks with proper logging
- All exceptions now logged via `ILogger`

**v4.5.9 - Dialog Title**
- Fixed hardcoded "TEST DIALOG" title
- Title now correctly uses `dialogTitle` variable

### 🖱️ UI Improvements

**v4.5.5 - Right-Click Delete**
- Right-click context menu for node deletion
- Uses existing `DeleteNodeCommand`

**v4.5.13 - Input Validation**
- Numeric-only input for address fields
- Visual validation feedback (red border)
- Improved tooltips with valid input range

## Technical Debt Resolved

| Issue | Resolution |
|-------|------------|
| Memory leaks | Event handler tracking and cleanup |
| Silent failures | All catch blocks now log to ILogger |
| Magic numbers | Extracted to named constants |
| Long methods | CreateNodeElement split into 10+ methods |
| Mixed logging | Consolidated to ILogger only |
| Input validation | Added with visual feedback |

## Files Changed

- `VisualNodeEditor.xaml.cs` - Major refactoring, constants, validation
- `MainWindow.xaml` - Maximized window state
- `MainViewModel.cs` - Catch block logging fixes
- `MainViewModel.Debug.cs` - Logging standardization
- `README.md` - Updated version info and changelog

## Breaking Changes

None. All changes are backward compatible.

## Tags

`v4.5.5` `v4.5.6` `v4.5.7` `v4.5.8` `v4.5.9` `v4.5.10` `v4.5.11` `v4.5.13` `v4.5.14`

## Installation

Download `ModbusForge-4.5.14-win-x64-setup.exe` (or zip for portable use).

Windows Defender may show a SmartScreen warning - click "More info" → "Run anyway" to proceed.
