using System;
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
using ModbusForge.Services;

namespace ModbusForge.Core.Xef
{
    /// <summary>
    /// A self-contained, start/stop-able runner that executes a <see cref="VisualNodeEditorConfig"/>
    /// (the graph the XEF translator produces) cycle-by-cycle against a shared <see cref="DataStore"/>.
    ///
    /// <para>It reuses the existing simulation engine (<see cref="ExecutionEngine"/> +
    /// <see cref="FunctionBlockCatalog"/>) and converts the visual graph in exactly the same way
    /// as <c>VisualSimulationServiceBase</c>: the same positional port mapping (Input1/Input2 onto the
    /// declared input ports, generic "Output" onto the primary output port), the same descriptor-gated
    /// address bindings, and the same declarative parameter population via <see cref="ParameterAccess"/>.
    /// </para>
    ///
    /// <para>Live values: the UI reads <see cref="GetLatestNodeValues"/> on its own refresh timer (poll).
    /// The returned dictionary maps node id to the node's primary output value as a double (bool ports
    /// surface as 1/0). A snapshot is taken after every completed cycle, so the UI never touches engine
    /// state directly. The shared <see cref="DataStore"/> is exposed via <see cref="DataStore"/> so the UI
    /// can read the IO cells directly; callers must lock on it while reading/writing if the runner is running.
    /// </para>
    /// </summary>
    public sealed class PlcSimulationRunner : IDisposable
    {
        /// <summary>Default scan period in milliseconds.</summary>
        private const int DefaultScanIntervalMs = 100;

        /// <summary>Minimum supported scan period in milliseconds (matches the simulation service).</summary>
        private const int MinScanIntervalMs = 10;

        /// <summary>How long Stop waits for a running cycle before returning anyway.</summary>
        private static readonly TimeSpan StopTickWaitTimeout = TimeSpan.FromSeconds(10);

        private readonly ExecutionEngine _engine;
        private readonly DataStore _dataStore;
        private readonly int _scanIntervalMs;
        private readonly ILogger<PlcSimulationRunner>? _logger;
        private readonly List<SimulationNode> _simNodes;

        private readonly SemaphoreSlim _tickLock = new(1, 1);
        private int _tickLockDisposed;
        private System.Threading.Timer? _timer;
        private readonly object _sync = new();
        private bool _isRunning;
        private int _cycleCount;

        private readonly Dictionary<string, double> _latestNodeValues = new(StringComparer.Ordinal);
        private int _disposed;

        /// <summary>
        /// Creates a runner over the supplied visual graph and shared data store.
        /// </summary>
        /// <param name="config">The visual node editor config (the graph the XEF translator emits).</param>
        /// <param name="dataStore">The shared Modbus memory bus the engine reads/writes.</param>
        /// <param name="scanIntervalMs">Scan period in milliseconds (clamped to the supported range).</param>
        /// <param name="logger">Optional logger.</param>
        public PlcSimulationRunner(
            VisualNodeEditorConfig config,
            DataStore dataStore,
            int scanIntervalMs = DefaultScanIntervalMs,
            ILogger<PlcSimulationRunner>? logger = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (dataStore == null) throw new ArgumentNullException(nameof(dataStore));

            _dataStore = dataStore;
            _scanIntervalMs = Math.Clamp(scanIntervalMs > 0 ? scanIntervalMs : DefaultScanIntervalMs, MinScanIntervalMs, int.MaxValue);
            _logger = logger;

            var catalog = CatalogProvider.Shared;
            _engine = new ExecutionEngine(catalog, logger is { } l ? new LoggerAdapter(l) : null, null);

            _simNodes = BuildGraph(config);
            _engine.LoadGraph(_simNodes, MapConnections(config));

            if (_engine.CycleNodeIds.Count > 0)
            {
                _logger?.LogWarning("PlcSimulationRunner graph has {Count} node(s) in cycles that will not be evaluated: {NodeIds}",
                    _engine.CycleNodeIds.Count, string.Join(", ", _engine.CycleNodeIds));
            }
        }

        /// <summary>
        /// The shared data store the runner reads inputs from and writes outputs to.
        /// The UI may read the IO cells directly; while the runner is running the caller
        /// should lock on this instance for a consistent snapshot (the engine ticks lock on it).
        /// </summary>
        public DataStore DataStore => _dataStore;

        /// <summary>True while a scan cycle is being driven by the runner's timer.</summary>
        public bool IsRunning
        {
            get { lock (_sync) return _isRunning; }
        }

        /// <summary>Number of scan cycles completed so far (resets each Start).</summary>
        public int CycleCount
        {
            get { lock (_sync) return _cycleCount; }
        }

        /// <summary>Fired after each completed scan cycle (on the runner's timer thread).</summary>
        public event EventHandler? CycleCompleted;

        /// <summary>
        /// Starts the runner: the engine is executed once per <c>scanIntervalMs</c> on a background
        /// timer thread. Safe to call repeatedly while already running (no-op in that case).
        /// </summary>
        public void Start()
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                if (_isRunning) return;
                _isRunning = true;
                _cycleCount = 0;
                ClearLatestValues();
            }

            _timer = new System.Threading.Timer(OnTick, null, 0, _scanIntervalMs);
            _logger?.LogInformation("PlcSimulationRunner started (scan {IntervalMs} ms, {Nodes} nodes)", _scanIntervalMs, _simNodes.Count);
        }

        /// <summary>
        /// Stops the runner and waits for any in-flight cycle to finish. Safe to call repeatedly
        /// and after <see cref="Start"/> was never called.
        /// </summary>
        public void Stop()
        {
            System.Threading.Timer? timer;
            lock (_sync)
            {
                timer = _timer;
                _timer = null;
                _isRunning = false;
            }

            timer?.Dispose();

            // Wait for a running cycle so the latest-values snapshot is stable when the
            // UI polls after Stop. If the wait times out (a cycle stuck on a contended
            // store lock) return anyway - no further cycles will start.
            var acquired = _tickLock.Wait(StopTickWaitTimeout);
            if (acquired)
            {
                try
                {
                    ClearLatestValues();
                }
                finally
                {
                    _tickLock.Release();
                }
            }

            _logger?.LogInformation("PlcSimulationRunner stopped after {Cycles} cycles", CycleCount);
        }

        /// <summary>
        /// Snapshot of each node's primary output value from the last completed cycle, keyed by
        /// visual node id. Boolean outputs surface as 1/0, numeric outputs as their value. Nodes
        /// that have not yet produced an output are absent from the dictionary. Safe to call from
        /// any thread at any time (including after Stop/Dispose).
        /// </summary>
        public IReadOnlyDictionary<string, double> GetLatestNodeValues()
        {
            lock (_sync)
            {
                return new Dictionary<string, double>(_latestNodeValues, StringComparer.Ordinal);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            Stop();

            if (Interlocked.Exchange(ref _tickLockDisposed, 1) == 0)
                _tickLock.Dispose();
        }

        private void OnTick(object? state)
        {
            ThrowIfDisposed();

            // At most one cycle at a time; drop overlapping ticks (the engine is not thread-safe).
            if (!_tickLock.Wait(0))
                return;

            try
            {
                if (!IsRunning) return;

                lock (_dataStore)
                {
                    _engine.Execute(_dataStore);
                }

                UpdateLatestValues();

                lock (_sync)
                {
                    _cycleCount++;
                }

                CycleCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error executing simulation cycle");
            }
            finally
            {
                _tickLock.Release();
            }
        }

        /// <summary>
        /// Copies each evaluated node's primary output value into the poll snapshot. Runs on the
        /// tick thread between cycles, so the engine's node state is stable here.
        /// </summary>
        private void UpdateLatestValues()
        {
            var snapshot = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var node in _engine.ExecutionOrder)
            {
                var primaryPort = BlockPorts.PrimaryOutput(node.Block.Ports);
                if (primaryPort == null) continue;

                if (!node.OutputValues.TryGetValue(primaryPort, out var value)) continue;

                double liveDouble;
                try
                {
                    liveDouble = value.AsReal();
                }
                catch
                {
                    liveDouble = value.AsInt32();
                }

                snapshot[node.Id] = liveDouble;
            }

            lock (_sync)
            {
                _latestNodeValues.Clear();
                foreach (var (id, value) in snapshot)
                    _latestNodeValues[id] = value;
            }
        }

        private void ClearLatestValues()
        {
            lock (_sync)
            {
                _latestNodeValues.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(PlcSimulationRunner));
        }

        /// <summary>
        /// Builds the engine graph from the visual config using the same conversion rules as
        /// <c>VisualSimulationServiceBase</c>: positional input-port mapping, descriptor-gated
        /// address bindings, and declarative parameters via <see cref="ParameterAccess"/>.
        /// </summary>
        private static List<SimulationNode> BuildGraph(VisualNodeEditorConfig config)
        {
            var catalog = CatalogProvider.Shared;
            var nodes = new List<SimulationNode>(config.Nodes.Count);

            foreach (var visualNode in config.Nodes)
            {
                var block = CreateBlock(catalog, visualNode);
                var simNode = new SimulationNode(visualNode.Id, visualNode.Name, block)
                {
                    IsEnabled = visualNode.IsEnabled
                };
                ApplyNodeBindings(simNode, visualNode);
                ApplyParameters(simNode, visualNode);
                nodes.Add(simNode);
            }

            return nodes;
        }

        /// <summary>
        /// Instantiates the function block for a visual node. <see cref="PlcElementType.Unsupported"/>
        /// nodes (the XEF "no simulation equivalent" marker) are deliberately NOT registered in the
        /// catalog, so they get a minimal inline no-op passthrough block: the node stays in the graph
        /// so its connections remain valid and it shows on the canvas, but it produces no output.
        /// </summary>
        private static IFunctionBlock CreateBlock(FunctionBlockCatalog catalog, VisualNode visualNode)
        {
            if (visualNode.ElementType == PlcElementType.Unsupported)
            {
                return new UnsupportedPassthroughBlock(visualNode.Name);
            }

            var typeId = visualNode.ElementType.ToString();
            if (!catalog.Contains(typeId))
            {
                // Defensive: unknown types also become inert passthroughs instead of failing the whole graph.
                return new UnsupportedPassthroughBlock(visualNode.Name);
            }

            return catalog.Create(typeId);
        }

        private static List<SimulationConnection> MapConnections(VisualNodeEditorConfig config)
        {
            return (config.Connections ?? Enumerable.Empty<NodeConnection>()).Select(MapToSimulationConnection).ToList();
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
        /// Binds visual addresses to engine ports (same mapping rules as
        /// <c>VisualSimulationServiceBase.ApplyNodeBindings</c>, with one deliberate difference:
        /// the primary output binding is honored for ANY node that carries an OutputAddress,
        /// not only descriptor-gated types). The XEF translator sets OutputAddress as its way of
        /// saying "write this block's output to tag X", so the runner honors it universally;
        /// input slots remain descriptor-gated, so only node types that expose address editors
        /// become Modbus-driven at the input side and everything else stays wire-driven.
        /// </summary>
        private static void ApplyNodeBindings(SimulationNode simulationNode, VisualNode visualNode)
        {
            simulationNode.InputBindings.Clear();
            simulationNode.OutputBindings.Clear();

            var primaryOutput = BlockPorts.PrimaryOutput(simulationNode.Block.Ports);

            var descriptor = NodeDescriptors.TryGet(visualNode.ElementType, out var d) ? d : null;

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
                    // Intentionally NOT descriptor-gated: an OutputAddress is always a live
                    // output binding here (see summary).
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
        /// Populates engine parameters from the block's declarative parameter list (copied from
        /// <c>VisualSimulationServiceBase.ApplyParameters</c>). Every declared parameter is set
        /// explicitly, so values of zero are honored instead of silently falling back to defaults.
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
        /// Builds the standard block catalog - the same registrations as
        /// <c>VisualSimulationServiceBase.CreateCatalog</c>, minus the XEF <c>Unsupported</c>
        /// marker (which is handled as an inline passthrough in <see cref="CreateBlock"/>).
        /// </summary>
        private sealed class CatalogProvider
        {
            private static readonly Lazy<FunctionBlockCatalog> SharedLazy = new(BuildCatalog);
            public static FunctionBlockCatalog Shared => SharedLazy.Value;

            private static FunctionBlockCatalog BuildCatalog()
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
        }

        /// <summary>
        /// Minimal no-op function block for nodes whose <see cref="PlcElementType"/> is the XEF
        /// <c>Unsupported</c> marker (or otherwise unknown). It exposes the generic three-slot port
        /// shape ("Input1"/"Input2"/"Output") so the engine's positional port mapping finds targets,
        /// but <c>Execute</c> writes nothing: the block is visibly present and its connections stay
        /// valid, while it produces no output value.
        /// </summary>
        private sealed class UnsupportedPassthroughBlock : IFunctionBlock
        {
            private static readonly BlockParameterDescriptor[] EmptyParameters = Array.Empty<BlockParameterDescriptor>();

            public UnsupportedPassthroughBlock(string nodeName)
            {
                DisplayName = string.IsNullOrWhiteSpace(nodeName) ? "Unsupported block" : $"{nodeName} (unsupported)";
            }

            public string TypeId => PlcElementType.Unsupported.ToString();
            public string DisplayName { get; }
            public string Category => "XEF";

            public IReadOnlyList<IPort> Ports { get; } = new List<IPort>
            {
                new PortDefinition("Input1", PortDirection.Input, SimulationDataType.Bool),
                new PortDefinition("Input2", PortDirection.Input, SimulationDataType.Bool),
                new PortDefinition("Output", PortDirection.Output, SimulationDataType.Bool)
            };

            public IReadOnlyList<BlockParameterDescriptor> Parameters => EmptyParameters;

            public void Execute(IExecutionContext context)
            {
                // Intentionally a no-op: the block is a visible placeholder with no simulation equivalent.
            }
        }
    }

    /// <summary>
    /// Adapts a typed <see cref="ILogger{T}"/> to the non-generic <see cref="ILogger"/> the
    /// <see cref="ExecutionEngine"/> constructor accepts, keeping the runner's category.
    /// </summary>
    internal sealed class LoggerAdapter : ILogger
    {
        private readonly ILogger _inner;

        public LoggerAdapter(ILogger inner)
        {
            _inner = inner;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
