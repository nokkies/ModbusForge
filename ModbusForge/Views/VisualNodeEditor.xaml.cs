using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using ModbusForge.Models;
using ModbusForge.ViewModels;
using ModbusForge.Services;
using Modbus.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ModbusForge.Views
{
    public partial class VisualNodeEditor : UserControl
    {
        private VisualNodeEditorViewModel? _viewModel;
        private bool _isDraggingNode = false;
        private bool _isConnecting = false;
        private VisualNode? _draggedNode = null;
        private Point _dragStartPoint;
        private Point _originalNodePosition;
        private DispatcherTimer? _liveUpdateTimer;
        
        public VisualNodeEditor()
        {
            InitializeComponent();
            DataContextChanged += VisualNodeEditor_DataContextChanged;
            KeyDown += VisualNodeEditor_KeyDown;
            MouseUp += VisualNodeEditor_MouseUp;
            
            // Initialize live update timer
            _liveUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250) // Update 4 times per second
            };
            _liveUpdateTimer.Tick += LiveUpdateTimer_Tick;
        }
        
        private void VisualNodeEditor_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Global mouse up handler - catch any missed mouse releases
            if (_isConnecting)
            {
                // If we're in connection mode and mouse is released anywhere,
                // cancel the connection to prevent getting stuck
                CancelConnection();
            }
        }
        
        private void VisualNodeEditor_KeyDown(object sender, KeyEventArgs e)
        {
            // Handle keyboard shortcuts
            switch (e.Key)
            {
                case Key.Delete:
                case Key.Back:
                    DeleteSelectedNode();
                    break;
                case Key.Escape:
                    CancelConnection();
                    break;
            }
        }

        private void DeleteSelectedNode()
        {
            if (_viewModel == null) return;

            // Find selected node (simple implementation - could be enhanced with visual selection)
            var nodeToDelete = _viewModel.Nodes.LastOrDefault(); // For now, delete last added node
            if (nodeToDelete != null)
            {
                // Remove connections to this node first
                var connectionsToRemove = _viewModel.Connections
                    .Where(c => c.SourceNodeId == nodeToDelete.Id || c.TargetNodeId == nodeToDelete.Id)
                    .ToList();
                
                foreach (var connection in connectionsToRemove)
                {
                    _viewModel.Connections.Remove(connection);
                }

                // Remove the node
                _viewModel.Nodes.Remove(nodeToDelete);
                
                // Refresh canvas
                RefreshCanvas();
                RefreshConnections();
                
                System.Diagnostics.Debug.WriteLine($"Deleted node {nodeToDelete.Id} (type: {nodeToDelete.ElementType})");
                AddDebugMessage($"Deleted node {nodeToDelete.Id} (type: {nodeToDelete.ElementType})");
            }
        }
        
        private void CancelConnection()
        {
            if (_isConnecting)
            {
                _viewModel.PendingConnectionStart = null;
                TempConnectionLine.Visibility = Visibility.Collapsed;
                _isConnecting = false;
                
                // Release any mouse capture from any element
                if (Mouse.Captured is UIElement captured)
                {
                    captured.ReleaseMouseCapture();
                }
                
                // Also release capture from the canvas itself
                if (NodeCanvas.IsMouseCaptured)
                {
                    NodeCanvas.ReleaseMouseCapture();
                }
            }
        }
        
        private Point GetCanvasPosition(MouseEventArgs e, FrameworkElement element)
        {
            var canvas = NodeCanvas;
            if (canvas == null) return new Point(0, 0);
            
            // Get position relative to the canvas, accounting for scroll offsets
            var position = e.GetPosition(canvas);
            
            // If we're inside a ScrollViewer, we need to account for scroll offsets
            var scrollViewer = FindParent<ScrollViewer>(canvas);
            if (scrollViewer != null)
            {
                position.X += scrollViewer.HorizontalOffset;
                position.Y += scrollViewer.VerticalOffset;
            }
            
            return position;
        }
        
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            
            if (parentObject == null) return null;
            
            if (parentObject is T parent)
                return parent;
            else
                return FindParent<T>(parentObject);
        }
        
        private void VisualNodeEditor_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _viewModel = DataContext as VisualNodeEditorViewModel;
            if (_viewModel != null)
            {
                // Subscribe to collection changes
                _viewModel.Nodes.CollectionChanged += Nodes_CollectionChanged;
                _viewModel.Connections.CollectionChanged += Connections_CollectionChanged;
                // Initial render of existing nodes and connections
                RefreshCanvas();
                RefreshConnections();
                
                // Subscribe to ShowLiveValues changes
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                
                // Start timer if live values are enabled
                if (_viewModel.ShowLiveValues)
                {
                    _liveUpdateTimer?.Start();
                }
            }
        }
        
        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VisualNodeEditorViewModel.ShowLiveValues))
            {
                if (_viewModel?.ShowLiveValues == true)
                {
                    _liveUpdateTimer?.Start();
                }
                else
                {
                    _liveUpdateTimer?.Stop();
                }
            }
        }
        
        private void LiveUpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_viewModel == null || !_viewModel.ShowLiveValues) return;
            
            // Update all InputInt and OutputInt nodes with current DataStore values
            foreach (var node in _viewModel.Nodes)
            {
                if (node.ElementType == PlcElementType.InputInt || 
                    node.ElementType == PlcElementType.OutputInt ||
                    node.ElementType == PlcElementType.Input ||
                    node.ElementType == PlcElementType.Output)
                {
                    // Toggle ShowLiveValues to force update
                    var wasShowing = node.ShowLiveValues;
                    node.ShowLiveValues = false;
                    node.ShowLiveValues = wasShowing;
                }
            }
        }
        
        private void Connections_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Debug: Log collection changes
            System.Diagnostics.Debug.WriteLine($"Connections collection changed: {e.Action}");
            RefreshConnections();
        }
        
        private void Nodes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RefreshCanvas();
            // Defer connection refresh until after the layout pass so TransformToAncestor works
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(RefreshConnections));
        }
        
        private void RefreshCanvas()
        {
            if (_viewModel == null || NodeCanvas == null) return;
            
            // Clear existing nodes from canvas (keep connections and temp line)
            var elementsToRemove = new List<UIElement>();
            foreach (UIElement child in NodeCanvas.Children)
            {
                if (child is Border border && border.DataContext is VisualNode)
                {
                    elementsToRemove.Add(child);
                }
            }
            
            foreach (var element in elementsToRemove)
            {
                NodeCanvas.Children.Remove(element);
            }
            
            // Add all nodes to canvas
            foreach (var node in _viewModel.Nodes)
            {
                var nodeElement = CreateNodeElement(node);
                NodeCanvas.Children.Add(nodeElement);
                Canvas.SetLeft(nodeElement, node.X);
                Canvas.SetTop(nodeElement, node.Y);
            }
        }
        
        private void RefreshConnections()
        {
            if (_viewModel == null || NodeCanvas == null) return;
            
            // Clear existing connection lines from canvas
            var elementsToRemove = new List<UIElement>();
            foreach (UIElement child in NodeCanvas.Children)
            {
                if (child is Line line && line != TempConnectionLine)
                    elementsToRemove.Add(line);
            }
            foreach (var element in elementsToRemove)
                NodeCanvas.Children.Remove(element);
            
            // Draw connections using actual visual-tree positions where available
            foreach (var connection in _viewModel.Connections)
            {
                var startPoint = GetActualConnectorPosition(connection.SourceNodeId, "Output");
                var endPoint   = GetActualConnectorPosition(connection.TargetNodeId, connection.TargetConnector);

                // Update model so ViewModel stays consistent
                if (startPoint.X != 0 || startPoint.Y != 0)
                {
                    connection.StartX = startPoint.X;
                    connection.StartY = startPoint.Y;
                }
                if (endPoint.X != 0 || endPoint.Y != 0)
                {
                    connection.EndX = endPoint.X;
                    connection.EndY = endPoint.Y;
                }

                var line = CreateConnectionLine(connection);
                // Right-click on a wire → delete it
                var capturedConn = connection;
                line.MouseRightButtonDown += (s, e) =>
                {
                    var menu = new ContextMenu();
                    var del  = new MenuItem { Header = "Delete Connection" };
                    del.Click += (_, __) => _viewModel.Connections.Remove(capturedConn);
                    menu.Items.Add(del);
                    ((Line)s).ContextMenu = menu;
                    menu.IsOpen = true;
                    e.Handled = true;
                };
                NodeCanvas.Children.Add(line);
            }
        }
        
        private Line CreateConnectionLine(NodeConnection connection)
        {
            var line = new Line
            {
                X1 = connection.StartX,
                Y1 = connection.StartY,
                X2 = connection.EndX,
                Y2 = connection.EndY,
                Stroke = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                StrokeThickness = 2
            };
            
            // Set dash array based on connection state
            if (connection.IsConnected)
            {
                line.StrokeDashArray = new DoubleCollection { 1, 0 };
            }
            else
            {
                line.StrokeDashArray = new DoubleCollection { 5, 5 };
            }
            
            return line;
        }
        
        private Border CreateNodeElement(VisualNode node)
        {
            var border = new Border
            {
                Style = (Style)FindResource("NodeStyle"),
                Width = node.Width,
                Height = node.Height,
                DataContext = node
            };
            
            border.MouseLeftButtonDown += Node_MouseLeftButtonDown;
            border.MouseMove += Node_MouseMove;
            border.MouseLeftButtonUp += Node_MouseLeftButtonUp;
            
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Header
            var header = new Border
            {
                Background = new SolidColorBrush(GetElementColor(node.ElementType)),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Padding = new Thickness(8, 6, 8, 6)
            };
            var headerStack = new StackPanel();
            var headerText = new TextBlock 
            { 
                Text = node.DisplayName, 
                FontWeight = FontWeights.Bold, 
                Foreground = Brushes.White,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var addressText = new TextBlock 
            { 
                Text = node.AddressDisplay, 
                FontWeight = FontWeights.Normal, 
                Foreground = Brushes.White,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            headerStack.Children.Add(headerText);
            if (!string.IsNullOrEmpty(node.AddressDisplay))
            {
                headerStack.Children.Add(addressText);
            }
            
            // Capture references for updates
            var capturedAddressText = addressText;
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            grid.Children.Add(header);
            
            // Content
            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            // Input connectors
            var inputStack = new StackPanel 
            { 
                Orientation = Orientation.Vertical, 
                VerticalAlignment = VerticalAlignment.Center 
            };
            
            var input1 = CreateConnector(node.Id, "Input1", true);
            inputStack.Children.Add(input1);
            
            if (node.HasSecondInput)
            {
                var input2 = CreateConnector(node.Id, "Input2", true);
                inputStack.Children.Add(input2);
            }
            
            Grid.SetColumn(inputStack, 0);
            contentGrid.Children.Add(inputStack);
            
            // Node content — vertically centred so it never overlaps the connector dots
            var contentStack = new StackPanel 
            { 
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var nameText = new TextBlock 
            { 
                Text = node.Name, 
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = node.Name
            };
            // Only show name label for non-I/O nodes (I/O nodes have header + inline controls)
            if (node.ElementType != PlcElementType.Input && node.ElementType != PlcElementType.Output &&
                node.ElementType != PlcElementType.InputBool && node.ElementType != PlcElementType.InputInt &&
                node.ElementType != PlcElementType.OutputBool && node.ElementType != PlcElementType.OutputInt)
            {
                contentStack.Children.Add(nameText);
            }
            
            // Live value indicator — always present, hidden until ShowLiveValues is on
            var liveText = new TextBlock
            {
                Text = "",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            contentStack.Children.Add(liveText);
            
            // Inline address editing for I/O blocks (replaces the old 🔗 popup)
            if (node.ElementType == PlcElementType.Input || node.ElementType == PlcElementType.Output ||
                node.ElementType == PlcElementType.InputBool || node.ElementType == PlcElementType.InputInt ||
                node.ElementType == PlcElementType.OutputBool || node.ElementType == PlcElementType.OutputInt)
            {
                var isInputType = node.ElementType == PlcElementType.Input ||
                                  node.ElementType == PlcElementType.InputBool ||
                                  node.ElementType == PlcElementType.InputInt;
                var addrRef = isInputType ? node.Input1Address : node.OutputAddress;

                var inlinePanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // Area ComboBox
                var areaCombo = new System.Windows.Controls.ComboBox
                {
                    Width = 200,
                    Height = 26,
                    FontSize = 11,
                    ItemsSource = Enum.GetValues(typeof(PlcArea)),
                    SelectedItem = addrRef.Area,
                    ToolTip = "Modbus area"
                };
                areaCombo.SelectionChanged += (s, ev) =>
                {
                    if (areaCombo.SelectedItem is PlcArea area)
                    {
                        addrRef.Area = area;
                        capturedAddressText.Text = node.AddressDisplay;
                    }
                };
                inlinePanel.Children.Add(areaCombo);

                // Address TextBox
                var addrBox = new TextBox
                {
                    Width = 200,
                    Height = 28,
                    FontSize = 11,
                    Text = addrRef.Address >= 0 ? addrRef.Address.ToString() : "",
                    ToolTip = "Modbus address",
                    Margin = new Thickness(0, 2, 0, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                addrBox.LostFocus += (s, ev) =>
                {
                    if (int.TryParse(addrBox.Text, out int addr) && addr >= 0)
                    {
                        addrRef.Address = addr;
                        capturedAddressText.Text = node.AddressDisplay;
                    }
                };
                addrBox.KeyDown += (s, ev) =>
                {
                    if (ev.Key == System.Windows.Input.Key.Enter)
                    {
                        if (int.TryParse(addrBox.Text, out int addr) && addr >= 0)
                        {
                            addrRef.Address = addr;
                            capturedAddressText.Text = node.AddressDisplay;
                        }
                        // Move focus away to commit
                        System.Windows.Input.Keyboard.ClearFocus();
                    }
                };
                inlinePanel.Children.Add(addrBox);

                contentStack.Children.Add(inlinePanel);
            }

            // React dynamically to CurrentValue / ShowLiveValues changes
            var capturedHeader = header;
            var capturedLiveText = liveText;
            var capturedHeaderText = headerText;
            var originalColor = GetElementColor(node.ElementType);
            node.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(VisualNode.CurrentValue) ||
                    e.PropertyName == nameof(VisualNode.ShowLiveValues))
                {
                    if (node.ShowLiveValues)
                    {
                        // Show appropriate live values based on element type
                        switch (node.ElementType)
                        {
                            case PlcElementType.InputBool:
                            case PlcElementType.OutputBool:
                                // Boolean types - show ON/OFF
                                capturedLiveText.Text = node.CurrentValue ? "● ON" : "● OFF";
                                capturedLiveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
                                break;
                                
                            case PlcElementType.InputInt:
                                // Integer input - show actual register values from DataStore
                                var actualValue = GetActualRegisterValue(node);
                                capturedLiveText.Text = $"● VAL:{actualValue}";
                                capturedLiveText.Foreground = Brushes.Cyan;
                                
                                // Debug output to both VS Debug and potential debug collection
                                System.Diagnostics.Debug.WriteLine($"InputInt: Reading from {node.Input1Address?.Area}:{node.Input1Address?.Address} = {actualValue}");
                                AddDebugMessage($"InputInt: Reading from {node.Input1Address?.Area}:{node.Input1Address?.Address} = {actualValue}");
                                break;
                                
                            case PlcElementType.OutputInt:
                                // Integer output - show the value being written to output address
                                var outputValue = GetOutputRegisterValue(node);
                                capturedLiveText.Text = $"● VAL:{outputValue}";
                                capturedLiveText.Foreground = Brushes.Cyan;
                                
                                // Debug output
                                System.Diagnostics.Debug.WriteLine($"OutputInt: Writing to {node.OutputAddress?.Area}:{node.OutputAddress?.Address} = {outputValue}");
                                AddDebugMessage($"OutputInt: Writing to {node.OutputAddress?.Area}:{node.OutputAddress?.Address} = {outputValue}");
                                break;
                                
                            case PlcElementType.Input:
                            case PlcElementType.Output:
                                // Legacy types - show based on address area
                                if ((node.Input1Address?.Area == PlcArea.HoldingRegister) ||
                                    (node.Input1Address?.Area == PlcArea.InputRegister))
                                {
                                    var actualLegacyValue = GetActualRegisterValue(node);
                                    capturedLiveText.Text = $"● VAL:{actualLegacyValue}";
                                    capturedLiveText.Foreground = Brushes.Cyan;
                                    
                                    // Debug output
                                    System.Diagnostics.Debug.WriteLine($"Legacy Input/Output: Reading from {node.Input1Address?.Area}:{node.Input1Address?.Address} = {actualLegacyValue}");
                                    AddDebugMessage($"Legacy Input/Output: Reading from {node.Input1Address?.Area}:{node.Input1Address?.Address} = {actualLegacyValue}");
                                }
                                else
                                {
                                    capturedLiveText.Text = node.CurrentValue ? "● ON" : "● OFF";
                                    capturedLiveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
                                }
                                break;
                                
                            case PlcElementType.MATH_ADD:
                            case PlcElementType.MATH_SUB:
                            case PlcElementType.MATH_MUL:
                            case PlcElementType.MATH_DIV:
                                // Math operations - show the result value from output address
                                var mathResult = GetOutputRegisterValue(node);
                                capturedLiveText.Text = $"● VAL:{mathResult}";
                                capturedLiveText.Foreground = Brushes.Orange;
                                
                                // Debug output
                                System.Diagnostics.Debug.WriteLine($"Math {node.ElementType}: Result = {mathResult}");
                                AddDebugMessage($"Math {node.ElementType}: Result = {mathResult}");
                                break;
                                
                            case PlcElementType.COMPARE_EQ:
                            case PlcElementType.COMPARE_NE:
                            case PlcElementType.COMPARE_GT:
                            case PlcElementType.COMPARE_LT:
                            case PlcElementType.COMPARE_GE:
                            case PlcElementType.COMPARE_LE:
                                // Comparison operations - show boolean result
                                capturedLiveText.Text = node.CurrentValue ? "● TRUE" : "● FALSE";
                                capturedLiveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
                                break;
                                
                            default:
                                // Logic blocks - show ON/OFF
                                capturedLiveText.Text = node.CurrentValue ? "● ON" : "● OFF";
                                capturedLiveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
                                break;
                        }
                        capturedLiveText.Visibility = Visibility.Visible;
                        capturedHeader.Background = new SolidColorBrush(node.CurrentValue
                            ? Color.FromRgb(40, 160, 40)
                            : Color.FromRgb(160, 40, 40));
                    }
                    else
                    {
                        capturedLiveText.Visibility = Visibility.Collapsed;
                        capturedHeader.Background = new SolidColorBrush(originalColor);
                    }
                }
                // Update header and address when properties change
                if (e.PropertyName == nameof(VisualNode.Input1Address) ||
                    e.PropertyName == nameof(VisualNode.ElementType))
                {
                    capturedHeaderText.Text = node.DisplayName;
                    capturedAddressText.Text = node.AddressDisplay;
                }
            };
            // Also update when inner address properties change (e.g. after right-click config)
            node.Input1Address.PropertyChanged  += (s, e) => 
            {
                capturedHeaderText.Text = node.DisplayName;
                capturedAddressText.Text = node.AddressDisplay;
            };
            node.OutputAddress.PropertyChanged  += (s, e) => 
            {
                capturedHeaderText.Text = node.DisplayName;
                capturedAddressText.Text = node.AddressDisplay;
            };
            
            Grid.SetColumn(contentStack, 1);
            contentGrid.Children.Add(contentStack);
            
            // Output connector
            var outputStack = new StackPanel 
            { 
                Orientation = Orientation.Vertical, 
                VerticalAlignment = VerticalAlignment.Center 
            };
            var output = CreateConnector(node.Id, "Output", false);
            outputStack.Children.Add(output);
            
            // Debug: Verify output connector creation
            System.Diagnostics.Debug.WriteLine($"Created output connector for {node.Id}, style: {output.Style}, actual width: {output.ActualWidth}, actual height: {output.ActualHeight}");
            
            Grid.SetColumn(outputStack, 2);
            contentGrid.Children.Add(outputStack);
            
            Grid.SetRow(contentGrid, 1);
            grid.Children.Add(contentGrid);
            
            // Footer with inline editable parameters
            if (node.HasParameters || node.ElementType == PlcElementType.RS)
            {
                var footer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                    CornerRadius = new CornerRadius(0, 0, 6, 6),
                    Padding = new Thickness(4, 2, 4, 2)
                };
                var footerPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                switch (node.ElementType)
                {
                    case PlcElementType.TON:
                    case PlcElementType.TOF:
                    case PlcElementType.TP:
                    {
                        footerPanel.Children.Add(new TextBlock { Text = "ms:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                        var timerBox = new TextBox { Width = 50, Height = 18, FontSize = 10, Text = node.TimerPresetMs.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
                        timerBox.LostFocus += (s, ev) => { if (int.TryParse(timerBox.Text, out int v) && v >= 0) node.TimerPresetMs = v; };
                        footerPanel.Children.Add(timerBox);
                        break;
                    }
                    case PlcElementType.CTU:
                    case PlcElementType.CTD:
                    case PlcElementType.CTC:
                    {
                        footerPanel.Children.Add(new TextBlock { Text = "Pre:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                        var counterBox = new TextBox { Width = 50, Height = 18, FontSize = 10, Text = node.CounterPreset.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
                        counterBox.LostFocus += (s, ev) => { if (int.TryParse(counterBox.Text, out int v)) node.CounterPreset = v; };
                        footerPanel.Children.Add(counterBox);
                        break;
                    }
                    case PlcElementType.COMPARE_EQ:
                    case PlcElementType.COMPARE_NE:
                    case PlcElementType.COMPARE_GT:
                    case PlcElementType.COMPARE_LT:
                    case PlcElementType.COMPARE_GE:
                    case PlcElementType.COMPARE_LE:
                    {
                        footerPanel.Children.Add(new TextBlock { Text = "Val:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                        var cmpBox = new TextBox { Width = 50, Height = 18, FontSize = 10, Text = node.CompareValue.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
                        cmpBox.LostFocus += (s, ev) => { if (int.TryParse(cmpBox.Text, out int v)) node.CompareValue = v; };
                        footerPanel.Children.Add(cmpBox);
                        break;
                    }
                    case PlcElementType.MATH_ADD:
                    case PlcElementType.MATH_SUB:
                    case PlcElementType.MATH_MUL:
                    case PlcElementType.MATH_DIV:
                    {
                        footerPanel.Children.Add(new TextBlock { Text = "Const:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                        var mathBox = new TextBox { Width = 50, Height = 18, FontSize = 10, Text = node.CompareValue.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
                        mathBox.LostFocus += (s, ev) => { if (int.TryParse(mathBox.Text, out int v)) node.CompareValue = v; };
                        footerPanel.Children.Add(mathBox);
                        break;
                    }
                    case PlcElementType.RS:
                    {
                        footerPanel.Children.Add(new TextBlock { Text = "Set Dom:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                        var setDomCheck = new System.Windows.Controls.CheckBox { IsChecked = node.SetDominant, VerticalAlignment = VerticalAlignment.Center };
                        setDomCheck.Checked += (s, ev) => node.SetDominant = true;
                        setDomCheck.Unchecked += (s, ev) => node.SetDominant = false;
                        footerPanel.Children.Add(setDomCheck);
                        break;
                    }
                }

                footer.Child = footerPanel;
                Grid.SetRow(footer, 2);
                grid.Children.Add(footer);
            }
            
            border.Child = grid;
            return border;
        }
        
        private Ellipse CreateConnector(string nodeId, string connectorType, bool isInput)
        {
            var node = _viewModel?.Nodes.FirstOrDefault(n => n.Id == nodeId);
            string toolTipText = "Internal connection";
            
            // All connectors are now internal connections only
            // Use the configure button on I/O blocks to set addresses
            toolTipText = isInput ? "Internal input connector" : "Internal output connector";
            
            var ellipse = new Ellipse
            {
                Style = (Style)FindResource(isInput ? "InputConnectorStyle" : "OutputConnectorStyle"),
                Tag = $"{nodeId},{connectorType}",
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = toolTipText
            };
            ellipse.MouseLeftButtonDown += Connector_MouseLeftButtonDown;
            ellipse.MouseRightButtonDown += Connector_MouseRightButtonDown;
            return ellipse;
        }

        
        private void ConfigureButton_Click(object sender, RoutedEventArgs e)
        {
            // DEBUG: Log that the button was clicked
            System.Diagnostics.Debug.WriteLine("DEBUG: ConfigureButton_Click called!");
            
            var button = sender as Button;
            if (button?.Tag == null)
            {
                System.Diagnostics.Debug.WriteLine("DEBUG: Button or Tag is null");
                return;
            }

            var nodeId = button.Tag.ToString();
            var node = _viewModel?.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
            {
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node not found for ID: {nodeId}");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"DEBUG: Found node {node.ElementType} with ID {nodeId}"); // DEBUG: Track which node and button we're working with
            System.Diagnostics.Debug.WriteLine($"DEBUG: Configure button clicked - Node: {node.Name} ({node.ElementType}), ID: {nodeId}");

            // Only I/O blocks should have configure buttons
            if (node.ElementType != PlcElementType.Input && node.ElementType != PlcElementType.Output &&
                node.ElementType != PlcElementType.InputBool && node.ElementType != PlcElementType.InputInt &&
                node.ElementType != PlcElementType.OutputBool && node.ElementType != PlcElementType.OutputInt)
            {
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node type {node.ElementType} not allowed for configure button");
                return;
            }

            // Grab custom entries from parent window so tags reflect real configuration
            var mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
            var customEntries = mainVm?.CustomEntries
                ?? System.Linq.Enumerable.Empty<ModbusForge.Models.CustomEntry>();

            // Determine which address to configure based on block type
            bool isInputType = node.ElementType == PlcElementType.Input || 
                              node.ElementType == PlcElementType.InputBool || 
                              node.ElementType == PlcElementType.InputInt;
            
            string connectorType = isInputType ? "Output" : "Input1";
            string dialogTitle = isInputType ? "Configure Input Tag" : "Configure Output Tag";

            // Pre-populate dialog with current address
            var addrRef = isInputType ? node.Input1Address : node.OutputAddress;
            
            // DEBUG: Show current address before dialog and track object
            System.Diagnostics.Debug.WriteLine($"DEBUG: Before dialog - {node.ElementType} address: {addrRef?.Area}:{addrRef?.Address}");
            System.Diagnostics.Debug.WriteLine($"DEBUG: Address reference object: {addrRef?.GetHashCode()}");
            System.Diagnostics.Debug.WriteLine($"DEBUG: Address reference is null: {addrRef == null}");
            
            // Simple debugging with MessageBox
            MessageBox.Show($"Current address: {addrRef?.Area}:{addrRef?.Address}\nNode: {node.Name}\nType: {node.ElementType}", "Debug Info");
            
            // Also check the node's address properties directly
            if (isInputType)
            {
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node Input1Address - Area:{node.Input1Address?.Area}:{node.Input1Address?.Address}");
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node Input1Address object: {node.Input1Address?.GetHashCode()}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node OutputAddress - Area:{node.OutputAddress?.Area}:{node.OutputAddress?.Address}");
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node OutputAddress object: {node.OutputAddress?.GetHashCode()}");
            }
            
            // Create dialog with initial values
            var initiallyLinked = addrRef?.Address >= 0;
            var initialArea = initiallyLinked ? addrRef.Area : PlcArea.HoldingRegister;
            var initialAddress = initiallyLinked ? addrRef.Address : 0;
            var initiallyInverted = addrRef?.Not ?? false;
            
            // TEMP: Use test dialog to isolate the issue
            var testDialog = new TestDialog(initialArea, initialAddress)
            {
                Owner = Window.GetWindow(this),
                Title = "TEST DIALOG"
            };
            
            testDialog.ShowDialog();
            if (testDialog.DialogResult == true)
            {
                // Update with test dialog results
                addrRef.Area = testDialog.SelectedArea;
                addrRef.Address = testDialog.SelectedAddress;
                addrRef.Not = testDialog.SelectedAddress < 0; // Simplified
                
                // Refresh canvas to show the updated address
                RefreshCanvas();
            }
            
            return; // Skip the original dialog for now
            
            // Original dialog code (temporarily commented out)
            /*
            // DEBUG: Show what we're passing to dialog
            System.Diagnostics.Debug.WriteLine($"DEBUG: Passing to dialog - Area:{initialArea}, Addr:{initialAddress}, Linked:{initiallyLinked}");
            
            var dialog = new ConnectorConfigWindow(nodeId, connectorType, node.Name, customEntries, initialArea, initialAddress, initiallyLinked, initiallyInverted)
            {
                Owner = Window.GetWindow(this),
                Title = dialogTitle
            };
            
            dialog.ShowDialog();
            if (dialog.Result != null && dialog.Result.IsConfigured)
            {
                var result = dialog.Result;
                
                // DEBUG: Show what we got from dialog
                System.Diagnostics.Debug.WriteLine($"DEBUG: Dialog result - Area:{result.Area}, Addr:{result.Address}");
                
                addrRef.Area = result.Area;
                addrRef.Address = result.Address;
                addrRef.Not = result.Not;
                
                // DEBUG: Show what we saved to address reference
                System.Diagnostics.Debug.WriteLine($"DEBUG: After save - {node.ElementType} address: {addrRef.Area}:{addrRef.Address}");
                System.Diagnostics.Debug.WriteLine($"DEBUG: Address reference object after save: {addrRef.GetHashCode()}");
                
                // DEBUG: Check if the node's address reference is still the same object
                var checkAddrRef = isInputType ? node.Input1Address : node.OutputAddress;
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node address ref check - Area:{checkAddrRef.Area}:{checkAddrRef.Address}, Same object: {checkAddrRef.GetHashCode() == addrRef.GetHashCode()}");
                
                // DEBUG: Check the node's address properties directly after save
                if (isInputType)
                {
                    System.Diagnostics.Debug.WriteLine($"DEBUG: AFTER SAVE Node Input1Address - Area:{node.Input1Address?.Area}:{node.Input1Address?.Address}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"DEBUG: AFTER SAVE Node OutputAddress - Area:{node.OutputAddress?.Area}:{node.OutputAddress?.Address}");
                }
                
                // DEBUG: Check if the DisplayName reflects the change
                System.Diagnostics.Debug.WriteLine($"DEBUG: Node DisplayName after save: {node.DisplayName}");
                
                // The address reference should trigger its own property changes automatically
                
                // Update button tooltip to show configured address
                button.ToolTip = $"{node.ElementType}: {result.Area}:{result.Address}{(result.Not ? " NOT" : "")}";
            }
            */
        }

        private void Connector_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Disable right-click configuration - use the configure button instead
            e.Handled = true;
        }

        private Color GetElementColor(PlcElementType elementType)
        {
            return elementType switch
            {
                PlcElementType.Input => Color.FromRgb(76, 175, 80),
                PlcElementType.Output => Color.FromRgb(255, 87, 34),
                PlcElementType.RS => Color.FromRgb(244, 67, 54),
                PlcElementType.AND => Color.FromRgb(33, 150, 243),
                PlcElementType.OR => Color.FromRgb(255, 152, 0),
                PlcElementType.NOT => Color.FromRgb(156, 39, 176),
                PlcElementType.TON or PlcElementType.TOF or PlcElementType.TP => Color.FromRgb(255, 193, 7),
                PlcElementType.CTU or PlcElementType.CTD or PlcElementType.CTC => Color.FromRgb(96, 125, 139),
                var compare when compare.ToString().StartsWith("COMPARE_") => Color.FromRgb(0, 188, 212),
                var math when math.ToString().StartsWith("MATH_") => Color.FromRgb(121, 85, 72),
                _ => Color.FromRgb(158, 158, 158)
            };
        }
        
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null) return;
            
            var canvas = sender as Canvas;
            if (canvas == null) return;
            
            var clickPoint = GetCanvasPosition(e, canvas);
            
            // Cancel any active connection when clicking on empty space
            if (_isConnecting)
            {
                CancelConnection();
                return;
            }
            
            // Check if clicking on empty space to start selection
            var hitTestResult = VisualTreeHelper.HitTest(canvas, clickPoint);
            if (hitTestResult?.VisualHit is Border border && border.DataContext is VisualNode)
            {
                // Clicking on a node - handled by node event handlers
                return;
            }
            
            // Start rubber band selection or clear selection
            _viewModel.ClearSelection();
        }
        
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_viewModel == null) return;
            
            var canvas = sender as Canvas;
            if (canvas == null) return;
            
            var currentPoint = e.GetPosition(canvas);
            
            if (_isDraggingNode && _draggedNode != null)
            {
                // Find the border element for the dragged node
                var nodeBorder = FindNodeBorder(_draggedNode.Id);
                if (nodeBorder != null)
                {
                    // Move the node based on the difference from the original click point
                    var deltaX = currentPoint.X - _dragStartPoint.X;
                    var deltaY = currentPoint.Y - _dragStartPoint.Y;
                    
                    // Update node position based on original position plus delta
                    var newX = _originalNodePosition.X + deltaX;
                    var newY = _originalNodePosition.Y + deltaY;
                    
                    Canvas.SetLeft(nodeBorder, newX);
                    Canvas.SetTop(nodeBorder, newY);
                    
                    // Update the node model
                    _draggedNode.X = newX;
                    _draggedNode.Y = newY;
                    
                    // Update connections
                    _viewModel.UpdateNodeConnections(_draggedNode.Id);
                    
                    // Refresh connection lines to match new positions
                    RefreshConnections();
                }
            }
            else if (_isConnecting && !string.IsNullOrEmpty(_viewModel.PendingConnectionStart))
            {
                // Update temporary connection line using actual connector positions
                var parts = _viewModel.PendingConnectionStart.Split(',');
                var nodeId = parts[0];
                var connectorType = parts[1];
                
                var node = _viewModel.Nodes.FirstOrDefault(n => n.Id == nodeId);
                if (node != null)
                {
                    // Use the new method to get actual connector position
                    var startPoint = GetActualConnectorPosition(nodeId, connectorType);
                    
                    TempConnectionLine.X1 = startPoint.X;
                    TempConnectionLine.Y1 = startPoint.Y;
                    TempConnectionLine.X2 = currentPoint.X;
                    TempConnectionLine.Y2 = currentPoint.Y;
                    TempConnectionLine.Visibility = Visibility.Visible;
                }
            }
        }
        
        private Border FindNodeBorder(string nodeId)
        {
            foreach (UIElement child in NodeCanvas.Children)
            {
                if (child is Border border && border.DataContext is VisualNode node && node.Id == nodeId)
                {
                    return border;
                }
            }
            return null!;
        }
        
        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Reset dragging state only - connections persist until user completes or cancels them
            _isDraggingNode = false;
            _draggedNode = null;
        }
        
        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null) return;
            
            var border = sender as Border;
            if (border?.DataContext is not VisualNode node) return;
            
            // Don't start drag while in connection mode
            if (_isConnecting) return;
            
            var canvas = NodeCanvas;
            var clickPoint = e.GetPosition(canvas);
            
            // Store the initial position and click point
            _draggedNode = node;
            _dragStartPoint = clickPoint;
            _isDraggingNode = true;
            
            // Store the original node position
            _originalNodePosition = new Point(Canvas.GetLeft(border), Canvas.GetTop(border));
            
            // Select the node
            _viewModel.SelectNode(node);
            
            border.CaptureMouse();
            e.Handled = true;
        }
        
        private void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingNode && _draggedNode != null)
            {
                Canvas_MouseMove(NodeCanvas, e);
            }
        }
        
        private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            border?.ReleaseMouseCapture();
            
            Canvas_MouseLeftButtonUp(NodeCanvas, e);
            e.Handled = true;
        }
        
        private void Connector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null) return;
            
            var ellipse = sender as Ellipse;
            if (ellipse == null) return;
            
            var tag = ellipse.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;
            
            var parts = tag.Split(',');
            var nodeId = parts[0];
            var connectorType = parts[1];
            
            // Debug: Log connector clicks
            System.Diagnostics.Debug.WriteLine($"Connector clicked: {nodeId}, {connectorType}");
            
            if (connectorType == "Output")
            {
                // Start connection from output
                _viewModel.PendingConnectionStart = tag;
                _isConnecting = true;
                e.Handled = true;
            }
            else
            {
                // Complete connection to input
                if (!string.IsNullOrEmpty(_viewModel.PendingConnectionStart))
                {
                    var startParts = _viewModel.PendingConnectionStart.Split(',');
                    var startNodeId = startParts[0];
                    
                    // Debug: Log connection creation attempt
                    System.Diagnostics.Debug.WriteLine($"Creating connection: {startNodeId} -> {nodeId}");
                    
                    if (startNodeId != nodeId) // Don't connect to self
                    {
                        _viewModel.CreateConnection(startNodeId, nodeId, connectorType);
                    }
                    
                    // Reset connection state
                    _viewModel.PendingConnectionStart = null;
                    TempConnectionLine.Visibility = Visibility.Collapsed;
                    _isConnecting = false;
                    
                }
                e.Handled = true;
            }
        }
    }
    
    // Helper converter for boolean to dash array
    public class BoolToDashArrayConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isConnected && isConnected)
                return new System.Windows.Media.DoubleCollection { 1, 0 };
            return new System.Windows.Media.DoubleCollection { 5, 5 };
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    // Helper methods for connector position detection
    public partial class VisualNodeEditor
    {
        private Point GetActualConnectorPosition(string nodeId, string connectorType)
        {
            var nodeBorder = FindNodeBorder(nodeId);
            if (nodeBorder == null) return new Point(0, 0);
            
            var connector = FindConnectorInNode(nodeBorder, connectorType);
            if (connector != null)
            {
                try
                {
                    // Get the actual position of the connector center
                    var position = connector.TransformToAncestor(NodeCanvas)
                        .Transform(new Point(connector.ActualWidth / 2, connector.ActualHeight / 2));
                    return position;
                }
                catch
                {
                    // Fallback to calculation if transform fails
                    return CalculateConnectorPosition(nodeId, connectorType);
                }
            }
            
            // Fallback to calculation
            return CalculateConnectorPosition(nodeId, connectorType);
        }
        
        private Ellipse FindConnectorInNode(Border nodeBorder, string connectorType)
        {
            // Recursively search the visual tree for an Ellipse tagged with the connector type
            return FindEllipseByTag(nodeBorder, connectorType);
        }
        
        private Ellipse FindEllipseByTag(DependencyObject parent, string connectorType)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Ellipse ellipse)
                {
                    var tag = ellipse.Tag as string;
                    if (tag != null && tag.Contains(connectorType))
                        return ellipse;
                }
                var result = FindEllipseByTag(child, connectorType);
                if (result != null) return result;
            }
            return null;
        }
        
        private Point CalculateConnectorPosition(string nodeId, string connectorType)
        {
            var node = _viewModel?.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return new Point(0, 0);
            
            // Header ~24px, content fills remaining space.
            // Output & single-input nodes: vertical centre of content.
            // Input1 on dual-input nodes: upper third. Input2: lower two-thirds.
            double headerH = 24;
            double contentH = node.Height - headerH;
            double yBase = node.Y + headerH;

            return connectorType switch
            {
                "Output" => new Point(node.X + node.Width - 6, yBase + contentH / 2),
                "Input2" => new Point(node.X + 6, yBase + contentH * 2 / 3),
                _        => new Point(node.X + 6, yBase + contentH / 3)   // Input1 or default
            };
        }

        private int GetOutputRegisterValue(VisualNode node)
        {
            try
            {
                // For OutputInt, we want to show the value that's being written to the output address
                // This should match what the VisualSimulationService is writing
                
                // Get the visual simulation service to access DataStore
                var serverService = App.ServiceProvider?.GetService<ModbusServerService>();
                if (serverService == null) return 0;

                // Check which Unit ID we're reading from
                var mainViewModel = Window.GetWindow(this)?.DataContext as MainViewModel;
                byte selectedUnitId = mainViewModel?.SelectedUnitId ?? 1;
                
                var dataStore = serverService.GetDataStore(selectedUnitId);
                if (dataStore == null) return 0;

                var outputAddress = node.OutputAddress;
                if (outputAddress?.Area == PlcArea.HoldingRegister && outputAddress.Address >= 0 && outputAddress.Address < dataStore.HoldingRegisters.Count)
                {
                    return dataStore.HoldingRegisters[outputAddress.Address];
                }
                else if (outputAddress?.Area == PlcArea.InputRegister && outputAddress.Address >= 0 && outputAddress.Address < dataStore.InputRegisters.Count)
                {
                    return dataStore.InputRegisters[outputAddress.Address];
                }
                else if (outputAddress?.Area == PlcArea.Coil && outputAddress.Address >= 0 && outputAddress.Address < dataStore.CoilDiscretes.Count)
                {
                    return dataStore.CoilDiscretes[outputAddress.Address] ? 1 : 0;
                }
                else if (outputAddress?.Area == PlcArea.DiscreteInput && outputAddress.Address >= 0 && outputAddress.Address < dataStore.InputDiscretes.Count)
                {
                    return dataStore.InputDiscretes[outputAddress.Address] ? 1 : 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading output register value: {ex.Message}");
                AddDebugMessage($"Error reading output register value: {ex.Message}");
            }
            return 0;
        }

        private int GetActualRegisterValue(VisualNode node)
        {
            try
            {
                // Get the visual simulation service to access DataStore
                var visualSimulationService = App.ServiceProvider?.GetService<IVisualSimulationService>();
                if (visualSimulationService == null) return 0;

                // Access the DataStore through reflection or create a public method
                // For now, we'll access it through the service's internal structure
                var serverService = App.ServiceProvider?.GetService<ModbusServerService>();
                if (serverService == null) return 0;

                // Check which Unit ID we're reading from
                var mainViewModel = Window.GetWindow(this)?.DataContext as MainViewModel;
                byte selectedUnitId = mainViewModel?.SelectedUnitId ?? 1;
                
                var dataStore = serverService.GetDataStore(selectedUnitId);
                if (dataStore == null) return 0;

                System.Diagnostics.Debug.WriteLine($"DEBUG: Reading from Unit ID {selectedUnitId}");
                AddDebugMessage($"Reading from Unit ID {selectedUnitId}");

                var address = node.Input1Address;
                int value = 0;
                
                if (address?.Area == PlcArea.HoldingRegister && address.Address >= 0 && address.Address < dataStore.HoldingRegisters.Count)
                {
                    // Debug: Check both 0-based and 1-based addressing
                    int directValue = dataStore.HoldingRegisters[address.Address];
                    int offsetValue = address.Address > 0 ? dataStore.HoldingRegisters[address.Address - 1] : 0;
                    
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Address={address.Address}, Direct={directValue}, Offset-1={offsetValue}");
                    AddDebugMessage($"Address={address.Address}, Direct={directValue}, Offset-1={offsetValue}");
                    
                    // Use direct addressing for now, but we might need offset
                    value = directValue;
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Read HoldingRegister[{address.Address}] = {value}");
                    AddDebugMessage($"Read HoldingRegister[{address.Address}] = {value}");
                }
                else if (address?.Area == PlcArea.InputRegister && address.Address >= 0 && address.Address < dataStore.InputRegisters.Count)
                {
                    value = dataStore.InputRegisters[address.Address];
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Read InputRegister[{address.Address}] = {value}");
                    AddDebugMessage($"Read InputRegister[{address.Address}] = {value}");
                }
                else if (address?.Area == PlcArea.Coil && address.Address >= 0 && address.Address < dataStore.CoilDiscretes.Count)
                {
                    value = dataStore.CoilDiscretes[address.Address] ? 1 : 0;
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Read Coil[{address.Address}] = {value}");
                    AddDebugMessage($"Read Coil[{address.Address}] = {value}");
                }
                else if (address?.Area == PlcArea.DiscreteInput && address.Address >= 0 && address.Address < dataStore.InputDiscretes.Count)
                {
                    value = dataStore.InputDiscretes[address.Address] ? 1 : 0;
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Read DiscreteInput[{address.Address}] = {value}");
                    AddDebugMessage($"Read DiscreteInput[{address.Address}] = {value}");
                }
                
                return value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading register value: {ex.Message}");
                AddDebugMessage($"Error reading register value: {ex.Message}");
            }
            return 0;
        }

        private void AddDebugMessage(string message)
        {
            try
            {
                var mainViewModel = Window.GetWindow(this)?.DataContext as MainViewModel;
                if (mainViewModel != null)
                {
                    // Use reflection to safely call AddDebugMessage if it exists
                    var method = mainViewModel.GetType().GetMethod("AddDebugMessage");
                    method?.Invoke(mainViewModel, new object[] { message });
                }
            }
            catch
            {
                // Fallback to VS Debug output if reflection fails
                System.Diagnostics.Debug.WriteLine($"DEBUG: {message}");
            }
        }
    }
}
