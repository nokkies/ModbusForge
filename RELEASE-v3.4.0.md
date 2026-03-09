# ModbusForge v3.4.0 — Multi-Unit-ID Fix & Visual Node Editor Improvements

## What's New

### Multi-Unit-ID Simulation (ID Fix)
- **Simulation now mirrors to all configured Unit IDs** — previously only Unit ID 1 was updated; IDs 2, 3, etc. now receive identical register/coil values
- Custom `ModbusMultiUnitServer` replaces NModbus4 `ModbusTcpSlave` — multiple Unit IDs on the same IP:port without allocating extra ports
- MATH operations (ADD/SUB/MUL/DIV) mirror correctly to all Unit IDs
- PDU 0-based ↔ DataStore 1-based address offset corrected

### Visual Node Editor — Improvements
- **Right-click connector dots** to configure Modbus address (Area + Address)
- **Right-click connection wires** to delete them
- Connection lines now point accurately to Input1/Input2 connector positions (not block centre)
- Live Values toggle: node header turns green (ON) / red (OFF) based on live DataStore value
- Address configuration dialog now populates **Available Tags** from your Custom tab entries
- Node header label auto-refreshes after address config
- Node layout: text trimming, wider nodes (140 px), vertically centred labels

### Configuration Persistence
- Visual node layout (nodes + connections + addresses) saved and loaded with the configuration file

---

## Known Issues / Work In Progress

### Classic Form Editor Simulation — WORKING ✅
The classic PLC simulation (Classic Form Editor tab) is fully functional:
- All element types: AND, OR, NOT, RS Latch, Timers (TON/TOF/TP), Counters (CTU/CTD/CTC), Comparators, Math operations
- Multi-Unit-ID mirroring confirmed working
- Save/load configuration confirmed working

### Graphical (Visual Node Editor) Simulation — WORK IN PROGRESS ⚠️
The visual node editor simulation has known limitations in this release:
- Address configuration on nodes may not always persist visually after dialog close (cosmetic only — the address IS stored internally)
- Live Values display requires the server to be running first, then toggle Live Values ON
- Recommend using the Classic Form Editor for production simulation scenarios until graphical sim is stabilised

---

## Upgrade Notes
- Existing configuration files (`.json`) from v2.x and v3.0.x are compatible
- Nodes saved at width=120 from older configs will render narrower — use **Clear All** and re-add to get the new 140 px width
