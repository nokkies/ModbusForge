# ModbusForge Avalonia Migration Fix Plan - EXECUTION CHECKLIST

## Executive Summary

The Avalonia UI port (ModbusForge.Avalonia) has significant gaps compared to the working WPF version (ModbusForge v6.1.0). This document provides **actionable tasks** organized by priority.

---

## CRITICAL ISSUES (Must Fix First)

### 1. ❌ Architecture Mismatch - HIGH PRIORITY

**Current State:**
- WPF: `FluentWindow` (572 lines) with `TitleBar`, `NavigationView`, `AvalonDock`
- Avalonia: `UserControl` (466 lines) with manual navigation

**Impact:** Core application structure fundamentally different

**Action Items:**

#### Task 1.1: Add Required NuGet Packages
```bash
cd /workspace/ModbusForge.Avalonia
dotnet add package Dock.Avalonia --version 11.2.0
dotnet add package Avalonia.Controls.Ribbon
```

#### Task 1.2: Convert MainView.axaml to MainWindow Structure
**File:** `/workspace/ModbusForge.Avalonia/Views/MainWindow.axaml`

Current structure needs to change from:
```axaml
<UserControl x:Class="MainView">
  <Grid>
    <Menu/>
    <ListBox for navigation/>
    <TabControl/>
  </Grid>
</UserControl>
```

To WPF-equivalent structure:
```axaml
<Window x:Class="MainWindow"
        xmlns="https://github.com/avaloniaui"
        xmlns:dock="https://github.com/wieslawsoltes/dock">
  <Grid RowDefinitions="Auto,*,Auto">
    <!-- TitleBar equivalent -->
    <Border Grid.Row="0" Classes="titlebar">
      <!-- Custom title bar content -->
    </Border>
    
    <!-- NavigationView or Ribbon -->
    <dock:DockPanel Grid.Row="1">
      <!-- Docking content here -->
    </dock:DockPanel>
  </Grid>
</Window>
```

#### Task 1.3: Update App.axaml.cs to Use MainWindow
**File:** `/workspace/ModbusForge.Avalonia/App.axaml.cs`

Change from launching MainView to launching MainWindow window.

---

### 2. ❌ Missing Docking System - HIGH PRIORITY

**Current State:**
- WPF uses `avalonDock:DockingManager` with full floating/tiling support
- Avalonia has `AvaloniaDockingHost.cs` (stub, 12KB) but NO docking in XAML

**Files Affected:**
- `/workspace/ModbusForge.Avalonia/Services/AvaloniaDockingHost.cs` - stub only
- `/workspace/ModbusForge/MainWindow.xaml` - lines with `<avalonDock:DockingManager>`

**Action Items:**

#### Task 2.1: Implement Dock.Avalonia Integration

**Step 1:** Add Dock.Avalonia to App.axaml
```axaml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:dock="https://github.com/wieslawsoltes/dock"
             x:Class="ModbusForge.Avalonia.App">
  <Application.Styles>
    <FluentTheme />
    <dock:DockFluentTheme />
  </Application.Styles>
</Application>
```

**Step 2:** Replace TabControl in MainView with DockPanel
```axaml
<dock:Dock x:Name="MainDockPanel"
           InitializeLayout="{Binding InitializeLayoutCommand}"
           CreateLayout="{Binding CreateLayoutCommand}">
  
  <dock:LayoutRoot>
    <dock:RootDock>
      <dock:RootDock.LeftDock>
        <dock:ProportionalDock Proportion="0.2">
          <dock:ToolDock>
            <!-- Navigation tools -->
          </dock:ToolDock>
        </dock:ProportionalDock>
      </dock:RootDock.LeftDock>
      
      <dock:RootDock.MainDock>
        <dock:DocumentDock>
          <!-- Main document area -->
        </dock:DocumentDock>
      </dock:RootDock.MainDock>
    </dock:RootDock>
  </dock:LayoutRoot>
</dock:Dock>
```

#### Task 2.2: Update ViewModel for Docking
Add docking layout management to `MainViewModel.cs`:
- `InitializeLayoutCommand`
- `CreateLayoutCommand`
- Track docked/floated state per view

---

### 3. ❌ Visual Node Editor Incomplete - HIGH PRIORITY

**Current State:**
- WPF: 416 lines XAML + extensive code-behind
- Avalonia: 340 lines XAML (76 lines shorter!)

**Missing Features (likely):**
- Canvas-based node rendering
- Connection line drawing (Bezier curves)
- Drag-drop operations
- Marquee selection
- Zoom/pan functionality
- Port connection logic

**Action Items:**

#### Task 3.1: Compare XAML Content
```bash
diff /workspace/ModbusForge/Views/VisualNodeEditor.xaml \
     /workspace/ModbusForge.Avalonia/Views/VisualNodeEditorView.axaml
```

#### Task 3.2: Port Missing XAML Elements
Key sections to verify/port from WPF:
- [ ] Canvas for node rendering
- [ ] Adorner layer for connection lines
- [ ] Zoom/pan control (ZoomBox, PanButton)
- [ ] Context menus
- [ ] Toolbars
- [ ] Property panels

#### Task 3.3: Verify Code-Behind Parity
**Files to compare:**
- WPF: `VisualNodeEditor.xaml.cs` (check line count)
- Avalonia: `VisualNodeEditorView.axaml.cs`

**Key features to implement in Avalonia:**
- Mouse event handlers (MouseDown, MouseMove, MouseUp)
- Touch gesture support
- Hit testing for nodes/ports
- Connection line rendering (SkiaSharp or Avalonia Paths)
- Marquee selection rectangle
- Undo/redo stack integration

---

### 4. ⚠️ Theming & Styling Gap - MEDIUM PRIORITY

**Current State:**
- WPF: 1001 lines of custom themes (Theme.xaml: 810, MetallicTheme.xaml: 191)
- Avalonia: Default FluentTheme only (4 lines in App.axaml)

**Action Items:**

#### Task 4.1: Create Theme Directory Structure
```bash
mkdir -p /workspace/ModbusForge.Avalonia/Themes
```

#### Task 4.2: Port Custom Theme Resources
**Source:** `/workspace/ModbusForge/Resources/Theme.xaml` (810 lines)
**Target:** `/workspace/ModbusForge.Avalonia/Themes/CustomTheme.axaml`

Key resources to port:
- Color brushes (MetallicTextBrush, LightMetallicCardBrush, etc.)
- Control templates (Button, TextBox, DataGrid styles)
- Converters
- Custom fonts

#### Task 4.3: Update App.axaml
```axaml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="ModbusForge.Avalonia.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://ModbusForge.Avalonia/Themes/CustomTheme.axaml" />
  </Application.Styles>
</Application>
```

#### Task 4.4: Implement Theme Service
**File:** `/workspace/ModbusForge.Avalonia/Services/AvaloniaThemeService.cs`

Already exists but needs enhancement:
- Support for switching between Light/Dark/Custom themes
- Persist theme preference
- Hot-reload theme changes

---

### 5. ❌ Missing Dialog Windows - MEDIUM PRIORITY

**Current State:**
- WPF has 5 dialog windows
- Avalonia only has 2 dialogs

**Missing Dialogs:**
- [ ] `GroupDeletionDialog.xaml` → Confirm group deletion
- [ ] `TestDialog.xaml` → Test configuration
- [ ] `WriteDialog.xaml` → Write register values

**Action Items:**

#### Task 5.1: Port Missing Dialog AXAML Files
For each missing dialog:

1. Copy XAML from WPF to Avalonia
2. Convert WPF syntax to Avalonia:
   - `Window` → `Window` (keep as window)
   - `MessageBox` → Use Avalonia's `MessageBox` or custom
   - `DataGrid` → `DataGrid` (similar but check properties)
   - `ComboBox` → `ComboBox` (verify binding syntax)
   - `Style` references → Update resource keys

3. Create code-behind `.axaml.cs` files
4. Register with dependency injection

#### Task 5.2: Update ViewModels to Use New Dialogs
Check these ViewModels for dialog calls:
- `VisualNodeEditorViewModel.cs`
- `WatchViewModel.cs`
- `TagBrowserViewModel.cs`

---

## PHASED IMPLEMENTATION PLAN

### Phase 1: Foundation (Week 1-2)
**Goal:** Get basic shell architecture working

- [ ] **Task 1.1:** Add Dock.Avalonia NuGet package
- [ ] **Task 1.2:** Convert MainView to MainWindow with proper shell
- [ ] **Task 2.1:** Implement basic Dock.Avalonia integration
- [ ] **Task 4.1:** Create theme directory structure
- [ ] Build and run - verify app starts without errors

**Success Criteria:**
- App launches with MainWindow
- Basic docking works (can drag tabs)
- No build errors

### Phase 2: Core Features (Week 3-4)
**Goal:** Restore Visual Node Editor functionality

- [ ] **Task 3.1:** Compare and identify missing XAML
- [ ] **Task 3.2:** Port missing VisualNodeEditor XAML
- [ ] **Task 3.3:** Implement canvas rendering and connections
- [ ] **Task 2.2:** Add docking layout management to ViewModel

**Success Criteria:**
- Visual Node Editor displays nodes
- Can create connections between nodes
- Drag-drop works
- Zoom/pan functional

### Phase 3: Polish (Week 5-6)
**Goal:** Complete theming and dialogs

- [ ] **Task 4.2:** Port custom theme resources
- [ ] **Task 4.3:** Integrate theme into App.axaml
- [ ] **Task 4.4:** Enhance theme service
- [ ] **Task 5.1:** Port 3 missing dialogs
- [ ] **Task 5.2:** Wire up dialogs in ViewModels

**Success Criteria:**
- Custom metallic theme applied
- All dialogs functional
- Dark/Light mode switching works

### Phase 4: Testing & Bug Fixes (Week 7-8)
**Goal:** Stabilize and validate

- [ ] Manual testing of all features
- [ ] Fix visual regressions
- [ ] Performance optimization
- [ ] Documentation updates

---

## QUICK REFERENCE: WPF vs Avalonia Differences

| Feature | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| Window Base | `Window`, `FluentWindow` | `Window`, `UserControl` | Avalonia prefers MVVM with Views |
| Docking | AvalonDock | Dock.Avalonia | Different APIs |
| Themes | ResourceDictionary | Styles, StyleIncludes | Similar concepts |
| DataGrid | System.Windows.Controls | Avalonia.Controls.DataGrid | Property names differ |
| Commands | ICommand | ICommand | Same interface |
| Bindings | `{Binding}` | `{Binding}` | Same syntax |
| Converters | IValueConverter | IValueConverter | Same interface |
| Events | Click, MouseDown | Click, PointerPressed | Event names differ |
| Animations | Storyboard | Animation, Transition | Different APIs |

---

## IMMEDIATE NEXT STEPS

1. **Start with Task 1.1** - Add Dock.Avalonia package
2. **Then Task 1.2** - Restructure MainWindow
3. **Build frequently** - Don't let errors accumulate
4. **Test after each task** - Verify before moving on

---

## SUPPORTING FILES TO REVIEW

- WPF Reference: `/workspace/ModbusForge/MainWindow.xaml` (572 lines)
- WPF Theme: `/workspace/ModbusForge/Resources/Theme.xaml` (810 lines)
- WPF Node Editor: `/workspace/ModbusForge/Views/VisualNodeEditor.xaml` (416 lines)
- Avalonia Current: `/workspace/ModbusForge.Avalonia/Views/MainView.axaml` (466 lines)
- Avalonia Docking Stub: `/workspace/ModbusForge.Avalonia/Services/AvaloniaDockingHost.cs`

---

*Generated: Based on comparison of ModbusForge v6.1.0 (WPF) vs current Avalonia migration*
