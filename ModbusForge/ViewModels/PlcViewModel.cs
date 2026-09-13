using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaPoint = Avalonia.Point;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Core.Xef;
using ModbusForge.Data;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// The IEC 61131-3 element kind of an FBD box, which drives its box shape and whether an
    /// instance name is drawn above the box (function blocks only) and its body glyph.
    /// </summary>
    public enum FbdBlockKind
    {
        /// <summary>An operator / function (ADD, AND, OR, REAL_TO_INT). No instance name above.</summary>
        Function,

        /// <summary>A function block (TON, CTU, R_TRIG, custom POU). Instance name above the box.</summary>
        FunctionBlock,
    }

    /// <summary>
    /// View model for the PLC screen — a separate window/tab from the normal Simulation screen.
    /// It opens a Schneider .XEF/.ZEF, shows the PLC program logic graphically (FBD block graph,
    /// or the ST source text), and monitors the live I/O values so the user can watch the real
    /// program's behaviour. This is a "true" view of the PLC code; it does not drive the normal
    /// (valve/motor/level) simulation, which remains its own screen.
    /// </summary>
    public partial class PlcViewModel : ObservableObject
    {
        private readonly IFileDialogService? _fileDialogService;
        private readonly IMessageBoxService? _messageBoxService;
        private readonly IConnectionManager? _connectionManager;
        private readonly ILogger<PlcViewModel> _logger;

        private XefProject? _project;
        private CancellationTokenSource? _monitorCts;
        private PlcSimulationRunner? _simRunner;
        private DataStore? _simDataStore;
        private System.Threading.Timer? _simUiTimer;
        private XefSimulationGraph? _simGraph;

        /// <summary>Captured on the UI thread (the VM is constructed by DI on the UI thread) so
        /// background simulation reads can marshal property updates back to the UI thread.</summary>
        private readonly System.Threading.SynchronizationContext? _uiContext;

        public PlcViewModel(
            IFileDialogService? fileDialogService = null,
            IMessageBoxService? messageBoxService = null,
            IConnectionManager? connectionManager = null,
            ILogger<PlcViewModel>? logger = null)
        {
            _fileDialogService = fileDialogService;
            _messageBoxService = messageBoxService;
            _connectionManager = connectionManager;
            _logger = logger ?? NullLogger<PlcViewModel>.Instance;
            _uiContext = System.Threading.SynchronizationContext.Current;
        }

        [ObservableProperty]
        private bool _isLoaded;

        [ObservableProperty]
        private string _statusText = "No XEF loaded. Use 'Open XEF' to load a Schneider export.";

        [ObservableProperty]
        private string? _fileName;

        [ObservableProperty]
        private string _projectName = string.Empty;

        [ObservableProperty]
        private bool _hasStPrograms;

        [ObservableProperty]
        private ObservableCollection<PlcProgramItem> _programs = new();

        [ObservableProperty]
        private PlcProgramItem? _selectedProgram;

        [ObservableProperty]
        private ObservableCollection<PlcBlockItem> _blocks = new();

        [ObservableProperty]
        private ObservableCollection<PlcLinkItem> _links = new();

        [ObservableProperty]
        private string _stSource = string.Empty;

        [ObservableProperty]
        private bool _showStSource;

        [ObservableProperty]
        private bool _isFbd;

        /// <summary>True when the selected program is a Ladder Diagram (LD) — show the LD view.</summary>
        [ObservableProperty]
        private bool _showLadder;

        /// <summary>The ladder rungs of the selected LD program (empty otherwise).</summary>
        [ObservableProperty]
        private IReadOnlyList<PlcLadderRungItem> _ladderRungs = Array.Empty<PlcLadderRungItem>();

        /// <summary>FBD zoom factor (1.0 = 100%). Drives the canvas ScaleTransform.</summary>
        [ObservableProperty]
        private double _fbdZoom = 1.0;

        /// <summary>FBD canvas render width in layout units (content bounds × zoom).</summary>
        [ObservableProperty]
        private double _fbdCanvasWidth = 1800;

        /// <summary>FBD canvas render height in layout units (content bounds × zoom).</summary>
        [ObservableProperty]
        private double _fbdCanvasHeight = 2400;

        [ObservableProperty]
        private ObservableCollection<PlcVariableItem> _variables = new();

        [ObservableProperty]
        private bool _monitorEnabled;

        /// <summary>
        /// Whether the live I/O monitor panel is visible. Toggling it off frees the full height
        /// for the diagram (a mainstream "collapse the I/O watch" control).
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ToggleIoMonitorCommand))]
        private bool _showIoMonitor = true;

        /// <summary>Toggles the I/O monitor panel's visibility (frees space for the diagram).</summary>
        [RelayCommand]
        private void ToggleIoMonitor() => ShowIoMonitor = !ShowIoMonitor;

        [ObservableProperty]
        private int _monitorPeriodMsValue = 500;

        [ObservableProperty]
        private byte _unitIdValue = 1;

        [ObservableProperty]
        private string _selectedBlockDetail = string.Empty;

        // ── Run/Stop the true PLC simulation ────────────────────────────────────
        // The selected FBD program is translated (XefToSimulationConfig) into the existing
        // visual-node graph and executed cycle-by-cycle by PlcSimulationRunner against a
        // shared DataStore. Live values are polled into the on-screen blocks and I/O table.
        [ObservableProperty]
        private string _simButtonContent = "Run simulation";

        [ObservableProperty]
        private bool _simulationRunning;

        [ObservableProperty]
        private string _simNotes = string.Empty;

        /// <summary>The XEF type-name→PlcElementType mapping report for the selected program.</summary>
        [ObservableProperty]
        private string _simUnsupportedSummary = string.Empty;

        [RelayCommand]
        private void ToggleSimulation()
        {
            if (SimulationRunning)
            {
                StopSimulation();
                return;
            }

            StartSimulation();
        }

        private void StartSimulation()
        {
            if (_project == null)
            {
                StatusText = "Load an XEF first, then run the simulation.";
                return;
            }

            var program = SelectedProgram == null ? null : _project.FindProgram(SelectedProgram.Name);
            if (program == null)
            {
                StatusText = "Select a program to simulate.";
                return;
            }

            if (program.Language == "ST" || program.Blocks.Count == 0)
            {
                StatusText = "This program is ST (or has no FBD blocks); it is shown as source, not executed.";
                SimButtonContent = "Run simulation";
                return;
            }

            try
            {
                // Translate the FBD program into the existing visual-node graph.
                _simGraph = XefToSimulationConfig.Translate(program, _project);

                // Report which blocks have no simulation equivalent.
                SimUnsupportedSummary = _simGraph.UnsupportedBlocks.Count == 0
                    ? string.Empty
                    : $"{_simGraph.UnsupportedBlocks.Count} block(s) have no simulation equivalent: "
                      + string.Join(", ", _simGraph.UnsupportedBlocks.Select(b => $"{b.InstanceName} ({b.TypeName})"));

                SimNotes = string.Join("\n", _simGraph.Notes);

                // Build the shared DataStore and seed it from the XEF variableInit values so
                // the program starts from the PLC's initial state (Q2 default).
                _simDataStore = new DataStore();
                SeedDataStore(_simDataStore);

                // Drive the runner. A faster UI refresh (e.g. 50 ms) than the scan keeps the
                // canvas current; the scan interval is user-adjustable in the toolbar.
                var scanMs = Math.Clamp(SimScanIntervalMs, 10, 10000);
                _simRunner = new PlcSimulationRunner(_simGraph.Config, _simDataStore, scanMs, null);
                _simRunner.Start();

                // Poll live values into the UI.
                _simUiTimer?.Dispose();
                _simUiTimer = new System.Threading.Timer(_ => RefreshSimulationLiveValues(),
                    null, 50, 50);

                SimulationRunning = true;
                SimButtonContent = "Stop simulation";
                StatusText = $"Simulating '{program.Name}': {_simGraph.MappedBlocks.Count} mapped block(s), "
                             + $"{_simGraph.UnsupportedBlocks.Count} unsupported, scan {scanMs} ms.";
                _logger.LogInformation("Started XEF simulation for program '{Name}' ({Mapped} mapped, {Unsupported} unsupported)",
                    program.Name, _simGraph.MappedBlocks.Count, _simGraph.UnsupportedBlocks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start XEF simulation for program '{Name}'", program.Name);
                StatusText = "Failed to start simulation.";
                CleanupSimulation();
                SimButtonContent = "Run simulation";
                if (_messageBoxService != null)
                {
                    _ = _messageBoxService.ShowAsync(
                        $"Could not start the simulation: {ex.Message}",
                        "PLC simulation", DialogButton.Ok, DialogIcon.Error);
                }
            }
        }

        private void StopSimulation()
        {
            CleanupSimulation();
            SimulationRunning = false;
            SimButtonContent = "Run simulation";
            StatusText = "Simulation stopped.";
            _logger.LogInformation("Stopped XEF simulation");
        }

        private void CleanupSimulation()
        {
            _simUiTimer?.Dispose();
            _simUiTimer = null;
            try
            {
                _simRunner?.Stop();
                _simRunner?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error stopping the XEF simulation runner");
            }
            _simRunner = null;
            _simDataStore = null;
            _simGraph = null;
        }

        /// <summary>The user-adjustable scan period for the simulation (also bound to a toolbar control).</summary>
        [ObservableProperty]
        private int _simScanIntervalMs = 100;

        /// <summary>
        /// Polls the runner's live values onto the UI: per-block outputs into
        /// <see cref="PlcBlockItem.LiveValue"/> and the mapped I/O cells into the I/O table.
        /// Runs on a timer thread, so it marshals the collection mutations back to the UI via
        /// property setters (Avalonia collections are mutated from this thread consistently).
        /// </summary>
        private void RefreshSimulationLiveValues()
        {
            // Runs on the runner's timer thread; marshal the UI mutation back to the UI thread.
            if (_uiContext != null && !ReferenceEquals(_uiContext, System.Threading.SynchronizationContext.Current))
            {
                _uiContext.Post(_ => RefreshSimulationLiveValuesUiThread(), null);
                return;
            }

            RefreshSimulationLiveValuesUiThread();
        }

        private void RefreshSimulationLiveValuesUiThread()
        {
            var runner = _simRunner;
            var dataStore = _simDataStore;
            if (runner == null || dataStore == null)
            {
                return;
            }

            var nodeValues = runner.GetLatestNodeValues();

            // Per-block live output values (keyed by node id == the block instance name).
            foreach (var block in Blocks)
            {
                if (nodeValues.TryGetValue(block.Block.InstanceName, out var val))
                {
                    var text = FormatSimValue(val);
                    if (!string.Equals(block.LiveValue, text, StringComparison.Ordinal))
                    {
                        block.LiveValue = text;
                    }
                }
            }

            // I/O table: read the mapped cells from the shared DataStore (lock with the runner).
            foreach (var v in Variables)
            {
                if (v.AddressValue == null)
                {
                    continue;
                }

                var text = ReadDataStoreCell(dataStore, v.AddressValue.Value, v.Area);
                if (text != null && !string.Equals(v.LiveValue, text, StringComparison.Ordinal))
                {
                    v.LiveValue = text;
                }
            }
        }

        private static string FormatSimValue(double value)
        {
            // Bool ports surface as 0/1; show them as TRUE/FALSE, numbers otherwise.
            if (value == 0.0) return "FALSE";
            if (value == 1.0) return "TRUE";
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Seeds the DataStore from the XEF variableInit values (best-effort).</summary>
        private void SeedDataStore(DataStore store)
        {
            if (_project == null) return;

            foreach (var variable in _project.Variables)
            {
                var address = variable.TryParseAddress();
                if (address == null) continue;

                var (area, addr) = address.Value;
                if (addr < 1 || addr > ushort.MaxValue) continue;

                try
                {
                    switch (area)
                    {
                        case PlcArea.HoldingRegister:
                            if (ushort.TryParse(variable.Value, System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture, out var hw))
                                store.HoldingRegisters[addr] = hw;
                            break;
                        case PlcArea.Coil:
                            store.CoilDiscretes[addr] = ParseBool(variable.Value);
                            break;
                        case PlcArea.InputRegister:
                            if (ushort.TryParse(variable.Value, System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture, out var iw))
                                store.InputRegisters[addr] = iw;
                            break;
                        case PlcArea.DiscreteInput:
                            store.InputDiscretes[addr] = ParseBool(variable.Value);
                            break;
                    }
                }
                catch
                {
                    // Skip an unparseable init value; the cell keeps its default.
                }
            }
        }

        private static bool ParseBool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Equals("1", StringComparison.Ordinal)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("on", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("T", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadDataStoreCell(DataStore store, int address, PlcArea area)
        {
            try
            {
                // The runner locks the DataStore during Execute; read under the same lock
                // (the runner exposes the store reference; lock on it).
                lock (store)
                {
                    return area switch
                    {
                        PlcArea.HoldingRegister => store.HoldingRegisters[address].ToString(),
                        PlcArea.Coil => (store.CoilDiscretes[address] ? "TRUE" : "FALSE"),
                        PlcArea.InputRegister => store.InputRegisters[address].ToString(),
                        PlcArea.DiscreteInput => (store.InputDiscretes[address] ? "TRUE" : "FALSE"),
                        _ => null,
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        partial void OnSelectedProgramChanged(PlcProgramItem? value)
        {
            PopulateProgramView(value);
        }

        partial void OnSelectedBlockItemChanged(PlcBlockItem? value)
        {
            SelectedBlockDetail = value?.Detail ?? string.Empty;
            // Keep each block's highlight flag in sync with the single selection.
            foreach (var b in Blocks)
            {
                b.IsSelected = ReferenceEquals(b, value);
            }
        }

        [ObservableProperty]
        private PlcBlockItem? _selectedBlockItem;

        /// <summary>Opens an XEF/ZEF file via the file dialog and parses it.</summary>
        [RelayCommand]
        private async Task OpenXefAsync()
        {
            if (_fileDialogService == null)
            {
                StatusText = "File dialog service unavailable.";
                return;
            }

            StatusText = "Choosing XEF file…";
            var path = await _fileDialogService.ShowOpenFileDialogAsync(
                "Open XEF",
                "XEF files (*.xef)|*.xef|ZEF files (*.zef)|*.zef|All files (*.*)|*.*");

            if (string.IsNullOrEmpty(path))
            {
                StatusText = "No file selected.";
                return;
            }

            try
            {
                StatusText = $"Parsing {System.IO.Path.GetFileName(path)}…";
                var project = await Task.Run(() => XefParser.ParseFile(path, _logger));
                LoadProject(project);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse XEF {Path}", path);
                StatusText = "Failed to parse XEF.";
                if (_messageBoxService != null)
                {
                    _ = _messageBoxService.ShowAsync(
                        $"Could not parse '{System.IO.Path.GetFileName(path)}': {ex.Message}",
                        "XEF parse failed",
                        DialogButton.Ok,
                        DialogIcon.Error);
                }
            }
        }

        /// <summary>
        /// Loads an XEF/ZEF file from an explicit path (used by the <c>--xef</c>
        /// startup switch and by automated checks). Parses the file and calls
        /// <see cref="LoadProject"/>. Returns true on success.
        /// </summary>
        public async Task<bool> LoadFileAsync(string path)
            => await LoadFileAsync(path, programName: null);

        /// <summary>
        /// Loads an XEF/ZEF file and, when <paramref name="programName"/> is supplied,
        /// selects that program for display (used by the <c>--xef</c>/<c>--program</c>
        /// startup switches). Returns true on success.
        /// </summary>
        public async Task<bool> LoadFileAsync(string path, string? programName)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                StatusText = "XEF file not found.";
                return false;
            }

            try
            {
                StatusText = $"Parsing {System.IO.Path.GetFileName(path)}…";
                var project = await Task.Run(() => XefParser.ParseFile(path, _logger));
                LoadProject(project);
                if (!string.IsNullOrEmpty(programName))
                {
                    SelectProgramByName(programName);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse XEF {Path}", path);
                StatusText = "Failed to parse XEF.";
                return false;
            }
        }

        /// <summary>Selects the program with the given name (case-insensitive), if loaded.</summary>
        public void SelectProgramByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var match = Programs.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SelectedProgram = match;
            }
        }

        /// <summary>Loads a parsed project into the view (also used by tests / embedded callers).</summary>
        public void LoadProject(XefProject project)
        {
            _project = project;
            ProjectName = project.Name;
            FileName = project.SourcePath ?? string.Empty;
            HasStPrograms = project.HasStPrograms;
            IsLoaded = true;

            Programs = new ObservableCollection<PlcProgramItem>(
                project.Programs.Select(p => new PlcProgramItem(p.Name, p.Language)));

            // Build the variable/IO monitor table.
            Variables = new ObservableCollection<PlcVariableItem>(
                project.Variables.Select(v => new PlcVariableItem(v)));

            StatusText = DescribeProject(project);

            // Default to the first program.
            SelectedProgram = Programs.FirstOrDefault();

            _logger.LogInformation("Loaded XEF project '{Name}': {Programs} programs, {Vars} variables",
                project.Name, project.Programs.Count, project.Variables.Count);
        }

        private static string DescribeProject(XefProject project)
        {
            var baseText = $"Loaded '{project.Name}': {project.Programs.Count} program(s), " +
                           $"{project.Variables.Count} variable(s), {project.DataTypes.Count} data type(s).";
            if (project.HasStPrograms)
            {
                baseText += " Contains ST programs (shown as source).";
            }

            if (project.Warnings.Count > 0)
            {
                baseText += $" {project.Warnings.Count} warning(s) — see console.";
            }

            return baseText;
        }

        private void PopulateProgramView(PlcProgramItem? item)
        {
            Blocks.Clear();
            Links.Clear();
            StSource = string.Empty;
            ShowStSource = false;
            IsFbd = false;
            ShowLadder = false;
            LadderRungs = Array.Empty<PlcLadderRungItem>();
            SelectedBlockItem = null;
            SelectedBlockDetail = string.Empty;

            if (item == null || _project == null)
            {
                return;
            }

            var program = _project.FindProgram(item.Name);
            if (program == null)
            {
                return;
            }

            if (program.Language == "ST" && !string.IsNullOrEmpty(program.StSource))
            {
                // ST program: show the source text.
                ShowStSource = true;
                IsFbd = false;
                StSource = program.StSource;
                StatusText = $"Program '{program.Name}' (ST) — showing source.";
                return;
            }

            // Ladder program: show the LD rungs.
            if (program.IsLadder)
            {
                ShowLadder = true;
                IsFbd = false;
                LadderRungs = program.Rungs
                    .Select(rung => new PlcLadderRungItem(rung.Index, rung.Elements
                        .Select(el => new PlcLadderElementItem(el.Kind, el.Tag, el.BlockType, el.Col, el.Row))
                        .ToList()))
                    .ToList();
                StatusText = $"Program '{program.Name}' (LD): {program.Rungs.Count} networks.";
                return;
            }

            // FBD program: show the block graph.
            IsFbd = true;
            ShowStSource = false;

            foreach (var block in program.Blocks)
            {
                var item2 = new PlcBlockItem(block, _project);
                Blocks.Add(item2);
            }

            foreach (var link in program.Links)
            {
                var src = program.FindBlock(link.SourceBlock);
                var dst = program.FindBlock(link.DestBlock);
                if (src == null || dst == null)
                {
                    continue;
                }

                // Resolve the matching view items (built above) to anchor the wire to each
                // block's BOX (not the whole item, which includes the side pin columns).
                var srcItem = Blocks.FirstOrDefault(b => b.Name == src.InstanceName);
                var dstItem = Blocks.FirstOrDefault(b => b.Name == dst.InstanceName);
                var side = PlcBlockItem.SideColumnWidth;

                // The box sits in the middle column: its left edge is item X + side width.
                double srcBoxX = (srcItem?.X ?? 0) + side;
                double srcBoxY = srcItem?.Y ?? 0;
                double srcBoxW = srcItem?.BoxWidth ?? Math.Max(120, src.Width * PlcBlockItem.CellsToPixels * PlcBlockItem.LayoutZoom);
                double srcBoxH = srcItem?.BoxHeight ?? Math.Max(52, src.Height * PlcBlockItem.CellsToPixels * 0.4 * PlcBlockItem.LayoutZoom);

                double dstBoxX = (dstItem?.X ?? 0) + side;
                double dstBoxY = dstItem?.Y ?? 0;
                double dstBoxW = dstItem?.BoxWidth ?? Math.Max(120, dst.Width * PlcBlockItem.CellsToPixels * PlcBlockItem.LayoutZoom);
                double dstBoxH = dstItem?.BoxHeight ?? Math.Max(52, dst.Height * PlcBlockItem.CellsToPixels * 0.4 * PlcBlockItem.LayoutZoom);

                Links.Add(new PlcLinkItem(
                    srcBoxX, srcBoxY, srcBoxW, srcBoxH,
                    dstBoxX, dstBoxY, dstBoxW, dstBoxH,
                    link.SourcePin,
                    link.DestPin));
            }

            StatusText = $"Program '{program.Name}' (FBD): {program.Blocks.Count} blocks, {program.Links.Count} links.";

            _logger.LogInformation(
                "FBD view '{Name}': IsFbd={IsFbd} ShowSt={St} Blocks={Blocks} Links={Links} first=(X={FX},Y={FY},W={FW},H={FH})",
                program.Name, IsFbd, ShowStSource, Blocks.Count, Links.Count,
                Blocks.Count > 0 ? Blocks[0].X : -1, Blocks.Count > 0 ? Blocks[0].Y : -1,
                Blocks.Count > 0 ? Blocks[0].Width : -1, Blocks.Count > 0 ? Blocks[0].Height : -1);

            // Size the canvas to the block bounds (with margin) and reset to 100%.
            RecomputeFbdCanvasSize();
        }

        /// <summary>
        /// Recomputes the FBD canvas render size (block bounds + margin) × current zoom, so the
        /// ScrollViewer's extent matches the visible content at the current zoom level.
        /// </summary>
        private void RecomputeFbdCanvasSize()
        {
            if (Blocks.Count == 0)
            {
                FbdCanvasWidth = 1800;
                FbdCanvasHeight = 2400;
                return;
            }

            double maxX = Blocks.Max(b => b.X + b.Width);
            double maxY = Blocks.Max(b => b.Y + b.Height);
            const double margin = 80;
            double logicalW = maxX + margin;
            double logicalH = maxY + margin;

            FbdCanvasWidth = Math.Max(800, logicalW * FbdZoom);
            FbdCanvasHeight = Math.Max(600, logicalH * FbdZoom);
        }

        private const double FbdZoomStep = 0.15;
        private const double FbdZoomMin = 0.25;
        private const double FbdZoomMax = 3.0;

        /// <summary>Zooms the FBD in by one step (relative to the current centre).</summary>
        [RelayCommand]
        private void FbdZoomIn()
        {
            FbdZoom = Math.Min(FbdZoomMax, FbdZoom + FbdZoomStep);
            RecomputeFbdCanvasSize();
        }

        /// <summary>Zooms the FBD out by one step.</summary>
        [RelayCommand]
        private void FbdZoomOut()
        {
            FbdZoom = Math.Max(FbdZoomMin, FbdZoom - FbdZoomStep);
            RecomputeFbdCanvasSize();
        }

        /// <summary>Resets the FBD zoom to 100%.</summary>
        [RelayCommand]
        private void FbdZoomReset()
        {
            FbdZoom = 1.0;
            RecomputeFbdCanvasSize();
        }

        /// <summary>Starts/stops the live I/O monitor that refreshes the variable values from the active Modbus connection.</summary>
        [RelayCommand]
        private void ToggleMonitor()
        {
            if (MonitorEnabled)
            {
                StopMonitor();
            }
            else
            {
                StartMonitor();
            }
        }

        private void StartMonitor()
        {
            if (_connectionManager?.ActiveService == null)
            {
                StatusText = "Cannot monitor: no active Modbus connection.";
                return;
            }

            _monitorCts?.Cancel();
            _monitorCts = new CancellationTokenSource();
            var token = _monitorCts.Token;
            MonitorEnabled = true;
            StatusText = $"Monitoring I/O every {MonitorPeriodMsValue} ms (unit {UnitIdValue}).";
            _ = MonitorLoopAsync(token);
        }

        private void StopMonitor()
        {
            _monitorCts?.Cancel();
            _monitorCts = null;
            MonitorEnabled = false;
            StatusText = "Monitor stopped.";
        }

        private async Task MonitorLoopAsync(CancellationToken token)
        {
            var service = _connectionManager?.ActiveService;
            if (service == null)
            {
                return;
            }

            // Group variables by the register area they map to, so we read in batches.
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(MonitorPeriodMsValue, token);
                    // The true simulation owns the I/O table while running; don't clobber it.
                    if (SimulationRunning) continue;
                    await RefreshLiveValuesAsync(service, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "I/O monitor tick failed (connection may have dropped)");
                    // Keep trying; the next tick or a reconnect will recover.
                }
            }
        }

        private async Task RefreshLiveValuesAsync(IModbusService service, CancellationToken token)
        {
            // For v1 we read holding registers in a single contiguous sweep covering the
            // mapped variable addresses. This is simple and sufficient for monitoring.
            var mapped = Variables.Where(v => v.AddressValue.HasValue).ToList();
            if (mapped.Count == 0)
            {
                return;
            }

            var start = mapped.Min(v => v.AddressValue!.Value) - 1; // 0-based
            var end = mapped.Max(v => v.AddressValue!.Value);       // inclusive 0-based
            var count = end - start + 1;
            if (count <= 0 || count > 125)
            {
                // Fall back to per-variable reads when the span is too wide for one batch.
                foreach (var v in mapped)
                {
                    if (token.IsCancellationRequested) break;
                    var addr = v.AddressValue!.Value - 1;
                    var val = await service.ReadHoldingRegistersAsync(UnitIdValue, addr, 1);
                    if (val != null && val.Length > 0)
                    {
                        v.LiveValue = val[0].ToString();
                    }
                }

                return;
            }

            var values = await service.ReadHoldingRegistersAsync(UnitIdValue, start, count);
            if (values == null)
            {
                return;
            }

            foreach (var v in mapped)
            {
                var idx = v.AddressValue!.Value - 1 - start;
                if (idx >= 0 && idx < values.Length)
                {
                    v.LiveValue = values[idx].ToString();
                }
            }
        }
    }

    /// <summary>A program row in the program tree.</summary>
    public sealed class PlcProgramItem
    {
        public PlcProgramItem(string name, string language)
        {
            Name = name;
            Language = language;
            Display = $"{name}  [{language}]";
        }

        public string Name { get; }
        public string Language { get; }
        public string Display { get; }
    }

    /// <summary>
    /// A single element on a ladder rung for the LD view: its IEC kind (NO/NC contact, coil,
    /// set/reset coil, inline block), its tag, and its grid position (col/row) used to lay it
    /// out on the rung.
    /// </summary>
    public sealed class PlcLadderElementItem
    {
        public PlcLadderElementItem(LadderElementKind kind, string tag, string blockType, int col, int row)
        {
            Kind = kind;
            Tag = tag;
            BlockType = blockType;
            Col = col;
            Row = row;
            Label = kind == LadderElementKind.Coil && string.IsNullOrEmpty(blockType) ? tag : tag;
            IsBlock = !string.IsNullOrEmpty(blockType);
            Glyph = GlyphFor(kind);
        }

        public LadderElementKind Kind { get; }
        public string Tag { get; }
        public string BlockType { get; }
        public int Col { get; }
        public int Row { get; }
        public bool IsBlock { get; }
        public string Label { get; }

        /// <summary>A short symbol hint for the element (e.g. the slash mark for NC).</summary>
        public string Glyph { get; }

        /// <summary>True when this element is a contact (NO/NC/edge) — renders the bars.</summary>
        public bool IsContact => Kind is LadderElementKind.Contact
                                  or LadderElementKind.ContactNegated
                                  or LadderElementKind.ContactRise
                                  or LadderElementKind.ContactFall;

        /// <summary>True when the contact is normally-closed (draw the diagonal slash).</summary>
        public bool IsNegatedContact => Kind == LadderElementKind.ContactNegated;

        /// <summary>True when the contact is a rising/falling-edge contact (draw the edge marker).</summary>
        public bool IsEdgeContact => Kind is LadderElementKind.ContactRise or LadderElementKind.ContactFall;

        /// <summary>True when this element is a coil (set/reset/negated) — renders the circle.</summary>
        public bool IsCoil => Kind is LadderElementKind.Coil
                               or LadderElementKind.CoilNegated
                               or LadderElementKind.CoilSet
                               or LadderElementKind.CoilReset;

        private static string GlyphFor(LadderElementKind kind) => kind switch
        {
            LadderElementKind.ContactNegated or LadderElementKind.CoilNegated => "/",
            LadderElementKind.ContactRise => "↑",
            LadderElementKind.ContactFall => "↓",
            LadderElementKind.CoilSet => "S",
            LadderElementKind.CoilReset => "R",
            _ => string.Empty,
        };
    }

    /// <summary>A ladder rung (network) for the LD view, with its ordered elements.</summary>
    public sealed class PlcLadderRungItem
    {
        public PlcLadderRungItem(int index, IReadOnlyList<PlcLadderElementItem> elements)
        {
            NetworkNo = index + 1;
            Elements = elements;
        }

        /// <summary>The display network number (1-based).</summary>
        public int NetworkNo { get; }

        public IReadOnlyList<PlcLadderElementItem> Elements { get; }
    }

    /// <summary>A block in the FBD graph view, positioned from the XEF canvas coordinates.</summary>
    public partial class PlcBlockItem : ObservableObject
    {
        /// <summary>
        /// Pixels per XEF FBD grid cell. Unity's FBD canvas is drawn on a grid where each
        /// cell is ~40px; the XEF stores positions/sizes in cells, so we scale up here.
        /// </summary>
        internal const double CellsToPixels = 40.0;

        /// <summary>
        /// Layout zoom applied on top of the cell→pixel scale so the (sparse) FBD canvas
        /// compacts into a readable size that fits the on-screen viewport.
        /// </summary>
        internal const double LayoutZoom = 0.4;

        /// <summary>Fixed pixel width of each side pin-field column (inputs left, outputs right).</summary>
        internal const double SideColumnWidth = 90.0;

        public PlcBlockItem(XefBlock block, XefProject project)
        {
            Block = block;
            Name = block.InstanceName;
            TypeName = block.TypeName;

            // Scale the XEF grid-cell coordinates to canvas pixels, then compact by
            // LayoutZoom so several blocks fit the viewport at once.
            X = block.PosX * CellsToPixels * LayoutZoom;
            Y = block.PosY * CellsToPixels * LayoutZoom;

            // The rendered box: a readable fixed-ish width and a height that scales with the
            // XEF grid cells (so a block with many pins is taller). The item is wider than the
            // box because the input/output pin fields sit in side columns.
            BoxWidth = Math.Max(110, block.Width * CellsToPixels * LayoutZoom);
            BoxHeight = Math.Max(44, block.Height * CellsToPixels * 0.35 * LayoutZoom);

            // The canvas item footprint (box + two 90px side pin columns) used for layout
            // spacing. The box therefore occupies the middle column of exactly BoxWidth.
            Width = BoxWidth + 2 * SideColumnWidth;
            Height = BoxHeight;

            // Resolve the declared data type per pin from the block's type signature (if known).
            var blockType = project.BlockTypes
                .FirstOrDefault(t => string.Equals(t.Name, block.TypeName, StringComparison.OrdinalIgnoreCase));

            var inputPins = new List<PlcPinItem>();
            foreach (var input in block.Inputs)
            {
                string? dtype = blockType?.Inputs
                    .FirstOrDefault(p => string.Equals(p.Name, input.FormalParameter, StringComparison.OrdinalIgnoreCase))?.Type;
                inputPins.Add(new PlcPinItem(input.FormalParameter, input.EffectiveParameter,
                    input.Inverted, isInput: true, dtype));
            }

            var outputPins = new List<PlcPinItem>();
            foreach (var output in block.Outputs)
            {
                string? dtype = blockType?.Outputs
                    .FirstOrDefault(p => string.Equals(p.Name, output.FormalParameter, StringComparison.OrdinalIgnoreCase))?.Type;
                outputPins.Add(new PlcPinItem(output.FormalParameter, output.EffectiveParameter,
                    output.Inverted, isInput: false, dtype));
            }

            InputPins = inputPins;
            OutputPins = outputPins;

            // Border tint by element type so function blocks, contacts and coils are distinct.
            BorderTint = ClassifyTint(block.InstanceName, block.TypeName);

            // IEC element kind + rendering flags (instance-name-above, logic glyph, EN/ENO).
            BlockKind = ClassifyKind(block.TypeName, out var glyph);
            LogicGlyph = glyph;
            HasEnEno = block.EnEno;
            // A function block is an instantiated POU (has a distinct instance name that differs
            // from its type); functions/conversions are bare operators with no instance.
            InstanceNameAbove = BlockKind == FbdBlockKind.FunctionBlock &&
                                !string.Equals(block.InstanceName, block.TypeName, StringComparison.OrdinalIgnoreCase);

            var pins = new List<string>();
            pins.AddRange(inputPins.Select(p =>
                $"  {p.Name}{(p.Inverted ? " (inv)" : "")} = {p.DisplayTag}"));
            pins.AddRange(outputPins.Select(p => $"  {p.Name} = {p.DisplayTag}"));

            Detail = $"Block: {block.InstanceName}\nType:   {block.TypeName}\nEN.ENO: {block.EnEno}\n\nPins:\n" +
                     string.Join("\n", pins);

            // Mark blocks whose type has no known simulation equivalent.
            var known = project.BlockTypes.Count == 0 ||
                        project.BlockTypes.Any(t => string.Equals(t.Name, block.TypeName, StringComparison.OrdinalIgnoreCase));
            HasSimulationEquivalent = known;
        }

        public XefBlock Block { get; }
        public string Name { get; }
        public string TypeName { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public string Detail { get; }
        public bool HasSimulationEquivalent { get; }

        /// <summary>
        /// True when this block is the currently selected one (highlighted in the diagram).
        /// Toggled by clicking a block; the view clears it from other blocks.
        /// </summary>
        [ObservableProperty]
        private bool _isSelected;

        /// <summary>Border colour by element type (function block / contact / coil).</summary>
        public global::Avalonia.Media.IBrush BorderTint { get; }

        /// <summary>The rendered box width (px); the item is wider (side pin fields).</summary>
        public double BoxWidth { get; }

        /// <summary>The rendered box minimum height (px).</summary>
        public double BoxHeight { get; }

        /// <summary>
        /// The IEC element kind, which drives the box shape and whether an instance name is
        /// drawn above the box: function blocks carry an instance name above; functions and
        /// conversions (operators) do not.
        /// </summary>
        public FbdBlockKind BlockKind { get; }

        /// <summary>True when an instance name should be drawn above the box (function blocks only).</summary>
        public bool InstanceNameAbove { get; }

        /// <summary>
        /// The IEC 81346 logic-operator glyph to draw centered in the box body for basic
        /// logic functions (AND → &amp;, OR → ≥1, XOR → =1). Empty for non-logic blocks.
        /// </summary>
        public string LogicGlyph { get; }

        /// <summary>True when the block declares EN/ENO (draws the dashed enable chain).</summary>
        public bool HasEnEno { get; }

        /// <summary>
        /// The block's input pins (IEC input variables), in declared order. Rendered on the
        /// left face of the block with their bound tag / constant / "(wired)".
        /// </summary>
        public IReadOnlyList<PlcPinItem> InputPins { get; }

        /// <summary>
        /// The block's output pins (IEC output variables), in declared order. Rendered on the
        /// right face of the block with their bound tag / constant / "(wired)".
        /// </summary>
        public IReadOnlyList<PlcPinItem> OutputPins { get; }

        /// <summary>
        /// The live output value of this block while the simulation is running
        /// (refreshed by the runner's readback). Empty when not running / no value yet.
        /// </summary>
        [ObservableProperty]
        private string _liveValue = string.Empty;

        /// <summary>Display text for the live value (empty string hides the row).</summary>
        [ObservableProperty]
        private string _liveValueText = string.Empty;

        partial void OnLiveValueChanged(string value) => LiveValueText = value;

        /// <summary>
        /// Classify a block type into its IEC element kind and (for basic logic functions) the
        /// IEC 81346 glyph drawn in the box body. Stateful timers/counters/triggers are
        /// function blocks; arithmetic/comparison/conversion/logic are functions.
        /// </summary>
        internal static FbdBlockKind ClassifyKind(string typeName, out string logicGlyph)
        {
            logicGlyph = string.Empty;
            string n = (typeName ?? string.Empty).Trim().ToUpperInvariant();

            // Basic logic functions → IEC 81346 glyphs.
            if (n == "AND") { logicGlyph = "&"; return FbdBlockKind.Function; }
            if (n == "OR") { logicGlyph = "≥1"; return FbdBlockKind.Function; }
            if (n == "XOR") { logicGlyph = "=1"; return FbdBlockKind.Function; }
            if (n == "NOT") { logicGlyph = "1"; return FbdBlockKind.Function; }
            if (n == "MUL" || n == "ADD" || n == "SUB" || n == "DIV" ||
                n.Contains("_TO_") || n.StartsWith("ABS") || n.StartsWith("SIN") ||
                n.StartsWith("COS") || n.StartsWith("SQRT") || n.StartsWith("LN") ||
                n.StartsWith("LOG") || n.StartsWith("EXP") || n.StartsWith("MAX") ||
                n.StartsWith("MIN") || n.StartsWith("LIMIT") || n.StartsWith("SEL"))
                return FbdBlockKind.Function;

            // Stateful function blocks (timers, counters, triggers, edge detectors).
            if (n == "TON" || n == "TOF" || n == "TP" || n == "TONR" ||
                n == "CTU" || n == "CTD" || n == "CTUD" ||
                n == "R_TRIG" || n == "F_TRIG" || n.StartsWith("TRIG"))
                return FbdBlockKind.FunctionBlock;

            // Anything else: treat as a function block (custom POU) if it looks like a POU,
            // otherwise a function. Heuristic: a leading "FBI_" instance prefix in our XEF
            // denotes an instantiated function-block call.
            return FbdBlockKind.FunctionBlock;
        }

        /// <summary>Classify a block by element type to pick its border tint.</summary>
        internal static global::Avalonia.Media.IBrush ClassifyTint(string name, string typeName)
        {
            string t = ((typeName ?? "") + " " + (name ?? "")).ToUpperInvariant();
            global::Avalonia.Media.Color c;
            if (t.Contains("FUNCTION") || t.Contains("_FB") || t.Contains("FBI_") ||
                t.Contains("AND") || t.Contains("OR") || t.Contains("XOR") ||
                t.Contains("NOT") || t.Contains("SCALE") || t.Contains("TRIGGER"))
                c = new global::Avalonia.Media.Color(255, 0x4F, 0xA6, 0xE8);
            else if (t.Contains("CONTACT") || t.Contains("_IN") || t.StartsWith("I_") ||
                t.Contains("INPUT") || t.Contains("X_"))
                c = new global::Avalonia.Media.Color(255, 0x66, 0xBB, 0x6A);
            else if (t.Contains("COIL") || t.Contains("_OUT") || t.StartsWith("Y_") ||
                t.Contains("OUTPUT") || t.Contains("Q_"))
                c = new global::Avalonia.Media.Color(255, 0xE6, 0x7E, 0x22);
            else
                c = new global::Avalonia.Media.Color(255, 0x9B, 0x59, 0xB6);
            return new global::Avalonia.Media.SolidColorBrush(c);
        }
    }

    /// <summary>
    /// A single IEC pin on a block face: its declared name (e.g. <c>IN</c>, <c>Q</c>), the tag,
    /// constant or equation bound to it, whether it is inverted, and its declared data type.
    /// </summary>
    public sealed class PlcPinItem
    {
        public PlcPinItem(string formalParameter, string? effectiveParameter, bool inverted,
                          bool isInput, string? dataType)
        {
            Name = formalParameter;
            Tag = effectiveParameter ?? (isInput ? "" : "");
            Inverted = inverted;
            IsInput = isInput;
            DataType = dataType;
            IsWired = string.IsNullOrEmpty(effectiveParameter);
            DisplayTag = string.IsNullOrEmpty(effectiveParameter) ? "(wired)" : effectiveParameter!;
        }

        /// <summary>The pin label on the block face (e.g. <c>IN</c>, <c>Q</c>, <c>IN1</c>).</summary>
        public string Name { get; }

        /// <summary>The bound tag / symbol / constant, or null when wired only.</summary>
        public string? Tag { get; }

        /// <summary>The display text for the pin's value ("(wired)" when only linked).</summary>
        public string DisplayTag { get; }

        public bool Inverted { get; }
        public bool IsInput { get; }
        public bool IsWired { get; }
        public string? DataType { get; }
    }

    /// <summary>
    /// A wire between two blocks, in canvas coordinates, routed orthogonally (right angles)
    /// from the source block's right edge to the destination block's left edge, per the IEC
    /// FBD convention. <see cref="Points"/> is the polyline path for the <c>Polyline</c>.
    /// </summary>
    public sealed class PlcLinkItem
    {
        public PlcLinkItem(double srcX, double srcY, double srcW, double srcH,
                           double dstX, double dstY, double dstW, double dstH,
                           string? sourcePin, string? destPin)
        {
            // Anchor at the source's right edge (output face) and destination's left edge
            // (input face), vertically centred on each block.
            var start = new AvaloniaPoint(srcX + srcW, srcY + srcH / 2);
            var end = new AvaloniaPoint(dstX, dstY + dstH / 2);
            StartPoint = start;
            EndPoint = end;
            SourcePin = sourcePin;
            DestPin = destPin;

            // Orthogonal routing: out of the source edge, horizontal to the midpoint X,
            // vertical to the destination Y, then horizontal into the destination edge.
            // Collapses to a straight line when the two pins share a row.
            var path = new List<AvaloniaPoint> { start };
            if (Math.Abs(start.Y - end.Y) < 0.5)
            {
                path.Add(end);
            }
            else
            {
                double midX = (start.X + end.X) / 2;
                path.Add(new AvaloniaPoint(midX, start.Y));
                path.Add(new AvaloniaPoint(midX, end.Y));
                path.Add(end);
            }
            Points = path;
        }

        public AvaloniaPoint StartPoint { get; }
        public AvaloniaPoint EndPoint { get; }

        /// <summary>The orthogonal polyline points (2–4) for the connection.</summary>
        public IReadOnlyList<AvaloniaPoint> Points { get; }

        public string? SourcePin { get; }
        public string? DestPin { get; }
    }

    /// <summary>A row in the live I/O monitor table.</summary>
    public partial class PlcVariableItem : ObservableObject
    {
        public PlcVariableItem(XefVariable variable)
        {
            Name = variable.Name;
            DataType = variable.DataType;
            Address = variable.Address;
            var parsed = variable.TryParseAddress();
            AddressValue = parsed?.Address;
            Area = parsed?.Area ?? PlcArea.HoldingRegister;
            Comment = variable.Comment;
        }

        public string Name { get; }
        public string DataType { get; }
        public string? Address { get; }
        public int? AddressValue { get; }
        public PlcArea Area { get; }
        public string? Comment { get; }

        [ObservableProperty]
        private string _liveValue = string.Empty;
    }
}
