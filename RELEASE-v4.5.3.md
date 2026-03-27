# ModbusForge v4.5.3 Release Notes

## Summary
This release removes legacy function blocks from the node palette and includes UI refinements for a cleaner visual simulation experience.

## Changes

### UI Improvements
- **Removed legacy I/O blocks**: "Input (Legacy)" and "Output (Legacy)" buttons removed from the node palette - users should use Input BOOL/INT and Output BOOL/INT blocks instead
- **Fixed duplicate labels**: I/O nodes no longer show duplicate type labels inside the block (header already shows the type)
- **Address TextBox sizing**: TextBox height increased to 28px with vertical centering to prevent number cutoff
- **Compact node layout**: Function blocks now 240×140 for better space efficiency
- **Aligned inline controls**: Area ComboBox and Address TextBox now share identical dimensions (200×26, 11pt font)

### Version History
- **v4.5.1**: Fixed int/bool collision issue in VisualSimulationService
- **v4.5.2**: Fixed FB size and textbox size issues
- **v4.5.3**: Removed legacy FB from node palette

## Migration Guide
If you have existing projects using legacy Input/Output blocks, please replace them with:
- **Input (Legacy)** → **Input BOOL** or **Input INT** (depending on data type needed)
- **Output (Legacy)** → **Output BOOL** or **Output INT** (depending on data type needed)

The new typed I/O blocks provide clearer intent and better support for both boolean and integer Modbus operations.
