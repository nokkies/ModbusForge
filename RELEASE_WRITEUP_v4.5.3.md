# ModbusForge v4.5.3 Release Writeup

## Overview
ModbusForge v4.5.3 finalizes the visual simulation UI overhaul by removing legacy function blocks and refining the node editor interface for a cleaner, more professional user experience.

---

## What's New

### 🧹 Legacy Cleanup
**Removed legacy I/O blocks from node palette**
- "Input (Legacy)" and "Output (Legacy)" buttons have been removed
- Users should migrate to the type-specific alternatives:
  - **Input BOOL** / **Input INT** for inputs
  - **Output BOOL** / **Output INT** for outputs
- These typed blocks provide clearer intent and better support for both boolean and integer Modbus operations

### 🎨 UI Refinements

**Fixed duplicate labels in I/O nodes**
- I/O blocks no longer show redundant type labels inside the node body
- The header already displays "IN INT", "OUT BOOL", etc. — no need for duplication
- Creates cleaner visual hierarchy and more usable space

**Improved text visibility**
- Address TextBox height increased to 28px with vertical centering
- Register numbers are now fully visible without cutoff
- TextBox and ComboBox share identical dimensions (200×26) for visual alignment

**Compact node design**
- Function block size reduced to 240×140 (was 320×220)
- More efficient use of canvas space
- Better for complex diagrams with many nodes

---

## Version History Context

| Version | Focus |
|---------|-------|
| **v4.5.1** | Fixed int/bool collision in VisualSimulationService — simulation engine now correctly handles mixed boolean and integer data types |
| **v4.5.2** | Fixed FB sizing and textbox sizing issues — improved visibility of register values |
| **v4.5.3** | Removed legacy FB from node palette — final UI cleanup |

---

## Assets Available

The GitHub release includes:

- **ModbusForge-4.5.3-win-x64.zip** — Framework-dependent (requires .NET 8 runtime)
- **ModbusForge-4.5.3-win-x64-sc.zip** — Self-contained (no dependencies)
- **ModbusForge-4.5.3-win-x64.exe** — Windows installer (Inno Setup)
- SHA256 checksums for all files

---

## Migration Guide

If you have existing projects using legacy Input/Output blocks:

1. **Legacy Input** → Replace with:
   - `Input BOOL` if reading coils/discrete inputs
   - `Input INT` if reading holding/input registers

2. **Legacy Output** → Replace with:
   - `Output BOOL` if writing to coils
   - `Output INT` if writing to holding registers

The functionality remains the same — only the UI representation is clearer.

---

## Technical Notes

This release completes the visual simulation refactoring started in v4.5.x:
- Legacy `SimulationService` and `SimulationCoordinator` were removed in earlier commits
- `VisualSimulationService` is now the sole simulation engine
- Inline parameter editing eliminates popup dialogs
- Two-phase evaluation with topological sorting ensures correct execution order

---

## Acknowledgments

Thanks to user feedback driving these UI improvements. The node editor is now cleaner, more compact, and ready for complex Modbus simulation scenarios.

---

**Full Changelog**: See `RELEASE-v4.5.3.md` in the repository root.

**Download**: https://github.com/nokkies/ModbusForge/releases/tag/v4.5.3
