using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

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
        private readonly IAvaloniaVisualSimulationService _visualSimulation;
        private readonly IFileDialogService? _fileDialogService;

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

        public ObservableCollection<PaletteItem> Palette { get; } = new();

        public IReadOnlyList<string> Waveforms { get; } = new[] { "Ramp", "Sine", "Triangle", "Square" };

        public ICommand AddNodeCommand { get; }
        public ICommand RemoveNodeCommand { get; }
        public ICommand AddConnectionCommand { get; }
        public ICommand RemoveConnectionCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand LoadCommand { get; }

        public VisualNodeEditorViewModel(
            IAvaloniaVisualSimulationService visualSimulation,
            IFileDialogService? fileDialogService = null)
        {
            _visualSimulation = visualSimulation ?? throw new ArgumentNullException(nameof(visualSimulation));
            _fileDialogService = fileDialogService;

            AddNodeCommand = new RelayCommand(AddNode, () => SelectedPaletteItem != null);
            RemoveNodeCommand = new RelayCommand(RemoveNode, () => SelectedNode != null);
            AddConnectionCommand = new RelayCommand(AddConnection, () => SelectedNode != null);
            RemoveConnectionCommand = new RelayCommand(RemoveConnection, () => SelectedConnection != null);
            RunCommand = new RelayCommand(Run, () => !IsRunning);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            LoadCommand = new AsyncRelayCommand(LoadAsync);

            BuildPalette();

            Config.Nodes.CollectionChanged += (s, e) => RefreshConnectionLines();
            Config.Connections.CollectionChanged += (s, e) => RefreshConnectionLines();
        }

        partial void OnSelectedNodeChanged(VisualNode? value)
        {
            RemoveNodeCommand?.Execute(null);
            if (value != null && SelectedConnection != null)
            {
                SelectedConnection = null;
            }
        }

        partial void OnSelectedPaletteItemChanged(PaletteItem? value)
        {
            ((IRelayCommand)AddNodeCommand).NotifyCanExecuteChanged();
        }

        partial void OnIsRunningChanged(bool value)
        {
            ((IRelayCommand)RunCommand).NotifyCanExecuteChanged();
            ((IRelayCommand)StopCommand).NotifyCanExecuteChanged();
        }

        private void BuildPalette()
        {
            Palette.Clear();
            foreach (var descriptor in NodeDescriptors.All.Where(d => d.ShowInPalette).OrderBy(d => d.Category).ThenBy(d => d.PaletteName))
            {
                Palette.Add(new PaletteItem
                {
                    ElementType = descriptor.ElementType,
                    DisplayName = descriptor.PaletteName,
                    Category = descriptor.Category
                });
            }
        }

        private void AddNode()
        {
            if (SelectedPaletteItem == null) return;

            var descriptor = NodeDescriptors.Get(SelectedPaletteItem.ElementType);
            var node = new VisualNode
            {
                Name = descriptor.DisplayName,
                ElementType = SelectedPaletteItem.ElementType,
                X = 100 + Config.Nodes.Count * 40,
                Y = 100 + Config.Nodes.Count * 30
            };

            Config.Nodes.Add(node);
            SelectedNode = node;
            StatusText = $"Added {descriptor.DisplayName}";
        }

        private void RemoveNode()
        {
            if (SelectedNode == null) return;

            var id = SelectedNode.Id;
            var toRemove = Config.Connections.Where(c => c.SourceNodeId == id || c.TargetNodeId == id).ToList();
            foreach (var c in toRemove)
            {
                Config.Connections.Remove(c);
            }

            Config.Nodes.Remove(SelectedNode);
            SelectedNode = null;
            StatusText = "Node removed";
        }

        private void AddConnection()
        {
            if (SelectedNode == null) return;

            var target = Config.Nodes.FirstOrDefault(n => n != SelectedNode);
            if (target == null)
            {
                StatusText = "Select another node to connect.";
                return;
            }

            var connection = new NodeConnection(SelectedNode.Id, target.Id);

            Config.Connections.Add(connection);
            SelectedConnection = connection;
            StatusText = "Connection added";
        }

        private void RemoveConnection()
        {
            if (SelectedConnection == null) return;
            Config.Connections.Remove(SelectedConnection);
            SelectedConnection = null;
            StatusText = "Connection removed";
        }

        private void Run()
        {
            _visualSimulation.Start(Config);
            IsRunning = true;
            StatusText = "Simulation running";
        }

        private void Stop()
        {
            _visualSimulation.Stop();
            IsRunning = false;
            StatusText = "Simulation stopped";
        }

        private void RefreshConnectionLines()
        {
            var lines = new ObservableCollection<ConnectionLine>();
            var nodeById = Config.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

            foreach (var connection in Config.Connections)
            {
                if (!nodeById.TryGetValue(connection.SourceNodeId, out var source) ||
                    !nodeById.TryGetValue(connection.TargetNodeId, out var target))
                {
                    continue;
                }

                lines.Add(new ConnectionLine
                {
                    SourceId = source.Id,
                    TargetId = target.Id,
                    FromX = source.X + source.Width / 2,
                    FromY = source.Y + source.Height / 2,
                    ToX = target.X + target.Width / 2,
                    ToY = target.Y + target.Height / 2
                });
            }

            ConnectionLines = lines;
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
                var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);
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
                Stop();
                var json = await File.ReadAllTextAsync(path);
                var loaded = JsonSerializer.Deserialize<VisualNodeEditorConfig>(json);
                if (loaded != null)
                {
                    Config = loaded;
                    Config.Nodes.CollectionChanged += (s, e) => RefreshConnectionLines();
                    Config.Connections.CollectionChanged += (s, e) => RefreshConnectionLines();
                    RefreshConnectionLines();
                    StatusText = $"Loaded {Path.GetFileName(path)}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        public void Dispose()
        {
            Stop();
            _visualSimulation.Dispose();
        }
    }
}
