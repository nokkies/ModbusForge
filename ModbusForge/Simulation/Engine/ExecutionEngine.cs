using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Modbus.Data;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.Simulation.Core;

namespace ModbusForge.Simulation.Engine
{
    /// <summary>
    /// Topologically executes a graph of function blocks with two-phase evaluate-then-write semantics.
    /// </summary>
    public sealed class ExecutionEngine : IExecutionEngine
    {
        private readonly ILogger<ExecutionEngine> _logger;
        private readonly FunctionBlockCatalog _catalog;
        private readonly IConsoleLoggerService? _consoleLoggerService;

        private List<SimulationNode> _nodes = new();
        private List<SimulationConnection> _connections = new();
        private List<SimulationNode> _executionOrder = new();
        private List<string> _cycleNodeIds = new();
        private Dictionary<string, SimulationNode> _nodeById = new();
        private DateTimeOffset _lastExecutionTime = DateTimeOffset.UtcNow;

        public ExecutionEngine(FunctionBlockCatalog catalog, ILogger<ExecutionEngine>? logger = null, IConsoleLoggerService? consoleLoggerService = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _logger = logger ?? NullLogger<ExecutionEngine>.Instance;
            _consoleLoggerService = consoleLoggerService;
        }

        public IReadOnlyList<SimulationNode> ExecutionOrder => _executionOrder;
        public IReadOnlyList<string> CycleNodeIds => _cycleNodeIds;
        public int CycleCount { get; private set; }

        public void LoadGraph(IEnumerable<SimulationNode> nodes, IEnumerable<SimulationConnection> connections)
        {
            _nodes = nodes?.ToList() ?? new List<SimulationNode>();
            _connections = connections?.ToList() ?? new List<SimulationConnection>();
            _nodeById = _nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

            // Validate connection endpoints.
            var validNodeIds = new HashSet<string>(_nodeById.Keys, StringComparer.Ordinal);
            var invalidConnections = _connections
                .Where(c => !validNodeIds.Contains(c.SourceNodeId) || !validNodeIds.Contains(c.TargetNodeId))
                .ToList();

            foreach (var invalid in invalidConnections)
            {
                _logger.LogWarning("Removing connection with missing endpoint: {Source}:{SourcePort} -> {Target}:{TargetPort}",
                    invalid.SourceNodeId, invalid.SourcePortName, invalid.TargetNodeId, invalid.TargetPortName);
            }

            _connections.RemoveAll(c => !validNodeIds.Contains(c.SourceNodeId) || !validNodeIds.Contains(c.TargetNodeId));

            RebuildExecutionOrder();

            _lastExecutionTime = DateTimeOffset.UtcNow;

            _logger.LogInformation("Loaded simulation graph with {NodeCount} nodes and {ConnectionCount} connections",
                _nodes.Count, _connections.Count);
        }

        public void Execute(DataStore? dataStore = null)
        {
            if (_executionOrder.Count == 0)
            {
                _logger.LogDebug("No simulation nodes to execute");
                return;
            }

            var currentTime = DateTimeOffset.UtcNow;
            var elapsed = currentTime - _lastExecutionTime;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

            // Phase 1: Evaluate all nodes.
            foreach (var node in _executionOrder)
            {
                try
                {
                    EvaluateNode(node, dataStore, currentTime, elapsed, CycleCount);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _logger.LogDebug(ex, "Failed to evaluate node {NodeId} ({BlockType})", node.Id, node.Block.TypeId);
                }
            }

            // Phase 2: Write outputs to the data store.
            foreach (var node in _executionOrder)
            {
                try
                {
                    WriteNodeOutputs(node, dataStore);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _logger.LogDebug(ex, "Failed to write outputs for node {NodeId}", node.Id);
                }
            }

            _lastExecutionTime = currentTime;
            CycleCount++;
        }

        private void RebuildExecutionOrder()
        {
            _executionOrder = new List<SimulationNode>();
            _cycleNodeIds = new List<string>();

            var inDegree = _nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
            var adjacency = _nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);

            foreach (var connection in _connections)
            {
                if (inDegree.ContainsKey(connection.TargetNodeId) && adjacency.ContainsKey(connection.SourceNodeId))
                {
                    inDegree[connection.TargetNodeId]++;
                    adjacency[connection.SourceNodeId].Add(connection.TargetNodeId);
                }
            }

            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!_nodeById.TryGetValue(id, out var node))
                    continue;

                _executionOrder.Add(node);
                visited.Add(id);

                foreach (var neighbor in adjacency[id])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }

            if (_executionOrder.Count < _nodes.Count)
            {
                _cycleNodeIds = _nodes.Where(n => !visited.Contains(n.Id)).Select(n => n.Id).ToList();
                _logger.LogWarning("Simulation graph contains {CycleCount} nodes in cycles; they will not be evaluated",
                    _cycleNodeIds.Count);
            }
        }

        private void EvaluateNode(SimulationNode node, DataStore? dataStore, DateTimeOffset currentTime, TimeSpan elapsed, int cycleCount)
        {
            var inputs = ResolveInputs(node, dataStore);

            using var loggerScope = _logger.BeginScope("Node {NodeId}", node.Id);
            var context = new ExecutionContext(
                node,
                inputs,
                currentTime,
                elapsed,
                cycleCount,
                dataStore,
                _logger);

            node.Block.Execute(context);

            // Copy evaluated outputs onto the node so downstream nodes can consume them.
            node.OutputValues.Clear();
            foreach (var output in context.GetOutputs())
            {
                node.OutputValues[output.Key] = output.Value;
            }
        }

        private Dictionary<string, ISimulationValue> ResolveInputs(SimulationNode node, DataStore? dataStore)
        {
            var inputs = new Dictionary<string, ISimulationValue>(StringComparer.Ordinal);

            var incoming = _connections
                .Where(c => string.Equals(c.TargetNodeId, node.Id, StringComparison.Ordinal))
                .ToList();

            foreach (var connection in incoming)
            {
                if (_nodeById.TryGetValue(connection.SourceNodeId, out var sourceNode) &&
                    sourceNode.OutputValues.TryGetValue(connection.SourcePortName, out var value))
                {
                    inputs[connection.TargetPortName] = value;
                }
            }

            foreach (var port in node.Block.Ports.Where(p => p.Direction == PortDirection.Input))
            {
                if (inputs.ContainsKey(port.Name))
                    continue;

                if (node.InputBindings.TryGetValue(port.Name, out var address) && address?.Address >= 0)
                {
                    var value = ReadDataStore(dataStore, address, port.DataType);
                    if (value != null)
                        inputs[port.Name] = value;
                }
            }

            return inputs;
        }

        private void WriteNodeOutputs(SimulationNode node, DataStore? dataStore)
        {
            if (dataStore == null) return;

            foreach (var outputPort in node.Block.Ports.Where(p => p.Direction == PortDirection.Output))
            {
                if (!node.OutputValues.TryGetValue(outputPort.Name, out var value))
                    continue;

                if (!node.OutputBindings.TryGetValue(outputPort.Name, out var address) || address is null || address.Address < 0)
                    continue;

                WriteDataStore(dataStore, address, value);
            }
        }

        private static ISimulationValue? ReadDataStore(DataStore? dataStore, PlcAddressReference address, SimulationDataType targetType)
        {
            if (dataStore == null || address.Address < 0)
                return null;

            object? raw = address.Area switch
            {
                PlcArea.Coil => address.Address < dataStore.CoilDiscretes.Count && dataStore.CoilDiscretes[address.Address],
                PlcArea.DiscreteInput => address.Address < dataStore.InputDiscretes.Count && dataStore.InputDiscretes[address.Address],
                PlcArea.HoldingRegister => address.Address < dataStore.HoldingRegisters.Count ? (object)dataStore.HoldingRegisters[address.Address] : null,
                PlcArea.InputRegister => address.Address < dataStore.InputRegisters.Count ? (object)dataStore.InputRegisters[address.Address] : null,
                _ => null
            };

            if (raw == null)
                return null;

            var value = SimulationValue.FromObject(targetType, raw);
            return address.Not ? Invert(value) : value;
        }

        private void WriteDataStore(DataStore dataStore, PlcAddressReference address, ISimulationValue value)
        {
            if (address.Address < 0) return;

            var finalValue = address.Not ? Invert(value) : value;

            switch (address.Area)
            {
                case PlcArea.HoldingRegister:
                    if (address.Address < dataStore.HoldingRegisters.Count)
                    {
                        var oldValue = dataStore.HoldingRegisters[address.Address];
                        var newValue = ToUInt16(finalValue);
                        if (oldValue != newValue)
                        {
                            dataStore.HoldingRegisters[address.Address] = newValue;
                            _consoleLoggerService?.Log($"Simulation wrote holding register {address.Address}: {oldValue} -> {newValue}");
                        }
                    }
                    break;
                case PlcArea.InputRegister:
                    if (address.Address < dataStore.InputRegisters.Count)
                    {
                        var oldValue = dataStore.InputRegisters[address.Address];
                        var newValue = ToUInt16(finalValue);
                        if (oldValue != newValue)
                        {
                            dataStore.InputRegisters[address.Address] = newValue;
                            _consoleLoggerService?.Log($"Simulation wrote input register {address.Address}: {oldValue} -> {newValue}");
                        }
                    }
                    break;
                case PlcArea.Coil:
                    if (address.Address < dataStore.CoilDiscretes.Count)
                    {
                        var oldValue = dataStore.CoilDiscretes[address.Address];
                        var newValue = finalValue.AsBool();
                        if (oldValue != newValue)
                        {
                            dataStore.CoilDiscretes[address.Address] = newValue;
                            _consoleLoggerService?.Log($"Simulation wrote coil {address.Address}: {(oldValue ? 1 : 0)} -> {(newValue ? 1 : 0)}");
                        }
                    }
                    break;
                case PlcArea.DiscreteInput:
                    if (address.Address < dataStore.InputDiscretes.Count)
                    {
                        var oldValue = dataStore.InputDiscretes[address.Address];
                        var newValue = finalValue.AsBool();
                        if (oldValue != newValue)
                        {
                            dataStore.InputDiscretes[address.Address] = newValue;
                            _consoleLoggerService?.Log($"Simulation wrote discrete input {address.Address}: {(oldValue ? 1 : 0)} -> {(newValue ? 1 : 0)}");
                        }
                    }
                    break;
            }
        }

        private static ISimulationValue Invert(ISimulationValue value)
        {
            return value.DataType switch
            {
                SimulationDataType.Bool => SimulationValue.Bool(!value.AsBool()),
                SimulationDataType.Real => SimulationValue.Real(value.AsReal() == 0 ? 1.0 : 0.0),
                _ => SimulationValue.Int32(value.AsInt32() == 0 ? 1 : 0)
            };
        }

        private static ushort ToUInt16(ISimulationValue value)
        {
            if (value.DataType == SimulationDataType.Bool)
                return value.AsBool() ? (ushort)1 : (ushort)0;

            var clamped = Math.Clamp(Math.Round(value.AsReal()), 0, ushort.MaxValue);
            return (ushort)clamped;
        }
    }
}
