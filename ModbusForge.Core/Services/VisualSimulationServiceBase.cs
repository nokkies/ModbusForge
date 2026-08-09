using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Modbus.Data;
using ModbusForge.Core.Simulation.Blocks;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Core.Simulation.Engine;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Carries both boolean and integer results from node evaluation,
    /// preventing bool/int cross-contamination in the data stores.
    /// </summary>
    public readonly record struct SimulationNodeResult
    {
        public bool BoolValue { get; init; }
        public int IntValue { get; init; }

        public static SimulationNodeResult FromBool(bool b) => new SimulationNodeResult { BoolValue = b, IntValue = b ? 1 : 0 };
        public static SimulationNodeResult FromInt(int i) => new SimulationNodeResult { BoolValue = i != 0, IntValue = i };
    }

    /// <summary>
    /// Shared engine and graph-mapping logic for the WPF and Avalonia visual simulation services.
    /// Derived types only supply a platform-specific timer and (for WPF) marshalling.
    /// </summary>
    public abstract class VisualSimulationServiceBase<T> : IVisualSimulationService, IDisposable
        where T : class
    {
        private readonly ILogger<T> _logger;
        private readonly IConsoleLoggerService? _consoleLoggerService;
        private readonly IConnectionManager? _connectionManager;
        private readonly FunctionBlockCatalog _catalog;
        private readonly ExecutionEngine _engine;
        private readonly DataStore _dataStore;

        private VisualNodeEditorConfig? _config;
        private int _lastGraphHash;
        private readonly ConcurrentDictionary<string, bool> _nodeValueCache = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastNodeUpdate = new();
        private readonly object _sync = new();

        public bool IsRunning { get; protected set; }

        public FunctionBlockCatalog Catalog => _catalog;

        protected VisualSimulationServiceBase(
            ILogger<T>? logger = null,
            IConsoleLoggerService? consoleLoggerService = null,
            IConnectionManager? connectionManager = null)
        {
            _logger = logger ?? NullLogger<T>.Instance;
            _consoleLoggerService = consoleLoggerService;
            _connectionManager = connectionManager;
            _catalog = CreateCatalog();
            _engine = new ExecutionEngine(_catalog, null, _consoleLoggerService);
            _dataStore = DataStoreFactory.CreateDefaultDataStore();
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

            // Industrial devices
            catalog.Register(new ValveBlock());

            return catalog;
        }

        public void Start(VisualNodeEditorConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            lock (_sync)
            {
                _config = config;
                _lastGraphHash = 0;
                IsRunning = true;
            }

            OnStartTimer();
            _logger.LogInformation("Visual simulation started");
        }

        public void Stop()
        {
            lock (_sync)
            {
                IsRunning = false;
            }

            OnStopTimer();

            if (_config?.Nodes != null)
            {
                foreach (var node in _config.Nodes)
                {
                    node.CurrentValue = false;
                    node.ShowLiveValues = false;
                }
            }

            _nodeValueCache.Clear();
            _lastNodeUpdate.Clear();

            lock (_sync)
            {
                _lastGraphHash = 0;
            }

            _logger.LogInformation("Visual simulation stopped");
        }

        protected abstract void OnStartTimer();
        protected abstract void OnStopTimer();

        /// <summary>
        /// Logs an error from a host timer callback. Exposed to the derived
        /// classes so they can use the same logger category.
        /// </summary>
        protected void LogError(Exception ex)
        {
            _logger.LogError(ex, "Error updating visual node values");
        }

        public void UpdateNodeValues()
        {
            VisualNodeEditorConfig? currentConfig;
            lock (_sync)
            {
                if (!IsRunning || _config?.Nodes == null) return;
                currentConfig = _config;
            }

            EnsureGraphLoaded(currentConfig);

            var dataStore = GetEffectiveDataStore();
            lock (dataStore)
            {
                _engine.Execute(dataStore);
            }

            var now = DateTime.UtcNow;

            foreach (var node in currentConfig.Nodes)
            {
                var simulationNode = _engine.ExecutionOrder.FirstOrDefault(n => n.Id == node.Id);
                if (simulationNode == null) continue;

                double? liveDouble = null;
                SimulationNodeResult result;
                if (simulationNode.OutputValues.TryGetValue("Output", out var value))
                {
                    result = SimulationNodeResult.FromInt(value.AsInt32());
                    try { liveDouble = value.AsReal(); } catch { liveDouble = value.AsInt32(); }
                }
                else
                {
                    result = SimulationNodeResult.FromBool(false);
                    liveDouble = 0;
                }

                var oldValue = _nodeValueCache.GetValueOrDefault(node.Id, false);
                var lastUpd = _lastNodeUpdate.GetValueOrDefault(node.Id, DateTime.MinValue);
                var shouldUpdate = result.BoolValue != oldValue || (now - lastUpd).TotalMilliseconds > 500;

                if (shouldUpdate)
                {
                    _consoleLoggerService?.Log($"Simulation: {node.Name} ({node.Id}) changed to {result.IntValue} ({(result.BoolValue ? "true" : "false")})");
                    node.CurrentValue = result.BoolValue;
                    node.IntValue = result.IntValue;

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
        }

        public bool GetNodeValue(string nodeId)
        {
            return _nodeValueCache.GetValueOrDefault(nodeId, false);
        }

        public void WriteNodeValue(string nodeId, double value)
        {
            if (_config?.Nodes == null) return;

            var node = _config.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return;

            var address = IsInput1Bound(node.ElementType)
                ? node.Input1Address
                : node.OutputAddress;

            if (address == null || address.Address <= 0)
            {
                _logger.LogDebug("WriteNodeValue skipped: node {NodeId} has no configured address", nodeId);
                return;
            }

            var dataStore = GetEffectiveDataStore();
            var modbusAddress = address.Address;
            lock (dataStore)
            {
                switch (address.Area)
                {
                    case PlcArea.HoldingRegister:
                        if (modbusAddress < dataStore.HoldingRegisters.Count)
                            dataStore.HoldingRegisters[modbusAddress] = ToClampedUInt16(value);
                        break;

                    case PlcArea.InputRegister:
                        if (modbusAddress < dataStore.InputRegisters.Count)
                            dataStore.InputRegisters[modbusAddress] = ToClampedUInt16(value);
                        break;

                    case PlcArea.Coil:
                        if (modbusAddress < dataStore.CoilDiscretes.Count)
                            dataStore.CoilDiscretes[modbusAddress] = Math.Abs(value) > 0.0001;
                        break;

                    case PlcArea.DiscreteInput:
                        if (modbusAddress < dataStore.InputDiscretes.Count)
                            dataStore.InputDiscretes[modbusAddress] = Math.Abs(value) > 0.0001;
                        break;
                }
            }
        }

        /// <summary>
        /// Returns the active Modbus server's data store when available, otherwise the
        /// fallback local data store used for offline simulation.
        /// </summary>
        private DataStore GetEffectiveDataStore()
        {
            if (_connectionManager?.ActiveService is { } service)
            {
                return service.GetDataStore() ?? _dataStore;
            }

            return _dataStore;
        }

        private static ushort ToClampedUInt16(double value)
        {
            var clamped = Math.Clamp(Math.Round(value), 0, ushort.MaxValue);
            return (ushort)clamped;
        }

        private void EnsureGraphLoaded(VisualNodeEditorConfig config)
        {
            if (config.Nodes == null) return;

            int currentHash;
            lock (_sync)
            {
                currentHash = ComputeGraphHash(config);

                if (_lastGraphHash == currentHash)
                {
                    return;
                }
            }

            var simulationNodes = config.Nodes.Select(MapToSimulationNode).ToList();
            var simulationConnections = (config.Connections ?? Enumerable.Empty<NodeConnection>()).Select(MapToSimulationConnection).ToList();

            _engine.LoadGraph(simulationNodes, simulationConnections);

            lock (_sync)
            {
                _lastGraphHash = currentHash;
            }

            _logger.LogDebug("Rebuilt simulation graph: {Count} nodes", _engine.ExecutionOrder.Count);
        }

        private int ComputeGraphHash(VisualNodeEditorConfig config)
        {
            var hash = new HashCode();

            foreach (var node in config.Nodes)
            {
                hash.Add(node.Id);
                hash.Add(node.Name);
                hash.Add((int)node.ElementType);

                AddAddressHash(ref hash, node.Input1Address);
                AddAddressHash(ref hash, node.Input2Address);
                AddAddressHash(ref hash, node.OutputAddress);

                foreach (var (portName, address) in node.OutputPortBindings)
                {
                    hash.Add(portName);
                    AddAddressHash(ref hash, address);
                }

                hash.Add(node.TimerPresetMs);
                hash.Add(node.CounterPreset);
                hash.Add(node.CompareValue);
                hash.Add(node.SetDominant);
                hash.Add(node.Waveform);
                hash.Add(node.PeriodMs);
                hash.Add(node.Amplitude.GetHashCode());
                hash.Add(node.Offset.GetHashCode());

                hash.Add(node.ValveTravelTimeMs);
                hash.Add(node.ValveNormallyOpen);
            }

            if (config.Connections != null)
            {
                foreach (var connection in config.Connections)
                {
                    hash.Add(connection.SourceNodeId);
                    hash.Add(connection.SourceConnector);
                    hash.Add(connection.TargetNodeId);
                    hash.Add(connection.TargetConnector);
                }
            }

            return hash.ToHashCode();

            static void AddAddressHash(ref HashCode hash, PlcAddressReference? address)
            {
                if (address == null)
                {
                    hash.Add(-1);
                    return;
                }

                hash.Add(address.Address);
                hash.Add((int)address.Area);
                hash.Add(address.Not);
            }
        }

        private SimulationNode MapToSimulationNode(VisualNode visualNode)
        {
            var block = _catalog.Create(visualNode.ElementType.ToString());
            var node = new SimulationNode(visualNode.Id, visualNode.Name, block);

            if (IsInput1Bound(visualNode.ElementType) && visualNode.Input1Address?.Address >= 0)
                node.InputBindings["Input1"] = visualNode.Input1Address;

            if (IsInput2Bound(visualNode.ElementType))
            {
                if (visualNode.Input1Address?.Address >= 0)
                    node.InputBindings["Input1"] = visualNode.Input1Address;

                // Input2 can also come from a bound Modbus address when no wire is connected.
                if (visualNode.Input2Address?.Address >= 0)
                    node.InputBindings["Input2"] = visualNode.Input2Address;
            }

            if (IsOutputBound(visualNode.ElementType) && visualNode.OutputAddress?.Address >= 0)
                node.OutputBindings["Output"] = visualNode.OutputAddress;

            // Secondary output ports (e.g. Fault, SpeedFeedback) for multi-output blocks.
            foreach (var (portName, address) in visualNode.OutputPortBindings)
            {
                if (address?.Address >= 0)
                    node.OutputBindings[portName] = address;
            }

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

            if (visualNode.ValveTravelTimeMs != 0)
                node.Parameters["ValveTravelTimeMs"] = visualNode.ValveTravelTimeMs;

            node.Parameters["ValveNormallyOpen"] = visualNode.ValveNormallyOpen;

            return node;
        }

        protected static bool IsInput1Bound(PlcElementType elementType)
        {
            return elementType is PlcElementType.Input or PlcElementType.InputBool or PlcElementType.InputInt
                or PlcElementType.Valve;
        }

        protected static bool IsInput2Bound(PlcElementType elementType)
        {
            return elementType is PlcElementType.Valve
                or PlcElementType.COMPARE_EQ
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

        protected static bool IsOutputBound(PlcElementType elementType)
        {
            return elementType is PlcElementType.Output or PlcElementType.OutputBool or PlcElementType.OutputInt or PlcElementType.Valve;
        }

        private static SimulationConnection MapToSimulationConnection(NodeConnection connection)
        {
            var sourcePort = string.IsNullOrEmpty(connection.SourceConnector) ? "Output" : connection.SourceConnector;
            var targetPort = string.IsNullOrEmpty(connection.TargetConnector) ? "Input1" : connection.TargetConnector;

            return new SimulationConnection(
                connection.SourceNodeId,
                sourcePort,
                connection.TargetNodeId,
                targetPort);
        }

        public abstract void Dispose();
    }
}
