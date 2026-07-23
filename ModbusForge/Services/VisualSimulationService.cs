using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Modbus.Data;
using ModbusForge.Models;
using ModbusForge.Simulation.Blocks;
using ModbusForge.Simulation.Core;
using ModbusForge.Simulation.Engine;
using ModbusForge.ViewModels;

namespace ModbusForge.Services
{
    public interface IVisualSimulationService
    {
        void Start(IList<VisualNode> nodes, IList<NodeConnection> connections, Func<bool> showLiveValuesProvider);
        void Stop();
        void UpdateNodeValues();
        bool GetNodeValue(string nodeId);

        /// <summary>
        /// Writes a user-supplied value to the DataStore at the node's configured
        /// input or output address. Used by the Live Values panel when the user
        /// manually edits a value while simulation is paused or for input nodes
        /// that should drive the DataStore directly.
        /// </summary>
        void WriteNodeValue(string nodeId, double value);
    }

    /// <summary>
    /// Carries both boolean and integer results from node evaluation,
    /// preventing bool/int cross-contamination in the data stores.
    /// </summary>
    public readonly record struct NodeResult
    {
        public bool BoolValue { get; init; }
        public int IntValue { get; init; }

        public static NodeResult FromBool(bool b) => new NodeResult { BoolValue = b, IntValue = b ? 1 : 0 };
        public static NodeResult FromInt(int i) => new NodeResult { BoolValue = i != 0, IntValue = i };
    }

    public class VisualSimulationService : IVisualSimulationService, IDisposable
    {
        private readonly ILogger<VisualSimulationService> _logger;
        private readonly ModbusServerService _serverService;
        private readonly FunctionBlockCatalog _catalog;
        private readonly ExecutionEngine _engine;
        private readonly IConsoleLoggerService? _consoleLoggerService;

        private IList<VisualNode>? _nodes;
        private IList<NodeConnection>? _connections;
        private Func<bool>? _showLiveValuesProvider;
        private DispatcherTimer? _animationTimer;
        private bool _isAnimating;
        private DateTime _lastUpdate;

        // Cache for node values to avoid excessive UI updates
        private readonly Dictionary<string, bool> _nodeValueCache = new();
        private readonly Dictionary<string, DateTime> _lastNodeUpdate = new();

        // Graph shape cache (rebuilt when the graph changes)
        private int _lastNodeCount;
        private int _lastConnectionCount;

        public VisualSimulationService(
            ILogger<VisualSimulationService> logger,
            ModbusServerService serverService)
            : this(logger, serverService, null)
        {
        }

        public VisualSimulationService(
            ILogger<VisualSimulationService> logger,
            ModbusServerService serverService,
            IConsoleLoggerService? consoleLoggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _consoleLoggerService = consoleLoggerService;

            _catalog = CreateCatalog();
            _engine = new ExecutionEngine(_catalog, consoleLoggerService: _consoleLoggerService);
        }

        private static FunctionBlockCatalog CreateCatalog()
        {
            var catalog = new FunctionBlockCatalog();

            // I/O
            catalog.Register(new LegacyInputBlock());
            catalog.Register(new InputBoolBlock());
            catalog.Register(new InputIntBlock());
            catalog.Register(new LegacyOutputBlock());
            catalog.Register(new OutputBoolBlock());
            catalog.Register(new OutputIntBlock());

            // Logic
            catalog.Register(new NotBlock());
            catalog.Register(new AndBlock());
            catalog.Register(new OrBlock());
            catalog.Register(new RsLatchBlock());

            // Timers
            catalog.Register(new TonBlock());
            catalog.Register(new TofBlock());
            catalog.Register(new TpBlock());

            // Counters
            catalog.Register(new CtuBlock());
            catalog.Register(new CtdBlock());
            catalog.Register(new CtcBlock());

            // Comparators
            catalog.Register(new CompareBlock(ComparisonOperation.Equal));
            catalog.Register(new CompareBlock(ComparisonOperation.NotEqual));
            catalog.Register(new CompareBlock(ComparisonOperation.GreaterThan));
            catalog.Register(new CompareBlock(ComparisonOperation.LessThan));
            catalog.Register(new CompareBlock(ComparisonOperation.GreaterThanOrEqual));
            catalog.Register(new CompareBlock(ComparisonOperation.LessThanOrEqual));

            // Math
            catalog.Register(new MathBlock(MathOperation.Add));
            catalog.Register(new MathBlock(MathOperation.Subtract));
            catalog.Register(new MathBlock(MathOperation.Multiply));
            catalog.Register(new MathBlock(MathOperation.Divide));

            // Sources
            catalog.Register(new SignalGeneratorBlock());

            return catalog;
        }

        public void Start(IList<VisualNode> nodes, IList<NodeConnection> connections, Func<bool> showLiveValuesProvider)
        {
            _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _showLiveValuesProvider = showLiveValuesProvider ?? throw new ArgumentNullException(nameof(showLiveValuesProvider));

            if (_animationTimer == null)
            {
                _animationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _animationTimer.Tick += AnimationTimer_Tick;
            }

            _lastUpdate = DateTime.UtcNow;
            _lastNodeCount = -1;
            _lastConnectionCount = -1;
            _animationTimer.Start();
            _isAnimating = true;

            _logger.LogInformation("Visual simulation started");
        }

        public void Stop()
        {
            _animationTimer?.Stop();
            _isAnimating = false;

            if (_nodes != null)
            {
                foreach (var node in _nodes)
                {
                    node.CurrentValue = false;
                    node.ShowLiveValues = false;
                }
            }

            _nodeValueCache.Clear();
            _lastNodeUpdate.Clear();
            _lastNodeCount = -1;
            _lastConnectionCount = -1;

            _logger.LogInformation("Visual simulation stopped");
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            if (_nodes == null || !_isAnimating) return;
            if (_showLiveValuesProvider == null || !_showLiveValuesProvider()) return;
            if (!_serverService.IsConnected) return;

            try
            {
                UpdateNodeValues();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error updating visual node values");
            }
        }

        public void UpdateNodeValues()
        {
            if (_nodes == null) return;

            var dataStore = _serverService.GetDataStore();
            if (dataStore == null) return;

            var now = DateTime.UtcNow;

            EnsureGraphLoaded();

            _engine.Execute(dataStore);

            // Update UI-visible properties and internal caches.
            foreach (var node in _nodes)
            {
                var simulationNode = _engine.ExecutionOrder.FirstOrDefault(n => n.Id == node.Id);
                if (simulationNode == null) continue;

                double? liveDouble = null;
                NodeResult result;
                if (simulationNode.OutputValues.TryGetValue("Output", out var value))
                {
                    // Preserve the native representation so the Live Values panel reflects
                    // the real signal (e.g. waveform generators produce Real values).
                    result = NodeResult.FromInt(value.AsInt32());
                    try { liveDouble = value.AsReal(); } catch { liveDouble = value.AsInt32(); }
                }
                else
                {
                    result = NodeResult.FromBool(false);
                    liveDouble = 0;
                }

                var oldValue = _nodeValueCache.GetValueOrDefault(node.Id, false);
                var lastUpd = _lastNodeUpdate.GetValueOrDefault(node.Id, DateTime.MinValue);
                var shouldUpdate = result.BoolValue != oldValue || (now - lastUpd).TotalMilliseconds > 500;

                if (shouldUpdate)
                {
                    node.CurrentValue = result.BoolValue;
                    node.IntValue = result.IntValue;
                    // Mirror the live value into the property bound by the Live Values panel.
                    // Use the suppress flag so we don't trigger a write-back to the DataStore
                    // for values the simulation itself just produced.
                    // Skip if the user is actively editing the value on the node canvas.
                    if (!node.IsEditingLiveValue)
                    {
                        node.SuppressWriteBack = true;
                        try
                        {
                            node.CurrentValueDouble = liveDouble ?? result.IntValue;
                        }
                        finally
                        {
                            node.SuppressWriteBack = false;
                        }
                    }
                    _nodeValueCache[node.Id] = result.BoolValue;
                    _lastNodeUpdate[node.Id] = now;
                }
            }

            _lastUpdate = now;
        }

        public bool GetNodeValue(string nodeId)
        {
            return _nodeValueCache.GetValueOrDefault(nodeId, false);
        }

        /// <summary>
        /// Writes a user-supplied value to the DataStore at the node's configured
        /// input or output address. The address used depends on the node type:
        ///  - Input nodes write to <see cref="VisualNode.Input1Address"/> (they drive the DataStore).
        ///  - Output nodes write to <see cref="VisualNode.OutputAddress"/>.
        /// </summary>
        public void WriteNodeValue(string nodeId, double value)
        {
            if (_nodes == null) return;

            var dataStore = _serverService.GetDataStore();
            if (dataStore == null) return;

            var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return;

            // Input nodes drive the DataStore via their Input1Address; output nodes
            // via their OutputAddress. Pick whichever is configured.
            var address = IsInputSource(node.ElementType)
                ? node.Input1Address
                : node.OutputAddress;

            if (address == null || address.Address < 0)
            {
                _logger.LogDebug("WriteNodeValue skipped: node {NodeId} has no configured address", nodeId);
                return;
            }

            switch (address.Area)
            {
                case PlcArea.HoldingRegister:
                    if (address.Address < dataStore.HoldingRegisters.Count)
                    {
                        var oldValue = dataStore.HoldingRegisters[address.Address];
                        var newValue = ToClampedUInt16(value);
                        if (oldValue != newValue)
                        {
                            dataStore.HoldingRegisters[address.Address] = newValue;
                            _consoleLoggerService?.Log($"Live Values wrote holding register {address.Address}: {oldValue} -> {newValue}");
                            _logger.LogDebug("Wrote node {NodeId} value {Value} to holding register {Address}", nodeId, newValue, address.Address);
                        }
                    }
                    break;

                case PlcArea.InputRegister:
                    if (address.Address < dataStore.InputRegisters.Count)
                    {
                        var oldValue = dataStore.InputRegisters[address.Address];
                        var newValue = ToClampedUInt16(value);
                        if (oldValue != newValue)
                        {
                            dataStore.InputRegisters[address.Address] = newValue;
                            _consoleLoggerService?.Log($"Live Values wrote input register {address.Address}: {oldValue} -> {newValue}");
                            _logger.LogDebug("Wrote node {NodeId} value {Value} to input register {Address}", nodeId, newValue, address.Address);
                        }
                    }
                    break;

                case PlcArea.Coil:
                    if (address.Address < dataStore.CoilDiscretes.Count)
                    {
                        var oldValue = dataStore.CoilDiscretes[address.Address];
                        var newValue = Math.Abs(value) > 0.0001;
                        if (oldValue != newValue)
                        {
                            dataStore.CoilDiscretes[address.Address] = newValue;
                            _consoleLoggerService?.Log($"Live Values wrote coil {address.Address}: {(oldValue ? 1 : 0)} -> {(newValue ? 1 : 0)}");
                            _logger.LogDebug("Wrote node {NodeId} value {Value} to coil {Address}", nodeId, newValue ? 1 : 0, address.Address);
                        }
                    }
                    break;

                case PlcArea.DiscreteInput:
                    if (address.Address < dataStore.InputDiscretes.Count)
                    {
                        var oldValue = dataStore.InputDiscretes[address.Address];
                        var newValue = Math.Abs(value) > 0.0001;
                        if (oldValue != newValue)
                        {
                            dataStore.InputDiscretes[address.Address] = newValue;
                            _consoleLoggerService?.Log($"Live Values wrote discrete input {address.Address}: {(oldValue ? 1 : 0)} -> {(newValue ? 1 : 0)}");
                            _logger.LogDebug("Wrote node {NodeId} value {Value} to discrete input {Address}", nodeId, newValue ? 1 : 0, address.Address);
                        }
                    }
                    break;
            }
        }

        private static ushort ToClampedUInt16(double value)
        {
            var clamped = Math.Clamp(Math.Round(value), 0, ushort.MaxValue);
            return (ushort)clamped;
        }

        private void EnsureGraphLoaded()
        {
            if (_nodes == null || _connections == null) return;

            var nodeCount = _nodes.Count;
            var connCount = _connections.Count;

            if (_lastNodeCount == nodeCount &&
                _lastConnectionCount == connCount)
            {
                return;
            }

            var simulationNodes = _nodes.Select(MapToSimulationNode).ToList();
            var simulationConnections = _connections.Select(MapToSimulationConnection).ToList();

            _engine.LoadGraph(simulationNodes, simulationConnections);

            _lastNodeCount = nodeCount;
            _lastConnectionCount = connCount;

            _logger.LogDebug("Rebuilt simulation graph: {Count} nodes", _engine.ExecutionOrder.Count);
        }

        private SimulationNode MapToSimulationNode(VisualNode visualNode)
        {
            var block = _catalog.Create(visualNode.ElementType.ToString());
            var node = new SimulationNode(visualNode.Id, visualNode.Name, block);

            // Bind DataStore addresses only to the ports that the element type actually uses.
            if (IsInputSource(visualNode.ElementType) && visualNode.Input1Address?.Address >= 0)
                node.InputBindings["Input1"] = visualNode.Input1Address;

            if (IsCompareOrMath(visualNode.ElementType))
            {
                if (visualNode.Input1Address?.Address >= 0)
                    node.InputBindings["Input1"] = visualNode.Input1Address;
                if (visualNode.Input2Address?.Address >= 0)
                    node.InputBindings["Input2"] = visualNode.Input2Address;
            }

            if (IsOutputSink(visualNode.ElementType) && visualNode.OutputAddress?.Address >= 0)
                node.OutputBindings["Output"] = visualNode.OutputAddress;

            if (visualNode.TimerPresetMs != 0)
                node.Parameters["TimerPresetMs"] = visualNode.TimerPresetMs;

            if (visualNode.CounterPreset != 0)
                node.Parameters["CounterPreset"] = visualNode.CounterPreset;

            node.Parameters["CompareValue"] = visualNode.CompareValue;
            node.Parameters["SetDominant"] = visualNode.SetDominant;

            if (!string.IsNullOrEmpty(visualNode.Waveform))
                node.Parameters["Waveform"] = visualNode.Waveform;

            if (visualNode.PeriodMs != 0)
                node.Parameters["PeriodMs"] = visualNode.PeriodMs;

            if (visualNode.Amplitude != 0)
                node.Parameters["Amplitude"] = visualNode.Amplitude;

            node.Parameters["Offset"] = visualNode.Offset;

            return node;
        }

        private static bool IsInputSource(PlcElementType elementType)
        {
            return elementType is PlcElementType.Input or PlcElementType.InputBool or PlcElementType.InputInt;
        }

        private static bool IsOutputSink(PlcElementType elementType)
        {
            return elementType is PlcElementType.Output or PlcElementType.OutputBool or PlcElementType.OutputInt;
        }

        private static bool IsCompareOrMath(PlcElementType elementType)
        {
            return elementType is PlcElementType.COMPARE_EQ
                or PlcElementType.COMPARE_NE
                or PlcElementType.COMPARE_GT
                or PlcElementType.COMPARE_LT
                or PlcElementType.COMPARE_GE
                or PlcElementType.COMPARE_LE
                or PlcElementType.MATH_ADD
                or PlcElementType.MATH_SUB
                or PlcElementType.MATH_MUL
                or PlcElementType.MATH_DIV;
        }

        private SimulationConnection MapToSimulationConnection(NodeConnection connection)
        {
            // The legacy UI only supports a single "Output" connector per node.
            var sourcePort = string.IsNullOrEmpty(connection.SourceConnector) ? "Output" : connection.SourceConnector;
            var targetPort = string.IsNullOrEmpty(connection.TargetConnector) ? "Input1" : connection.TargetConnector;

            return new SimulationConnection(
                connection.SourceNodeId,
                sourcePort,
                connection.TargetNodeId,
                targetPort);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
