# ModbusForge Versioned Roadmap — CalVer 2026.7.x

Version format: `YYYY.M.INCREMENT`. All `.csproj` files are bumped together.

## Important: 2026.7.4–2026.7.12 are NOT completed

Those milestones were previously marked complete, but only the **UI shell / packaging** was done. The actual features were not ported from the working WPF app. This roadmap is now the living **Avalonia = WPF parity plan**. Items will only be marked **Done** when the Avalonia feature works end-to-end.

## Legend

- `[ ]` Not started / not working
- `[~]` In progress
- `[x]` Done (end-to-end verified)

---

## Shipped Baseline

| Version | State | Notes |
|---------|-------|-------|
| `2026.7.1` | [x] | WPF / Core / Headless shipped. |
| `2026.7.2` | [x] | Avalonia spike (window, dialogs, publish profiles). |
| `2026.7.3` | [x] | Avalonia foundations: CI, Linux publish, default startup. |

---

## Master/Slave Client-Server Mode — [x] Done

- [x] `ConnectionProfile` has `Mode` (Client/Server) and `ServerUnitIds`.
- [x] `ConnectionManager` creates `ModbusServerService` for Server mode.
- [x] `ModbusServerService` uses `ServerUnitIds` and supports 0-based data store.
- [x] Avalonia `MainViewModel` exposes `Mode`, `IsServerMode`, `EffectiveUnitId`, `AvailableUnitIds`, `SelectedUnitId`.
- [x] Avalonia `MainView` connection bar shows Mode ComboBox and conditional server/client fields.
- [x] End-to-end test: server + client read/write through `ConnectionManager`.

---

## Main Shell & Navigation — [~] In Progress

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Left NavigationView with icons | Yes | No | 2026.7.13 |
| Status bar at bottom (status, version, connection) | Yes | Partial (status only) | 2026.7.13 |
| View menu: dark mode, sim visibility, tab visibility toggles | Yes | Minimal | 2026.7.13 |
| Debug / Console tabs | Yes | No | 2026.7.13 |
| Connection bar styled card, status indicator, Diagnostics button | Yes | Partial | 2026.7.13 |
| Main menu: Open Pcap, Import/Export Unit IDs, Save All, Trend export | Yes | Missing | 2026.7.13 |
| Full AvalonDock docking with 11 documents | Yes | Simple TabControl | 2026.7.14 |
| DataGrid context menus (Quick Write, Add to Watch, etc.) | Yes | No | 2026.7.14 |
| Dashboard tab | Yes | No | 2026.7.14 |

### 2026.7.13 subtasks
- [ ] Add left NavigationView or equivalent with all WPF items.
- [ ] Add StatusBar at bottom: status, version, connection indicator.
- [ ] Add View menu with tab visibility toggles, Show All, Reset to Default.
- [ ] Add Debug and Console tabs/collections.
- [ ] Add connection status indicator ellipse and Diagnostics button.
- [ ] Add missing File menu items (Open Pcap, Import/Export Unit IDs, Save All).
- [ ] Add missing Options/Tools menu items (Connection Manager, Device Scanner, Script Editor, Advanced Functions, Frame Inspector).

### 2026.7.14 subtasks
- [ ] Add Dashboard tab.
- [ ] Add DataGrid context menus for Quick Write, Add to Custom Watch, Add to Trend, Copy.
- [ ] Evaluate/imitate dockable/floatable layout if required.

---

## Registers — [ ] Not Done

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| 4 register tabs | Yes | Yes | — |
| Per-area Start/Count | Yes | Global only | 2026.7.15 |
| Per-area monitoring toggle + period + auto-pause | Yes | Global only | 2026.7.15 |
| Inline editing (Holding/Coils) | Yes | Read-only | 2026.7.15 |
| Per-area Read/Write buttons | Yes | Global only | 2026.7.15 |
| Quick Write via context menu | Yes | No | 2026.7.15 |
| PollingEngine command coalescing | Yes | Simple loop | 2026.7.16 |
| Comprehensive error handling (HasConnectionError, auto-pause, dialogs) | Yes | Basic status only | 2026.7.16 |

### 2026.7.15 subtasks
- [ ] Add per-area Start/Count properties for Holding/Input/Coils/Discrete.
- [ ] Add per-area monitoring toggles and period controls.
- [ ] Enable inline editing in Holding Registers and Coils DataGrids.
- [ ] Add per-area Read and Write commands.
- [ ] Implement Quick Write context menu.

### 2026.7.16 subtasks
- [ ] Port `PollingEngine` (or equivalent) for optimized/coalesced background reads.
- [ ] Add `HasConnectionError` flag and per-area auto-pause on errors.
- [ ] Add error dialogs for monitoring failures.

---

## Custom Watch — [ ] Not Done

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Grid columns | Yes | Yes (missing per-row action buttons) | 2026.7.17 |
| Continuous Read | Yes | Yes | — |
| Continuous Write | Yes | Missing | 2026.7.17 |
| Import/Export custom entries | Yes | Yes (via service) | — |
| Auto-increment address | `uint`/`real` +2 | `real` only +2 (bug) | 2026.7.17 |
| Per-row Trend checkbox | Yes | Yes | — |

### 2026.7.17 subtasks
- [ ] Implement continuous write timer.
- [ ] Fix auto-increment: `uint` and `real` should both increment by 2.
- [ ] Add per-row Read/Write action buttons.

---

## Trends — [ ] Not Done

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Series management | Yes | Simpler | 2026.7.18 |
| Play/Pause | Yes | Start/Stop (naming only) | — |
| Lock X/Y zoom controls | Yes | Hardcoded X only | 2026.7.18 |
| Export CSV | Yes | Missing | 2026.7.18 |
| Import CSV | Yes | Missing | 2026.7.18 |
| Export PNG | Yes | Missing | 2026.7.18 |
| Reset view | Yes | Missing | 2026.7.18 |
| Retention control | Yes | Yes | — |
| Sample rate control | Internal | Yes (UI) | — |

### 2026.7.18 subtasks
- [ ] Add Export CSV, Import CSV, Export PNG buttons/commands.
- [ ] Add Lock X / Lock Y zoom controls.
- [ ] Add Reset View button.

---

## Decode View — [ ] Not Done

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| 16-bit decoder (None, Swap Bytes, Swap Words, Swap B+W) | Yes | Missing | 2026.7.19 |
| 32-bit decoder (UInt32, Int32, Float32, ASCII 4) | Yes | Missing | 2026.7.19 |

### 2026.7.19 subtasks
- [ ] Create `DecodeView.axaml` and `DecodeViewModel`.
- [ ] Implement all 16-bit and 32-bit swap combinations.

---

## Connection, Transport & Frame Tools — [~] Partial

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Connection profile management (TCP/Serial) | Yes | Yes | — |
| Serial RTU/ASCII settings | Yes | Yes | — |
| Frame Inspector | Yes | Yes (with extra features) | — |
| Device Scanner | Yes | Yes | — |
| Open Pcap / offline replay | Yes | In Frame Inspector only | 2026.7.20 |
| MQTT gateway | Integrated | Dedicated tab | — |
| Update service (download/install) | Yes | Check only | 2026.7.20 |

### 2026.7.20 subtasks
- [ ] Add Open Pcap to File menu.
- [ ] Implement update download/install in Avalonia.

---

## Scripting & Advanced Functions — [~] Partial

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Script editor | Yes | Yes | — |
| Advanced Functions (FC22, FC23, FC43) | Yes | Missing | 2026.7.21 |

### 2026.7.21 subtasks
- [ ] Create `AdvancedFunctionsWindow.axaml` and `AdvancedFunctionsViewModel`.
- [ ] Implement FC22 Mask Write, FC23 Read/Write Multiple, FC43 Device Identification.

---

## Visual Simulation — [~] Partial

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Node palette | Yes | Yes (basic) | 2026.7.22 |
| Drag-drop canvas | Yes | Missing | 2026.7.22 |
| POU tree (programs) | Yes | Missing | 2026.7.22 |
| Run/stop | Yes | Yes | — |
| Save/load simulation | Yes | Yes (basic) | 2026.7.22 |
| Live values, auto layout, snap, zoom, undo/redo | Yes | Missing | 2026.7.23 |
| Tag Browser / Watch integration | Yes | Missing | 2026.7.23 |

### 2026.7.22 subtasks
- [ ] Implement drag-drop canvas for Visual Node Editor.
- [ ] Add POU tree (programs) with create/rename/duplicate/delete.
- [ ] Save/load visual simulation in project file.

### 2026.7.23 subtasks
- [ ] Add live values panel, auto layout, snap to grid, zoom, undo/redo.
- [ ] Integrate Tag Browser / Watch Window.

---

## Project Save/Load & Unit ID State — [ ] Not Done

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Project save/load (.mfp) | Full (per-unit configs, visual nodes) | Partial (profiles + custom entries) | 2026.7.24 |
| Import/Export Unit IDs | Yes | Missing | 2026.7.24 |
| Per-Unit ID state (UnitConfigurationStore) | Yes | Missing | 2026.7.24 |

### 2026.7.24 subtasks
- [ ] Integrate `IUnitConfigurationStore` in Avalonia.
- [ ] Save/load `UnitConfigurations`, `VisualNodes`, `VisualConnections` in project.
- [ ] Add Import/Export Unit ID commands and menu items.

---

## Missing Tool Windows — [ ] Not Done

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Tag Browser | Yes | Missing | 2026.7.25 |
| Register Template Import | Yes | Missing | 2026.7.25 |

### 2026.7.25 subtasks
- [ ] Create `TagBrowserWindow.axaml` and `TagBrowserViewModel`.
- [ ] Create `RegisterTemplateImportWindow.axaml` and view model.

---

## Release / Packaging

- [ ] No version bump until the above features are actually working.
- [ ] No `v*` tags or GitHub Releases until parity is reached.
- [ ] Keep `release.yml` on `workflow_dispatch` only until then.
- [ ] When parity is reached, bump to `2026.7.26` or later and re-enable releases.

---

## Notes

- WPF is the source of truth.
- Every feature must be verified end-to-end before it is marked `[x]`.
- Core improvements (fixing `ModbusServerService` bounds, etc.) can ship independently but do not count as Avalonia parity.
