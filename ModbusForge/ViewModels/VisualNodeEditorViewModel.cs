using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.Services.EditorCommands;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// A line used to render a node connection on the preview canvas.
    /// </summary>
    public partial class ConnectionLine : ObservableObject
    {
        [ObservableProperty]
        private double _fromX;

        [ObservableProperty]
        private double _fromY;

        [ObservableProperty]
        private double _toX;

        [ObservableProperty]
        private double _toY;

        [ObservableProperty]
        private string _sourceId = string.Empty;

        [ObservableProperty]
        private string _targetId = string.Empty;

        [ObservableProperty]
        private string _connectionId = string.Empty;

        [ObservableProperty]
        private string _targetConnector = "Input1";

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private IList<Point> _pathPoints = new List<Point>();

        [ObservableProperty]
        private Geometry? _pathData;

        [ObservableProperty]
        private IBrush? _lineBrush;

        [ObservableProperty]
        private PortSide _targetPortSide = PortSide.Left;

        public double Length => Math.Sqrt(Math.Pow(ToX - FromX, 2) + Math.Pow(ToY - FromY, 2));
        public double Angle => Math.Atan2(ToY - FromY, ToX - FromX) * 180 / Math.PI;
        public double LineTop => FromY - 1;

        public void UpdatePoints(IList<Point>? points, bool isSelected, PortSide targetPortSide = PortSide.Left)
        {
            TargetPortSide = targetPortSide;
            LineBrush = new SolidColorBrush(Color.Parse(isSelected ? "#1976D2" : "#607D8B"));

            if (points == null || points.Count < 2)
            {
                PathPoints = new List<Point>();
                PathData = null;
                return;
            }

            PathPoints = points;

            var figure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = false,
                IsFilled = false
            };

            if (points.Count == 2)
            {
                var (c1, c2) = ComputeBezierControlPoints(points[0], points[1]);
                figure.Segments!.Add(new BezierSegment
                {
                    Point1 = c1,
                    Point2 = c2,
                    Point3 = points[1]
                });
            }
            else
            {
                var segment = new PolyLineSegment();
                for (var i = 1; i < points.Count; i++)
                {
                    segment.Points.Add(points[i]);
                }

                figure.Segments!.Add(segment);
            }

            var geometry = new PathGeometry();
            geometry.Figures!.Add(figure);
            PathData = geometry;
        }

        private static (Point C1, Point C2) ComputeBezierControlPoints(Point start, Point end)
        {
            double dx = Math.Max(40, Math.Abs(end.X - start.X) * 0.5);
            return (new Point(start.X + dx, start.Y), new Point(end.X - dx, end.Y));
        }
    }

    public sealed class GridLine
    {
        public GridLine(double left, double top, double width, double height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }
    }

    /// <summary>
    /// A palette item used to add a new node to the graph.
    /// </summary>
    public partial class PaletteItem : ObservableObject
    {
        [ObservableProperty]
        private PlcElementType _elementType;

        [ObservableProperty]
        private string _displayName = string.Empty;

        [ObservableProperty]
        private string _category = string.Empty;
    }

    public partial class VisualNodeEditorViewModel : ObservableObject, IDisposable
    {
        private const double MinimumZoom = 0.25;
        private const double MaximumZoom = 3.0;
        private const double MinimumGridSize = 4.0;
        private const double MaximumGridSize = 200.0;
        private const double ZoomStep = 0.1;
        private const double LayoutMargin = 40.0;
        private const double LayoutHorizontalGap = 80.0;
        private const double LayoutVerticalGap = 40.0;

        private readonly IVisualSimulationService _visualSimulation;
        private readonly IFileDialogService? _fileDialogService;
        private readonly IMessageBoxService? _messageBoxService;
        private readonly ITagWindowService? _tagWindowService;
        private readonly ILogger<VisualNodeEditorViewModel> _logger;

        private ObservableCollection<VisualNode>? _observedNodes;
        private ObservableCollection<NodeConnection>? _observedConnections;
        private readonly HashSet<VisualNode> _attachedNodes = new();
        private ProgramModel? _activeProgram;

        /// <summary>
        /// The in-flight coalesced node-edit series: every editable-property change on one
        /// node (a parameter drag, a rename, an enable toggle) merges into a single undo
        /// step until the series is finalized by the next structural edit, undo/redo, or a
        /// different node.
        /// </summary>
        private VisualNode? _nodeEditNode;
        private Dictionary<string, object?>? _nodeEditBefore;
        private Dictionary<string, object?>? _nodeEditAfter;

        /// <summary>
        /// Last known value of every undoable property on every attached node.
        /// PropertyChanged does not carry the previous value, so the baselines are kept
        /// here to build the "before" half of the undo series.
        /// </summary>
        private readonly Dictionary<(VisualNode Node, string Property), object?> _nodeEditLastKnown = new();

        /// <summary>Set while applying an undo/redo so the resulting writes are not re-recorded.</summary>
        private bool _suppressingNodeEdits;

        /// <summary>
        /// Node properties holding a <see cref="PlcAddressReference"/> whose edits
        /// participate in undo. Editors mutate the reference in place, so these are
        /// observed on the reference objects themselves and snapshotted by clone.
        /// </summary>
        private static readonly HashSet<string> UndoableAddressProperties = new(StringComparer.Ordinal)
        {
            nameof(VisualNode.Input1Address),
            nameof(VisualNode.Input2Address),
            nameof(VisualNode.OutputAddress),
        };

        /// <summary>Maps every observed address reference to its owning node and property.</summary>
        private readonly List<(VisualNode Node, string Property, PlcAddressReference Reference)> _addressReferenceOwners = new();
        private bool _isSwitchingProgram;
        private bool _isUpdatingSelection;
        private bool _isDisposed;

        partial void InitializeProgramTreeCommands();

        [ObservableProperty]
        private VisualNodeEditorConfig _config = new();

        [ObservableProperty]
        private VisualNode? _selectedNode;

        [ObservableProperty]
        private NodeConnection? _selectedConnection;

        [ObservableProperty]
        private PaletteItem? _selectedPaletteItem;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private ObservableCollection<ConnectionLine> _connectionLines = new();

        [ObservableProperty]
        private ProgramFolder _programTree = new() { Name = "Programs" };

        [ObservableProperty]
        private ProgramModel? _selectedProgram;

        [ObservableProperty]
        private string _newProgramName = "New Program";

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedWaveform = "Ramp";

        [ObservableProperty]
        private int _waveformPeriodMs = 1000;

        [ObservableProperty]
        private double _waveformAmplitude = 100;

        [ObservableProperty]
        private double _waveformOffset;

        public static IReadOnlyList<string> WaveformOptions { get; } =
            new[] { "Ramp", "Sine", "Triangle", "Square" };

        [ObservableProperty]
        private string _simulationStoreMode = string.Empty;

        [ObservableProperty]
        private double _scaledCanvasWidth = 1200;

        [ObservableProperty]
        private double _scaledCanvasHeight = 800;

        [ObservableProperty]
        private bool _isConnectMode;

        [ObservableProperty]
        private VisualNode? _connectionSourceNode;

        [ObservableProperty]
        private string _selectedTargetConnector = "Input1";

        [ObservableProperty]
        private bool _useOrthogonalRouting;

        public ObservableCollection<PaletteItem> Palette { get; } = new();

        public ObservableCollection<PaletteItem> FilteredPalette { get; } = new();

        public ObservableCollection<GridLine> GridLines { get; } = new();

        public ObservableCollection<VisualNode> SelectedNodes { get; } = new();

        public IReadOnlyList<string> Waveforms { get; } = new[] { "Ramp", "Sine", "Triangle", "Square" };

        public IReadOnlyList<string> TargetConnectorOptions { get; } = new[] { "Input1", "Input2" };

        public UndoRedoService UndoRedo { get; } = new();

        public ICommand AddNodeCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand RemoveNodeCommand { get; }
        public ICommand AddConnectionCommand { get; }
        public ICommand RemoveConnectionCommand { get; }
        public ICommand CancelConnectCommand { get; }
        public ICommand AutoLayoutCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand LoadCommand { get; }
        public ICommand LoadDemoCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand ApplyWaveformCommand { get; }
        public ICommand EnableNodeCommand { get; }
        public ICommand DisableNodeCommand { get; }
        public ICommand ResetValuesCommand { get; }
        public ICommand RandomizeValuesCommand { get; }
        public ICommand ExportConfigCommand { get; }
        public ICommand OpenTagBrowserCommand { get; }
        public ICommand OpenWatchWindowCommand { get; }
        public ICommand AddSelectedNodeToWatchCommand { get; }
        public ICommand AlignLeftCommand { get; }
        public ICommand AlignTopCommand { get; }
        public ICommand DistributeHorizontallyCommand { get; }
        public ICommand ClearAllCommand { get; }

        public ObservableCollection<VisualNode> Nodes => Config.Nodes;
        public ObservableCollection<NodeConnection> Connections => Config.Connections;
        public ObservableCollection<ConnectorConfiguration> ConnectorConfigs => Config.ConnectorConfigs;

        /// <summary>
        /// True when there is a pushed edit OR an in-flight node-edit series: the series
        /// can always be undone immediately (undo finalizes it first).
        /// </summary>
        public bool CanUndo => UndoRedo.CanUndo || _nodeEditNode != null;
        public bool CanRedo => UndoRedo.CanRedo;
        public bool HasMultipleSelection => SelectedNodes.Count > 1;
        public int SelectedNodeCount => SelectedNodes.Count;
        public string ZoomText => $"{ZoomLevel:P0}";
        public string ConnectButtonText => IsConnectMode ? "Cancel Connect" : "Connect";
        public string ConnectionSourceText => ConnectionSourceNode == null
            ? "No source selected"
            : $"Source: {ConnectionSourceNode.Name}";

        public bool SnapToGrid
        {
            get => Config.SnapToGrid;
            set
            {
                if (Config.SnapToGrid == value) return;
                Config.SnapToGrid = value;
                OnPropertyChanged();
            }
        }

        public double GridSize
        {
            get => double.IsFinite(Config.GridSize)
                ? Math.Clamp(Config.GridSize, MinimumGridSize, MaximumGridSize)
                : 20.0;
            set
            {
                var normalized = double.IsFinite(value)
                    ? Math.Clamp(value, MinimumGridSize, MaximumGridSize)
                    : 20.0;
                if (Math.Abs(Config.GridSize - normalized) < double.Epsilon) return;
                Config.GridSize = normalized;
                OnPropertyChanged();
                RefreshGridLines();
            }
        }

        public double ZoomLevel
        {
            get => double.IsFinite(Config.ZoomLevel)
                ? Math.Clamp(Config.ZoomLevel, MinimumZoom, MaximumZoom)
                : 1.0;
            set
            {
                var normalized = double.IsFinite(value)
                    ? Math.Clamp(value, MinimumZoom, MaximumZoom)
                    : 1.0;
                if (Math.Abs(Config.ZoomLevel - normalized) < double.Epsilon) return;
                Config.ZoomLevel = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ZoomText));
                UpdateScaledCanvasSize();
                UpdateConnectionLines();
            }
        }

        /// <summary>
        /// Keeps the canvas layout size in step with the zoom level so the ScrollViewer
        /// extents follow the zoom (no dead space zoomed out, no clipping zoomed in).
        /// </summary>
        private void UpdateScaledCanvasSize()
        {
            var zoom = ZoomLevel;
            ScaledCanvasWidth = Config.CanvasWidth * zoom;
            ScaledCanvasHeight = Config.CanvasHeight * zoom;
        }

        public bool ShowGrid
        {
            get => Config.ShowGrid;
            set
            {
                if (Config.ShowGrid == value) return;
                Config.ShowGrid = value;
                OnPropertyChanged();
                RefreshGridLines();
            }
        }

        /// <summary>
        /// Single source of truth for "is the simulation running": the Run/Stop buttons and
        /// the Live checkbox all flip this. Start/stop is handled in one place
        /// (<see cref="OnConfigPropertyChanged"/>) so the states can never diverge.
        /// </summary>
        public bool ShowLiveValues
        {
            get => Config.ShowLiveValues;
            set
            {
                if (Config.ShowLiveValues == value) return;
                Config.ShowLiveValues = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Simulation scan period in milliseconds (clamped by the service to its supported range).
        /// </summary>
        public int ScanIntervalMs
        {
            get => Config.ScanIntervalMs;
            set
            {
                if (Config.ScanIntervalMs == value) return;
                Config.ScanIntervalMs = value;
                OnPropertyChanged();
            }
        }

        public VisualNodeEditorViewModel(
            IVisualSimulationService visualSimulation,
            ITagWindowService tagWindowService,
            IFileDialogService? fileDialogService = null,
            IMessageBoxService? messageBoxService = null,
            TagService? tagService = null,
            ILogger<VisualNodeEditorViewModel>? logger = null)
        {
            _visualSimulation = visualSimulation ?? throw new ArgumentNullException(nameof(visualSimulation));
            _tagWindowService = tagWindowService ?? throw new ArgumentNullException(nameof(tagWindowService));
            _fileDialogService = fileDialogService;
            _messageBoxService = messageBoxService;
            _tagService = tagService;
            _logger = logger ?? NullLogger<VisualNodeEditorViewModel>.Instance;

            AddNodeCommand = new RelayCommand(AddNode, () => SelectedPaletteItem != null);
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            RemoveNodeCommand = new RelayCommand(RemoveNode, () => SelectedNode != null);
            AddConnectionCommand = new RelayCommand(BeginOrCompleteConnection, () => SelectedNode != null || IsConnectMode);
            RemoveConnectionCommand = new RelayCommand(RemoveConnection, () => SelectedConnection != null);
            CancelConnectCommand = new RelayCommand(CancelConnectionCommand, () => IsConnectMode);
            AutoLayoutCommand = new RelayCommand(AutoLayout, () => Config.Nodes.Count > 0);
            RunCommand = new RelayCommand(Run, () => !IsRunning);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            LoadCommand = new AsyncRelayCommand(LoadAsync);
            LoadDemoCommand = new AsyncRelayCommand(LoadDemoAsync);
            UndoCommand = new RelayCommand(Undo, () => UndoRedo.CanUndo || _nodeEditNode != null);
            RedoCommand = new RelayCommand(Redo, () => UndoRedo.CanRedo);
            ZoomInCommand = new RelayCommand(ZoomIn);
            ZoomOutCommand = new RelayCommand(ZoomOut);
            ResetZoomCommand = new RelayCommand(ResetZoom);
            InitializeProgramTreeCommands();
            OpenTagBrowserCommand = new RelayCommand(() => _tagWindowService?.ShowTagBrowser());
            OpenWatchWindowCommand = new RelayCommand(() => _tagWindowService?.ShowWatchWindow());
            AddSelectedNodeToWatchCommand = new AsyncRelayCommand(AddSelectedNodeToWatchAsync, () => SelectedNode != null && _tagService != null);
            AlignLeftCommand = new RelayCommand(AlignLeft, () => SelectedNodes.Count >= 2);
            AlignTopCommand = new RelayCommand(AlignTop, () => SelectedNodes.Count >= 2);
            DistributeHorizontallyCommand = new RelayCommand(DistributeHorizontally, () => SelectedNodes.Count >= 3);
            ClearAllCommand = new AsyncRelayCommand(ClearAllAsync);
            ApplyWaveformCommand = new RelayCommand(ApplyWaveformToSelectedNode, () => SelectedNode != null);
            EnableNodeCommand = new RelayCommand(EnableNode, () => SelectedNode != null && !SelectedNode.IsEnabled);
            DisableNodeCommand = new RelayCommand(DisableNode, () => SelectedNode != null && SelectedNode.IsEnabled);
            ResetValuesCommand = new RelayCommand(ResetValues, () => Config.Nodes.Count > 0);
            RandomizeValuesCommand = new RelayCommand(RandomizeValues, () => Config.Nodes.Count > 0);
            ExportConfigCommand = new AsyncRelayCommand(ExportConfigAsync);

            BuildPalette();

            var defaultProgram = new ProgramModel { Name = "Main", ExecutionOrder = 0 };
            ProgramTree.Programs.Add(defaultProgram);
            SelectedProgram = defaultProgram;
            SelectedTreeItem = defaultProgram;

            Config.PropertyChanged += OnConfigPropertyChanged;

            AttachConfigHandlers();
            NormalizeViewSettings();
            UpdateScaledCanvasSize();
            RefreshConnectionLines();
            RefreshGridLines();

            _visualSimulation.CyclesChanged += OnSimulationCyclesChanged;

            if (Config.ShowLiveValues)
            {
                StartSimulation();
            }
        }

        /// <summary>
        /// Responds to property changes on the active <see cref="VisualNodeEditorConfig"/>:
        /// starts/stops the simulation when the master Live flag flips and applies scan
        /// period changes while running.
        /// </summary>
        private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VisualNodeEditorConfig.ShowLiveValues))
            {
                if (Config.ShowLiveValues && !IsRunning)
                {
                    StartSimulation();
                }
                else if (!Config.ShowLiveValues && IsRunning)
                {
                    StopSimulation();
                }
            }
            else if (e.PropertyName == nameof(VisualNodeEditorConfig.ScanIntervalMs) && IsRunning)
            {
                _visualSimulation.SetScanIntervalMs(Config.ScanIntervalMs);
            }

            foreach (var node in Config.Nodes)
            {
                node.ShowLiveValues = Config.ShowLiveValues;
            }
        }

        private void OnSimulationCyclesChanged(IReadOnlyList<string> cycleNodeIds)
        {
            if (cycleNodeIds.Count == 0) return;

            var names = cycleNodeIds
                .Select(id => Config.Nodes.FirstOrDefault(n => n.Id == id)?.Name ?? id)
                .ToList();
            StatusText = $"Warning: {names.Count} node(s) form a loop and will not run: {string.Join(", ", names)}";
        }

        private void StartSimulation()
        {
            foreach (var node in Config.Nodes)
            {
                node.ShowLiveValues = Config.ShowLiveValues;
            }

            _visualSimulation.SetScanIntervalMs(Config.ScanIntervalMs);
            _visualSimulation.Start(Config);
            IsRunning = true;
            SimulationStoreMode = _visualSimulation.StoreMode == "local"
                ? "local store (offline)"
                : "device store";
            StatusText = $"Simulation running — {SimulationStoreMode}";
        }

        private void StopSimulation()
        {
            _visualSimulation.Stop();
            foreach (var node in Config.Nodes)
            {
                node.ShowLiveValues = Config.ShowLiveValues;
            }

            IsRunning = false;
            SimulationStoreMode = string.Empty;
            StatusText = "Simulation stopped";
        }

        partial void OnConfigChanged(VisualNodeEditorConfig value)
        {
            DetachConfigHandlers();
            AttachConfigHandlers();
            NormalizeViewSettings();
            OnPropertyChanged(nameof(Nodes));
            OnPropertyChanged(nameof(Connections));
            OnPropertyChanged(nameof(ConnectorConfigs));
            OnPropertyChanged(nameof(SnapToGrid));
            OnPropertyChanged(nameof(GridSize));
            OnPropertyChanged(nameof(ZoomLevel));
            OnPropertyChanged(nameof(ZoomText));
            OnPropertyChanged(nameof(ShowGrid));
            OnPropertyChanged(nameof(ShowLiveValues));
            UpdateScaledCanvasSize();
            ClearSelection();
            CancelConnection(false);
            RefreshConnectionLines();
            RefreshGridLines();

            foreach (var node in Config.Nodes)
            {
                node.ShowLiveValues = Config.ShowLiveValues;
            }

            if (IsRunning && Config.ShowLiveValues)
            {
                // The service is still bound to the previous graph; re-attach it so the
                // newly selected program actually runs.
                _visualSimulation.Stop();
                StartSimulation();
            }
            else if (Config.ShowLiveValues && !IsRunning)
            {
                StartSimulation();
            }
            else if (!Config.ShowLiveValues && IsRunning)
            {
                StopSimulation();
            }

            RefreshAllParameterFields();

            ((IRelayCommand)AutoLayoutCommand).NotifyCanExecuteChanged();
        }

        private void RefreshAllParameterFields()
        {
            // Rebuild every node's parameter editor fields so the right panel and the
            // node footers track the active blocks' declared parameters (after a program
            // load, demo, or clear the node set has changed).
            foreach (var node in Config.Nodes)
            {
                BuildParameterFields(node);
            }
        }

        partial void OnSelectedNodeChanged(VisualNode? value)
        {
            if (!_isUpdatingSelection && IsConnectMode && ConnectionSourceNode != null
                && value != null && !ReferenceEquals(ConnectionSourceNode, value))
            {
                if (TryConnectNodes(ConnectionSourceNode, value, SelectedTargetConnector))
                {
                    return;
                }
            }

            if (!_isUpdatingSelection)
            {
                SetSelection(value == null ? Array.Empty<VisualNode>() : new[] { value }, value);
            }

            if (value != null && SelectedConnection != null)
            {
                SelectedConnection = null;
            }

            NotifySelectionCommands();
        }

        partial void OnSelectedConnectionChanged(NodeConnection? value)
        {
            if (value != null && SelectedNode != null)
            {
                SelectedNode = null;
            }

            RefreshConnectionLines();
            NotifySelectionCommands();
        }

        partial void OnSelectedPaletteItemChanged(PaletteItem? value)
        {
            NotifySelectionCommands();
        }

        partial void OnIsConnectModeChanged(bool value)
        {
            OnPropertyChanged(nameof(ConnectButtonText));
            ((IRelayCommand)AddConnectionCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)CancelConnectCommand).NotifyCanExecuteChanged();
        }

        partial void OnConnectionSourceNodeChanged(VisualNode? value)
        {
            OnPropertyChanged(nameof(ConnectionSourceText));
        }

        partial void OnSelectedTargetConnectorChanged(string value)
        {
            if (value is not ("Input1" or "Input2"))
            {
                SelectedTargetConnector = "Input1";
            }
        }

        partial void OnIsRunningChanged(bool value)
        {
            ((IRelayCommand)RunCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)StopCommand).NotifyCanExecuteChanged();
        }

        partial void OnSelectedProgramChanged(ProgramModel? value)
        {
            if (_isSwitchingProgram || ReferenceEquals(value, _activeProgram)) return;

            // Program switching clears the undo stack; a half-recorded node-edit series
            // would otherwise resurface after the switch.
            DiscardPendingNodeEdit();

            _isSwitchingProgram = true;
            try
            {
                if (_activeProgram != null)
                {
                    SaveProgramSnapshot(_activeProgram);
                }

                _activeProgram = value;
                if (value != null)
                {
                    BindConfigToProgram(value);
                    StatusText = $"Program selected: {value.Name}";
                }
            }
            finally
            {
                _isSwitchingProgram = false;
            }
        }

        private void BuildPalette()
        {
            Palette.Clear();
            foreach (var descriptor in NodeDescriptors.All
                         .Where(d => d.ShowInPalette)
                         .OrderBy(d => d.Category)
                         .ThenBy(d => d.PaletteName))
            {
                Palette.Add(new PaletteItem
                {
                    ElementType = descriptor.ElementType,
                    DisplayName = descriptor.PaletteName,
                    Category = descriptor.Category
                });
            }

            RefreshFilteredPalette();
        }

        partial void OnSearchTextChanged(string value)
        {
            RefreshFilteredPalette();
        }

        private void RefreshFilteredPalette()
        {
            var filter = SearchText?.Trim() ?? string.Empty;
            var isEmpty = string.IsNullOrEmpty(filter);

            FilteredPalette.Clear();
            foreach (var item in Palette)
            {
                if (isEmpty ||
                    item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    item.Category.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredPalette.Add(item);
                }
            }
        }

        private void AttachConfigHandlers()
        {
            if (_observedNodes != null || _observedConnections != null)
            {
                DetachConfigHandlers();
            }

            _observedNodes = Config.Nodes ?? new ObservableCollection<VisualNode>();
            _observedConnections = Config.Connections ?? new ObservableCollection<NodeConnection>();

            if (!ReferenceEquals(Config.Nodes, _observedNodes))
            {
                Config.Nodes = _observedNodes;
            }

            if (!ReferenceEquals(Config.Connections, _observedConnections))
            {
                Config.Connections = _observedConnections;
            }

            _observedNodes.CollectionChanged += OnNodesCollectionChanged;
            _observedConnections.CollectionChanged += OnConnectionsCollectionChanged;

            foreach (var node in _observedNodes)
            {
                AttachNode(node);
                node.ShowLiveValues = Config.ShowLiveValues;
            }
        }

        private void DetachConfigHandlers()
        {
            if (_observedNodes != null)
            {
                _observedNodes.CollectionChanged -= OnNodesCollectionChanged;
                foreach (var node in _observedNodes)
                {
                    DetachNode(node);
                }
            }

            if (_observedConnections != null)
            {
                _observedConnections.CollectionChanged -= OnConnectionsCollectionChanged;
            }

            _observedNodes = null;
            _observedConnections = null;
        }

        private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (VisualNode node in e.OldItems)
                {
                    DetachNode(node);
                    node.IsSelected = false;
                    SelectedNodes.Remove(node);
                }
            }

            if (e.NewItems != null)
            {
                foreach (VisualNode node in e.NewItems)
                {
                    AttachNode(node);
                    if (Config.ShowLiveValues)
                    {
                        node.ShowLiveValues = true;
                    }
                }
            }

            if (SelectedNode != null && !Config.Nodes.Contains(SelectedNode))
            {
                SetSelection(SelectedNodes, SelectedNodes.LastOrDefault());
            }

            if (ConnectionSourceNode != null && !Config.Nodes.Contains(ConnectionSourceNode))
            {
                CancelConnection(false);
            }

            RefreshConnectionLines();
            RefreshGridLines();
            OnPropertyChanged(nameof(Nodes));
            OnPropertyChanged(nameof(SelectedNodeCount));
            OnPropertyChanged(nameof(HasMultipleSelection));
            ((IRelayCommand)AutoLayoutCommand).NotifyCanExecuteChanged();
        }

        private void OnConnectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (SelectedConnection != null && !Config.Connections.Contains(SelectedConnection))
            {
                SelectedConnection = null;
            }

            RefreshConnectionLines();
            OnPropertyChanged(nameof(Connections));
        }

        private void AttachNode(VisualNode node)
        {
            node.ValueChangedCallback = OnNodeValueEditedByUser;
            if (_attachedNodes.Add(node))
            {
                node.PropertyChanged += OnNodePropertyChanged;
            }

            if (node.ParameterFields == null)
            {
                BuildParameterFields(node);
            }

            InitializeNodeEditBaselines(node);
            AttachAddressReferenceHandlers(node);
        }

        private void AttachAddressReferenceHandlers(VisualNode node)
        {
            var references = new (string Property, PlcAddressReference? Reference)[]
            {
                (nameof(VisualNode.Input1Address), node.Input1Address),
                (nameof(VisualNode.Input2Address), node.Input2Address),
                (nameof(VisualNode.OutputAddress), node.OutputAddress),
            };

            foreach (var (property, reference) in references)
            {
                if (reference == null) continue;
                reference.PropertyChanged += OnAddressReferencePropertyChanged;
                _addressReferenceOwners.Add((node, property, reference));
            }
        }

        private void OnAddressReferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressingNodeEdits || sender is not PlcAddressReference) return;

            foreach (var (node, property, reference) in _addressReferenceOwners)
            {
                if (ReferenceEquals(reference, sender))
                {
                    RecordNodeEdit(node, property);
                    break;
                }
            }
        }

        /// <summary>
        /// Builds the node's data-driven parameter editor fields from the function block's
        /// declarative <c>Parameters</c> list (via <see cref="ParameterAccess"/>), so the UI
        /// and the simulation engine share one source of truth.
        /// </summary>
        private void BuildParameterFields(VisualNode node)
        {
            var prototype = _visualSimulation.Catalog.Create(node.ElementType.ToString());

            var fields = new List<ParameterField>();
            foreach (var spec in prototype.Parameters)
            {
                if (ParameterAccess.TryGet(spec.Name) is { } access)
                    fields.Add(new ParameterField(node, spec, access.Getter, access.Setter));
            }

            node.ParameterFields = fields;
        }

        private void DetachNode(VisualNode node)
        {
            if (_attachedNodes.Remove(node))
            {
                node.PropertyChanged -= OnNodePropertyChanged;
            }

            node.ValueChangedCallback = null;

            if (ReferenceEquals(_nodeEditNode, node))
            {
                // The node the series belongs to is gone; the edit can no longer be undone.
                DiscardPendingNodeEdit();
            }

            foreach (var key in _nodeEditLastKnown.Keys.Where(k => ReferenceEquals(k.Node, node)).ToList())
            {
                _nodeEditLastKnown.Remove(key);
            }

            var owners = _addressReferenceOwners.Where(owner => ReferenceEquals(owner.Node, node)).ToList();
            foreach (var (_, _, reference) in owners)
            {
                reference.PropertyChanged -= OnAddressReferencePropertyChanged;
            }
            _addressReferenceOwners.RemoveAll(owner => ReferenceEquals(owner.Node, node));
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(VisualNode.X)
                or nameof(VisualNode.Y)
                or nameof(VisualNode.Width)
                or nameof(VisualNode.Height))
            {
                RefreshConnectionLines();
            }

            if (e.PropertyName == nameof(VisualNode.Name)
                && ReferenceEquals(sender, ConnectionSourceNode))
            {
                OnPropertyChanged(nameof(ConnectionSourceText));
            }

            if (e.PropertyName == nameof(VisualNode.ElementType))
            {
                // The block type changed: rebuild the data-driven parameter fields.
                if (sender is VisualNode node)
                    BuildParameterFields(node);
            }

            if (!_suppressingNodeEdits
                && sender is VisualNode editableNode
                && e.PropertyName != null
                && UndoableNodeProperties.Contains(e.PropertyName))
            {
                RecordNodeEdit(editableNode, e.PropertyName);
            }
        }

        /// <summary>
        /// The <see cref="VisualNode"/> properties that participate in undo: name, enabled
        /// state, and every configurable function-block parameter (see
        /// <see cref="ParameterAccess"/> — parameter names double as property names).
        /// </summary>
        private static readonly HashSet<string> UndoableNodeProperties = new(StringComparer.Ordinal)
        {
            nameof(VisualNode.Name),
            nameof(VisualNode.IsEnabled),
            nameof(VisualNode.TimerPresetMs),
            nameof(VisualNode.CounterPreset),
            nameof(VisualNode.CompareValue),
            nameof(VisualNode.CompareValueReal),
            nameof(VisualNode.SetDominant),
            nameof(VisualNode.Waveform),
            nameof(VisualNode.PeriodMs),
            nameof(VisualNode.Amplitude),
            nameof(VisualNode.Offset),
            nameof(VisualNode.ValveTravelTimeMs),
            nameof(VisualNode.ValveNormallyOpen),
            nameof(VisualNode.ValveLatching),
            nameof(VisualNode.MotorDolRunDelayMs),
            nameof(VisualNode.VsdMaxSpeed),
            nameof(VisualNode.VsdRampUpMs),
            nameof(VisualNode.VsdRampDownMs),
            nameof(VisualNode.VsdAtSpeedTolerance),
        };

        private static IEnumerable<string> AllUndoableNodeProperties()
            => UndoableNodeProperties.Union(UndoableAddressProperties);

        private static object? ReadUndoableNodeProperty(VisualNode node, string property)
        {
            switch (property)
            {
                case nameof(VisualNode.Name):
                    return node.Name;
                case nameof(VisualNode.IsEnabled):
                    return node.IsEnabled;
                case nameof(VisualNode.Input1Address):
                    return node.Input1Address?.Clone();
                case nameof(VisualNode.Input2Address):
                    return node.Input2Address?.Clone();
                case nameof(VisualNode.OutputAddress):
                    return node.OutputAddress?.Clone();
                default:
                    return ParameterAccess.TryGet(property)?.Getter(node);
            }
        }

        private void InitializeNodeEditBaselines(VisualNode node)
        {
            foreach (var property in AllUndoableNodeProperties())
            {
                var value = ReadUndoableNodeProperty(node, property);
                if (value != null)
                    _nodeEditLastKnown[(node, property)] = value;
            }
        }

        /// <summary>
        /// Records one node-property change, coalescing it into the node's current undo
        /// series. The "before" value comes from <see cref="_nodeEditLastKnown"/> because
        /// PropertyChanged already fired with the new value in place.
        /// </summary>
        private void RecordNodeEdit(VisualNode node, string property)
        {
            if (_isSwitchingProgram) return;

            var after = ReadUndoableNodeProperty(node, property);

            if (_nodeEditNode is not null && !ReferenceEquals(_nodeEditNode, node))
            {
                FinalizeNodeEditSeries();
            }

            var afterValues = _nodeEditAfter;
            if (afterValues == null)
            {
                // The baselines hold the pre-edit values: PropertyChanged already fired
                // with the new value in place, so the node itself can no longer be read
                // for the property that started this series.
                var before = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var name in AllUndoableNodeProperties())
                {
                    before[name] = _nodeEditLastKnown.GetValueOrDefault((node, name)) ?? ReadUndoableNodeProperty(node, name);
                }

                _nodeEditNode = node;
                _nodeEditBefore = before;
                _nodeEditAfter = afterValues = new Dictionary<string, object?>(StringComparer.Ordinal);

                // The Undo button is enabled as soon as a series starts.
                NotifyUndoRedoCommands();
            }

            afterValues[property] = after;
            if (after != null)
                _nodeEditLastKnown[(node, property)] = after;
        }

        /// <summary>
        /// Pushes the in-flight node-edit series onto the undo stack as one command
        /// (no-op when nothing was recorded).
        /// </summary>
        private void FinalizeNodeEditSeries()
        {
            if (_nodeEditNode is not { } node)
            {
                return;
            }

            if (_nodeEditBefore is { } before && _nodeEditAfter is { Count: > 0 } after)
            {
                UndoRedo.Push(new EditorCommand(
                    () => ApplyNodeEditValues(node, after),
                    () => ApplyNodeEditValues(node, before)));
                NotifyUndoRedoCommands();
            }

            _nodeEditNode = null;
            _nodeEditBefore = null;
            _nodeEditAfter = null;
        }

        private void DiscardPendingNodeEdit()
        {
            var hadPending = _nodeEditNode != null;
            _nodeEditNode = null;
            _nodeEditBefore = null;
            _nodeEditAfter = null;

            if (hadPending)
            {
                NotifyUndoRedoCommands();
            }
        }

        private void ApplyNodeEditValues(VisualNode node, IReadOnlyDictionary<string, object?> values)
        {
            _suppressingNodeEdits = true;
            try
            {
                foreach (var (property, value) in values)
                {
                    switch (property)
                    {
                        case nameof(VisualNode.Name):
                            node.Name = value as string ?? node.Name;
                            break;
                        case nameof(VisualNode.IsEnabled):
                            node.IsEnabled = value is bool b && b;
                            break;
                        case nameof(VisualNode.Input1Address) when value is PlcAddressReference stored1:
                            ApplyAddressReference(node.Input1Address, stored1);
                            break;
                        case nameof(VisualNode.Input2Address) when value is PlcAddressReference stored2:
                            ApplyAddressReference(node.Input2Address, stored2);
                            break;
                        case nameof(VisualNode.OutputAddress) when value is PlcAddressReference stored3:
                            ApplyAddressReference(node.OutputAddress, stored3);
                            break;
                        default:
                            if (ParameterAccess.TryGet(property) is { } access)
                                access.Setter(node, value);
                            break;
                    }
                }
            }
            finally
            {
                _suppressingNodeEdits = false;
            }

            // Re-sync the parameter editor fields and the undo baselines with the
            // restored values.
            if (node.ParameterFields != null)
            {
                foreach (var field in node.ParameterFields)
                    field.LoadFromNode();
            }

            InitializeNodeEditBaselines(node);
        }

        /// <summary>
        /// Copies a snapshotted address reference onto the live one in place, so the
        /// editor controls (bound to the reference object) keep working.
        /// </summary>
        private static void ApplyAddressReference(PlcAddressReference target, PlcAddressReference source)
        {
            if (target == null) return;
            target.Area = source.Area;
            target.Address = source.Address;
            target.Not = source.Not;
            target.SymbolicName = source.SymbolicName;
        }

        private void OnNodeValueEditedByUser(VisualNode node, double value)
        {
            try
            {
                _visualSimulation.WriteNodeValue(node.Id, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live value write failed for node {NodeId} with value {Value}", node.Id, value);
                StatusText = $"Live value write failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Select a node from the canvas. Holding Ctrl/Shift extends the selection;
        /// in connect mode the first node is the source and the next node is the target.
        /// </summary>
        public void SelectNode(VisualNode node, bool extendSelection = false)
        {
            if (node == null || !Config.Nodes.Contains(node)) return;

            if (IsConnectMode)
            {
                if (ConnectionSourceNode == null)
                {
                    ConnectionSourceNode = node;
                    SetSelection(new[] { node }, node);
                    StatusText = $"Source selected: {node.Name}. Select a target node.";
                }
                else if (!ReferenceEquals(ConnectionSourceNode, node))
                {
                    TryConnectNodes(ConnectionSourceNode, node, SelectedTargetConnector);
                }

                return;
            }

            var selection = SelectedNodes.ToList();
            VisualNode? primary = node;
            if (!extendSelection)
            {
                selection.Clear();
                selection.Add(node);
            }
            else if (selection.Contains(node))
            {
                selection.Remove(node);
                primary = selection.LastOrDefault();
            }
            else
            {
                selection.Add(node);
            }

            SetSelection(selection, primary);
        }

        public void ClearSelection()
        {
            SetSelection(Array.Empty<VisualNode>(), null);
        }

        private void SetSelection(IEnumerable<VisualNode> nodes, VisualNode? primary)
        {
            var validNodes = nodes
                .Where(Config.Nodes.Contains)
                .Distinct()
                .ToList();
            if (primary != null && !validNodes.Contains(primary))
            {
                primary = validNodes.LastOrDefault();
            }

            _isUpdatingSelection = true;
            try
            {
                SelectedNodes.Clear();
                foreach (var node in validNodes)
                {
                    SelectedNodes.Add(node);
                }

                foreach (var node in Config.Nodes)
                {
                    node.IsSelected = validNodes.Contains(node);
                }

                if (!ReferenceEquals(SelectedNode, primary))
                {
                    SelectedNode = primary;
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            OnPropertyChanged(nameof(SelectedNodeCount));
            OnPropertyChanged(nameof(HasMultipleSelection));
        }

        public IReadOnlyList<VisualNode> GetNodesForDrag(VisualNode node)
        {
            if (SelectedNodes.Contains(node) && SelectedNodes.Count > 0)
            {
                return SelectedNodes.ToList();
            }

            return new[] { node };
        }

        private void NormalizeViewSettings()
        {
            Config.GridSize = double.IsFinite(Config.GridSize)
                ? Math.Clamp(Config.GridSize, MinimumGridSize, MaximumGridSize)
                : 20.0;
            Config.ZoomLevel = double.IsFinite(Config.ZoomLevel)
                ? Math.Clamp(Config.ZoomLevel, MinimumZoom, MaximumZoom)
                : 1.0;
        }

        private void RefreshConnectionLines()
        {
            var lines = new ObservableCollection<ConnectionLine>();
            var nodeById = Config.Nodes
                .GroupBy(n => n.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var connection in Config.Connections)
            {
                if (!nodeById.TryGetValue(connection.SourceNodeId, out var source)
                    || !nodeById.TryGetValue(connection.TargetNodeId, out var target))
                {
                    continue;
                }

                var sourceY = GetPortY(source, connection.SourceConnector, false);
                var sourcePoint = new Point(source.X + source.Width, sourceY);
                var targetY = GetPortY(target, connection.TargetConnector, true);
                var targetPoint = new Point(target.X, targetY);

                var obstacles = Config.Nodes.Where(n => n != source && n != target);
                var points = UseOrthogonalRouting
                    ? GetOrthogonalPoints(sourcePoint, targetPoint, source, target, obstacles)
                    : new List<Point> { sourcePoint, targetPoint };

                var line = new ConnectionLine
                {
                    ConnectionId = connection.Id,
                    SourceId = source.Id,
                    TargetId = target.Id,
                    TargetConnector = connection.TargetConnector,
                    IsSelected = ReferenceEquals(SelectedConnection, connection),
                    FromX = sourcePoint.X,
                    FromY = sourcePoint.Y,
                    ToX = targetPoint.X,
                    ToY = targetPoint.Y
                };
                line.UpdatePoints(points, ReferenceEquals(SelectedConnection, connection), PortSide.Left);
                lines.Add(line);
            }

            ConnectionLines = lines;
        }

        private static double GetPortY(VisualNode node, string? connector, bool isInput)
        {
            const double headerHeight = 24;
            var contentH = node.Height - headerHeight;

            if (isInput)
            {
                var ratio = connector == "Input2" ? 0.667 : 0.333;
                return node.Y + headerHeight + contentH * ratio;
            }

            var outputPortNames = node.OutputPortNames;
            var portName = string.IsNullOrWhiteSpace(connector) ? "Output" : connector;
            var portIndex = outputPortNames.IndexOf(portName);
            if (portIndex < 0) portIndex = 0;
            var outputCount = Math.Max(outputPortNames.Count, 1);
            var yRatio = (portIndex + 1.0) / (outputCount + 1.0);
            return node.Y + headerHeight + contentH * yRatio;
        }

        private void RefreshGridLines()
        {
            GridLines.Clear();
            if (!ShowGrid) return;

            var spacing = GridSize;
            if (double.IsFinite(Config.CanvasWidth) && Config.CanvasWidth / spacing <= 1000)
            {
                for (var x = 0.0; x <= Config.CanvasWidth; x += spacing)
                {
                    GridLines.Add(new GridLine(x, 0, 1, Config.CanvasHeight));
                }
            }

            if (double.IsFinite(Config.CanvasHeight) && Config.CanvasHeight / spacing <= 1000)
            {
                for (var y = 0.0; y <= Config.CanvasHeight; y += spacing)
                {
                    GridLines.Add(new GridLine(0, y, Config.CanvasWidth, 1));
                }
            }
        }

        public double SnapCoordinate(double value)
        {
            if (!double.IsFinite(value)) return 0;
            if (!SnapToGrid) return value;
            return Math.Round(value / GridSize, MidpointRounding.AwayFromZero) * GridSize;
        }

        public (double X, double Y) GetSnappedPosition(double x, double y, VisualNode? node = null)
        {
            var snappedX = SnapCoordinate(x);
            var snappedY = SnapCoordinate(y);
            var canvasWidth = double.IsFinite(Config.CanvasWidth) ? Math.Max(0, Config.CanvasWidth) : 2000;
            var canvasHeight = double.IsFinite(Config.CanvasHeight) ? Math.Max(0, Config.CanvasHeight) : 2000;
            var nodeWidth = node != null && double.IsFinite(node.Width) ? Math.Max(0, node.Width) : 0;
            var nodeHeight = node != null && double.IsFinite(node.Height) ? Math.Max(0, node.Height) : 0;
            var maxX = Math.Max(0, canvasWidth - nodeWidth);
            var maxY = Math.Max(0, canvasHeight - nodeHeight);
            return (Math.Clamp(snappedX, 0, maxX), Math.Clamp(snappedY, 0, maxY));
        }

        public void SetNodePosition(VisualNode node, double x, double y)
        {
            if (!Config.Nodes.Contains(node)) return;
            var position = GetSnappedPosition(x, y, node);
            node.X = position.X;
            node.Y = position.Y;
        }

        public void CommitNodeMove(VisualNode node, double oldX, double oldY)
        {
            CommitNodeMoves(new Dictionary<VisualNode, (double X, double Y)>
            {
                [node] = (oldX, oldY)
            });
        }

        public void CommitNodeMoves(IReadOnlyDictionary<VisualNode, (double X, double Y)> oldPositions)
        {
            var moved = oldPositions
                .Where(pair => Config.Nodes.Contains(pair.Key))
                .Select(pair => new
                {
                    Node = pair.Key,
                    Old = pair.Value,
                    New = (X: pair.Key.X, Y: pair.Key.Y)
                })
                .Where(item => Math.Abs(item.Old.X - item.New.X) >= double.Epsilon
                    || Math.Abs(item.Old.Y - item.New.Y) >= double.Epsilon)
                .ToList();

            if (moved.Count == 0) return;

            FinalizeNodeEditSeries();

            var command = new EditorCommand(
                () =>
                {
                    foreach (var item in moved)
                    {
                        SetRawNodePosition(item.Node, item.New.X, item.New.Y);
                    }
                },
                () =>
                {
                    foreach (var item in moved)
                    {
                        SetRawNodePosition(item.Node, item.Old.X, item.Old.Y);
                    }
                });
            UndoRedo.Push(command);
            NotifyUndoRedoCommands();
            StatusText = moved.Count == 1
                ? $"Moved {moved[0].Node.Name}"
                : $"Moved {moved.Count} nodes";
        }

        private void SetRawNodePosition(VisualNode node, double x, double y)
        {
            if (!Config.Nodes.Contains(node)) return;
            node.X = x;
            node.Y = y;
            RefreshConnectionLines();
        }

        public void AddNode()
        {
            if (SelectedPaletteItem == null) return;

            var (x, y) = FindFreeNodePosition();
            AddNodeAt(SelectedPaletteItem, x, y);
        }

        public (double X, double Y) FindFreeNodePosition()
        {
            const double startX = 100;
            const double startY = 100;
            const double stepX = 40;
            const double stepY = 30;
            const int maxCols = 25;

            var defaultNodeSize = (Width: 160.0, Height: 80.0);

            for (int row = 0; row < 100; row++)
            {
                for (int col = 0; col < maxCols; col++)
                {
                    var x = startX + col * stepX;
                    var y = startY + row * stepY;
                    var testRect = new Rect(x, y, defaultNodeSize.Width, defaultNodeSize.Height);

                    bool overlaps = false;
                    foreach (var node in Config.Nodes)
                    {
                        if (!double.IsFinite(node.X) || !double.IsFinite(node.Y))
                        {
                            continue;
                        }

                        var nodeWidth = double.IsFinite(node.Width) ? node.Width : defaultNodeSize.Width;
                        var nodeHeight = double.IsFinite(node.Height) ? node.Height : defaultNodeSize.Height;
                        var nodeRect = new Rect(node.X, node.Y, nodeWidth, nodeHeight);

                        if (nodeRect.Intersects(testRect))
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps)
                    {
                        return (x, y);
                    }
                }
            }

            // Fallback if the grid is full.
            var count = Config.Nodes.Count;
            return (startX + count * stepX, startY + count * stepY);
        }

        public VisualNode? AddNodeAt(double x, double y)
        {
            return SelectedPaletteItem == null ? null : AddNodeAt(SelectedPaletteItem, x, y);
        }

        public VisualNode? AddNodeAt(PlcElementType elementType, double x, double y)
        {
            var paletteItem = Palette.FirstOrDefault(item => item.ElementType == elementType);
            if (paletteItem == null)
            {
                var descriptor = NodeDescriptors.Get(elementType);
                paletteItem = new PaletteItem
                {
                    ElementType = elementType,
                    DisplayName = descriptor.PaletteName,
                    Category = descriptor.Category
                };
            }

            return AddNodeAt(paletteItem, x, y);
        }

        public VisualNode? AddNodeAt(PaletteItem paletteItem, double x, double y)
        {
            FinalizeNodeEditSeries();
            if (paletteItem == null || !double.IsFinite(x) || !double.IsFinite(y)) return null;

            var descriptor = NodeDescriptors.Get(paletteItem.ElementType);
            var position = GetSnappedPosition(x, y);
            var node = new VisualNode
            {
                Name = descriptor.DisplayName,
                ElementType = paletteItem.ElementType,
                X = position.X,
                Y = position.Y,
                ShowLiveValues = Config.ShowLiveValues
            };

            InitializeNodeDefaults(node);
            var command = new EditorCommand(
                () =>
                {
                    if (!Config.Nodes.Contains(node)) Config.Nodes.Add(node);
                    SetSelection(new[] { node }, node);
                },
                () =>
                {
                    RemoveNodeCore(node, null, null);
                    if (SelectedNodes.Contains(node))
                    {
                        SetSelection(SelectedNodes.Where(selected => !ReferenceEquals(selected, node)), SelectedNode);
                    }
                });

            command.Execute();
            UndoRedo.Push(command);
            NotifyUndoRedoCommands();
            StatusText = $"Added {descriptor.DisplayName}";
            return node;
        }

        private static readonly PlcArea BoolIoArea = PlcArea.Coil;
        private static readonly PlcArea IntIoArea = PlcArea.HoldingRegister;

        private void InitializeNodeDefaults(VisualNode node)
        {
            switch (node.ElementType)
            {
                case PlcElementType.Input:
                    node.Input1Address = new PlcAddressReference
                    {
                        Area = BoolIoArea,
                        Address = -1
                    };
                    break;
                case PlcElementType.InputBool:
                    node.Input1Address = new PlcAddressReference
                    {
                        Area = BoolIoArea,
                        Address = GetNextAvailableAddress(BoolIoArea)
                    };
                    node.OutputAddress = new PlcAddressReference
                    {
                        Area = BoolIoArea,
                        Address = node.Input1Address.Address
                    };
                    break;
                case PlcElementType.InputInt:
                    node.Input1Address = new PlcAddressReference
                    {
                        Area = IntIoArea,
                        Address = GetNextAvailableAddress(IntIoArea)
                    };
                    node.OutputAddress = new PlcAddressReference
                    {
                        Area = IntIoArea,
                        Address = node.Input1Address.Address
                    };
                    break;
                case PlcElementType.Output:
                    node.OutputAddress = new PlcAddressReference
                    {
                        Area = BoolIoArea,
                        Address = -1
                    };
                    break;
                case PlcElementType.OutputBool:
                    node.OutputAddress = new PlcAddressReference
                    {
                        Area = BoolIoArea,
                        Address = GetNextAvailableAddress(BoolIoArea)
                    };
                    break;
                case PlcElementType.OutputInt:
                    node.OutputAddress = new PlcAddressReference
                    {
                        Area = IntIoArea,
                        Address = GetNextAvailableAddress(IntIoArea)
                    };
                    break;
                case PlcElementType.MATH_ADD:
                case PlcElementType.MATH_SUB:
                case PlcElementType.MATH_MUL:
                case PlcElementType.MATH_DIV:
                case PlcElementType.MATH_ADD_REAL:
                case PlcElementType.MATH_SUB_REAL:
                case PlcElementType.MATH_MUL_REAL:
                case PlcElementType.MATH_DIV_REAL:
                    node.Input2Address = new PlcAddressReference { Area = IntIoArea, Address = -1 };
                    node.OutputAddress = new PlcAddressReference
                    {
                        Area = IntIoArea,
                        Address = GetNextAvailableAddress(IntIoArea)
                    };
                    break;
                case PlcElementType.COMPARE_EQ:
                case PlcElementType.COMPARE_NE:
                case PlcElementType.COMPARE_GT:
                case PlcElementType.COMPARE_LT:
                case PlcElementType.COMPARE_GE:
                case PlcElementType.COMPARE_LE:
                case PlcElementType.COMPARE_EQ_REAL:
                case PlcElementType.COMPARE_NE_REAL:
                case PlcElementType.COMPARE_GT_REAL:
                case PlcElementType.COMPARE_LT_REAL:
                case PlcElementType.COMPARE_GE_REAL:
                case PlcElementType.COMPARE_LE_REAL:
                    node.Input2Address = new PlcAddressReference { Area = IntIoArea, Address = -1 };
                    break;
                case PlcElementType.Valve:
                    node.Input1Address = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.Input2Address = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.OutputAddress = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.OutputPortBindings["Fault"] = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.ValveTravelTimeMs = 5000;
                    node.ValveNormallyOpen = false;
                    break;
                case PlcElementType.MotorDol:
                    node.Input1Address = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.Input2Address = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.OutputAddress = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.MotorDolRunDelayMs = 100;
                    break;
                case PlcElementType.Vsd:
                    node.Input1Address = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.Input2Address = new PlcAddressReference { Area = IntIoArea, Address = -1 };
                    node.OutputAddress = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.OutputPortBindings["SpeedFeedback"] = new PlcAddressReference { Area = IntIoArea, Address = -1 };
                    node.OutputPortBindings["AtSpeed"] = new PlcAddressReference { Area = BoolIoArea, Address = -1 };
                    node.VsdMaxSpeed = 100.0;
                    node.VsdRampUpMs = 2000;
                    node.VsdRampDownMs = 2000;
                    node.VsdAtSpeedTolerance = 2.0;
                    break;
            }

            UpdateOutputPortNames(node);
        }

        private void UpdateOutputPortNames(VisualNode node)
        {
            var descriptor = _visualSimulation?.Catalog.GetDescriptor(node.ElementType.ToString());
            if (descriptor == null)
            {
                node.OutputPortNames = new ObservableCollection<string>(new[] { "Output" });
                return;
            }

            var names = descriptor.Ports
                .Where(p => p.Direction == PortDirection.Output)
                .Select(p => p.Name)
                .ToList();

            node.OutputPortNames = new ObservableCollection<string>(names);

            foreach (var stale in node.OutputPortBindings.Keys.Where(k => !names.Contains(k, StringComparer.Ordinal)).ToList())
                node.OutputPortBindings.Remove(stale);

            // The primary output ("Output" when present, else the first output, e.g. the
            // VSD's "Running") is addressed via the node's main OutputAddress, so it gets
            // no per-port binding.
            var primary = names.FirstOrDefault(n => n == "Output") ?? names.FirstOrDefault();

            foreach (var name in names)
            {
                if (name == primary) continue;
                if (!node.OutputPortBindings.ContainsKey(name))
                    node.OutputPortBindings[name] = new PlcAddressReference { Area = PlcArea.Coil, Address = -1 };
            }
        }

        private int GetNextAvailableAddress(PlcArea area)
        {
            var existing = Config?.Nodes
                .SelectMany(n => new[] { n.Input1Address, n.Input2Address, n.OutputAddress })
                .Where(a => a?.Area == area && a.Address >= 0)
                .Select(a => a!.Address)
                .ToList() ?? new List<int>();

            int next = 1;
            while (existing.Contains(next))
            {
                next++;
            }

            return next;
        }

        private void RemoveNode()
        {
            if (SelectedNode == null) return;

            FinalizeNodeEditSeries();

            var node = SelectedNode;
            var removedConnections = Config.Connections
                .Where(c => c.SourceNodeId == node.Id || c.TargetNodeId == node.Id)
                .ToList();
            var removedConfigs = Config.ConnectorConfigs
                .Where(c => c.NodeId == node.Id)
                .ToList();

            var command = new EditorCommand(
                () =>
                {
                    RemoveNodeCore(node, removedConnections, removedConfigs);
                    if (ReferenceEquals(SelectedNode, node)) SelectedNode = null;
                },
                () =>
                {
                    if (!Config.Nodes.Contains(node)) Config.Nodes.Add(node);
                    foreach (var connectorConfig in removedConfigs)
                    {
                        if (!Config.ConnectorConfigs.Contains(connectorConfig)) Config.ConnectorConfigs.Add(connectorConfig);
                    }

                    foreach (var connection in removedConnections)
                    {
                        if (!Config.Connections.Contains(connection)) Config.Connections.Add(connection);
                    }

                    SelectedNode = node;
                });

            command.Execute();
            UndoRedo.Push(command);
            NotifyUndoRedoCommands();
            StatusText = "Node removed";
        }

        private void RemoveNodeCore(
            VisualNode node,
            IReadOnlyCollection<NodeConnection>? connections,
            IReadOnlyCollection<ConnectorConfiguration>? connectorConfigs)
        {
            var connectionsToRemove = connections
                ?? Config.Connections.Where(c => c.SourceNodeId == node.Id || c.TargetNodeId == node.Id).ToList();
            var configsToRemove = connectorConfigs
                ?? Config.ConnectorConfigs.Where(c => c.NodeId == node.Id).ToList();

            foreach (var connection in connectionsToRemove)
            {
                Config.Connections.Remove(connection);
            }

            foreach (var connectorConfig in configsToRemove)
            {
                Config.ConnectorConfigs.Remove(connectorConfig);
            }

            Config.Nodes.Remove(node);
        }

        private void BeginOrCompleteConnection()
        {
            if (IsConnectMode)
            {
                if (ConnectionSourceNode != null && SelectedNode != null
                    && !ReferenceEquals(ConnectionSourceNode, SelectedNode))
                {
                    TryConnectNodes(ConnectionSourceNode, SelectedNode, SelectedTargetConnector);
                }
                else
                {
                    CancelConnection();
                }

                return;
            }

            if (SelectedNode == null)
            {
                StatusText = "Select a source node first.";
                return;
            }

            if (Config.Nodes.Count < 2)
            {
                StatusText = "Add another node before connecting.";
                return;
            }

            ConnectionSourceNode = SelectedNode;
            SelectedConnection = null;
            IsConnectMode = true;
            StatusText = $"Source selected: {SelectedNode.Name}. Select a target node.";
        }

        private void CancelConnectionCommand() => CancelConnection();

        private void CancelConnection(bool updateStatus = true)
        {
            var wasActive = IsConnectMode || ConnectionSourceNode != null;
            IsConnectMode = false;
            ConnectionSourceNode = null;
            if (updateStatus && wasActive)
            {
                StatusText = "Connect mode cancelled";
            }
        }

        public bool TryConnectNodes(VisualNode source, VisualNode target, string? targetConnector = null, string? sourceConnector = null)
        {
            if (!Config.Nodes.Contains(source) || !Config.Nodes.Contains(target)) return false;
            FinalizeNodeEditSeries();
            if (ReferenceEquals(source, target))
            {
                StatusText = "A node cannot connect to itself.";
                return false;
            }

            var connector = string.IsNullOrWhiteSpace(targetConnector)
                ? SelectedTargetConnector
                : targetConnector;
            if (connector is not ("Input1" or "Input2"))
            {
                connector = "Input1";
            }

            if (connector == "Input2" && !target.HasSecondInput)
            {
                StatusText = $"{target.Name} has no Input2 port.";
                return false;
            }

            var sourcePort = string.IsNullOrWhiteSpace(sourceConnector) ? "Output" : sourceConnector;
            if (!source.OutputPortNames.Contains(sourcePort))
            {
                StatusText = $"{source.Name} has no {sourcePort} output port.";
                return false;
            }

            var existingDriver = Config.Connections.FirstOrDefault(connection =>
                    connection.TargetNodeId == target.Id
                    && connection.TargetConnector == connector);
            if (existingDriver != null)
            {
                var driverName = Config.Nodes.FirstOrDefault(n => n.Id == existingDriver.SourceNodeId)?.Name
                                  ?? existingDriver.SourceNodeId;
                StatusText = $"{target.Name} {connector} already has a driver ({driverName}); remove it first.";
                return false;
            }

            var connection = new NodeConnection(source.Id, target.Id, connector)
            {
                SourceConnector = sourcePort,
                IsConnected = true
            };
            var command = new EditorCommand(
                () =>
                {
                    if (!Config.Connections.Contains(connection)) Config.Connections.Add(connection);
                    CancelConnection(false);
                    SelectedConnection = connection;
                },
                () =>
                {
                    Config.Connections.Remove(connection);
                    if (ReferenceEquals(SelectedConnection, connection)) SelectedConnection = null;
                });

            command.Execute();
            UndoRedo.Push(command);
            NotifyUndoRedoCommands();
            StatusText = $"Connected {source.Name} → {target.Name} ({connector})";
            return true;
        }

        private void RemoveConnection()
        {
            if (SelectedConnection == null) return;

            FinalizeNodeEditSeries();

            var connection = SelectedConnection;
            var command = new EditorCommand(
                () =>
                {
                    Config.Connections.Remove(connection);
                    if (ReferenceEquals(SelectedConnection, connection)) SelectedConnection = null;
                },
                () =>
                {
                    if (!Config.Connections.Contains(connection)) Config.Connections.Add(connection);
                    SelectedConnection = connection;
                });

            command.Execute();
            UndoRedo.Push(command);
            NotifyUndoRedoCommands();
            StatusText = "Connection removed";
        }

        private void Run()
        {
            if (IsRunning) return;
            if (!Config.ShowLiveValues)
            {
                Config.ShowLiveValues = true; // routes through OnConfigPropertyChanged
                return;
            }

            StartSimulation();
        }

        private void Stop()
        {
            if (!IsRunning) return;
            if (Config.ShowLiveValues)
            {
                Config.ShowLiveValues = false; // routes through OnConfigPropertyChanged
                return;
            }

            StopSimulation();
        }

        private void Undo()
        {
            // Flush any in-flight node-edit series so undo acts in chronological order.
            FinalizeNodeEditSeries();
            if (!UndoRedo.CanUndo)
            {
                return;
            }

            UndoRedo.Undo();
            NotifyUndoRedoCommands();
            RefreshConnectionLines();
            StatusText = "Undid last edit";
        }

        private void Redo()
        {
            // Flush any in-flight node-edit series so redo acts in chronological order.
            FinalizeNodeEditSeries();
            if (!UndoRedo.CanRedo)
            {
                return;
            }

            UndoRedo.Redo();
            NotifyUndoRedoCommands();
            RefreshConnectionLines();
            StatusText = "Redid last edit";
        }

        private void ApplyWaveformToSelectedNode()
        {
            if (SelectedNode == null) return;

            var node = SelectedNode;

            // The node-edit series records the before/after snapshot for these
            // properties, so both undo and redo work (the previous dedicated command
            // had a no-op unexecute).
            node.Waveform = SelectedWaveform;
            node.PeriodMs = WaveformPeriodMs;
            node.Amplitude = WaveformAmplitude;
            node.Offset = WaveformOffset;

            FinalizeNodeEditSeries();
            StatusText = $"Applied {SelectedWaveform} waveform to {node.Name}";
        }

        private void EnableNode()
        {
            if (SelectedNode == null) return;
            SelectedNode.IsEnabled = true;
        }

        private void DisableNode()
        {
            if (SelectedNode == null) return;
            SelectedNode.IsEnabled = false;
        }

        private void ResetValues()
        {
            foreach (var node in Config.Nodes)
            {
                // Display-only: do not write back into bound Modbus addresses.
                node.SuppressWriteBack = true;
                try
                {
                    node.CurrentValueDouble = 0;
                }
                finally
                {
                    node.SuppressWriteBack = false;
                }
            }

            StatusText = "Display values reset (block state and bound addresses unchanged)";
        }

        private void RandomizeValues()
        {
            var random = new Random();
            foreach (var node in Config.Nodes)
            {
                // Display-only: do not write back into bound Modbus addresses.
                node.SuppressWriteBack = true;
                try
                {
                    node.CurrentValueDouble = random.NextDouble() * 100;
                }
                finally
                {
                    node.SuppressWriteBack = false;
                }
            }

            StatusText = "Display values randomized (block state and bound addresses unchanged)";
        }

        private async Task ExportConfigAsync()
        {
            if (_fileDialogService == null)
            {
                StatusText = "Export not available";
                return;
            }

            var path = await _fileDialogService.ShowSaveFileDialogAsync(
                "Export visual node configuration",
                "JSON files|*.json|All files|*.*",
                "config.json");

            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);
                StatusText = $"Exported configuration to {path}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        private void ZoomIn() => ZoomLevel += ZoomStep;

        private void ZoomOut() => ZoomLevel -= ZoomStep;

        private void ResetZoom() => ZoomLevel = 1.0;

        private void AlignLeft()
        {
            var selection = SelectedNodes.ToList();
            if (selection.Count < 2) return;
            var minX = selection.Min(n => n.X);
            foreach (var node in selection)
            {
                SetNodePosition(node, minX, node.Y);
            }
            StatusText = "Aligned left.";
        }

        private void AlignTop()
        {
            var selection = SelectedNodes.ToList();
            if (selection.Count < 2) return;
            var minY = selection.Min(n => n.Y);
            foreach (var node in selection)
            {
                SetNodePosition(node, node.X, minY);
            }
            StatusText = "Aligned top.";
        }

        private void DistributeHorizontally()
        {
            var selection = SelectedNodes.ToList();
            if (selection.Count < 3) return;
            var sorted = selection.OrderBy(n => n.X).ToList();
            var startX = sorted.First().X;
            var endX = sorted.Last().X;
            var step = (endX - startX) / (sorted.Count - 1);
            for (var i = 0; i < sorted.Count; i++)
            {
                var node = sorted[i];
                SetNodePosition(node, startX + i * step, node.Y);
            }
            StatusText = "Distributed horizontally.";
        }

        /// <summary>
        /// Asks the user to confirm a destructive action. Proceeds without prompting when
        /// no message box service is available (headless / automated scenarios).
        /// </summary>
        private async Task<bool> ConfirmAsync(string message, string title)
        {
            if (_messageBoxService == null) return true;

            var result = await _messageBoxService.ShowAsync(message, title, DialogButton.YesNo, DialogIcon.Question);
            return result == DialogResult.Yes;
        }

        private async Task ClearAllAsync()
        {
            if (Config.Nodes.Count == 0 && Config.Connections.Count == 0) return;

            if (!await ConfirmAsync(
                    "Clear all nodes and connections from the current program?",
                    "Clear Program"))
            {
                return;
            }

            DiscardPendingNodeEdit();
            Config.Nodes.Clear();
            Config.Connections.Clear();
            Config.ConnectorConfigs.Clear();
            ClearSelection();
            StatusText = "Cleared all nodes and connections.";
        }

        private void ClearAll()
        {
            _ = ClearAllAsync(); // fire-and-forge; kept for old XAML command binding fallback
        }

        private void AutoLayout()
        {
            var nodes = Config.Nodes.ToList();
            if (nodes.Count == 0) return;

            FinalizeNodeEditSeries();

            var indexById = nodes
                .Select((node, index) => (node.Id, index))
                .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
            var indegree = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var outgoing = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);

            foreach (var connection in Config.Connections)
            {
                if (!indegree.ContainsKey(connection.SourceNodeId)
                    || !indegree.ContainsKey(connection.TargetNodeId)
                    || connection.SourceNodeId == connection.TargetNodeId)
                {
                    continue;
                }

                outgoing[connection.SourceNodeId].Add(connection.TargetNodeId);
                indegree[connection.TargetNodeId]++;
            }

            var layers = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var queue = new Queue<string>(indegree
                .Where(pair => pair.Value == 0)
                .OrderBy(pair => indexById[pair.Key])
                .Select(pair => pair.Key));
            var processed = new HashSet<string>(StringComparer.Ordinal);

            while (queue.Count > 0)
            {
                var sourceId = queue.Dequeue();
                if (!processed.Add(sourceId)) continue;

                foreach (var targetId in outgoing[sourceId])
                {
                    layers[targetId] = Math.Max(layers[targetId], layers[sourceId] + 1);
                    indegree[targetId]--;
                    if (indegree[targetId] == 0)
                    {
                        queue.Enqueue(targetId);
                    }
                }
            }

            // Keep cyclic components usable instead of leaving all of them at (0, 0).
            var nextLayer = layers.Values.DefaultIfEmpty(0).Max() + 1;
            foreach (var node in nodes.Where(node => !processed.Contains(node.Id)))
            {
                layers[node.Id] = nextLayer++;
            }

            var maxNodeWidth = nodes
                .Select(node => double.IsFinite(node.Width) ? Math.Max(40, node.Width) : 240)
                .DefaultIfEmpty(240)
                .Max();
            var maxNodeHeight = nodes
                .Select(node => double.IsFinite(node.Height) ? Math.Max(40, node.Height) : 140)
                .DefaultIfEmpty(140)
                .Max();
            var horizontalStep = maxNodeWidth + LayoutHorizontalGap;
            var verticalStep = maxNodeHeight + LayoutVerticalGap;
            var canvasHeight = double.IsFinite(Config.CanvasHeight) ? Math.Max(1, Config.CanvasHeight) : 2000;
            var usableHeight = Math.Max(maxNodeHeight, canvasHeight - LayoutMargin * 2);
            var rowsPerColumn = Math.Max(1, (int)Math.Floor((usableHeight + LayoutVerticalGap) / verticalStep));
            var oldPositions = new Dictionary<VisualNode, (double X, double Y)>();
            var newPositions = new Dictionary<VisualNode, (double X, double Y)>();

            foreach (var layerGroup in nodes
                         .OrderBy(node => layers[node.Id])
                         .ThenBy(node => indexById[node.Id])
                         .GroupBy(node => layers[node.Id]))
            {
                var layerIndex = layerGroup.Key;
                var positionInLayer = 0;
                foreach (var node in layerGroup)
                {
                    var column = layerIndex + positionInLayer / rowsPerColumn;
                    var row = positionInLayer % rowsPerColumn;
                    var position = GetSnappedPosition(
                        LayoutMargin + column * horizontalStep,
                        LayoutMargin + row * verticalStep,
                        node);
                    oldPositions[node] = (node.X, node.Y);
                    newPositions[node] = position;
                    positionInLayer++;
                }
            }

            if (!newPositions.Any(pair =>
                    Math.Abs(oldPositions[pair.Key].X - pair.Value.X) >= double.Epsilon
                    || Math.Abs(oldPositions[pair.Key].Y - pair.Value.Y) >= double.Epsilon))
            {
                StatusText = "Nodes are already laid out.";
                return;
            }

            var command = new EditorCommand(
                () =>
                {
                    foreach (var pair in newPositions)
                    {
                        SetRawNodePosition(pair.Key, pair.Value.X, pair.Value.Y);
                    }
                },
                () =>
                {
                    foreach (var pair in oldPositions)
                    {
                        SetRawNodePosition(pair.Key, pair.Value.X, pair.Value.Y);
                    }
                });
            command.Execute();
            UndoRedo.Push(command);
            NotifyUndoRedoCommands();
            StatusText = $"Auto-laid out {nodes.Count} nodes";
        }

        private async Task SaveAsync()
        {
            if (_fileDialogService == null) return;

            var path = await _fileDialogService.ShowSaveFileDialogAsync(
                "Save Simulation",
                "ModbusForge Simulation|*.mfsim|All files|*.*",
                "program.mfsim");

            if (path == null) return;

            try
            {
                SaveProgramSnapshot(_activeProgram);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var root = JsonSerializer.SerializeToNode(Config, options) as JsonObject
                    ?? throw new InvalidOperationException("Unable to serialize the simulation configuration.");

                // Keep VisualNodeEditorConfig at the document root so existing .mfsim/.json
                // files remain compatible. The optional fields are ignored by old readers.
                root["VisualSimulationFormatVersion"] = 2;
                root["ProgramTree"] = JsonSerializer.SerializeToNode(ProgramTree, options);
                root["ActiveProgramId"] = _activeProgram?.Id;
                await File.WriteAllTextAsync(path, root.ToJsonString(options));
                StatusText = $"Saved to {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
        }

        private async Task LoadAsync()
        {
            if (_fileDialogService == null) return;

            var path = await _fileDialogService.ShowOpenFileDialogAsync(
                "Load Simulation",
                "ModbusForge Simulation|*.mfsim;*.json|All files|*.*");

            if (path == null) return;

            try
            {
                if (Config.Nodes.Count > 0 &&
                    !await ConfirmAsync(
                        $"Replace the current program with the contents of {Path.GetFileName(path)}?",
                        "Load Simulation"))
                {
                    return;
                }

                // Preserve the session's running state across the load: a simulation that
                // was running keeps running on the loaded program.
                var wasLive = Config.ShowLiveValues;
                Stop();
                var json = await File.ReadAllTextAsync(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = JsonNode.Parse(json) as JsonObject
                    ?? throw new InvalidDataException("The simulation file does not contain a JSON object.");

                // The current format stores the config at the root. Accept a wrapped
                // config as well, which makes this loader tolerant of future envelopes.
                var configNode = root["Config"] ?? root;
                var loaded = configNode.Deserialize<VisualNodeEditorConfig>(options)
                    ?? throw new InvalidDataException("The simulation file has no configuration.");
                var loadedTree = root["ProgramTree"]?.Deserialize<ProgramFolder>(options);
                var activeProgramId = root["ActiveProgramId"]?.GetValue<string>();

                ApplyLoadedProgramState(loaded, loadedTree, activeProgramId);
                DiscardPendingNodeEdit();
                UndoRedo.Clear();
                NotifyUndoRedoCommands();
                SelectedConnection = null;

                if (wasLive)
                {
                    if (Config.ShowLiveValues)
                        StartSimulation();
                    else
                        Config.ShowLiveValues = true; // routes through OnConfigPropertyChanged
                }

                StatusText = $"Loaded {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private async Task LoadDemoAsync()
        {
            if (Config.Nodes.Count > 0 &&
                !await ConfirmAsync(
                    "Replace the current program with the demo?",
                    "Load Demo"))
            {
                return;
            }

            // The demo is a live showcase: preserve the session's running state across
            // the load so a running simulation keeps running on the demo program.
            var wasLive = Config.ShowLiveValues;
            Stop();
            Config.Nodes.Clear();
            Config.Connections.Clear();
            Config.ConnectorConfigs.Clear();
            ConnectionLines.Clear();
            SelectedNode = null;
            SelectedConnection = null;

            var input1 = AddNodeAt(PlcElementType.InputBool, 80, 80);
            var input2 = AddNodeAt(PlcElementType.InputBool, 80, 240);
            var and = AddNodeAt(PlcElementType.AND, 320, 160);
            var output = AddNodeAt(PlcElementType.OutputBool, 560, 160);

            if (input1 != null && input2 != null && and != null && output != null)
            {
                Config.Connections.Add(new NodeConnection(input1.Id, and.Id, "Input1"));
                Config.Connections.Add(new NodeConnection(input2.Id, and.Id, "Input2"));
                Config.Connections.Add(new NodeConnection(and.Id, output.Id, "Input1"));
            }

            RefreshConnectionLines();
            NotifyUndoRedoCommands();

            if (wasLive)
            {
                Config.ShowLiveValues = true; // Stop() cleared it; routes through OnConfigPropertyChanged
            }

            StatusText = "Demo loaded";
        }

        private void LoadDemo()
        {
            _ = LoadDemoAsync(); // fire-and-forge; kept for old XAML command binding fallback
        }

        private static IEnumerable<ProgramModel> EnumeratePrograms(ProgramFolder folder)
        {
            foreach (var program in folder.Programs ?? Enumerable.Empty<ProgramModel>())
            {
                yield return program;
            }

            foreach (var childFolder in folder.Folders ?? Enumerable.Empty<ProgramFolder>())
            {
                foreach (var program in EnumeratePrograms(childFolder))
                {
                    yield return program;
                }
            }
        }

        private static IEnumerable<ProgramFolder> EnumerateFolders(ProgramFolder folder)
        {
            yield return folder;
            foreach (var childFolder in folder.Folders ?? Enumerable.Empty<ProgramFolder>())
            {
                foreach (var nestedFolder in EnumerateFolders(childFolder))
                {
                    yield return nestedFolder;
                }
            }
        }

        private void ApplyLoadedProgramState(
            VisualNodeEditorConfig loaded,
            ProgramFolder? loadedTree,
            string? activeProgramId)
        {
            loaded.Nodes ??= new ObservableCollection<VisualNode>();
            loaded.Connections ??= new ObservableCollection<NodeConnection>();
            loaded.ConnectorConfigs ??= new ObservableCollection<ConnectorConfiguration>();

            var tree = loadedTree ?? new ProgramFolder { Name = "Programs" };
            tree.Programs ??= new ObservableCollection<ProgramModel>();
            tree.Folders ??= new ObservableCollection<ProgramFolder>();
            var allPrograms = EnumeratePrograms(tree).ToList();
            if (allPrograms.Count == 0)
            {
                tree.Programs.Add(new ProgramModel
                {
                    Name = "Main",
                    ExecutionOrder = 0
                });
                allPrograms.Add(tree.Programs[0]);
            }

            foreach (var folder in EnumerateFolders(tree))
            {
                folder.Programs ??= new ObservableCollection<ProgramModel>();
                folder.Folders ??= new ObservableCollection<ProgramFolder>();
            }

            foreach (var program in allPrograms)
            {
                program.Nodes ??= new ObservableCollection<VisualNode>();
                program.Connections ??= new ObservableCollection<NodeConnection>();
                program.ConnectorConfigs ??= new ObservableCollection<ConnectorConfiguration>();
            }

            var active = allPrograms.FirstOrDefault(program => program.Id == activeProgramId)
                ?? allPrograms.First();
            // The root config is the active program in the backwards-compatible format.
            active.Nodes = loaded.Nodes;
            active.Connections = loaded.Connections;
            active.ConnectorConfigs = loaded.ConnectorConfigs;

            _isSwitchingProgram = true;
            try
            {
                Config = loaded;
                ProgramTree = tree;
                _activeProgram = active;
                SelectedProgram = active;
                SelectedTreeItem = active;
                ClearSelection();
            }
            finally
            {
                _isSwitchingProgram = false;
            }
        }

        private static VisualNode CloneNode(VisualNode source)
        {
            return new VisualNode
            {
                Id = Guid.NewGuid().ToString(),
                Name = source.Name,
                ElementType = source.ElementType,
                X = source.X + 50,
                Y = source.Y + 50,
                Width = source.Width,
                Height = source.Height,
                ShowLiveValues = source.ShowLiveValues,
                IsEnabled = source.IsEnabled,
                Waveform = source.Waveform,
                PeriodMs = source.PeriodMs,
                Amplitude = source.Amplitude,
                Offset = source.Offset,
                TimerPresetMs = source.TimerPresetMs,
                SetDominant = source.SetDominant,
                CounterPreset = source.CounterPreset,
                CompareValue = source.CompareValue,
                Input1Address = source.Input1Address?.Clone() ?? new PlcAddressReference(),
                Input2Address = source.Input2Address?.Clone() ?? new PlcAddressReference(),
                OutputAddress = source.OutputAddress?.Clone() ?? new PlcAddressReference(),
                OutputPortBindings = new Dictionary<string, PlcAddressReference>(
                    source.OutputPortBindings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone()),
                    StringComparer.Ordinal),
                OutputPortNames = new ObservableCollection<string>(source.OutputPortNames)
            };
        }

        private void SaveProgramSnapshot(ProgramModel? program)
        {
            if (program == null) return;
            program.Nodes = Config.Nodes;
            program.Connections = Config.Connections;
            program.ConnectorConfigs = Config.ConnectorConfigs;
            program.ModifiedAt = DateTime.Now;
        }

        private void BindConfigToProgram(ProgramModel program)
        {
            DetachConfigHandlers();
            Config.Nodes = program.Nodes ?? new ObservableCollection<VisualNode>();
            Config.Connections = program.Connections ?? new ObservableCollection<NodeConnection>();
            Config.ConnectorConfigs = program.ConnectorConfigs ?? new ObservableCollection<ConnectorConfiguration>();
            AttachConfigHandlers();
            DiscardPendingNodeEdit();
            UndoRedo.Clear();
            NotifyUndoRedoCommands();
            SelectedNode = null;
            SelectedConnection = null;
            RefreshConnectionLines();

            // The collections were swapped to the program's instances; the canvas binds
            // to the property getters, so re-notify or it keeps showing the previous
            // program's content.
            OnPropertyChanged(nameof(Nodes));
            OnPropertyChanged(nameof(Connections));
            OnPropertyChanged(nameof(ConnectorConfigs));
        }

        private void NotifySelectionCommands()
        {
            ((IRelayCommand)AddNodeCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)RemoveNodeCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)AddConnectionCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)RemoveConnectionCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)AlignLeftCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)AlignTopCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)DistributeHorizontallyCommand).NotifyCanExecuteChanged();
        }

        private void NotifyUndoRedoCommands()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            ((IRelayCommand)UndoCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)RedoCommand).NotifyCanExecuteChanged();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
            DetachConfigHandlers();
            _visualSimulation.Dispose();
        }

        private sealed class EditorCommand : IEditorCommand
        {
            private readonly Action _execute;
            private readonly Action _unexecute;

            public EditorCommand(Action execute, Action unexecute)
            {
                _execute = execute;
                _unexecute = unexecute;
            }

            public void Execute() => _execute();
            public void Unexecute() => _unexecute();
        }
    }
}
