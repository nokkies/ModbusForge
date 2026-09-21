using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels;

/// <summary>
/// Control Expert-style project navigator for a loaded Unity Pro (XEF) project:
/// Application > Section/Programs (functional locations), Task Configuration,
/// Device Configuration (hardware), and IO/Communication busses.
/// </summary>
public sealed partial class PlcProjectViewModel : ObservableObject
{
    public PlcProjectViewModel()
    {
        RootNodes = new ObservableCollection<PlcTreeNodeViewModel>();
    }

    public bool HasProject { get; private set; }

    public string SourceFile { get; private set; } = "";

    public ObservableCollection<PlcTreeNodeViewModel> RootNodes { get; }

    /// <summary>Raised when the selection changes; the PLC page swaps its content area.</summary>
    public event EventHandler<PlcTreeNodeViewModel?>? SelectionChanged;

    /// <summary>FBD sections by name, so the PLC page can hand the editor the right canvas.</summary>
    public IReadOnlyDictionary<string, PlcXmlSection> SectionsByName { get; private set; }
        = new Dictionary<string, PlcXmlSection>();

        public IReadOnlyList<PlcXmlHardwareModule> Hardware => _hardware;
    private IReadOnlyList<PlcXmlHardwareModule> _hardware = Array.Empty<PlcXmlHardwareModule>();

    public string Summary { get; private set; } = "";

    private PlcTreeNodeViewModel? _selectedNode;

    /// <summary>Name of the selected FBD program (drives the editor pane), else null.</summary>
    public string? SelectedFbdProgram { get; private set; }

    /// <summary>True when the selection is an FBD program with a canvas.</summary>
    public bool HasFbdSelected { get; private set; }

    public PlcTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value)) return;

            // Only FBD program rows (not task-assignment rows) switch the canvas.
            SelectedFbdProgram = value != null && value.Kind == PlcNodeKind.ProgramFbd
                ? value.ProgramName
                : null;
            HasFbdSelected = !string.IsNullOrEmpty(SelectedFbdProgram);

            OnPropertyChanged(nameof(SelectedFbdProgram));
            OnPropertyChanged(nameof(HasFbdSelected));
            SelectionChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Switches to the editor program whose name matches the tree selection. The
    /// editor tree can suffix duplicates ("Name_2"), so an exact miss falls back
    /// to a unique "&lt;name&gt;_" prefix match.
    /// </summary>
    public bool ActivateEditorProgram(VisualNodeEditorViewModel? editor, string programName)
    {
        if (editor == null || string.IsNullOrEmpty(programName)) return false;
        var ok = editor.ActivateProgramByName(programName);
        if (!ok)
        {
            var prefixed = editor.ProgramTree.Programs
                .Where(p => p.Name.StartsWith(programName + "_", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name)
                .ToList();
            ok = prefixed.Count == 1 && editor.ActivateProgramByName(prefixed[0]);
        }
        if (ok)
        {
            FitEditorToImportedLayout(editor, programName);
        }
        return ok;
    }

    /// <summary>
    /// Unity Pro layouts spread across huge canvases (GGPLC007's ALARMS spans
    /// 8000x5000 even after the importer caps it). At 100% zoom a program shows
    /// only its top-left corner, which looks like "no logic" — so the PLC page
    /// zooms the editor out until the imported bounding box fits a typical
    /// laptop viewport, the same first-glance behavior as Control Expert.
    /// A program smaller than the viewport stays at 100% (never zoom IN).
    /// A zoom the user sets sticks until the next program is selected.
    /// </summary>
    public void FitEditorToImportedLayout(VisualNodeEditorViewModel? editor, string programName)
    {
        if (editor == null || string.IsNullOrEmpty(programName)) return;
        if (!SectionsByName.TryGetValue(programName, out var section) || section.Nodes.Count == 0) return;
        if (_lastFitProgram == programName && editor.HasUserZoomed) return;

        double maxX = section.Nodes.Max(n => n.X + n.Width);
        double maxY = section.Nodes.Max(n => n.Y + n.Height);
        double fit = Math.Min(FitViewportWidth / Math.Max(maxX, 1.0),
                              FitViewportHeight / Math.Max(maxY, 1.0));
        editor.ResetUserZoomFlag();
        editor.SetZoom(Math.Clamp(fit, MinFitZoom, 1.0));
        _lastFitProgram = programName;
    }

    private string? _lastFitProgram;

    // Reference viewport for the fit: the fit is zoom-OUT only (a program smaller
    // than the viewport stays at 100%), and the zoom floor keeps text readable.
    // ALARMS (6.9k x 4.4k px) lands at ~25%, where block titles still paint.
    private const double FitViewportWidth = 1200;
    private const double FitViewportHeight = 650;
    private const double MinFitZoom = 0.25;

    /// <summary>
    /// Test seam: inject FBD sections without a full XEF import (the fit logic
    /// only needs Nodes per section).
    /// </summary>
    internal void LoadImportedSectionsForTest(IReadOnlyDictionary<string, PlcXmlSection> sections)
        => SectionsByName = sections;

    /// <summary>
    /// Replaces the whole tree with the contents of a freshly imported project.
    /// </summary>
    public void LoadProject(PlcXmlImportResult result)
    {
        SourceFile = result.SourceFile;
        SectionsByName = result.Sections.ToDictionary(s => s.Name, StringComparer.Ordinal);
        _hardware = result.Hardware;
        Summary = $"{result.SourceFile}: {result.TotalNodes} nodes / {result.TotalConnections} wires, " +
                  $"{result.Programs.Count} programs, {result.TagCount} tags";

        RootNodes.Clear();
        RootNodes.Add(BuildApplicationNode(result, SectionsByName));
        RootNodes.Add(BuildTaskNode(result));
        RootNodes.Add(BuildHardwareNode(result));
        RootNodes.Add(BuildDdbNode(result));

        HasProject = true;
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(RootNodes));
        OnPropertyChanged(nameof(Summary));
        SelectedNode = RootNodes[0];
    }

    public void Clear()
    {
        RootNodes.Clear();
        HasProject = false;
        SourceFile = "";
        Summary = "";
        SectionsByName = new Dictionary<string, PlcXmlSection>();
        _hardware = Array.Empty<PlcXmlHardwareModule>();
        SelectedNode = null;
        OnPropertyChanged(nameof(SelectedFbdProgram));
        OnPropertyChanged(nameof(HasFbdSelected));
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(RootNodes));
        OnPropertyChanged(nameof(SourceFile));
        OnPropertyChanged(nameof(Summary));
    }

    // ------------------------------------------------------------------
    // Tree construction (Control Expert layout)
    // ------------------------------------------------------------------

    private static PlcTreeNodeViewModel BuildApplicationNode(
        PlcXmlImportResult result,
        IReadOnlyDictionary<string, PlcXmlSection> sectionsByName)
    {
        var app = new PlcTreeNodeViewModel("Application", PlcNodeKind.Application);
        var secProgs = new PlcTreeNodeViewModel("Section / Programs", PlcNodeKind.SectionProgramsRoot);

        // Programs in MAST order, grouped under their functional-location folder.
        var byLocation = result.Programs
            .GroupBy(p => string.IsNullOrEmpty(p.Location) ? "(unassigned)" : p.Location, StringComparer.Ordinal)
            .OrderBy(g => g.Key.Equals("(unassigned)", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var locGroup in byLocation)
        {
            var locNode = new PlcTreeNodeViewModel(locGroup.Key, PlcNodeKind.Location);
            foreach (var p in locGroup.OrderBy(p => p.Order))
            {
                // A tree program is canvas-editable only when the import actually
                // produced nodes for it (HasFbdCanvas). An FBD section that parsed
                // to zero nodes is shown but flagged as empty, not silently blank.
                var hasCanvas = p.HasFbdCanvas;
                var nodeCount = hasCanvas && sectionsByName.TryGetValue(p.Name, out var s) ? s.Nodes.Count : 0;
                var kind = hasCanvas ? PlcNodeKind.ProgramFbd : PlcNodeKind.ProgramReadOnly;
                var suffix = hasCanvas
                    ? (nodeCount == 0 ? " (empty)" : $" ({nodeCount})")
                    : $" [{p.Language}]";
                var node = new PlcTreeNodeViewModel(p.Name + suffix, kind)
                {
                    ProgramName = p.Name,
                    Detail = hasCanvas
                        ? $"{p.Type} ({p.Language}) — task {p.Task}, location {p.Location}, order {p.Order}, {nodeCount} block(s)"
                        : $"{p.Type} ({p.Language}) — task {p.Task}, location {p.Location}, order {p.Order}. " +
                          (string.Equals(p.Language, "FBD", StringComparison.OrdinalIgnoreCase)
                              ? "No FBD logic was exported for this program (empty section)."
                              : "FBD canvas not available (program is not FBD).")
                };
                locNode.Children.Add(node);
            }
            secProgs.Children.Add(locNode);
        }

        app.Children.Add(secProgs);
        app.Children.Add(new PlcTreeNodeViewModel("Data Management", PlcNodeKind.DdtList) { Detail = $"{result.TagCount} tags (DDB)" });
        return app;
    }


    private static PlcTreeNodeViewModel BuildTaskNode(PlcXmlImportResult result)
    {
        var tc = new PlcTreeNodeViewModel("Task Configuration", PlcNodeKind.TaskConfigRoot);
        foreach (var task in result.Tasks)
        {
            var taskNode = new PlcTreeNodeViewModel($"{task.Name} ({task.Kind})", PlcNodeKind.Task)
            {
                Detail = $"{task.Kind} task, max execution time {task.MaxExecTimeMs} ms, " +
                         $"{task.AssignedPrograms.Count} programs assigned"
            };
            foreach (var prog in task.AssignedPrograms)
            {
                taskNode.Children.Add(new PlcTreeNodeViewModel(prog, PlcNodeKind.AssignedProgram) { ProgramName = prog });
            }
            tc.Children.Add(taskNode);
        }
        return tc;
    }

    private static PlcTreeNodeViewModel BuildHardwareNode(PlcXmlImportResult result)
    {
        var dc = new PlcTreeNodeViewModel("Device Configuration", PlcNodeKind.DeviceConfigRoot);

        var cpu = result.Hardware.FirstOrDefault(h => h.Family.Equals("Quantum", StringComparison.OrdinalIgnoreCase))
                  ?? result.Hardware.FirstOrDefault();
        var station = new PlcTreeNodeViewModel(
            cpu != null ? $"Local Station ({cpu.PartNumber})" : "Local Station", PlcNodeKind.Rack);

        foreach (var h in result.Hardware
                     .Where(h => !string.IsNullOrEmpty(h.TopologicalAddress))
                     .OrderBy(h => TopoDepth(h.TopologicalAddress))
                     .ThenBy(h => h.Position))
        {
            // Skip bus/drop pseudo-nodes; keep CPU, rack, supply and field modules.
            if (h.Family.Equals("drop", StringComparison.OrdinalIgnoreCase) ||
                h.PartNumber.Contains("Bus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var kind = ClassifyModule(h);
            station.Children.Add(new PlcTreeNodeViewModel($"{h.PartNumber} (slot {h.Position})", kind)
            {
                Detail = $"{h.Family} {h.PartNumber} v{h.Version} at {h.TopologicalAddress}" +
                         (string.IsNullOrEmpty(h.Vendor) ? "" : $" — {h.Vendor}")
            });
        }

        dc.Children.Add(station);
        return dc;
    }

    private static PlcNodeKind ClassifyModule(PlcXmlHardwareModule h)
    {
        var pn = h.PartNumber;
        if (pn.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return PlcNodeKind.Cpu;
        if (pn.Contains("CPS", StringComparison.OrdinalIgnoreCase)) return PlcNodeKind.Supply;
        if (pn.Contains("XBP", StringComparison.OrdinalIgnoreCase)) return PlcNodeKind.Rack;
        if (pn.StartsWith("140D", StringComparison.Ordinal)) return PlcNodeKind.DiscreteModule;
        if (pn.StartsWith("140A", StringComparison.Ordinal)) return PlcNodeKind.AnalogModule;
        return PlcNodeKind.Module;
    }

    private static int TopoDepth(string topo) => topo.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;

    private static PlcTreeNodeViewModel BuildDdbNode(PlcXmlImportResult result)
    {
        var io = new PlcTreeNodeViewModel("I/O and Communication Busses", PlcNodeKind.BusRoot)
        {
            Detail = $"{result.Hardware.Count} hardware entries in IOConf"
        };
        return io;
    }
}

public enum PlcNodeKind
{
    Application,
    SectionProgramsRoot,
    Location,
    ProgramFbd,
    ProgramReadOnly,
    DdtList,
    TaskConfigRoot,
    Task,
    AssignedProgram,
    DeviceConfigRoot,
    Rack,
    Cpu,
    Supply,
    DiscreteModule,
    AnalogModule,
    Module,
    BusRoot,
}

/// <summary>One row of the PLC project tree.</summary>
public sealed class PlcTreeNodeViewModel : ObservableObject
{
    public PlcTreeNodeViewModel(string title, PlcNodeKind kind)
    {
        Title = title;
        Kind = kind;
        Children = new ObservableCollection<PlcTreeNodeViewModel>();
    }

    public string Title { get; }
    public PlcNodeKind Kind { get; }

    /// <summary>Program name this node refers to (FBD sections, task assignments).</summary>
    public string? ProgramName { get; init; }

    /// <summary>Shown in the properties pane when this node is selected.</summary>
    public string Detail { get; init; } = "";

    public ObservableCollection<PlcTreeNodeViewModel> Children { get; }

    public bool IsFbdProgram => Kind == PlcNodeKind.ProgramFbd;
    public bool IsHardware => Kind is PlcNodeKind.Cpu or PlcNodeKind.Supply or PlcNodeKind.Rack
        or PlcNodeKind.DiscreteModule or PlcNodeKind.AnalogModule or PlcNodeKind.Module;
}





