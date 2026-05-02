using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Modbus.Data;
using ModbusForge.Models;
using ModbusForge.ViewModels;

namespace ModbusForge.Services
{
    public interface IVisualSimulationService
    {
        void Start(VisualNodeEditorViewModel viewModel);
        void Stop();
        void UpdateNodeValues();
        bool GetNodeValue(string nodeId);
    }

    /// <summary>
    /// Carries both boolean and integer results from node evaluation,
    /// preventing bool/int cross-contamination in the data stores.
    /// </summary>
    public struct NodeResult
    {
        public bool BoolValue;
        public int IntValue;

        public static NodeResult FromBool(bool b) => new NodeResult { BoolValue = b, IntValue = b ? 1 : 0 };
        public static NodeResult FromInt(int i) => new NodeResult { BoolValue = i != 0, IntValue = i };
    }

    public class VisualSimulationService : IVisualSimulationService, IDisposable
    {
        private readonly ILogger<VisualSimulationService> _logger;
        private readonly ModbusServerService _serverService;

        private VisualNodeEditorViewModel? _viewModel;
        private DispatcherTimer? _animationTimer;
        private bool _isAnimating;
        private DateTime _lastUpdate;

        // Cache for node values to avoid excessive UI updates
        private readonly Dictionary<string, bool> _nodeValueCache = new();
        private readonly Dictionary<string, DateTime> _lastNodeUpdate = new();

        // Evaluated results per tick (phase 1 cache)
        private readonly Dictionary<string, NodeResult> _evaluatedResults = new();

        // Topological evaluation order (rebuilt when graph changes)
        private List<VisualNode>? _topoOrder;
        private int _lastNodeCount;
        private int _lastConnectionCount;

#if DEBUG
        private static bool _debugLogging = true;
#else
        private static bool _debugLogging = false;
#endif

        public VisualSimulationService(
            ILogger<VisualSimulationService> logger,
            ModbusServerService serverService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
        }

        public void Start(VisualNodeEditorViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            if (_animationTimer == null)
            {
                _animationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _animationTimer.Tick += AnimationTimer_Tick;
            }

            _lastUpdate = DateTime.UtcNow;
            _topoOrder = null; // force rebuild
            _animationTimer.Start();
            _isAnimating = true;

            _logger.LogInformation("Visual simulation started");
        }

        public void Stop()
        {
            _animationTimer?.Stop();
            _isAnimating = false;

            if (_viewModel != null)
            {
                foreach (var node in _viewModel.Nodes)
                {
                    node.CurrentValue = false;
                    node.ShowLiveValues = false;
                }
            }

            _nodeValueCache.Clear();
            _lastNodeUpdate.Clear();
            _evaluatedResults.Clear();
            _topoOrder = null;

            _logger.LogInformation("Visual simulation stopped");
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            if (_viewModel == null || !_isAnimating) return;
            if (!_viewModel.ShowLiveValues) return;
            if (!_serverService.IsConnected) return;

            try
            {
                UpdateNodeValues();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating visual node values");
            }
        }

        // ───────────────────────── TOPOLOGICAL SORT (Kahn's) ─────────────────────────

        private List<VisualNode> BuildTopologicalOrder()
        {
            if (_viewModel == null) return new List<VisualNode>();

            var nodes = _viewModel.Nodes.ToList();
            var connections = _viewModel.Connections.ToList();

            // Build adjacency: source → targets
            var inDegree = nodes.ToDictionary(n => n.Id, _ => 0);
            var adjacency = nodes.ToDictionary(n => n.Id, _ => new List<string>());

            foreach (var conn in connections)
            {
                if (inDegree.ContainsKey(conn.TargetNodeId) && adjacency.ContainsKey(conn.SourceNodeId))
                {
                    inDegree[conn.TargetNodeId]++;
                    adjacency[conn.SourceNodeId].Add(conn.TargetNodeId);
                }
            }

            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var sorted = new List<VisualNode>();
            var nodeMap = nodes.ToDictionary(n => n.Id);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (nodeMap.TryGetValue(id, out var node))
                    sorted.Add(node);

                foreach (var neighbor in adjacency[id])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }

            // Nodes not in sorted list are part of a cycle — append them with a warning
            if (sorted.Count < nodes.Count)
            {
                var cycleNodes = nodes.Where(n => !sorted.Contains(n)).ToList();
                _logger.LogWarning("Simulation graph has {Count} nodes in cycles — they will be evaluated last", cycleNodes.Count);
                sorted.AddRange(cycleNodes);
            }

            return sorted;
        }

        private void EnsureTopoOrder()
        {
            if (_viewModel == null) return;

            var nodeCount = _viewModel.Nodes.Count;
            var connCount = _viewModel.Connections.Count;

            if (_topoOrder == null || nodeCount != _lastNodeCount || connCount != _lastConnectionCount)
            {
                _topoOrder = BuildTopologicalOrder();
                _lastNodeCount = nodeCount;
                _lastConnectionCount = connCount;
                DebugLog($"Rebuilt topo order: {_topoOrder.Count} nodes");
            }
        }

        // ───────────────────────── TWO-PHASE UPDATE ─────────────────────────

        public void UpdateNodeValues()
        {
            if (_viewModel == null) return;

            var dataStore = _serverService.GetDataStore();
            if (dataStore == null) return;

            var now = DateTime.UtcNow;
            _lastUpdate = now;

            EnsureTopoOrder();
            if (_topoOrder == null || _topoOrder.Count == 0) return;

            // ── Phase 1: Evaluate all nodes (no DataStore writes) ──
            _evaluatedResults.Clear();

            foreach (var node in _topoOrder)
            {
                try
                {
                    var result = EvaluateNode(node, dataStore);
                    _evaluatedResults[node.Id] = result;

                    // Update UI-visible properties
                    var oldValue = _nodeValueCache.GetValueOrDefault(node.Id, false);
                    var lastUpd = _lastNodeUpdate.GetValueOrDefault(node.Id, DateTime.MinValue);
                    var shouldUpdate = result.BoolValue != oldValue || (now - lastUpd).TotalMilliseconds > 500;

                    if (shouldUpdate)
                    {
                        node.CurrentValue = result.BoolValue;
                        node.IntValue = result.IntValue;
                        _nodeValueCache[node.Id] = result.BoolValue;
                        _lastNodeUpdate[node.Id] = now;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to evaluate node {NodeId}", node.Id);
                }
            }

            // ── Phase 2: Write only output nodes to DataStore ──
            foreach (var node in _topoOrder)
            {
                try
                {
                    if (!IsOutputNode(node.ElementType)) continue;
                    if (node.OutputAddress?.Address < 0) continue;
                    if (!_evaluatedResults.TryGetValue(node.Id, out var result)) continue;

                    WriteOutputToDataStore(node, result, dataStore);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to write output for node {NodeId}", node.Id);
                }
            }
        }

        private static bool IsOutputNode(PlcElementType type) =>
            type == PlcElementType.Output || type == PlcElementType.OutputBool || type == PlcElementType.OutputInt;

        // ───────────────────────── PHASE 1: EVALUATE ─────────────────────────

        private NodeResult EvaluateNode(VisualNode node, DataStore dataStore)
        {
            switch (node.ElementType)
            {
                // ── Input nodes: read from DataStore ──
                case PlcElementType.Input:
                case PlcElementType.InputBool:
                    return NodeResult.FromBool(ReadModbusValue(node.Input1Address, dataStore));

                case PlcElementType.InputInt:
                    return NodeResult.FromInt(ReadModbusValueInt(node.Input1Address, dataStore));

                // ── Output nodes: pass-through from connected input ──
                case PlcElementType.Output:
                case PlcElementType.OutputBool:
                {
                    var input = GetConnectedResult(node, "Input1");
                    return input ?? NodeResult.FromBool(false);
                }
                case PlcElementType.OutputInt:
                {
                    var input = GetConnectedResult(node, "Input1");
                    return input ?? NodeResult.FromInt(0);
                }

                // ── Logic gates ──
                case PlcElementType.NOT:
                {
                    var in1 = GetConnectedBool(node, "Input1");
                    return NodeResult.FromBool(!in1);
                }
                case PlcElementType.AND:
                {
                    var in1 = GetConnectedBool(node, "Input1");
                    var in2 = GetConnectedBool(node, "Input2");
                    return NodeResult.FromBool(in1 && in2);
                }
                case PlcElementType.OR:
                {
                    var in1 = GetConnectedBool(node, "Input1");
                    var in2 = GetConnectedBool(node, "Input2");
                    return NodeResult.FromBool(in1 || in2);
                }

                // ── RS Latch ──
                case PlcElementType.RS:
                {
                    var set = GetConnectedBool(node, "Input1");
                    var reset = GetConnectedBool(node, "Input2");
                    return NodeResult.FromBool(EvaluateRsLatch(node, set, reset));
                }

                // ── Timers ──
                case PlcElementType.TON:
                    return NodeResult.FromBool(EvaluateTonTimer(node, GetConnectedBool(node, "Input1")));
                case PlcElementType.TOF:
                    return NodeResult.FromBool(EvaluateTofTimer(node, GetConnectedBool(node, "Input1")));
                case PlcElementType.TP:
                    return NodeResult.FromBool(EvaluateTpTimer(node, GetConnectedBool(node, "Input1")));

                // ── Counters ──
                case PlcElementType.CTU:
                    return NodeResult.FromBool(EvaluateCtuCounter(node, GetConnectedBool(node, "Input1")));
                case PlcElementType.CTD:
                    return NodeResult.FromBool(EvaluateCtdCounter(node, GetConnectedBool(node, "Input1")));
                case PlcElementType.CTC:
                    return NodeResult.FromBool(EvaluateCtcCounter(node, GetConnectedBool(node, "Input1"), GetConnectedBool(node, "Input2")));

                // ── Comparators ──
                case PlcElementType.COMPARE_EQ:
                case PlcElementType.COMPARE_NE:
                case PlcElementType.COMPARE_GT:
                case PlcElementType.COMPARE_LT:
                case PlcElementType.COMPARE_GE:
                case PlcElementType.COMPARE_LE:
                    return NodeResult.FromBool(EvaluateCompare(node, dataStore));

                // ── Math ──
                case PlcElementType.MATH_ADD:
                case PlcElementType.MATH_SUB:
                case PlcElementType.MATH_MUL:
                case PlcElementType.MATH_DIV:
                    return NodeResult.FromInt(EvaluateMathInt(node, dataStore));

                default:
                    return NodeResult.FromBool(false);
            }
        }

        /// <summary>
        /// Gets the already-evaluated result of the node connected to the specified input connector.
        /// Because we evaluate in topological order, upstream nodes are already in _evaluatedResults.
        /// </summary>
        private NodeResult? GetConnectedResult(VisualNode node, string connector)
        {
            var conn = _viewModel?.Connections.FirstOrDefault(c => c.TargetNodeId == node.Id && c.TargetConnector == connector);
            if (conn == null) return null;
            return _evaluatedResults.TryGetValue(conn.SourceNodeId, out var result) ? result : null;
        }

        private bool GetConnectedBool(VisualNode node, string connector)
        {
            return GetConnectedResult(node, connector)?.BoolValue ?? false;
        }

        private int GetConnectedInt(VisualNode node, string connector)
        {
            return GetConnectedResult(node, connector)?.IntValue ?? 0;
        }

        // ───────────────────────── PHASE 2: WRITE (with area guards) ─────────────────────────

        private void WriteOutputToDataStore(VisualNode node, NodeResult result, DataStore dataStore)
        {
            var addr = node.OutputAddress;
            if (addr == null || addr.Address < 0) return;

            switch (node.ElementType)
            {
                case PlcElementType.OutputBool:
                    // Area guard: bool outputs should write to Coil or DiscreteInput
                    if (addr.Area == PlcArea.Coil || addr.Area == PlcArea.DiscreteInput)
                    {
                        WriteModbusValue(addr, result.BoolValue ? (ushort)1 : (ushort)0, dataStore);
                    }
                    else
                    {
                        _logger.LogWarning("OutputBool node {NodeId} targets {Area}:{Address} — expected Coil or DiscreteInput",
                            node.Id, addr.Area, addr.Address);
                        WriteModbusValue(addr, result.BoolValue ? (ushort)1 : (ushort)0, dataStore);
                    }
                    break;

                case PlcElementType.OutputInt:
                    // Area guard: int outputs should write to HoldingRegister or InputRegister
                    if (addr.Area == PlcArea.HoldingRegister || addr.Area == PlcArea.InputRegister)
                    {
                        WriteModbusValue(addr, (ushort)Math.Clamp(result.IntValue, 0, 65535), dataStore);
                    }
                    else
                    {
                        _logger.LogWarning("OutputInt node {NodeId} targets {Area}:{Address} — expected HoldingRegister or InputRegister",
                            node.Id, addr.Area, addr.Address);
                        WriteModbusValue(addr, (ushort)Math.Clamp(result.IntValue, 0, 65535), dataStore);
                    }
                    break;

                case PlcElementType.Output:
                    // Legacy output: auto-detect based on area
                    if (addr.Area == PlcArea.HoldingRegister || addr.Area == PlcArea.InputRegister)
                    {
                        WriteModbusValue(addr, (ushort)Math.Clamp(result.IntValue, 0, 65535), dataStore);
                    }
                    else
                    {
                        WriteModbusValue(addr, result.BoolValue ? (ushort)1 : (ushort)0, dataStore);
                    }
                    break;
            }
        }

        // ───────────────────────── EVALUATORS ─────────────────────────

        private bool EvaluateRsLatch(VisualNode node, bool setInput, bool resetInput)
        {
            if (node.SetDominant)
            {
                if (resetInput) node.RsState = false;
                if (setInput) node.RsState = true;
            }
            else
            {
                if (setInput) node.RsState = true;
                if (resetInput) node.RsState = false;
            }
            return node.RsState;
        }

        private bool EvaluateTonTimer(VisualNode node, bool input)
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (int)(now - _lastUpdate).TotalMilliseconds;

            if (input)
            {
                node.TimerAccumulatorMs += elapsedMs;
                if (node.TimerAccumulatorMs >= node.TimerPresetMs)
                    node.TimerOutput = true;
            }
            else
            {
                node.TimerAccumulatorMs = 0;
                node.TimerOutput = false;
            }

            node.TimerLastInput = input;
            return node.TimerOutput;
        }

        private bool EvaluateTofTimer(VisualNode node, bool input)
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (int)(now - _lastUpdate).TotalMilliseconds;

            if (input)
            {
                node.TimerAccumulatorMs = 0;
                node.TimerOutput = true;
            }
            else if (node.TimerOutput)
            {
                node.TimerAccumulatorMs += elapsedMs;
                if (node.TimerAccumulatorMs >= node.TimerPresetMs)
                {
                    node.TimerOutput = false;
                    node.TimerAccumulatorMs = 0;
                }
            }

            node.TimerLastInput = input;
            return node.TimerOutput;
        }

        private bool EvaluateTpTimer(VisualNode node, bool input)
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (int)(now - _lastUpdate).TotalMilliseconds;
            var risingEdge = input && !node.TimerLastInput;

            if (risingEdge)
            {
                node.TimerAccumulatorMs = 0;
                node.TimerOutput = true;
            }

            if (node.TimerOutput)
            {
                node.TimerAccumulatorMs += elapsedMs;
                if (node.TimerAccumulatorMs >= node.TimerPresetMs)
                    node.TimerOutput = false;
            }

            node.TimerLastInput = input;
            return node.TimerOutput;
        }

        private bool EvaluateCtuCounter(VisualNode node, bool input)
        {
            if (input && !node.CounterLastInput) node.CounterValue++;
            node.CounterLastInput = input;
            return node.CounterValue >= node.CounterPreset;
        }

        private bool EvaluateCtdCounter(VisualNode node, bool input)
        {
            if (input && !node.CounterLastInput) node.CounterValue--;
            node.CounterLastInput = input;
            return node.CounterValue <= 0;
        }

        private bool EvaluateCtcCounter(VisualNode node, bool input, bool direction)
        {
            if (input && !node.CounterLastInput)
            {
                if (direction) node.CounterValue++;
                else node.CounterValue--;
            }
            node.CounterLastInput = input;
            return node.CounterValue >= node.CounterPreset;
        }

        private bool EvaluateCompare(VisualNode node, DataStore dataStore)
        {
            var in1 = GetConnectedInt(node, "Input1");
            // If no connection on Input1, fallback to direct address read
            if (GetConnectedResult(node, "Input1") == null)
                in1 = ReadModbusValueInt(node.Input1Address, dataStore);

            var in2 = GetConnectedInt(node, "Input2");
            if (GetConnectedResult(node, "Input2") == null)
                in2 = node.Input2Address?.Address >= 0 ? ReadModbusValueInt(node.Input2Address, dataStore) : node.CompareValue;

            return node.ElementType switch
            {
                PlcElementType.COMPARE_EQ => in1 == in2,
                PlcElementType.COMPARE_NE => in1 != in2,
                PlcElementType.COMPARE_GT => in1 > in2,
                PlcElementType.COMPARE_LT => in1 < in2,
                PlcElementType.COMPARE_GE => in1 >= in2,
                PlcElementType.COMPARE_LE => in1 <= in2,
                _ => false
            };
        }

        /// <summary>
        /// Evaluates a math node and returns only the integer result — no DataStore writes.
        /// </summary>
        private int EvaluateMathInt(VisualNode node, DataStore dataStore)
        {
            var in1 = GetConnectedInt(node, "Input1");
            if (GetConnectedResult(node, "Input1") == null)
                in1 = ReadModbusValueInt(node.Input1Address, dataStore);

            var in2 = GetConnectedInt(node, "Input2");
            if (GetConnectedResult(node, "Input2") == null)
                in2 = node.Input2Address?.Address >= 0 ? ReadModbusValueInt(node.Input2Address, dataStore) : node.CompareValue;

            return node.ElementType switch
            {
                PlcElementType.MATH_ADD => in1 + in2,
                PlcElementType.MATH_SUB => in1 - in2,
                PlcElementType.MATH_MUL => in1 * in2,
                PlcElementType.MATH_DIV => in2 != 0 ? in1 / in2 : 0,
                _ => 0
            };
        }

        // ───────────────────────── MODBUS I/O ─────────────────────────

        private bool ReadModbusValue(PlcAddressReference address, DataStore dataStore)
        {
            if (address.Address < 0) return false;

            var value = address.Area switch
            {
                PlcArea.Coil => address.Address < dataStore.CoilDiscretes.Count && dataStore.CoilDiscretes[address.Address],
                PlcArea.DiscreteInput => address.Address < dataStore.InputDiscretes.Count && dataStore.InputDiscretes[address.Address],
                PlcArea.HoldingRegister => address.Address < dataStore.HoldingRegisters.Count && dataStore.HoldingRegisters[address.Address] != 0,
                PlcArea.InputRegister => address.Address < dataStore.InputRegisters.Count && dataStore.InputRegisters[address.Address] != 0,
                _ => false
            };

            return address.Not ? !value : value;
        }

        private int ReadModbusValueInt(PlcAddressReference address, DataStore dataStore)
        {
            if (address.Address < 0) return 0;

            var value = address.Area switch
            {
                PlcArea.Coil => (address.Address < dataStore.CoilDiscretes.Count && dataStore.CoilDiscretes[address.Address]) ? 1 : 0,
                PlcArea.DiscreteInput => (address.Address < dataStore.InputDiscretes.Count && dataStore.InputDiscretes[address.Address]) ? 1 : 0,
                PlcArea.HoldingRegister => address.Address < dataStore.HoldingRegisters.Count ? dataStore.HoldingRegisters[address.Address] : 0,
                PlcArea.InputRegister => address.Address < dataStore.InputRegisters.Count ? dataStore.InputRegisters[address.Address] : 0,
                _ => 0
            };

            return address.Not ? (value == 0 ? 1 : 0) : value;
        }

        private void WriteModbusValue(PlcAddressReference output, ushort value, DataStore dataStore)
        {
            if (output?.Address < 0) return;

            var finalValue = output.Not ? (value == 0 ? (ushort)1 : (ushort)0) : value;

            switch (output.Area)
            {
                case PlcArea.HoldingRegister:
                    if (output.Address < dataStore.HoldingRegisters.Count)
                        dataStore.HoldingRegisters[output.Address] = finalValue;
                    break;
                case PlcArea.InputRegister:
                    if (output.Address < dataStore.InputRegisters.Count)
                        dataStore.InputRegisters[output.Address] = finalValue;
                    break;
                case PlcArea.Coil:
                    if (output.Address < dataStore.CoilDiscretes.Count)
                        dataStore.CoilDiscretes[output.Address] = finalValue != 0;
                    break;
                case PlcArea.DiscreteInput:
                    if (output.Address < dataStore.InputDiscretes.Count)
                        dataStore.InputDiscretes[output.Address] = finalValue != 0;
                    break;
            }
        }

        // ───────────────────────── MISC ─────────────────────────

        public bool GetNodeValue(string nodeId)
        {
            return _nodeValueCache.GetValueOrDefault(nodeId, false);
        }

        private void DebugLog(string message)
        {
            if (_debugLogging)
                System.Diagnostics.Debug.WriteLine($"[VisualSim] {message}");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
