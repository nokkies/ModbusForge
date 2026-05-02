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
                System.Diagnostics.Debug.WriteLine($"DEBUG: AddNode called with nodeType: {nodeType}");
                
                if (!Enum.TryParse<PlcElementType>(nodeType, out var elementType))
                {
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Failed to parse nodeType: {nodeType}");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"DEBUG: Successfully parsed elementType: {elementType}");
                
                var newNode = new VisualNode
                {
                    Id = Guid.NewGuid().ToString(),
                    ElementType = elementType,
                    X = 100 + Nodes.Count * 30,
                    Y = 100 + Nodes.Count * 30,
                    Width = 240,
                    Height = 140
                };
                
                System.Diagnostics.Debug.WriteLine($"DEBUG: Created new node with ID: {newNode.Id}");
                
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
                    case PlcElementType.InputBool:
                        newNode.Name = "Input BOOL";
                        newNode.Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = GetNextAvailableAddress(PlcArea.Coil) };
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = newNode.Input1Address.Address };
                        break;
                    case PlcElementType.InputInt:
                        newNode.Name = "Input INT";
                        newNode.Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = GetNextAvailableAddress(PlcArea.HoldingRegister) };
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = newNode.Input1Address.Address };
                        break;
                    case PlcElementType.OutputBool:
                        newNode.Name = "Output BOOL";
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = GetNextAvailableAddress(PlcArea.Coil) };
                        break;
                    case PlcElementType.OutputInt:
                        newNode.Name = "Output INT";
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
                        newNode.Name = "ADD";
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = GetNextAvailableAddress(PlcArea.HoldingRegister) };
                        break;
                    case PlcElementType.MATH_SUB:
                        newNode.Name = "SUB";
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = GetNextAvailableAddress(PlcArea.HoldingRegister) };
                        break;
                    case PlcElementType.MATH_MUL:
                        newNode.Name = "MUL";
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = GetNextAvailableAddress(PlcArea.HoldingRegister) };
                        break;
                    case PlcElementType.MATH_DIV:
                        newNode.Name = "DIV";
                        newNode.OutputAddress = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = GetNextAvailableAddress(PlcArea.HoldingRegister) };
                        break;
                }
                
                System.Diagnostics.Debug.WriteLine($"DEBUG: About to add node to Nodes collection. Current count: {Nodes.Count}");
                Nodes.Add(newNode);
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node added successfully. New count: {Nodes.Count}");
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
