using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Core.Plc;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Data;
using ModbusForge.Models;
using Timer = System.Timers.Timer;

namespace ModbusForge.Services
{
    /// <summary>
    /// Run/Stop for the PLC tab: scans the whole imported project in the PLC runtime
    /// (every FBD section of every task, as the controller would) against the Modbus
    /// data store, and shows the values of the section on screen on its blocks. Unlike
    /// the Simulation's engine it does not execute the canvas graph: the canvas only
    /// displays what the runtime computed.
    /// </summary>
    public sealed class PlcRuntimeService : IVisualSimulationService
    {
        /// <summary>The longest time one scan may account for, so a pause does not fire every timer at once.</summary>
        private static readonly TimeSpan MaxScanElapsed = TimeSpan.FromSeconds(1);

        private readonly ILogger<PlcRuntimeService> _logger;
        private readonly IConsoleLoggerService? _consoleLoggerService;
        private readonly IConnectionManager? _connectionManager;
        private readonly DataStore _localStore = DataStoreFactory.CreateDefaultDataStore();
        private readonly Timer _timer;
        private readonly SemaphoreSlim _tickLock = new(1, 1);
        private readonly Stopwatch _clock = new();
        private readonly HashSet<VisualNode> _decorated = new();
        private readonly object _sync = new();

        private PlcRuntime? _runtime;
        private Dictionary<string, PlcBlock> _blocksByNode = new(StringComparer.Ordinal);
        private VisualNodeEditorConfig? _config;
        private TimeSpan _lastTick;
        private int _disposed;

        public PlcRuntimeService(
            ILogger<PlcRuntimeService>? logger = null,
            IConsoleLoggerService? consoleLoggerService = null,
            IConnectionManager? connectionManager = null)
        {
            _logger = logger ?? NullLogger<PlcRuntimeService>.Instance;
            _consoleLoggerService = consoleLoggerService;
            _connectionManager = connectionManager;
            Catalog = VisualSimulationServiceBase<AvaloniaVisualSimulationService>.CreateCatalog();
            _timer = new Timer(ScanIntervalMs) { AutoReset = true };
            _timer.Elapsed += (_, _) => Tick();
        }

        public bool IsRunning { get; private set; }

        public FunctionBlockCatalog Catalog { get; }

        public int ScanIntervalMs { get; private set; } = VisualSimulationServiceBase<AvaloniaVisualSimulationService>.DefaultScanIntervalMs;

        /// <summary>The runtime of the loaded project, or null before an import.</summary>
        public PlcRuntime? Runtime => _runtime;

        public string StoreMode => ReferenceEquals(EffectiveStore, _localStore) ? "local" : "device";

        /// <summary>
        /// The data store scans read and write: the running Modbus server's, or a
        /// private offline store when no server is active.
        /// </summary>
        public DataStore CurrentDataStore => EffectiveStore;

        /// <summary>The PLC runtime has no graph cycles to report: loops go through variables.</summary>
        public event Action<IReadOnlyList<string>>? CyclesChanged
        {
            add { }
            remove { }
        }

        /// <summary>
        /// Loads the project an XEF import compiled; the next Run is a cold start (initial
        /// values applied). Null unloads.
        /// </summary>
        public void Load(PlcProject? project)
        {
            Stop();
            lock (_sync)
            {
                _runtime = project != null ? new PlcRuntime(project) : null;
                _blocksByNode = project?.Blocks
                    .Where(b => b.NodeId != null)
                    .GroupBy(b => b.NodeId!, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
                    ?? new Dictionary<string, PlcBlock>(StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// Starts scanning. <paramref name="config"/> is the editor's: its nodes are the
        /// section on screen, whose blocks get live values.
        /// </summary>
        public void Start(VisualNodeEditorConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.ScanIntervalMs > 0) SetScanIntervalMs(config.ScanIntervalMs);

            lock (_sync)
            {
                _config = config;
                if (_runtime == null)
                {
                    _logger.LogInformation("PLC Run with no project loaded; nothing to scan");
                }
                _runtime?.Start();
                _clock.Restart();
                _lastTick = TimeSpan.Zero;
                IsRunning = true;
            }

            _timer.Start();
            _logger.LogInformation("PLC runtime started (scan {IntervalMs} ms, store: {StoreMode})", ScanIntervalMs, StoreMode);
        }

        /// <summary>Stops scanning; variables keep their values (a later Run is a warm restart).</summary>
        public void Stop()
        {
            lock (_sync)
            {
                if (!IsRunning) return;
                IsRunning = false;
            }

            _timer.Stop();
            var acquired = _tickLock.Wait(TimeSpan.FromSeconds(10));
            try
            {
                foreach (var node in _decorated) node.PlcLive = null;
                _decorated.Clear();
            }
            finally
            {
                if (acquired) _tickLock.Release();
            }

            _logger.LogInformation("PLC runtime stopped");
        }

        public void SetScanIntervalMs(int ms)
        {
            ScanIntervalMs = Math.Clamp(ms,
                VisualSimulationServiceBase<AvaloniaVisualSimulationService>.MinScanIntervalMs,
                VisualSimulationServiceBase<AvaloniaVisualSimulationService>.MaxScanIntervalMs);
            _timer.Interval = ScanIntervalMs;
        }

        /// <summary>One scan now (the timer calls this every scan period).</summary>
        public void UpdateNodeValues() => Tick();

        /// <summary>
        /// One scan of <paramref name="elapsed"/> controller time, then the section on
        /// screen gets its values. Tests pass the elapsed time; the timer measures it.
        /// </summary>
        public void Tick(TimeSpan? elapsed = null)
        {
            // A timer callback already queued when Dispose ran must not touch the lock.
            if (Volatile.Read(ref _disposed) != 0) return;
            try
            {
                if (!_tickLock.Wait(0)) return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                PlcRuntime? runtime;
                VisualNodeEditorConfig? config;
                lock (_sync)
                {
                    if (!IsRunning || _runtime == null) return;
                    runtime = _runtime;
                    config = _config;
                }

                var now = _clock.Elapsed;
                var step = elapsed ?? now - _lastTick;
                _lastTick = now;
                if (step > MaxScanElapsed) step = MaxScanElapsed;

                var store = EffectiveStore;
                lock (store)
                {
                    runtime.Scan(store, step);
                }

                ShowValues(config);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "PLC scan failed");
            }
            finally
            {
                _tickLock.Release();
            }
        }

        /// <summary>The block a canvas node draws, when the loaded project has it.</summary>
        public PlcBlock? BlockFor(string nodeId)
        {
            lock (_sync)
            {
                return _blocksByNode.TryGetValue(nodeId, out var block) ? block : null;
            }
        }

        public bool GetNodeValue(string nodeId)
            => BlockFor(nodeId) is { } block && block.OutputLinks.FirstOrDefault().AsBool();

        /// <summary>Live edits on the canvas do not apply to imported PLC logic.</summary>
        public void WriteNodeValue(string nodeId, double value)
            => _logger.LogDebug("Ignoring live edit on PLC node {NodeId}", nodeId);

        private DataStore EffectiveStore
            => _connectionManager?.ActiveService?.GetDataStore() ?? _localStore;

        private void ShowValues(VisualNodeEditorConfig? config)
        {
            if (config?.Nodes == null) return;

            VisualNode[] nodes;
            try
            {
                nodes = config.Nodes.ToArray();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // The UI thread is swapping the section on screen; show it next scan.
                return;
            }

            foreach (var node in nodes)
            {
                if (!_blocksByNode.TryGetValue(node.Id, out var block)) continue;
                ShowPrimaryValue(node, block);
                var state = LiveState(block);
                if (state.SameAs(node.PlcLive)) continue;
                node.PlcLive = state;
                _decorated.Add(node);
            }
        }

        /// <summary>
        /// The node's own live value (the controls panel's) follows the block's first
        /// output, without echoing it back as a user edit.
        /// </summary>
        private static void ShowPrimaryValue(VisualNode node, PlcBlock block)
        {
            if (block.OutputLinks.Length == 0 || block.OutputLinks[0] is not { Type: { IsElementary: true } } value) return;
            if (node.IsEditingLiveValue) return;

            var number = value.AsDouble();
            node.CurrentValue = value.AsBool();
            node.IntValue = (int)Math.Clamp(value.AsInteger(), int.MinValue, int.MaxValue);
            if (node.CurrentValueDouble.Equals(number)) return;
            node.SuppressWriteBack = true;
            try
            {
                node.CurrentValueDouble = number;
            }
            finally
            {
                node.SuppressWriteBack = false;
            }
        }

        private static PlcLiveState LiveState(PlcBlock block)
        {
            // Tag the variables only: a literal shows its own value, and a link's value
            // is tagged once, at the output it comes from.
            var inputs = new Dictionary<string, string>(block.Inputs.Count + 1, StringComparer.OrdinalIgnoreCase);
            if (block.En is { IsLinked: false } enPin && block.LastEn is { } en) inputs[enPin.Name] = en ? "TRUE" : "FALSE";
            for (var i = 0; i < block.Inputs.Count; i++)
            {
                var pin = block.Inputs[i];
                if (pin.IsLinked || pin.Operand is PlcConstantOperand) continue;
                if (Display(block.LastInputs[i]) is { } text) inputs[pin.Name] = text;
            }

            var outputs = new Dictionary<string, string>(block.Outputs.Count + 1, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < block.Outputs.Count; i++)
            {
                if (Display(block.OutputLinks[i]) is { } text) outputs[block.Outputs[i].Name] = text;
            }
            if (block.Eno != null) outputs[block.Eno.Name] = block.LastEno ? "TRUE" : "FALSE";

            return new PlcLiveState(inputs, outputs, block.ExecutedLastScan, block.IsSimulated);
        }

        /// <summary>Elementary values only; whole structures have no one-line value.</summary>
        private static string? Display(PlcValue value)
            => value.Type is { IsElementary: true } ? value.ToDisplayString() : null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Stop();
            _timer.Dispose();
            _tickLock.Dispose();
        }
    }
}
