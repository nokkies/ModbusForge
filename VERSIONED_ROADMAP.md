# ModbusForge Versioned Roadmap — CalVer 2026.7.x / 2026.07.x

Version format: `YYYY.M.INCREMENT` (repo convention), e.g. `2026.7.3`.  
All `.csproj` files are bumped together for a release so Core, WPF, Headless and Avalonia share the same assembly version.

## Baseline

| Version | Project | State |
|---------|---------|-------|
| `2026.7.1` | `ModbusForge`, `ModbusForge.Core`, `ModbusForge.Headless` | Shipped |
| `2026.7.2` | `ModbusForge.Avalonia` (spike) | Shipped |

Avalonia currently contains: main window, connection manager, device scanner, basic holding register grid, file/message-box/input dialogs, and self-contained publish profiles for `win-x64` and `linux-x64`.

---

## 2026.7.3 — Avalonia Foundations (Completed)
**Theme:** Cross-platform shell and packaging are solid.

- Align all `.csproj` versions to `2026.7.3`.
- Make `ModbusForge.Avalonia` the default startup project in the solution.
- Add GitHub Actions CI that builds and tests Avalonia on Windows and Linux.
- Validate Linux `linux-x64` self-contained publish.
- Smoke-test both `win-x64` and `linux-x64` single-file executables.
- WPF: keep parity; no new WPF-only features this release.

**Apps:** Avalonia, Core, WPF, Headless  
**Impact:** Low risk; infrastructure only.

---

## 2026.7.4 — Core Register Operations in Avalonia (Completed)
**Theme:** Port the register read/write experience.

- Holding / input registers, coils, discrete inputs.
- Read, write single, write multiple, continuous read.
- Data grid with address, value, type, swap, format columns.
- Unit ID switching with per-unit state.

- Holding / input registers, coils, discrete inputs.
- Read, write single, write multiple, continuous read.
- Data grid with address, value, type, swap, format columns.
- Unit ID switching with per-unit state.
- Connection diagnostics (TCP latency / serial loopback).
- WPF: fix any register-grid regressions; add Avalonia-only improvements back to Core.

**Affects:** `ModbusForge.Avalonia`, `ModbusForge.Core`  
**Key services:** `ModbusTcpService`, `ModbusSerialService`, `RegisterCoordinator`.

---

## 2026.7.5 — Custom Watch & Project Save (Completed)
**Theme:** User-defined tags and project persistence.

- Custom Watch tab in Avalonia.
- Continuous read/write with per-entry periods.
- Project save/load (`.mfp`/`.json`).
- Headless `--custom` JSON support.

- Custom Watch tab (area, type, read/write value, continuous read/write, read period).
- Per-row trend enable.
- Project save/load (`.mfp`) including Unit IDs, connections, custom entries.
- JSON import/export for custom configurations.
- Headless: `--custom` JSON import for headless polling.

**Affects:** `ModbusForge.Avalonia`, `ModbusForge.Core`, `ModbusForge.Headless`

---

## 2026.7.6 — Trends & Visualization (Completed)
**Theme:** Real-time charts and export.

- Avalonia `Trends` tab with LiveCharts Cartesian chart.
- `TrendLoggingService` integrated and auto-started/stopped with connection.
- Custom Watch entries can trend numeric/coil values.
- Start/stop, remove, clear, retention, and sample-rate controls.

---

## 2026.7.7 — Connection, Transport & Frame Tools (Completed)
**Theme:** Complete the connection experience and diagnostics.

- Serial RTU/ASCII settings in Avalonia Connection Manager (COM, baud, parity, RTS toggle, pre/post tx delays).
- Connection profile save/load across sessions.
- Frame Inspector window (live PDU/byte log with timestamps).
- Pcap import / offline replay (uses `PcapImportService`).
- MQTT gateway publish from Core (configured in Avalonia preferences).

- Trend view with multiple traces, zoom/pan, retention.
- CSV and PNG export.
- Avalonia Skia chart integration (`LiveChartsCore.SkiaSharpView.Avalonia` or equivalent).
- Trend auto-enable on custom/register line add.

**Affects:** `ModbusForge.Avalonia`, `ModbusForge.Core` (`TrendLoggingService`)

---

## 2026.7.7 — Connection, Transport & Frame Tools (Completed)
**Theme:** Complete the connection experience and diagnostics.

- Serial RTU/ASCII settings in Avalonia connection manager (COM, baud, parity, RTS toggle, pre/post tx delays).
- Connection profile save/load across sessions.
- Frame Inspector window (live PDU/byte log with timestamps).
- Pcap import / offline replay (uses `PcapImportService`).
- MQTT gateway publish from Core (configured in Avalonia preferences).

**Affects:** `ModbusForge.Avalonia`, `ModbusForge.Core`

---

## 2026.7.8 — Scripting & Advanced Functions (Completed)
**Theme:** Automation and extended Modbus function codes.

- Script Editor in Avalonia (read, write, delay, log, repeat, run/stop).
- `.mbscript` save/load.
- Advanced function codes: FC22 Mask Write, FC23 Read/Write Multiple, FC43 Read Device Identification.
- Signal generator configuration (ramp, sine, triangle, square).

**Affects:** `ModbusForge.Avalonia`, `ModbusForge.Core` (`ScriptRuleService`, `ModbusServerService`)

---

## 2026.7.9 — Visual Simulation (Completed)
**Theme:** Port the visual node editor.

- Visual Node Editor (node palette, canvas, wiring, ADD/COMPARE/CONST/POU blocks).
- Signal generator nodes.
- Real-time simulation execution.
- Save/load simulation programs.

**Affects:** `ModbusForge.Avalonia`, `ModbusForge.Core` (`SimulationService`)

---

## 2026.7.10 — Application Shell & Preferences
**Theme:** Complete the desktop shell and settings.

- Preferences window (theme, polling defaults, MQTT, API server, update checks).
- Help, About, Keyboard Shortcuts, Troubleshooting windows.
- Global keyboard shortcuts (Ctrl+R read, Ctrl+T trends, Ctrl+S save, F5 refresh, F1 help).
- Theming / dark mode parity with WPF.
- Auto-updater for Avalonia (asset matching, download, silent install).

**Affects:** `ModbusForge.Avalonia`, `ModbusForge.Core`

---

## 2026.7.11 — Performance & Reliability
**Theme:** Hardening before broader release.

- Data grid virtualization for large address ranges.
- Connection pooling / multi-device support improvements.
- Structured logging with correlation IDs.
- Address-calculation and boundary-check audit across all services.
- Unit test coverage for the Avalonia port.

**Affects:** All projects

---

## 2026.7.12 — Release Polish & Cross-Platform Packaging
**Theme:** Ship Avalonia as the primary entry point.

- Final version bump to `2026.7.12` in all `.csproj` files.
- Windows installer (`setup/ModbusForge.iss` or a new Avalonia installer).
- Linux `.tar.gz` packaging.
- Release notes and README update.
- Tag `v2026.7.12` and GitHub release.

**Affects:** All projects

---

## Beyond 2026.7.x — Major Features

These are larger initiatives and should start in `2026.8.1` or later once Avalonia parity is reached:

- **Unit ID isolation & save structure redesign** (per-Unit ID state, unified project file).
- **Alarm / Event system**.
- **Device Template Library**.
- **Calculation Engine**.
- **MQTT subscriber / historian**.
- **Plugin architecture**.
- **OpenAPI / Swagger API documentation**.

---

## Summary Table

| Version | Theme | Main Deliverables | Apps |
|---------|-------|-------------------|------|
| `2026.7.3` | Avalonia foundations | CI, Linux publish, default startup | All |
| `2026.7.4` | Registers | Read/write/poll for all areas | Avalonia, Core |
| `2026.7.5` | Custom Watch | Custom entries + project save | Avalonia, Core, Headless |
| `2026.7.6` | Trends | Charts, CSV/PNG, retention | Avalonia, Core |
| `2026.7.7` | Connection tools | Serial, Frame Inspector, pcap, MQTT | Avalonia, Core |
| `2026.7.8` | Scripting | Script editor + advanced FCs | Avalonia, Core |
| `2026.7.9` | Simulation | Visual node editor | Avalonia, Core |
| `2026.7.10` | Shell | Preferences, help, shortcuts, theme | Avalonia, Core |
| `2026.7.11` | Hardening | Virtualization, logging, tests | All |
| `2026.7.12` | Release | Installer, packaging, tag | All |

---

## Notes

- Patch increments (`2026.7.x`) are used for the Avalonia porting milestones while the application is in transition.
- WPF receives only regression fixes and Core improvements during this period.
- Headless gets CLI parity for Custom Watch and project save where it makes sense.
- When Avalonia reaches feature parity, the next minor month (`2026.8.1`) begins the major feature track.

*Roadmap created: July 2026*
