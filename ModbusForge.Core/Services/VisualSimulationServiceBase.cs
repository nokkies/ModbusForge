using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Data;
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
    /// Shared engine and graph-mapping logic for the visual simulation service.
    /// Derived types only supply a platform-specific timer and (for WPF) marshalling.
    ///
    /// Design notes:
    /// - SimulationNode instances are REUSED across graph rebuilds (keyed by visual node id),
    ///   so block state (timers, counters, latches, valve positions) survives edits such as
    ///   renaming a node or tweaking a parameter while the simulation runs.
    /// - Parameters are populated from the block's declarative <c>IFunctionBlock.Parameters</c>
    ///   list (see <see cref="ParameterAccess"/>), never via per-type hard-coded switches.
    /// </summary>
    public abstract class VisualSimulationServiceBase<T> : IVisualSimulationService, IDisposable
        where T : class
    {
        /// <summary>
        /// Default scan period in milliseconds.
        /// </summary>
        public const int DefaultScanIntervalMs = 100;

        /// <summary>Minimum supported scan period in milliseconds.</summary>
        public const int MinScanIntervalMs = 10;

        /// <summary>Maximum supported scan period in milliseconds.</summary>
        public const int MaxScanIntervalMs = 10000;

        private readonly ILogger<T> _logger;
        private readonly IConsoleLoggerService? _consoleLoggerService;
        private readonly IConnectionManager? _connectionManager;
        private readonly FunctionBlockCatalog _catalog;
        private readonly ExecutionEngine _engine;
        private readonly DataStore _dataStore;
        private readonly Dictionary<string, SimulationNode> _simNodes = new(StringComparer.Ordinal);

        private VisualNodeEditorConfig? _config;
        private int _lastGraphHash;
        private string? _lastCycleKey;
        private readonly ConcurrentDictionary<string, bool> _nodeValueCache = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastNodeUpdate = new();
        private readonly object _sync = new();

        /// <summary>
        /// Ensures at most one simulation tick executes at a time. The host timer
        /// (System.Timers.Timer, AutoReset) can raise the next tick while a slow
        /// previous tick is still running, and the effective data store can change
        /// between ticks (connect/disconnect), so the per-store lock alone cannot
        /// keep two ticks from running the shared engine concurrently.
        /// </summary>
        private readonly SemaphoreSlim _tickLock = new(1, 1);
        private int _tickLockDisposed;

        /// <summary>How long Stop waits for a running tick before resetting anyway.</summary>
        private static readonly TimeSpan StopTickWaitTimeout = TimeSpan.FromSeconds(10);

        public bool IsRunning { get; protected set; }

        public FunctionBlockCatalog Catalog => _catalog;

        /// <summary>
        /// Current scan period in milliseconds.
        /// </summary>
        public int ScanIntervalMs { get; private set; } = DefaultScanIntervalMs;

        /// <summary>
        /// Raised after a graph rebuild when the set of cycle-locked (non-evaluated) nodes changes.
        /// </summary>
        public event Action<IReadOnlyList<string>>? CyclesChanged;

        protected VisualSimulationServiceBase(
            ILogger<T>? logger = null,
            IConsoleLoggerService? consoleLoggerService = null,
            IConnectionManager? connectionManager = null)
        {
            _logger = logger ?? NullLogger<T>.Instance;
            _consoleLoggerService = consoleLoggerService;
            _connectionManager = connectionManager;
            _catalog = CreateCatalog();
            // Pass a real logger so engine diagnostics (cycle detection, dropped connections)
            // are visible in the console.
            _engine = new ExecutionEngine(_catalog, _logger, _consoleLoggerService);
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

            // Real (double) comparators and math
            foreach (var operation in Enum.GetValues<ComparisonOperation>())
                catalog.Register(new CompareBlock(operation, isReal: true));
            foreach (var operation in Enum.GetValues<MathOperation>())
                catalog.Register(new MathBlock(operation, isReal: true));

            // Sources
            catalog.Register(new SignalGeneratorBlock());
            catalog.Register(new SignalGeneratorRealBlock());

            // Industrial devices
            catalog.Register(new ValveBlock());
            catalog.Register(new MotorDolBlock());
            catalog.Register(new VsdBlock());

            // Signal conditioning
            catalog.Register(new ScaleBlock());
            catalog.Register(new EdgeDetectBlock());
            catalog.Register(new MovingAverageBlock());

            return catalog;
        }

        public void Start(VisualNodeEditorConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (config.ScanIntervalMs > 0)
                SetScanIntervalMs(config.ScanIntervalMs);

            lock (_sync)
            {
                _config = config;
                _lastGraphHash = 0;
                _lastCycleKey = null;
                IsRunning = true;
            }

            OnStartTimer();
            _logger.LogInformation("Visual simulation started (scan {IntervalMs} ms, store: {StoreMode})", ScanIntervalMs, StoreMode);
        }

        public void Stop()
        {
            lock (_sync)
            {
                IsRunning = false;
            }

            OnStopTimer();

            // Wait for a running tick so the reset below cannot be clobbered by a
            // half-finished node update. If the wait times out (a tick stuck on a
            // contended store lock), reset anyway - IsRunning is already false, so
            // no further ticks will start.
            var acquired = _tickLock.Wait(StopTickWaitTimeout);
            try
            {
                if (_config?.Nodes != null)
                {
                    VisualNode[] nodes;
                    try
                    {
                        nodes = _config.Nodes.ToArray();
                    }
                    catch (InvalidOperationException)
                    {
                        nodes = Array.Empty<VisualNode>();
                    }

                    foreach (var node in nodes)
                    {
                        node.CurrentValue = false;
                        node.ShowLiveValues = false;
                        node.SetSecondaryOutputs(Array.Empty<KeyValuePair<string, string>>());
                        node.SetErrorText(null);
                    }
                }

                _nodeValueCache.Clear();
                _lastNodeUpdate.Clear();
            }
            finally
            {
                if (acquired)
                    _tickLock.Release();
            }

            lock (_sync)
            {
                _lastGraphHash = 0;
                _lastCycleKey = null;
            }

            _logger.LogInformation("Visual simulation stopped");
        }

        protected abstract void OnStartTimer();
        protected abstract void OnStopTimer();

        /// <summary>
        /// Sets the simulation scan period (clamped to the supported range).
        /// Takes effect immediately, including while running.
        /// </summary>
        public void SetScanIntervalMs(int ms)
        {
            var clamped = Math.Clamp(ms, MinScanIntervalMs, MaxScanIntervalMs);
            if (ScanIntervalMs == clamped) return;
            ScanIntervalMs = clamped;
            OnScanIntervalChanged();
        }

        /// <summary>
        /// Called after <see cref="ScanIntervalMs"/> changes; derived types apply it to their timer.
        /// </summary>
        protected virtual void OnScanIntervalChanged()
        {
        }

        /// <summary>
        /// "device" when bound addresses read/write the connected Modbus server's store,
        /// "local" when they use the private offline store (client mode or disconnected).
        /// </summary>
        public string StoreMode
        {
            get
            {
                var store = GetEffectiveDataStore();
                return ReferenceEquals(store, _dataStore) ? "local" : "device";
            }
        }

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
            // At most one tick at a time; drop overlapping ticks (see _tickLock).
            if (!_tickLock.Wait(0))
                return;

            try
            {
                UpdateNodeValuesCore();
            }
            finally
            {
                _tickLock.Release();
            }
        }

        private void UpdateNodeValuesCore()
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

            // Snapshot the node list: the UI thread may be mutating the collection
            // (add/remove) while this runs on the timer thread. If we lose the race,
            // skip the tick instead of throwing.
            VisualNode[] nodes;
            try
            {
                nodes = currentConfig.Nodes.ToArray();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            var simById = _engine.ExecutionOrder.ToDictionary(n => n.Id, StringComparer.Ordinal);
            var now = DateTime.UtcNow;

            foreach (var node in nodes)
            {
                if (!simById.TryGetValue(node.Id, out var simulationNode)) continue;

                var (primaryPort, primaryValue) = GetPrimaryOutput(simulationNode);

                double? liveDouble = null;
                SimulationNodeResult result;
                if (primaryValue is { } value)
                {
                    result = SimulationNodeResult.FromInt(value.AsInt32());
                    try { liveDouble = value.AsReal(); } catch { liveDouble = value.AsInt32(); }
                }
                else
                {
                    result = SimulationNodeResult.FromBool(false);
                    liveDouble = 0;
                }

                // Secondary outputs (Fault, SpeedFeedback, ...) for the node's display chip.
                var secondaryOutputs = simulationNode.Block.Ports
                    .Where(p => p.Direction == PortDirection.Output && !string.Equals(p.Name, primaryPort, StringComparison.Ordinal))
                    .Select(p => new KeyValuePair<string, string>(
                        p.Name,
                        simulationNode.OutputValues.TryGetValue(p.Name, out var v) ? FormatValue(v) : "n/a"))
                    .ToList();
                node.SetSecondaryOutputs(secondaryOutputs);

                // Surface per-block failures (and clear them on recovery). Nodes in
                // cycles are not in the engine's execution order, so they get no
                // runtime error here; the editor marks those separately.
                node.SetErrorText(simulationNode.LastError);

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

        /// <summary>
        /// Picks the port used for the node's primary live value: the block's primary
        /// output port ("Output" when declared, otherwise the first declared output,
        /// e.g. the VSD's "Running").
        /// </summary>
        private static (string? Port, ISimulationValue? Value) GetPrimaryOutput(SimulationNode simulationNode)
        {
            var primaryPort = BlockPorts.PrimaryOutput(simulationNode.Block.Ports) ?? "Output";
            var value = simulationNode.OutputValues.TryGetValue(primaryPort, out var v) ? v : null;
            return (primaryPort, value);
        }

        private static string FormatValue(ISimulationValue value)
        {
            return value.DataType switch
            {
                SimulationDataType.Bool => value.AsBool() ? "ON" : "OFF",
                SimulationDataType.Real => value.AsReal().ToString("G5", System.Globalization.CultureInfo.InvariantCulture),
                _ => value.AsString()
            };
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

            // For nodes whose primary writable address is the input (I/O, devices),
            // the user's live edit drives the input. For computed blocks (math, compare,
            // timers, ...) the live edit writes the output address instead.
            var address = IsInput1Writable(node.ElementType)
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
                try
                {
                    currentHash = ComputeGraphHash(config);
                }
                catch (InvalidOperationException)
                {
                    // The UI thread is mutating the node collection right now;
                    // skip this tick instead of throwing.
                    return;
                }

                if (_lastGraphHash == currentHash)
                {
                    return;
                }
            }

            List<VisualNode> visualNodes;
            try
            {
                visualNodes = config.Nodes.ToList();
            }
            catch (InvalidOperationException)
            {
                // The UI thread is mutating the collection right now; skip this tick.
                return;
            }

            // Reuse existing SimulationNode instances (preserving block state) and only
            // create fresh ones for new nodes or nodes whose block type changed.
            var simulationNodes = new List<SimulationNode>(visualNodes.Count);
            var currentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var visualNode in visualNodes)
            {
                currentIds.Add(visualNode.Id);
                simulationNodes.Add(GetOrCreateSimNode(visualNode));
            }

            foreach (var staleId in _simNodes.Keys.Where(id => !currentIds.Contains(id)).ToList())
            {
                _simNodes.Remove(staleId);
            }

            var simulationConnections = (config.Connections ?? Enumerable.Empty<NodeConnection>()).Select(MapToSimulationConnection).ToList();

            _engine.LoadGraph(simulationNodes, simulationConnections);

            lock (_sync)
            {
                _lastGraphHash = currentHash;
            }

            NotifyCyclesIfNeeded();
            _logger.LogDebug("Rebuilt simulation graph: {Count} nodes", _engine.ExecutionOrder.Count);
        }

        private SimulationNode GetOrCreateSimNode(VisualNode visualNode)
        {
            var expectedTypeId = visualNode.ElementType.ToString();

            if (_simNodes.TryGetValue(visualNode.Id, out var existing) && existing.Block.TypeId == expectedTypeId)
            {
                existing.Name = visualNode.Name;
                existing.IsEnabled = visualNode.IsEnabled;
                ApplyNodeBindings(existing, visualNode);
                ApplyParameters(existing, visualNode);
                return existing;
            }

            var fresh = new SimulationNode(visualNode.Id, visualNode.Name, _catalog.Create(expectedTypeId));
            fresh.IsEnabled = visualNode.IsEnabled;
            ApplyNodeBindings(fresh, visualNode);
            ApplyParameters(fresh, visualNode);

            _simNodes[visualNode.Id] = fresh;
            return fresh;
        }

        private void NotifyCyclesIfNeeded()
        {
            var ids = _engine.CycleNodeIds;
            var key = string.Join("|", ids);
            if (key == _lastCycleKey) return;
            _lastCycleKey = key;

            if (ids.Count > 0)
            {
                _logger.LogWarning("Simulation graph has {Count} node(s) in cycles that will not be evaluated: {NodeIds}",
                    ids.Count, string.Join(", ", ids));
            }

            CyclesChanged?.Invoke(ids);
        }

        private int ComputeGraphHash(VisualNodeEditorConfig config)
        {
            var hash = new HashCode();

            foreach (var node in config.Nodes)
            {
                hash.Add(node.Id);
                hash.Add(node.Name);
                hash.Add((int)node.ElementType);
                hash.Add(node.IsEnabled);

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
                hash.Add(node.CompareValueReal.GetHashCode());
                hash.Add(node.SetDominant);
                hash.Add(node.Waveform);
                hash.Add(node.PeriodMs);
                hash.Add(node.Amplitude.GetHashCode());
                hash.Add(node.Offset.GetHashCode());

                hash.Add(node.ValveTravelTimeMs);
                hash.Add(node.ValveNormallyOpen);
                hash.Add(node.ValveLatching);
                hash.Add(node.MotorDolRunDelayMs);
                hash.Add(node.VsdMaxSpeed.GetHashCode());
                hash.Add(node.VsdRampUpMs);
                hash.Add(node.VsdRampDownMs);
                hash.Add(node.VsdAtSpeedTolerance.GetHashCode());
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

        /// <summary>
        /// Binds visual addresses to engine ports declaratively. Input addresses map
        /// positionally onto the declared input ports and outputs map to the node's
        /// OutputAddress (primary port) or named OutputPortBindings. Which slots are live is
        /// driven by the NodeDescriptor's address flags, so only node types that expose
        /// address editors in the UI become Modbus-bound; everything else stays wire-driven.
        /// </summary>
        private void ApplyNodeBindings(SimulationNode simulationNode, VisualNode visualNode)
        {
            simulationNode.InputBindings.Clear();
            simulationNode.OutputBindings.Clear();

            // The primary output ("Output" when present, else the first declared output,
            // e.g. the VSD's "Running") is addressed via the node's main OutputAddress;
            // the remaining outputs use the per-port bindings.
            var primaryOutput = BlockPorts.PrimaryOutput(simulationNode.Block.Ports);

            // Address bindings are only honored for slots the node type exposes in the UI
            // (descriptor flags). Other node types stay wire-driven, so their default
            // (unedited) address references never become live Modbus bindings.
            var descriptor = NodeDescriptors.TryGet(visualNode.ElementType, out var d) ? d : null;

            // Input addresses bind positionally onto the declared input ports: the editor's
            // "Input1"/"Input2" slots map to the block's first/second input port, whatever
            // its declared name ("Start", "Run", ...).
            var inputPorts = BlockPorts.Inputs(simulationNode.Block.Ports);
            if (descriptor?.HasInput1Address == true && inputPorts.Count > 0 &&
                visualNode.Input1Address is { } input1 && input1.Address >= 0)
            {
                simulationNode.InputBindings[inputPorts[0].Name] = input1;
            }
            if (descriptor?.HasInput2Address == true && inputPorts.Count > 1 &&
                visualNode.Input2Address is { } input2 && input2.Address >= 0)
            {
                simulationNode.InputBindings[inputPorts[1].Name] = input2;
            }

            foreach (var port in simulationNode.Block.Ports)
            {
                if (port.Direction != PortDirection.Output)
                    continue;

                if (port.Name == primaryOutput)
                {
                    if (descriptor?.HasOutputAddress != true)
                        continue;

                    if (visualNode.OutputAddress is { } primary && primary.Address >= 0)
                        simulationNode.OutputBindings[port.Name] = primary;
                }
                else if (visualNode.OutputPortBindings.TryGetValue(port.Name, out var named) && named.Address >= 0)
                {
                    simulationNode.OutputBindings[port.Name] = named;
                }
            }
        }

        /// <summary>
        /// Populates engine parameters from the block's declarative parameter list.
        /// Every declared parameter is set explicitly (no sentinel checks), so values of
        /// zero are honored instead of silently falling back to defaults.
        /// </summary>
        private static void ApplyParameters(SimulationNode simulationNode, VisualNode visualNode)
        {
            simulationNode.Parameters.Clear();

            foreach (var spec in simulationNode.Block.Parameters)
            {
                if (ParameterAccess.TryGet(spec.Name) is { } access)
                    simulationNode.Parameters[spec.Name] = access.Getter(visualNode);
            }
        }

        /// <summary>
        /// Node types whose primary writable (live-edit) address is the Input1 address
        /// rather than the Output address. Kept deliberately narrow — it routes
        /// <see cref="WriteNodeValue"/>, so computed blocks (math, compare, timers, counters,
        /// logic, signal generators) write their output address instead.
        /// </summary>
        private static bool IsInput1Writable(PlcElementType elementType)
        {
            return elementType is PlcElementType.Input or PlcElementType.InputBool or PlcElementType.InputInt
                or PlcElementType.Valve or PlcElementType.MotorDol or PlcElementType.Vsd;
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

        /// <summary>
        /// Disposes the tick lock exactly once. Derived types must call this from
        /// their Dispose implementation, after <see cref="Stop"/>.
        /// </summary>
        protected void DisposeTickLock()
        {
            if (Interlocked.Exchange(ref _tickLockDisposed, 1) == 0)
                _tickLock.Dispose();
        }

        public abstract void Dispose();
    }
}
