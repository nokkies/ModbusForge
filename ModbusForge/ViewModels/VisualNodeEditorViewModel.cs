using System;
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
        
        public VisualNodeEditorViewModel()
        {
            // Start with empty canvas - no sample nodes
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
                Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 0 }
            };
            
            var input2 = new VisualNode
            {
                Name = "Input 2",
                ElementType = PlcElementType.Input,
                X = 50,
                Y = 200,
                Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 }
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
                if (Enum.TryParse<PlcElementType>(nodeType, out var elementType))
                {
                    var newNode = new VisualNode
                    {
                        Name = $"{elementType} Node",
                        ElementType = elementType,
                        X = 100 + (Nodes.Count * 30) % 400,
                        Y = 100 + (Nodes.Count * 30) % 300
                    };
                    
                    // Set default parameters based on type
                    switch (elementType)
                    {
                        case PlcElementType.Input:
                            newNode.Name = "Input";
                            newNode.Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = -1 };
                            break;
                        case PlcElementType.Output:
                            newNode.Name = "Output";
                            newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = -1 };
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
                            newNode.CompareValue = 0;
                            break;
                    }
                    
                    Nodes.Add(newNode);
                    SelectedNode = newNode;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding node: {ex.Message}");
            }
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
        
        public void SelectNode(VisualNode node)
        {
            // Clear previous selection
            foreach (var n in Nodes)
            {
                n.IsSelected = false;
            }
            
            node.IsSelected = true;
            SelectedNode = node;
        }
        
        public void ClearSelection()
        {
            foreach (var node in Nodes)
            {
                node.IsSelected = false;
            }
            SelectedNode = null;
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
            // For now, use fixed calculations until we can properly access actual positions
            // This ensures connections work while we debug the positioning issue
            
            // Source point (output connector on right side of source node)
            connection.StartX = sourceNode.X + 114;  // 120 - 6 = center of output connector
            connection.StartY = sourceNode.Y + 30;   // 60 / 2 = vertical center
            
            // Target point (input connector on left side of target node)
            connection.EndX = targetNode.X + 6;      // center of input connector
            connection.EndY = targetNode.Y + 30;    // vertical center
            
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
            // This method will be called by the simulation service to update node values
            if (!ShowLiveValues) return;
            
            foreach (var node in Nodes)
            {
                // Update the CurrentValue property based on simulation state
                // This will be implemented when integrating with the simulation service
                node.CurrentValue = GetNodeSimulationValue(node);
            }
        }
        
        private bool GetNodeSimulationValue(VisualNode node)
        {
            // Placeholder for simulation value retrieval
            // This will be connected to the actual simulation service
            return false;
        }
        
        // Convert visual nodes to PLC simulation elements for the simulation engine
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
                    System.Diagnostics.Debug.WriteLine("VisualSimulationService started");
                }
                else
                {
                    visualSimulationService.Stop();
                    System.Diagnostics.Debug.WriteLine("VisualSimulationService stopped");
                }
            }
        }
    }
}
