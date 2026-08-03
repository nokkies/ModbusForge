# ModbusForge Versioned Roadmap — CalVer 2026.7.x

Version format: `YYYY.M.INCREMENT`. All `.csproj` files are bumped together.

## Important: baseline reset to `2026.7.1`

The baseline reset occurred at `2026.7.1`. The `v2026.7.2`–`v2026.7.11` tags still exist in history, but they represent premature/fake milestones. This roadmap is now the living **Avalonia = WPF parity plan**. Items will only be marked **Done** when the Avalonia feature works end-to-end. Real versions resumed at `2026.7.12` so we do not overwrite the historical tags.

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
| `2026.7.14` | [x] | Avalonia Dashboard tab with connection status, quick actions, and recent profile summary. |
| `2026.7.25` | [x] | Swarm parity batch: polling, Custom Watch, Trends, Decode, Advanced Functions, project state, visual editor foundation, and tool windows. |
| `2026.8.1` | [x] | Final parity batch: AvalonDock-style floating tool windows, marquee selection, visible connector ports/wire editing, full POU folder management, advanced graph routing, and deep Tag Browser/Watch integration. |

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

## Main Shell & Navigation — [x] 2026.8.1 shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Left NavigationView with icons | Yes | Sidebar equivalent | 2026.7.13 |
| Status bar at bottom (status, version, connection) | Yes | Yes | 2026.7.13 |
| View menu: dark mode, sim visibility, tab visibility toggles | Yes | Yes | 2026.7.13 |
| Debug / Console tabs | Yes | Yes | 2026.7.13 |
| Connection bar styled card, status indicator, Diagnostics button | Yes | Yes | 2026.7.13 |
| Main menu: Open Pcap, Import/Export Unit IDs, Save All, Trend export | Yes | Core items | 2026.7.13 |
| Full AvalonDock docking with 11 documents | Yes | Lightweight tear-off/dock manager (`AvaloniaDockingHost`) | 2026.8.1 |
| DataGrid context menus (Quick Write, Add to Watch, etc.) | Yes | Yes | 2026.7.12 |
| Dashboard tab | Yes | Yes (core dashboard) | 2026.7.14 |

### 2026.7.13 shipped subtasks
- [x] Add left navigation sidebar equivalent for the Avalonia views.
- [x] Add status bar with status, version, and connection state.
- [x] Add View menu with theme/tab toggles, Show All, and Reset to Default.
- [x] Add Debug and Console tabs/collections.
- [x] Add connection status indicators and Diagnostics button.
- [x] Add File menu items for Open Pcap, Import/Export Unit IDs, and Save All.
- [x] Add Options/Tools menu items for Connection Manager, Device Scanner, Script Editor, Advanced Functions placeholder, and Frame Inspector.

### 2026.7.14 shipped subtasks
- [x] Add Dashboard tab with connection status, quick actions, and recent profiles.
- [x] Add DataGrid context menus for Quick Write, Add to Custom Watch, Add to Trend, Copy (shipped with 2026.7.12).
- [x] Evaluate layout parity; retain the cross-platform sidebar plus TabControl while deferring full AvalonDock docking.

### 2026.8.1 shipped subtasks
- [x] Add `AvaloniaDockingHost` tear-off/dock manager for Tag Browser, Watch, and Connection Manager.
- [x] Wire tool windows to float and re-dock into the main TabControl.

---

## Registers — [x] 2026.7.16 shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| 4 register tabs | Yes | Yes | — |
| Per-area Start/Count | Yes | Yes | 2026.7.12 |
| Per-area monitoring toggle + period + auto-pause | Yes | Yes | 2026.7.12 |
| Inline editing (Holding/Coils) | Yes | Yes | 2026.7.12 |
| Per-area Read/Write buttons | Yes | Yes | 2026.7.12 |
| Quick Write via context menu | Yes | Yes | 2026.7.12 |
| PollingEngine command coalescing | Yes | Per-area serialized/coalesced polling | 2026.7.16 |
| Comprehensive error handling (HasConnectionError, auto-pause, dialogs) | Yes | Yes | 2026.7.16 |

### 2026.7.12 shipped subtasks
- [x] Add per-area Start/Count properties for Holding/Input/Coils/Discrete.
- [x] Add per-area monitoring toggles and period controls.
- [x] Enable inline editing in Holding Registers and Coils DataGrids.
- [x] Add per-area Read and Write commands.
- [x] Implement Quick Write context menu.

### 2026.7.16 shipped subtasks
- [x] Add per-area serialized/coalesced background reads.
- [x] Add `HasConnectionError` state and per-area auto-pause on errors.
- [x] Add error dialogs for monitoring failures.

---

## Custom Watch — [x] 2026.7.17 shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Grid columns | Yes | Yes | 2026.7.17 |
| Continuous Read | Yes | Yes | — |
| Continuous Write | Yes | Yes | 2026.7.17 |
| Import/Export custom entries | Yes | Yes (via service) | — |
| Auto-increment address | `uint`/`real` +2 | Yes | 2026.7.17 |
| Per-row Trend checkbox | Yes | Yes | — |

### 2026.7.17 shipped subtasks
- [x] Implement continuous write timer.
- [x] Fix auto-increment: `uint` and `real` both increment by 2.
- [x] Add per-row Read/Write action buttons.

---

## Trends — [x] 2026.7.18 shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Series management | Yes | Yes | 2026.7.18 |
| Play/Pause | Yes | Yes | — |
| Lock X/Y zoom controls | Yes | Yes | 2026.7.18 |
| Export CSV | Yes | Yes | 2026.7.18 |
| Import CSV | Yes | Yes | 2026.7.18 |
| Export PNG | Yes | Yes | 2026.7.18 |
| Reset view | Yes | Yes | 2026.7.18 |
| Retention control | Yes | Yes | — |
| Sample rate control | Internal | Yes (UI) | — |

### 2026.7.18 shipped subtasks
- [x] Add Export CSV, Import CSV, and Export PNG buttons/commands.
- [x] Add Lock X / Lock Y zoom controls.
- [x] Add Reset View button.

---

## Decode View — [x] 2026.7.19 shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| 16-bit decoder (None, Swap Bytes, Swap Words, Swap B+W) | Yes | Yes | 2026.7.19 |
| 32-bit decoder (UInt32, Int32, Float32, ASCII 4) | Yes | Yes | 2026.7.19 |

### 2026.7.19 shipped subtasks
- [x] Create `DecodeView.axaml` and `DecodeViewModel`.
- [x] Implement all 16-bit and 32-bit swap combinations.

---

## Connection, Transport & Frame Tools — [x] 2026.7.20 shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Connection profile management (TCP/Serial) | Yes | Yes | — |
| Serial RTU/ASCII settings | Yes | Yes | — |
| Frame Inspector | Yes | Yes (with extra features) | — |
| Device Scanner | Yes | Yes | — |
| Open Pcap / offline replay | Yes | Yes (Frame Inspector) | 2026.7.20 |
| MQTT gateway | Integrated | Dedicated tab | — |
| Update service (download/install) | Yes | Yes | 2026.7.20 |

### 2026.7.20 shipped subtasks
- [x] Add Open Pcap to File menu.
- [x] Implement update download/install in Avalonia.

---

## Scripting & Advanced Functions — [x] 2026.7.21 shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Script editor | Yes | Yes | — |
| Advanced Functions (FC22, FC23, FC43) | Yes | Yes | 2026.7.21 |

### 2026.7.21 shipped subtasks
- [x] Create `AdvancedFunctionsWindow.axaml` and `AdvancedFunctionsViewModel`.
- [x] Implement FC22 Mask Write, FC23 Read/Write Multiple, FC43 Device Identification.

---

## Visual Simulation — [x] 2026.8.1 shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Node palette | Yes | Yes | 2026.7.22 |
| Drag-drop canvas | Yes | Yes | 2026.7.22 |
| POU tree (programs) | Yes | Yes (hierarchical folders) | 2026.8.1 |
| Run/stop | Yes | Yes | — |
| Save/load simulation | Yes | Yes (backward-compatible extension) | 2026.7.22 |
| Live values, auto layout, snap, zoom, undo/redo | Yes | Yes | 2026.7.23 |
| Marquee selection | Yes | Yes | 2026.8.1 |
| Visible connector ports / wire editing | Yes | Yes | 2026.8.1 |
| Advanced graph routing | Yes | Yes (orthogonal / straight toggle) | 2026.8.1 |
| Tag Browser / Watch integration | Yes | Yes (drag/drop and Add to Watch) | 2026.8.1 |

### 2026.7.22 shipped subtasks
- [x] Implement basic palette drag/drop and node dragging for the Visual Node Editor.
- [x] Add POU/program selection with create, duplicate, and delete operations.
- [x] Extend visual simulation save/load with backward-compatible program metadata.

### 2026.7.23 shipped subtasks
- [x] Add live values panel, auto layout, snap to grid, zoom, and undo/redo.
- [x] Add standalone Avalonia Tag Browser and Watch Window tools.

### 2026.8.1 shipped subtasks
- [x] Implement marquee/rubber-band selection on the node canvas.
- [x] Add visible input/output connector ports on nodes.
- [x] Add drag-from-port-to-port wire creation and temporary connection line.
- [x] Add connection line selection and orthogonal routing toggle.
- [x] Implement full POU folder management (create, rename, delete, drag/drop, hierarchical tree).
- [x] Add drag/drop from Tag Browser onto node inputs and empty canvas.
- [x] Add "Add to Watch" for selected nodes and Watch Window integration.

---

## Project Save/Load & Unit ID State — [x] 2026.7.24 core shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Project save/load (.mfp) | Full (per-unit configs, visual nodes) | Yes (workspace snapshot) | 2026.7.24 |
| Import/Export Unit IDs | Yes | Yes (workspace plus legacy byte-list import) | 2026.7.24 |
| Per-Unit ID state (UnitConfigurationStore) | Yes | Yes | 2026.7.24 |

### 2026.7.24 shipped subtasks
- [x] Integrate `IUnitConfigurationStore` in Avalonia.
- [x] Save/load `UnitConfigurations`, `VisualNodes`, and `VisualConnections` in project.
- [x] Add bulk and single Unit ID import/export commands and menu items.

---

## Missing Tool Windows — [x] 2026.7.25 core shipped

| Feature | WPF | Avalonia | Target |
|---------|-----|----------|--------|
| Tag Browser | Yes | Yes | 2026.7.25 |
| Register Template Import | Yes | Yes (through Tag Browser) | 2026.7.25 |

### 2026.7.25 shipped subtasks
- [x] Create `TagBrowserWindow.axaml` and `TagBrowserViewModel`.
- [x] Create `RegisterTemplateImportDialog.axaml` and supporting view model.

---

## Release / Packaging

- [x] Bumped to `2026.7.25` after the swarm parity batch passed build and tests.
- [x] Bumped to `2026.8.1` after the final parity batch passed build and tests.
- [ ] No `v*` tags or GitHub Releases until parity is reached (per `AGENTS.md`).
- [ ] Keep `release.yml` on `workflow_dispatch` only until then.
- [ ] `v*` tags and GitHub Releases can be added after the `2026.8.1` parity milestone is smoke-tested.

---

## Notes

- WPF is the source of truth.
- Every feature must be verified end-to-end before it is marked `[x]`.
- Core improvements (fixing `ModbusServerService` bounds, etc.) can ship independently but do not count as Avalonia parity.
- Avalonia functional parity with the WPF baseline has been reached as of `2026.8.1`.
- Remaining differences are implementation-specific (e.g. lightweight custom docking instead of Dirkster.AvalonDock) and do not affect functional parity.
