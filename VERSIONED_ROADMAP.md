# ModbusForge Versioned Roadmap — CalVer 2026.7.x

Version format: `YYYY.M.INCREMENT`. All `.csproj` files are bumped together.

## Important: baseline reset to `2026.7.1`

All `.csproj` files are now back at `2026.7.1`. The `v2026.7.2`–`v2026.7.11` tags still exist in history, but they represent premature/fake milestones. This roadmap is now the living **Avalonia = WPF parity plan**. Items will only be marked **Done** when the Avalonia feature works end-to-end. Real versions will resume at `2026.7.12` so we do not overwrite the historical tags.

## Legend

- `[ ]` Not started / not working
- `[~]` In progress
- `[x]` Done (end-to-end verified)

---

## Shipped Baseline

| Version | State | Notes |
|---------|-------|-------|
| `2026.7.1` | [x] | WPF / Core / Headless shipped. Baseline reset. |
| `2026.7.12` | [x] | Avalonia per-area registers (Start/Count, monitoring, inline edit, per-area R/W, Quick Write). |
| `2026.7.13` | [x] | Avalonia shell navigation, status bar, View toggles, Diagnostics, File/Tools menu parity, and Debug/Console tabs. |

The `v2026.7.2`–`v2026.7.11` tags were created during the previous fake milestone tracking and should not be treated as real shipped versions. Avalonia parity work will be versioned from `2026.7.12` onward.

---

## Master/Slave Client-Server Mode — [x] Done

- [x] `ConnectionProfile` has `Mode` (Client/Server) and `ServerUnitIds`.
- [x] `ConnectionManager` creates `ModbusServerService` for Server mode.
- [x] `ModbusServerService` uses `ServerUnitIds` and supports 0-based data store.
- [x] Avalonia `MainViewModel` exposes `Mode`, `IsServerMode`, `EffectiveUnitId`, `AvailableUnitIds`, `SelectedUnitId`.
- [x] Avalonia `MainView` connection bar shows Mode ComboBox and conditional server/client fields.
- [x] End-to-end test: server + client read/write through `ConnectionManager`.

---

## Main Shell & Navigation — [x] 2026.7.13 core shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Left NavigationView with icons | Yes | Sidebar equivalent | 2026.7.13 |
| Status bar at bottom (status, version, connection) | Yes | Yes | 2026.7.13 |
| View menu: dark mode, sim visibility, tab visibility toggles | Yes | Yes | 2026.7.13 |
| Debug / Console tabs | Yes | Yes | 2026.7.13 |
| Connection bar styled card, status indicator, Diagnostics button | Yes | Yes | 2026.7.13 |
| Main menu: Open Pcap, Import/Export Unit IDs, Save All, Trend export | Yes | Core items | 2026.7.13 |
| Full AvalonDock docking with 11 documents | Yes | Simple TabControl | 2026.7.14 |
| DataGrid context menus (Quick Write, Add to Watch, etc.) | Yes | Yes | 2026.7.12 |
| Dashboard tab | Yes | No | 2026.7.14 |

### 2026.7.13 shipped subtasks
- [x] Add left navigation sidebar equivalent for the Avalonia views.
- [x] Add status bar with status, version, and connection state.
- [x] Add View menu with theme/tab toggles, Show All, and Reset to Default.
- [x] Add Debug and Console tabs/collections.
- [x] Add connection status indicators and Diagnostics button.
- [x] Add File menu items for Open Pcap, Import/Export Unit IDs, and Save All.
- [x] Add Options/Tools menu items for Connection Manager, Device Scanner, Script Editor, Advanced Functions placeholder, and Frame Inspector.

### 2026.7.14 subtasks
- [ ] Add Dashboard tab.
- [ ] Add DataGrid context menus for Quick Write, Add to Custom Watch, Add to Trend, Copy.
- [ ] Evaluate/imitate dockable/floatable layout if required.

---

## Registers — [~] Partial (2026.7.12 core shipped)

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| 4 register tabs | Yes | Yes | — |
| Per-area Start/Count | Yes | Yes | 2026.7.12 |
| Per-area monitoring toggle + period + auto-pause | Yes | Yes | 2026.7.12 |
| Inline editing (Holding/Coils) | Yes | Yes | 2026.7.12 |
| Per-area Read/Write buttons | Yes | Yes | 2026.7.12 |
| Quick Write via context menu | Yes | Yes | 2026.7.12 |
| PollingEngine command coalescing | Yes | Simple loop | 2026.7.16 |
| Comprehensive error handling (HasConnectionError, auto-pause, dialogs) | Yes | Basic status only | 2026.7.16 |

### 2026.7.12 shipped subtasks
- [x] Add per-area Start/Count properties for Holding/Input/Coils/Discrete.
- [x] Add per-area monitoring toggles and period controls.
- [x] Enable inline editing in Holding Registers and Coils DataGrids.
- [x] Add per-area Read and Write commands.
- [x] Implement Quick Write context menu.

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

- [x] Bumped to `2026.7.12` when the per-area register block passed build and tests.
- [ ] No `v*` tags or GitHub Releases until parity is reached (per `AGENTS.md`).
- [ ] Keep `release.yml` on `workflow_dispatch` only until then.
- [ ] When the next feature block is done and tested, bump to the next available CalVer and commit. `v*` tags can be added after parity is complete.

---

## Notes

- WPF is the source of truth.
- Every feature must be verified end-to-end before it is marked `[x]`.
- Core improvements (fixing `ModbusServerService` bounds, etc.) can ship independently but do not count as Avalonia parity.
