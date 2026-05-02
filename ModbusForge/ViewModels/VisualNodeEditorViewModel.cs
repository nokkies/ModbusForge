using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ModbusForge.ViewModels
{
    public partial class VisualNodeEditorViewModel : ViewModelBase
    {
        [ObservableProperty]
        private VisualNodeEditorConfig _config = new VisualNodeEditorConfig();
        
        [ObservableProperty]
        private ObservableCollection<VisualNode> _nodes = new ObservableCollection<VisualNode>();
        
        [ObservableProperty]
        private ObservableCollection<NodeConnection> _connections = new ObservableCollection<NodeConnection>();
        
        [ObservableProperty]
        private ObservableCollection<ConnectorConfiguration> _connectorConfigs = new ObservableCollection<ConnectorConfiguration>();
        
        [ObservableProperty]
        private VisualNode? _selectedNode;
        
        [ObservableProperty]
        private double _canvasWidth = 2000;
        
        [ObservableProperty]
        private double _canvasHeight = 2000;
        
        [ObservableProperty]
        private double _zoomLevel = 1.0;
        
        [ObservableProperty]
        private bool _showLiveValues = false;
        
        [ObservableProperty]
        private string? _pendingConnectionStart;
        
        [ObservableProperty]
        private string? _selectedWaveform = "Ramp";
        
        [ObservableProperty]
        private int _waveformPeriodMs = 1000;
        
        [ObservableProperty]
        private double _waveformAmplitude = 100;
        
        [ObservableProperty]
        private double _waveformOffset = 0;
        
        [ObservableProperty]
        private ProgramFolder _programTree = new ProgramFolder { Name = "Programs" };
        
        [ObservableProperty]
        private ProgramModel? _selectedProgram;
        
        [ObservableProperty]
        private string _newProgramName = "New Program";
        
        public VisualNodeEditorViewModel()
        {
            // Initialize with a default program
            var defaultProgram = new ProgramModel { Name = "Main", ExecutionOrder = 0 };
            ProgramTree.Programs.Add(defaultProgram);
            SelectedProgram = defaultProgram;
        }
        
        private void InitializeSampleNodes()
        {
            // Create a simple AND gate example
            var input1 = new VisualNode
            {
                Name = "Input 1",
                ElementType = PlcElementType.Input,
                X = 50,
                Y = 100,
                Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 }
            };
            
            var input2 = new VisualNode
            {
                Name = "Input 2",
                ElementType = PlcElementType.Input,
                X = 50,
                Y = 200,
                Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 2 }
            };
            
            var andGate = new VisualNode
            {
                Name = "AND Gate",
                ElementType = PlcElementType.AND,
                X = 250,
                Y = 150,
                OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 }
            };
            
            var output = new VisualNode
            {
                Name = "Output",
                ElementType = PlcElementType.Output,
                X = 450,
                Y = 150,
                OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 }
            };
            
            Nodes.Add(input1);
            Nodes.Add(input2);
            Nodes.Add(andGate);
            Nodes.Add(output);
            
            // Create connections
            CreateConnection(input1.Id, andGate.Id, "Input1");
            CreateConnection(input2.Id, andGate.Id, "Input2");
            CreateConnection(andGate.Id, output.Id, "Input1");
        }
        
        [RelayCommand]
        private void AddNode(string nodeType)
        {
            try
            {
                if (!Enum.TryParse<PlcElementType>(nodeType, out var elementType))
                {
                    return;
                }
                
                // Generate a unique name for the node
                var programName = SelectedProgram?.Name ?? "Program";
                var nodeTypeName = elementType.ToString();
                var nodeNumber = GetNextNodeNumber(elementType);
                var nodeName = $"{programName}_{nodeTypeName}_{nodeNumber:D2}";
                
                var newNode = new VisualNode
                {
                    Id = Guid.NewGuid().ToString(),
                    ElementType = elementType,
                    Name = nodeName,
                    X = 100 + Nodes.Count * 30,
                    Y = 100 + Nodes.Count * 30,
                    Width = 240,
                    Height = 140
                };
                
                // Set default parameters based on type
                switch (elementType)
                {
                    case PlcElementType.Input:
                        newNode.Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = -1 };
                        break;
                    case PlcElementType.Output:
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = -1 };
                        break;
                    case PlcElementType.InputBool:
                        newNode.Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = GetNextAvailableAddress(PlcArea.Coil) };
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = newNode.Input1Address.Address };
                        break;
                    case PlcElementType.InputInt:
                        newNode.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = GetNextAvailableAddress(PlcArea.HoldingRegister) };
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = newNode.Input1Address.Address };
                        break;
                    case PlcElementType.OutputBool:
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = GetNextAvailableAddress(PlcArea.Coil) };
                        break;
                    case PlcElementType.OutputInt:
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = GetNextAvailableAddress(PlcArea.HoldingRegister) };
                        break;
                    case PlcElementType.TON:
                    case PlcElementType.TOF:
                    case PlcElementType.TP:
                        newNode.TimerPresetMs = 1000;
                        break;
                    case PlcElementType.CTU:
                    case PlcElementType.CTD:
                    case PlcElementType.CTC:
                        newNode.CounterPreset = 10;
                        break;
                    case PlcElementType.MATH_ADD:
                    case PlcElementType.MATH_SUB:
                    case PlcElementType.MATH_MUL:
                    case PlcElementType.MATH_DIV:
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = GetNextAvailableAddress(PlcArea.HoldingRegister) };
                        break;
                }
                
                System.Diagnostics.Debug.WriteLine($"DEBUG: About to add node to Nodes collection. Current count: {Nodes.Count}");
                Nodes.Add(newNode);
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node added successfully. New count: {Nodes.Count}");
                
                // Also add to the selected program's nodes
                if (SelectedProgram != null)
                {
                    SelectedProgram.Nodes.Add(newNode);
                }
                
                SelectedNode = newNode;
                System.Diagnostics.Debug.WriteLine($"DEBUG: SelectedNode set to: {newNode.Id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DEBUG: Exception in AddNode: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"DEBUG: Stack trace: {ex.StackTrace}");
            }
        }
        
        private int GetNextAvailableAddress(PlcArea area)
        {
            // Get all existing addresses for this area
            var existingAddresses = new List<int>();
            
            foreach (var node in Nodes)
            {
                if (node.Input1Address?.Area == area && node.Input1Address.Address >= 0)
                    existingAddresses.Add(node.Input1Address.Address);
                if (node.Input2Address?.Area == area && node.Input2Address.Address >= 0)
                    existingAddresses.Add(node.Input2Address.Address);
                if (node.OutputAddress?.Area == area && node.OutputAddress.Address >= 0)
                    existingAddresses.Add(node.OutputAddress.Address);
            }
            
            // Start from address 1 and find the first available
            int nextAddress = 1;
            while (existingAddresses.Contains(nextAddress))
            {
                nextAddress++;
            }
            
            return nextAddress;
        }
        
        private int GetNextNodeNumber(PlcElementType elementType)
        {
            // Count existing nodes of this type in the current program
            int count = 0;
            foreach (var node in Nodes)
            {
                if (node.ElementType == elementType)
                {
                    count++;
                }
            }
            return count + 1;
        }
        
        [RelayCommand]
        private void DeleteNode(VisualNode node)
        {
            if (node != null)
            {
                // Remove connections to/from this node
                var connectionsToRemove = Connections.Where(c => 
                    c.SourceNodeId == node.Id || c.TargetNodeId == node.Id).ToList();
                
                foreach (var connection in connectionsToRemove)
                {
                    Connections.Remove(connection);
                }
                
                // Remove connector configurations
                var configsToRemove = ConnectorConfigs.Where(c => c.NodeId == node.Id).ToList();
                foreach (var config in configsToRemove)
                {
                    ConnectorConfigs.Remove(config);
                }
                
                // Remove the node
                Nodes.Remove(node);
                
                if (SelectedNode == node)
                {
                    SelectedNode = null;
                }
            }
        }
        
        [RelayCommand]
        private void ClearAll()
        {
            Nodes.Clear();
            Connections.Clear();
            ConnectorConfigs.Clear();
            SelectedNode = null;
        }
        
        [RelayCommand]
        private void AutoLayout()
        {
            // Simple auto-layout algorithm - arrange nodes in a grid
            const double nodeSpacing = 150;
            const double rowSpacing = 120;
            const int nodesPerRow = 4;
            
            for (int i = 0; i < Nodes.Count; i++)
            {
                var row = i / nodesPerRow;
                var col = i % nodesPerRow;
                
                Nodes[i].X = 50 + col * nodeSpacing;
                Nodes[i].Y = 50 + row * rowSpacing;
            }
            
            // Update all connections
            UpdateAllConnections();
        }
        
        [RelayCommand]
        private void ApplyWaveform()
        {
            if (SelectedNode == null) return;
            
            SelectedNode.Waveform = SelectedWaveform;
            SelectedNode.PeriodMs = WaveformPeriodMs;
            SelectedNode.Amplitude = WaveformAmplitude;
            SelectedNode.Offset = WaveformOffset;
        }
        
        [RelayCommand]
        private void EnableNode()
        {
            if (SelectedNode != null)
            {
                SelectedNode.IsEnabled = true;
            }
        }
        
        [RelayCommand]
        private void DisableNode()
        {
            if (SelectedNode != null)
            {
                SelectedNode.IsEnabled = false;
            }
        }
        
        [RelayCommand]
        private void ResetValues()
        {
            foreach (var node in Nodes)
            {
                node.CurrentValueDouble = 0;
            }
        }
        
        [RelayCommand]
        private void RandomizeValues()
        {
            var random = new Random();
            foreach (var node in Nodes)
            {
                node.CurrentValueDouble = random.NextDouble() * 100;
            }
        }
        
        [RelayCommand]
        private async Task ExportConfig()
        {
            // Placeholder for export functionality
            await Task.CompletedTask;
        }
        
        [RelayCommand]
        private void CreateProgram()
        {
            var newProgram = new ProgramModel 
            { 
                Name = NewProgramName,
                ExecutionOrder = ProgramTree.Programs.Count
            };
            ProgramTree.Programs.Add(newProgram);
            SelectedProgram = newProgram;
            NewProgramName = "New Program";
        }
        
        [RelayCommand]
        private void SwitchTab(string tabName)
        {
            // No longer needed - both panels are always visible
        }
        
        [RelayCommand]
        private void DeleteProgram(ProgramModel? program)
        {
            if (program == null) return;
            if (ProgramTree.Programs.Count <= 1) return; // Keep at least one program
            
            ProgramTree.Programs.Remove(program);
            if (SelectedProgram == program)
            {
                SelectedProgram = ProgramTree.Programs.FirstOrDefault();
            }
        }
        
        [RelayCommand]
        private void DuplicateProgram(ProgramModel? program)
        {
            if (program == null) return;
            
            var duplicate = new ProgramModel
            {
                Name = $"{program.Name}_Copy",
                Description = program.Description,
                IsEnabled = program.IsEnabled,
                ExecutionOrder = ProgramTree.Programs.Count
            };
            
            // Copy nodes
            foreach (var node in program.Nodes)
            {
                var nodeCopy = new VisualNode
                {
                    Name = node.Name,
                    ElementType = node.ElementType,
                    X = node.X + 50,
                    Y = node.Y + 50,
                    Width = node.Width,
                    Height = node.Height,
                    Input1Address = node.Input1Address,
                    Input2Address = node.Input2Address,
                    OutputAddress = node.OutputAddress,
                    TimerPresetMs = node.TimerPresetMs,
                    SetDominant = node.SetDominant,
                    CounterPreset = node.CounterPreset,
                    CompareValue = node.CompareValue,
                    Waveform = node.Waveform,
                    PeriodMs = node.PeriodMs,
                    Amplitude = node.Amplitude,
                    Offset = node.Offset
                };
                duplicate.Nodes.Add(nodeCopy);
            }
            
            ProgramTree.Programs.Add(duplicate);
        }
        
        [RelayCommand]
        private void SelectProgram(ProgramModel? program)
        {
            if (program == null) return;
            
            // Save current program's nodes/connections
            if (SelectedProgram != null)
            {
                SelectedProgram.Nodes.Clear();
                foreach (var node in Nodes)
                {
                    SelectedProgram.Nodes.Add(node);
                }
                SelectedProgram.Connections.Clear();
                foreach (var conn in Connections)
                {
                    SelectedProgram.Connections.Add(conn);
                }
                SelectedProgram.ConnectorConfigs.Clear();
                foreach (var config in ConnectorConfigs)
                {
                    SelectedProgram.ConnectorConfigs.Add(config);
                }
            }
            
            // Load selected program's nodes/connections
            SelectedProgram = program;
            Nodes.Clear();
            foreach (var node in program.Nodes)
            {
                Nodes.Add(node);
            }
            Connections.Clear();
            foreach (var conn in program.Connections)
            {
                Connections.Add(conn);
            }
            ConnectorConfigs.Clear();
            foreach (var config in program.ConnectorConfigs)
            {
                ConnectorConfigs.Add(config);
            }
        }
        
        partial void OnSelectedProgramChanged(ProgramModel? value)
        {
            if (value != null)
            {
                // Load the program's content
                Nodes.Clear();
                foreach (var node in value.Nodes)
                {
                    Nodes.Add(node);
                }
                Connections.Clear();
                foreach (var conn in value.Connections)
                {
                    Connections.Add(conn);
                }
                ConnectorConfigs.Clear();
                foreach (var config in value.ConnectorConfigs)
                {
                    ConnectorConfigs.Add(config);
                }
            }
        }
        
        public void SelectNode(VisualNode? node)
        {
            ClearSelection();

            if (node != null)
            {
                node.IsSelected = true;
                SelectedNode = node;
            }
        }
        
        public void ClearSelection()
        {
            foreach (var node in Nodes)
            {
                node.IsSelected = false;
            }
            SelectedNode = null;
        }

        /// <summary>
        /// Migrates old node configurations to the latest format, fixing missing or invalid addresses.
        /// </summary>
        public void MigrateNodes()
        {
            foreach (var node in Nodes)
            {
                // Fix InputInt nodes with missing or default OutputAddress (should match Input1)
                if (node.ElementType == PlcElementType.InputInt)
                {
                    if (node.OutputAddress == null || (node.OutputAddress.Area == PlcArea.Coil && node.OutputAddress.Address == 0))
                    {
                        node.OutputAddress = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = (node.Input1Address != null && node.Input1Address.Address >= 0) ? node.Input1Address.Address : 1
                        };
                    }
                }

                // Fix InputBool nodes with missing or default OutputAddress (should match Input1)
                if (node.ElementType == PlcElementType.InputBool)
                {
                    if (node.OutputAddress == null || (node.OutputAddress.Area == PlcArea.Coil && node.OutputAddress.Address == 0))
                    {
                        node.OutputAddress = new PlcAddressReference
                        {
                            Area = PlcArea.Coil,
                            Address = (node.Input1Address != null && node.Input1Address.Address >= 0) ? node.Input1Address.Address : 1
                        };
                    }
                }

                // Fix any nodes with Coil:0 addresses (invalid - should be at least 1)
                if (node.OutputAddress != null && node.OutputAddress.Area == PlcArea.Coil && node.OutputAddress.Address == 0)
                {
                    node.OutputAddress.Address = 1;
                }

                if (node.Input1Address != null && node.Input1Address.Area == PlcArea.Coil && node.Input1Address.Address == 0)
                {
                    node.Input1Address.Address = 1;
                }

                if (node.Input2Address != null && node.Input2Address.Area == PlcArea.Coil && node.Input2Address.Address == 0)
                {
                    node.Input2Address.Address = 1;
                }
            }
        }
        
        public void CreateConnection(string sourceNodeId, string targetNodeId, string targetConnector = "Input1")
        {
            System.Diagnostics.Debug.WriteLine($"CreateConnection called: {sourceNodeId} -> {targetNodeId} ({targetConnector})");
            
            // Check if connection already exists
            var existingConnection = Connections.FirstOrDefault(c => 
                c.SourceNodeId == sourceNodeId && 
                c.TargetNodeId == targetNodeId && 
                c.TargetConnector == targetConnector);
            
            if (existingConnection != null)
            {
                System.Diagnostics.Debug.WriteLine($"Connection already exists, skipping");
                return; // Connection already exists
            }
            
            var sourceNode = Nodes.FirstOrDefault(n => n.Id == sourceNodeId);
            var targetNode = Nodes.FirstOrDefault(n => n.Id == targetNodeId);
            
            System.Diagnostics.Debug.WriteLine($"Source node found: {sourceNode != null}, Target node found: {targetNode != null}");
            
            if (sourceNode != null && targetNode != null)
            {
                var connection = new NodeConnection(sourceNodeId, targetNodeId, targetConnector);
                
                UpdateConnectionPoints(connection, sourceNode, targetNode);
                Connections.Add(connection);
                
                System.Diagnostics.Debug.WriteLine($"Connection added to collection. Total: {Connections.Count}");
                System.Diagnostics.Debug.WriteLine($"Connection points: Start({connection.StartX}, {connection.StartY}) -> End({connection.EndX}, {connection.EndY})");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create connection - missing nodes");
            }
        }
        
        public void UpdateNodeConnections(string nodeId)
        {
            var node = Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return;
            
            // Update all connections involving this node
            var connectionsToUpdate = Connections.Where(c => 
                c.SourceNodeId == nodeId || c.TargetNodeId == nodeId).ToList();
            
            foreach (var connection in connectionsToUpdate)
            {
                var sourceNode = Nodes.FirstOrDefault(n => n.Id == connection.SourceNodeId);
                var targetNode = Nodes.FirstOrDefault(n => n.Id == connection.TargetNodeId);
                
                if (sourceNode != null && targetNode != null)
                {
                    UpdateConnectionPoints(connection, sourceNode, targetNode);
                }
            }
        }
        
        public void UpdateAllConnections()
        {
            foreach (var connection in Connections)
            {
                var sourceNode = Nodes.FirstOrDefault(n => n.Id == connection.SourceNodeId);
                var targetNode = Nodes.FirstOrDefault(n => n.Id == connection.TargetNodeId);
                
                if (sourceNode != null && targetNode != null)
                {
                    UpdateConnectionPoints(connection, sourceNode, targetNode);
                }
            }
        }
        
        private void UpdateConnectionPoints(NodeConnection connection, VisualNode sourceNode, VisualNode targetNode)
        {
            // Source point (output connector on right side of source node)
            connection.StartX = sourceNode.X + sourceNode.Width - 6;
            connection.StartY = sourceNode.Y + sourceNode.Height / 2;
            
            // Target point (input connector on left side of target node)
            connection.EndX = targetNode.X + 6;
            connection.EndY = targetNode.Y + targetNode.Height / 2;
            
            System.Diagnostics.Debug.WriteLine($"Updated connection points: Start({connection.StartX}, {connection.StartY}) -> End({connection.EndX}, {connection.EndY})");
        }
        
        public void UpdateNodeValues(bool showLiveValues)
        {
            ShowLiveValues = showLiveValues;
            
            foreach (var node in Nodes)
            {
                node.ShowLiveValues = showLiveValues;
            }
        }
        
        public void RefreshSimulationValues()
        {
            // This method is called by the simulation service to update node values
            // The actual value updates are handled via the IVisualSimulationService
            if (!ShowLiveValues) return;
        }
        public ObservableCollection<PlcSimulationElement> ConvertToSimulationElements()
        {
            var elements = new ObservableCollection<PlcSimulationElement>();
            
            // Pre-calculate lookups for O(1) retrieval
            var nodeDict = Nodes.ToDictionary(n => n.Id);
            var connectionLookup = Connections.ToLookup(c => c.TargetNodeId);

            foreach (var visualNode in Nodes)
            {
                var element = new PlcSimulationElement
                {
                    Id = visualNode.Id,
                    ElementType = visualNode.ElementType,
                    Input1 = visualNode.Input1Address,
                    Input2 = visualNode.Input2Address,
                    Output = visualNode.OutputAddress,
                    TimerPresetMs = visualNode.TimerPresetMs,
                    SetDominant = visualNode.SetDominant,
                    CounterPreset = visualNode.CounterPreset,
                    CompareValue = visualNode.CompareValue
                };
                
                // Map connections to input addresses using optimized lookups
                MapConnectionsToInputs(visualNode, element, nodeDict, connectionLookup);
                
                elements.Add(element);
            }
            
            return elements;
        }
        
        private void MapConnectionsToInputs(VisualNode visualNode, PlcSimulationElement element,
            System.Collections.Generic.Dictionary<string, VisualNode> nodeDict,
            System.Linq.ILookup<string, NodeConnection> connectionLookup)
        {
            // Find connections that target this node using optimized lookup - O(1)
            var inputConnections = connectionLookup[visualNode.Id];
            
            foreach (var connection in inputConnections)
            {
                // Find source node using optimized dictionary - O(1)
                if (nodeDict.TryGetValue(connection.SourceNodeId, out var sourceNode))
                {
                    var targetAddress = new PlcAddressReference
                    {
                        Area = sourceNode.OutputAddress.Area,
                        Address = sourceNode.OutputAddress.Address,
                        Not = sourceNode.OutputAddress.Not
                    };
                    
                    if (connection.TargetConnector == "Input1")
                    {
                        element.Input1 = targetAddress;
                    }
                    else if (connection.TargetConnector == "Input2")
                    {
                        element.Input2 = targetAddress;
                    }
                }
            }
        }
        
        partial void OnShowLiveValuesChanged(bool value)
        {
            UpdateNodeValues(value);
            
            // Start or stop the visual simulation service based on the toggle
            var visualSimulationService = App.ServiceProvider?.GetService<IVisualSimulationService>();
            if (visualSimulationService != null)
            {
                if (value)
                {
                    visualSimulationService.Start(this);
                }
                else
                {
                    visualSimulationService.Stop();
                }
            }
        }
    }
}
