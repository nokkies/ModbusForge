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

    public class VisualSimulationService : IVisualSimulationService, IDisposable
    {
        private readonly ILogger<VisualSimulationService> _logger;
        private readonly ISimulationService _simulationService;
        private readonly ModbusServerService _serverService;
        
        private VisualNodeEditorViewModel? _viewModel;
        private DispatcherTimer? _animationTimer;
        private bool _isAnimating;
        private DateTime _lastUpdate;
        
        // Cache for node values to avoid excessive updates
        private readonly Dictionary<string, bool> _nodeValueCache = new Dictionary<string, bool>();
        private readonly Dictionary<string, DateTime> _lastNodeUpdate = new Dictionary<string, DateTime>();

        public VisualSimulationService(
            ILogger<VisualSimulationService> logger,
            ISimulationService simulationService,
            ModbusServerService serverService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _simulationService = simulationService ?? throw new ArgumentNullException(nameof(simulationService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
        }

        public void Start(VisualNodeEditorViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            
            if (_animationTimer == null)
            {
                _animationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100) // Update 10 times per second for smooth animation
                };
                _animationTimer.Tick += AnimationTimer_Tick;
            }
            
            _lastUpdate = DateTime.UtcNow;
            _animationTimer.Start();
            _isAnimating = true;
            
            _logger.LogInformation("Visual simulation animation started");
        }

        public void Stop()
        {
            _animationTimer?.Stop();
            _isAnimating = false;
            
            // Clear all node values to reset visual state
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
            
            _logger.LogInformation("Visual simulation animation stopped");
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            if (_viewModel == null || !_isAnimating) return;
            if (!_viewModel.ShowLiveValues) return;
            
            // Only run when server is running
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

        public void UpdateNodeValues()
        {
            if (_viewModel == null) return;

            var dataStore = _serverService.GetDataStore();
            if (dataStore == null) return;

            var now = DateTime.UtcNow;
            var elapsedMs = (int)(now - _lastUpdate).TotalMilliseconds;
            _lastUpdate = now;

            foreach (var node in _viewModel.Nodes)
            {
                try
                {
                    var newValue = GetNodeValueFromSimulation(node, dataStore, elapsedMs);
                    
                    // Only update if value changed or it's been a while (to avoid flickering)
                    var oldValue = _nodeValueCache.GetValueOrDefault(node.Id, false);
                    var lastUpdate = _lastNodeUpdate.GetValueOrDefault(node.Id, DateTime.MinValue);
                    var shouldUpdate = newValue != oldValue || 
                                      (now - lastUpdate).TotalMilliseconds > 500; // Force update every 500ms

                    if (shouldUpdate)
                    {
                        node.CurrentValue = newValue;
                        _nodeValueCache[node.Id] = newValue;
                        _lastNodeUpdate[node.Id] = now;
                        
                        // For Output blocks, write the value to the configured Modbus address
                        if (node.OutputAddress?.Address >= 0)
                        {
                            switch (node.ElementType)
                            {
                                case PlcElementType.Output:
                                    // Legacy output - mixed logic (keep for compatibility)
                                    if (node.OutputAddress?.Area == PlcArea.HoldingRegister || node.OutputAddress?.Area == PlcArea.InputRegister)
                                    {
                                        var inputInt = ReadModbusValueInt(node.Input1Address, dataStore);
                                        WriteModbusValue(node.OutputAddress, (ushort)inputInt, dataStore);
                                    }
                                    else
                                    {
                                        WriteModbusValue(node.OutputAddress, newValue ? (ushort)1 : (ushort)0, dataStore);
                                    }
                                    break;
                                    
                                case PlcElementType.OutputBool:
                                    // Boolean output - always write 1/0
                                    WriteModbusValue(node.OutputAddress, newValue ? (ushort)1 : (ushort)0, dataStore);
                                    break;
                                    
                                case PlcElementType.OutputInt:
                                    // Integer output - write actual connected value
                                    var inputIntValue = 0;
                                    
                                    // Find connections to this node's inputs
                                    var nodeConnections = _viewModel?.Connections.Where(c => c.TargetNodeId == node.Id).ToList() ?? new List<NodeConnection>();
                                    
                                    // Find the connected source node
                                    var sourceConnection = nodeConnections.FirstOrDefault(c => c.TargetConnector == "Input1");
                                    if (sourceConnection != null)
                                    {
                                        var sourceNode = _viewModel?.Nodes.FirstOrDefault(n => n.Id == sourceConnection.SourceNodeId);
                                        if (sourceNode != null)
                                        {
                                            // If connected to InputInt, read the actual integer value from the source
                                            if (sourceNode.ElementType == PlcElementType.InputInt)
                                            {
                                                inputIntValue = ReadModbusValueInt(sourceNode.Input1Address, dataStore);
                                            }
                                            else
                                            {
                                                // For other node types, use the boolean value converted to int
                                                var boolValue = GetNodeValueFromSimulation(sourceNode, dataStore, elapsedMs);
                                                inputIntValue = boolValue ? 1 : 0;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Fallback: read from Input1Address (legacy behavior)
                                        inputIntValue = ReadModbusValueInt(node.Input1Address, dataStore);
                                    }
                                    
                                    WriteModbusValue(node.OutputAddress, (ushort)inputIntValue, dataStore);
                                    break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to update value for node {NodeId}", node.Id);
                }
            }
        }

        private bool GetNodeValueFromSimulation(VisualNode node, DataStore dataStore, int elapsedMs, HashSet<string>? visited = null)
        {
            visited ??= new HashSet<string>();
            if (!visited.Add(node.Id))
                return false; // Circular reference - break the cycle

            // For input nodes, read directly from the configured address
            if (node.ElementType == PlcElementType.Input)
            {
                return ReadModbusValue(node.Input1Address, dataStore);
            }

            // For other nodes, we need to evaluate the logic
            // This is a simplified version - in production, we'd use the full simulation engine
            
            // Get input values from connected nodes
            var input1Value = false;
            var input2Value = false;

            // Find connections to this node's inputs
            var connections = _viewModel?.Connections.Where(c => c.TargetNodeId == node.Id).ToList() ?? new List<NodeConnection>();

            foreach (var connection in connections)
            {
                var sourceNode = _viewModel?.Nodes.FirstOrDefault(n => n.Id == connection.SourceNodeId);
                if (sourceNode != null)
                {
                    var sourceValue = GetNodeValueFromSimulation(sourceNode, dataStore, elapsedMs, visited);
                    
                    if (connection.TargetConnector == "Input1")
                        input1Value = sourceValue;
                    else if (connection.TargetConnector == "Input2")
                        input2Value = sourceValue;
                }
            }

            // Evaluate based on node type
            return node.ElementType switch
            {
                PlcElementType.Input => ReadModbusValue(node.Input1Address, dataStore),
                PlcElementType.InputBool => ReadModbusValue(node.Input1Address, dataStore),
                PlcElementType.InputInt => ReadModbusValueInt(node.Input1Address, dataStore) != 0, // Convert int to bool for logic
                PlcElementType.Output => input1Value, // Output blocks just pass through the input value
                PlcElementType.OutputBool => input1Value, // Boolean output
                PlcElementType.OutputInt => input1Value, // Integer output (still bool for logic evaluation)
                PlcElementType.NOT => !input1Value,
                PlcElementType.AND => input1Value && input2Value,
                PlcElementType.OR => input1Value || input2Value,
                PlcElementType.RS => EvaluateRsLatch(node, input1Value, input2Value),
                PlcElementType.TON => EvaluateTonTimer(node, input1Value, elapsedMs),
                PlcElementType.TOF => EvaluateTofTimer(node, input1Value, elapsedMs),
                PlcElementType.TP => EvaluateTpTimer(node, input1Value, elapsedMs),
                PlcElementType.CTU => EvaluateCtuCounter(node, input1Value),
                PlcElementType.CTD => EvaluateCtdCounter(node, input1Value),
                PlcElementType.CTC => EvaluateCtcCounter(node, input1Value, input2Value),
                PlcElementType.COMPARE_EQ => EvaluateCompare(node, dataStore, (a, b) => a == b),
                PlcElementType.COMPARE_NE => EvaluateCompare(node, dataStore, (a, b) => a != b),
                PlcElementType.COMPARE_GT => EvaluateCompare(node, dataStore, (a, b) => a > b),
                PlcElementType.COMPARE_LT => EvaluateCompare(node, dataStore, (a, b) => a < b),
                PlcElementType.COMPARE_GE => EvaluateCompare(node, dataStore, (a, b) => a >= b),
                PlcElementType.COMPARE_LE => EvaluateCompare(node, dataStore, (a, b) => a <= b),
                PlcElementType.MATH_ADD => EvaluateMath(node, dataStore, (a, b) => a + b),
                PlcElementType.MATH_SUB => EvaluateMath(node, dataStore, (a, b) => a - b),
                PlcElementType.MATH_MUL => EvaluateMath(node, dataStore, (a, b) => a * b),
                PlcElementType.MATH_DIV => EvaluateMath(node, dataStore, (a, b) => b != 0 ? a / b : 0),
                _ => false
            };
        }

        private bool ReadModbusValue(PlcAddressReference? address, DataStore dataStore)
        {
            if (address == null || address.Address < 0) return false;

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

        private int ReadModbusValueInt(PlcAddressReference? address, DataStore dataStore)
        {
            if (address == null || address.Address < 0) return 0;

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

        private bool EvaluateRsLatch(VisualNode node, bool setInput, bool resetInput)
        {
            if (node.SetDominant)
            {
                // Set dominant: if both Set and Reset are true, Set wins (runs last)
                if (resetInput) node.RsState = false;
                if (setInput) node.RsState = true;
            }
            else
            {
                // Reset dominant: if both Set and Reset are true, Reset wins (runs last)
                if (setInput) node.RsState = true;
                if (resetInput) node.RsState = false;
            }
            return node.RsState;
        }

        private bool EvaluateTonTimer(VisualNode node, bool input, int elapsedMs)
        {
            if (input)
            {
                node.TimerAccumulatorMs += elapsedMs;
                if (node.TimerAccumulatorMs >= node.TimerPresetMs)
                {
                    node.TimerOutput = true;
                }
            }
            else
            {
                node.TimerAccumulatorMs = 0;
                node.TimerOutput = false;
            }

            node.TimerLastInput = input;
            return node.TimerOutput;
        }

        private bool EvaluateTofTimer(VisualNode node, bool input, int elapsedMs)
        {
            if (input)
            {
                node.TimerAccumulatorMs = 0;
                node.TimerOutput = true;
            }
            else
            {
                if (node.TimerOutput)
                {
                    node.TimerAccumulatorMs += elapsedMs;
                    if (node.TimerAccumulatorMs >= node.TimerPresetMs)
                    {
                        node.TimerOutput = false;
                        node.TimerAccumulatorMs = 0;
                    }
                }
            }

            node.TimerLastInput = input;
            return node.TimerOutput;
        }

        private bool EvaluateTpTimer(VisualNode node, bool input, int elapsedMs)
        {
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
                {
                    node.TimerOutput = false;
                }
            }

            node.TimerLastInput = input;
            return node.TimerOutput;
        }

        private bool EvaluateCtuCounter(VisualNode node, bool input)
        {
            var risingEdge = input && !node.CounterLastInput;

            if (risingEdge)
            {
                node.CounterValue++;
            }

            node.CounterLastInput = input;
            return node.CounterValue >= node.CounterPreset;
        }

        private bool EvaluateCtdCounter(VisualNode node, bool input)
        {
            var risingEdge = input && !node.CounterLastInput;

            if (risingEdge)
            {
                node.CounterValue--;
            }

            node.CounterLastInput = input;
            return node.CounterValue <= 0;
        }

        private bool EvaluateCtcCounter(VisualNode node, bool input, bool direction)
        {
            var risingEdge = input && !node.CounterLastInput;

            if (risingEdge)
            {
                if (direction)
                    node.CounterValue++;
                else
                    node.CounterValue--;
            }

            node.CounterLastInput = input;
            return node.CounterValue >= node.CounterPreset;
        }

        private bool EvaluateCompare(VisualNode node, DataStore dataStore, Func<int, int, bool> comparison)
        {
            var input1Val = ReadModbusValueInt(node.Input1Address, dataStore);
            var input2Val = node.Input2Address?.Address >= 0 ? 
                ReadModbusValueInt(node.Input2Address, dataStore) : 
                node.CompareValue;
            
            return comparison(input1Val, input2Val);
        }

        private bool EvaluateMath(VisualNode node, DataStore dataStore, Func<int, int, int> operation)
        {
            var input1Val = ReadModbusValueInt(node.Input1Address, dataStore);
            var input2Val = node.Input2Address?.Address >= 0 ? 
                ReadModbusValueInt(node.Input2Address, dataStore) : 
                node.CompareValue;
            
            var result = operation(input1Val, input2Val);
            
            // Write result to output if configured
            if (node.OutputAddress?.Address >= 0)
            {
                WriteModbusValue(node.OutputAddress, (ushort)Math.Max(0, Math.Min(65535, result)), dataStore);
            }
            
            return result != 0;
        }

        private void WriteModbusValue(PlcAddressReference? output, ushort value, DataStore dataStore)
        {
            if (output == null || output.Address < 0) return;

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

        public bool GetNodeValue(string nodeId)
        {
            return _nodeValueCache.GetValueOrDefault(nodeId, false);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
