$ErrorActionPreference = 'Stop'
$key = (Get-Content "$env:TEMP\.jules_key" -Raw).Trim()

function Post-Session {
    param([string]$Title, [string]$Prompt)
    $body = @{
        prompt = $Prompt
        title = $Title
        sourceContext = @{
            source = 'sources/github/nokkies/ModbusForge'
            githubRepoContext = @{ startingBranch = 'master' }
        }
        requirePlanApproval = $false
        automationMode = 'AUTO_CREATE_PR'
    } | ConvertTo-Json -Depth 10 -Compress

    $req = [System.Net.HttpWebRequest]::Create('https://jules.googleapis.com/v1alpha/sessions')
    $req.Method = 'POST'
    $req.ContentType = 'application/json'
    $req.Headers.Add('X-Goog-Api-Key', $key)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    $req.ContentLength = $bytes.Length
    $req.GetRequestStream().Write($bytes, 0, $bytes.Length)
    $resp = $req.GetResponse()
    $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
    $j = ($reader.ReadToEnd()) | ConvertFrom-Json
    Write-Host "OK -> $($j.url)"
}

$snap = @'
GOAL
Add snap-to-grid and alignment tools to the Visual Node Editor toolbar. Layouts get messy because nodes can be placed at any pixel and have no alignment helpers.

WHAT TO BUILD

1. Toolbar buttons in ModbusForge/Views/VisualNodeEditor.xaml
   Add the following buttons to the existing toolbar StackPanel around line 187 (where "Clear All", "Auto Layout" already are). Group them in a Separator-delimited section.
   - ToggleButton "Snap to Grid" bound to a new ViewModel property SnapToGrid (default true). When ON, dragging a node snaps its X/Y to the nearest 20px multiple on drop.
   - Button "Align Left" - aligns all selected nodes (currently only 1, but designed for future multi-select) to the leftmost X. For now this aligns the SelectedNode to nothing (no-op) - implement as a working command but note in PR description that it becomes useful when multi-select lands.
   - Button "Align Top" - same pattern for Y.
   - Button "Distribute Horizontally" - evenly distributes selected nodes between leftmost and rightmost. Same multi-select caveat.
   Use small icon-like text labels for the buttons (e.g., "⊥", "⊤", "⫶") OR plain text "Align L", "Align T", "Distrib H". Keep them compact.

2. ViewModel changes in ModbusForge/ViewModels/VisualNodeEditorViewModel.cs
   - Add: [ObservableProperty] private bool _snapToGrid = true;
   - Add: public IRelayCommand AlignLeftCommand { get; }
   - Add: public IRelayCommand AlignTopCommand { get; }
   - Add: public IRelayCommand DistributeHorizontallyCommand { get; }
   - Add: public int GridSize { get; set; } = 20; (for snap math, allow future configuration)
   - The commands operate on a "selection" - for now, use a List that contains just SelectedNode if non-null. Structure the code so adding multi-select later is a small change (e.g., a SelectedNodes property that returns the active selection list).

3. Snap logic in ModbusForge/Views/VisualNodeEditor.xaml.cs
   - In the existing mouse-drag handler (Canvas_MouseLeftButtonUp or wherever drag ends), if _viewModel.SnapToGrid is true, round the node's X and Y to the nearest GridSize multiple BEFORE committing the MoveNodeCommand to the undo stack.

4. Tests in ModbusForge.Tests/ViewModels/VisualNodeEditorSnapTests.cs (NEW)
   - At least 5 tests:
     a. SnapToGrid default is true
     b. Helper method (extract from xaml.cs into a pure static) "SnapToGrid(value, gridSize)" returns expected snapped values for (0,20)->0, (9,20)->0, (10,20)->20, (19,20)->20, (20,20)->20, (-5,20)->0
     c. AlignLeftCommand with no selection is a no-op
     d. AlignLeftCommand with one selected node is a no-op (only meaningful with multi-select)
     e. DistributeHorizontallyCommand with less than 2 nodes is a no-op

SCOPE - files you may modify
- ModbusForge/Views/VisualNodeEditor.xaml (toolbar additions, no other layout changes)
- ModbusForge/Views/VisualNodeEditor.xaml.cs (snap logic in drag, command wiring)
- ModbusForge/ViewModels/VisualNodeEditorViewModel.cs (new properties + commands)
- ModbusForge.Tests/ViewModels/VisualNodeEditorSnapTests.cs (NEW)

OUT OF SCOPE
- Do NOT add multi-select / marquee selection (separate task)
- Do NOT touch VisualSimulationService.cs
- Do NOT change existing toolbar buttons (Clear All, Auto Layout, Tags, Watch)
- Do NOT change connection routing or palette
- Do NOT add new NuGet packages

ACCEPTANCE CRITERIA
- dotnet build ModbusForge.sln -> 0 errors, no new warnings
- dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -> all 201+ existing tests pass plus the 5+ new ones
- The PR description pastes the actual dotnet test output
- Manual test plan in PR description: enable Snap, drag a node, release -> position is on 20px grid. Disable Snap, drag a node, release -> position is free.

PROHIBITED
- Do NOT add Assert.True(true) outside the (non-existent here) skip branch
- Do NOT pretend tests pass without running dotnet test
- Do NOT add shell scripts to the repo
- Do NOT modify existing tests
'@

$multi = @'
GOAL
Add multi-select with marquee (rubber-band) selection and Ctrl+D duplicate to the Visual Node Editor. Currently the user can only select / move / delete one node at a time, which is a major usability pain.

WHAT TO BUILD

1. Multi-selection state in ModbusForge/ViewModels/VisualNodeEditorViewModel.cs
   - Add: public ObservableCollection<VisualNode> SelectedNodes { get; } = new();
   - Keep the existing SelectedNode property (for last-clicked / focused). Maintain SelectedNodes such that it ALWAYS contains SelectedNode when non-null.
   - Add helper methods: AddToSelection(node), RemoveFromSelection(node), ClearSelection() that operate on SelectedNodes.

2. Click behavior in ModbusForge/Views/VisualNodeEditor.xaml.cs
   - Plain click on a node: clear SelectedNodes, add this node, set SelectedNode = this node.
   - Ctrl+Click on a node: toggle membership in SelectedNodes. If becoming first selected, set SelectedNode = this node.
   - Click on empty canvas: clear SelectedNodes and SelectedNode.

3. Marquee selection
   - On Canvas_MouseLeftButtonDown over empty canvas (not over a node), start a marquee. Show a translucent Rectangle that follows the mouse.
   - On MouseMove, update the marquee rectangle dimensions.
   - On MouseLeftButtonUp, compute which node Borders' bounding rects intersect the marquee, set SelectedNodes to those nodes, set SelectedNode = first.
   - Reuse the existing selection visual (the blue border) so all selected nodes show the accent. Update RefreshNodeSelectionVisuals to check SelectedNodes instead of just SelectedNode.

4. Group operations
   - Delete key: when SelectedNodes.Count > 1, delete ALL selected nodes (wrap each removal in DeleteNodeCommand so undo restores them; also remove their connections).
   - Drag a selected node: move ALL selected nodes by the same delta (preserve relative positions).
   - Ctrl+D when at least 1 node is selected: duplicate every selected node, offsetting by (20, 20) px. Wrap in AddNodeCommand for undo.

5. Tests in ModbusForge.Tests/ViewModels/VisualNodeEditorSelectionTests.cs (NEW)
   - At least 6 tests:
     a. Default SelectedNodes is empty
     b. AddToSelection adds + sets SelectedNode for the first
     c. RemoveFromSelection removes + updates SelectedNode appropriately (or null if empty)
     d. ClearSelection empties the collection
     e. Adding the same node twice does not duplicate
     f. Ctrl+D path: pure helper for "compute duplicated nodes with offset" returns correct count and offsets

SCOPE - files you may modify
- ModbusForge/Views/VisualNodeEditor.xaml (the marquee Rectangle element, hidden by default)
- ModbusForge/Views/VisualNodeEditor.xaml.cs (click handling, marquee, group ops)
- ModbusForge/ViewModels/VisualNodeEditorViewModel.cs (SelectedNodes + helpers)
- ModbusForge.Tests/ViewModels/VisualNodeEditorSelectionTests.cs (NEW)
- Update KeyboardShortcutsWindow.xaml to add Ctrl+D and Ctrl+Click entries

OUT OF SCOPE
- Do NOT change snap-to-grid (separate task)
- Do NOT touch connection routing
- Do NOT touch VisualSimulationService.cs
- Do NOT add new NuGet packages

ACCEPTANCE CRITERIA
- dotnet build ModbusForge.sln -> 0 errors, no new warnings
- dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -> all existing tests pass plus the 6+ new ones
- PR description pastes the actual dotnet test output
- Manual test plan: click 3 nodes with Ctrl+Click -> all 3 have blue border. Drag one of them -> all 3 move together. Press Delete -> all 3 gone. Ctrl+Z restores all 3.

PROHIBITED
- Do NOT add Assert.True(true) outside skip branch
- Do NOT pretend tests pass
- Do NOT add shell scripts
- Do NOT modify existing tests beyond updating any that assert single-selection assumptions
'@

$bezier = @'
GOAL
Replace the straight-line connection rendering in the Visual Node Editor with smooth cubic Bezier curves. Straight lines become unreadable spaghetti as graphs grow; gentle horizontal-flowing curves are the industry-standard look for node editors (Blender, Unreal, Comfy, n8n).

WHAT TO BUILD

1. Replace Line elements with Path elements in ModbusForge/Views/VisualNodeEditor.xaml.cs
   - In CreateConnectionLine (and wherever Line elements are created/updated for connections), use System.Windows.Shapes.Path with a PathGeometry containing a BezierSegment instead.
   - Control points: for a connection from (x1, y1) to (x2, y2), the two Bezier control points should be:
     C1 = (x1 + dx, y1)
     C2 = (x2 - dx, y2)
     where dx = max(40, abs(x2 - x1) * 0.5). This produces a smooth S-curve that flows left-to-right with appropriate tension based on horizontal distance.

2. Update incremental redraw in RefreshConnectionsForNode (the dirty-flag method from PR #100)
   - The Dictionary<string, Line> _connectionLines becomes Dictionary<string, Path> _connectionPaths.
   - When updating a connection's endpoints during a drag, rebuild the PathGeometry's BezierSegment in-place (do not recreate the Path element - matches the SIM-5 perf pattern).

3. Hover / right-click behavior must be preserved
   - The right-click-to-delete-connection still works on the Path.
   - StrokeThickness and Stroke color stay the same as before (so visuals are consistent except for the curve shape).

4. Update the temp connection line (the dashed line shown while dragging a wire from a connector)
   - Currently a Line element TempConnectionLine in the XAML. Replace with a Path that uses the same Bezier control-point math.

5. Tests in ModbusForge.Tests/Views/BezierConnectionTests.cs (NEW)
   - Extract the control-point math into a pure helper: public static (Point C1, Point C2) ComputeBezierControlPoints(Point start, Point end).
   - At least 5 tests:
     a. Short horizontal: C1.X = start.X + 40, C2.X = end.X - 40 (when end.X - start.X < 80)
     b. Long horizontal: C1.X and C2.X use the 0.5 * distance rule
     c. Vertical control offset stays at the endpoint Y (control points are horizontal pulls)
     d. Reverse direction (end.X < start.X): control points should still produce a usable curve (the dx formula uses abs)
     e. Same point (start == end): does not throw, returns reasonable values

SCOPE - files you may modify
- ModbusForge/Views/VisualNodeEditor.xaml (only the TempConnectionLine element - replace Line with Path)
- ModbusForge/Views/VisualNodeEditor.xaml.cs (CreateConnectionLine, RefreshConnections, RefreshConnectionsForNode, temp-line updates)
- ModbusForge.Tests/Views/BezierConnectionTests.cs (NEW)

OUT OF SCOPE
- Do NOT add bend points or orthogonal routing (that is a separate, larger task)
- Do NOT change connection colors or stroke thickness
- Do NOT touch the connector dots themselves (input/output ellipses)
- Do NOT touch VisualSimulationService.cs
- Do NOT change the underlying NodeConnection model
- Do NOT add new NuGet packages

ACCEPTANCE CRITERIA
- dotnet build ModbusForge.sln -> 0 errors, no new warnings
- dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj -> all existing tests pass plus the 5+ new ones
- PR description pastes the actual dotnet test output
- Manual test plan: add 2 nodes, connect them - the line is a smooth left-to-right S-curve, not straight. Drag one node around - the curve follows smoothly. Right-click the curve - the existing context-menu delete works.

PROHIBITED
- Do NOT add Assert.True(true) outside skip branch
- Do NOT pretend tests pass
- Do NOT add shell scripts
- Do NOT modify existing tests beyond ones that depend on the exact element type being Line (update them to Path)
'@

Post-Session -Title 'POLISH-1: Snap-to-grid + alignment toolbar (designed for future multi-select)' -Prompt $snap
Post-Session -Title 'POLISH-2: Multi-select (marquee + Ctrl+Click) + Ctrl+D duplicate' -Prompt $multi
Post-Session -Title 'POLISH-3: Bezier curve connections (replaces straight lines)' -Prompt $bezier
