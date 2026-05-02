using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        #region Layout Constants
        // Node layout constants
        private const double NodeHeaderHeight = 24;
        private const double ConnectorOffset = 6;  // Distance from node edge to connector center
        
        // Vertical positioning ratios for connectors (relative to content height)
        private const double SingleInputVerticalRatio = 0.5;     // Center (1/2)
        private const double DualInputTopRatio = 0.333;          // Upper third (1/3)
        private const double DualInputBottomRatio = 0.667;       // Lower two-thirds (2/3)
        #endregion
        
        private VisualNodeEditorViewModel? _viewModel;
        private bool _isDraggingNode = false;
        private bool _isConnecting = false;
        private VisualNode? _draggedNode = null;
        private Point _dragStartPoint;
        private Point _originalNodePosition;
        private DispatcherTimer? _liveUpdateTimer;
        
        // Track event handlers for cleanup to prevent memory leaks
        private readonly Dictionary<string, List<(object Target, PropertyChangedEventHandler Handler)>> _nodeEventHandlers = new();
        
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

                // Clean up event handlers before removing node
                CleanupNodeEventHandlers(nodeToDelete);

                // Remove the node
                _viewModel.Nodes.Remove(nodeToDelete);
                
                // Refresh canvas
                RefreshCanvas();
                RefreshConnections();
                
                AddDebugMessage($"Deleted node {nodeToDelete.Id} (type: {nodeToDelete.ElementType})");
            }
        }
        
        /// <summary>
        /// Detaches event handlers from a node and its address references to prevent memory leaks.
        /// </summary>
        private void CleanupNodeEventHandlers(VisualNode node)
        {
            if (_nodeEventHandlers.TryGetValue(node.Id, out var handlers))
            {
                foreach (var (target, handler) in handlers)
                {
                    if (target is VisualNode n)
                    {
                        n.PropertyChanged -= handler;
                    }
                    else if (target is PlcAddressReference addr)
                    {
                        addr.PropertyChanged -= handler;
                    }
                }
                _nodeEventHandlers.Remove(node.Id);
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
            border.MouseRightButtonDown += Node_MouseRightButtonDown;
            border.MouseMove += Node_MouseMove;
            border.MouseLeftButtonUp += Node_MouseLeftButtonUp;
            
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Header row
            var (header, headerText, addressText) = CreateNodeHeader(node);
            var capturedAddressText = addressText;
            var capturedHeaderText = headerText;
            Grid.SetRow(header, 0);
            grid.Children.Add(header);
            
            // Content row
            var (contentGrid, contentStack, liveText) = CreateNodeContent(node);
            Grid.SetRow(contentGrid, 1);
            grid.Children.Add(contentGrid);
            
            // Setup inline I/O controls if applicable
            if (IsIoNode(node.ElementType))
            {
                var (inlinePanel, isInputType) = CreateInlineAddressEditor(node, capturedAddressText);
                contentStack.Children.Add(inlinePanel);
            }
            
            // Setup event handlers for live value updates
            var capturedHeader = header;
            var capturedLiveText = liveText;
            var originalColor = GetElementColor(node.ElementType);
            SetupNodeEventHandlers(node, capturedHeader, capturedLiveText, capturedHeaderText, capturedAddressText, originalColor);
            
            // Footer row (if needed)
            var footer = CreateNodeFooter(node);
            if (footer != null)
            {
                Grid.SetRow(footer, 2);
                grid.Children.Add(footer);
            }
            
            border.Child = grid;
            return border;
        }

        private (Border header, TextBlock headerText, TextBlock addressText) CreateNodeHeader(VisualNode node)
        {
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
            
            var header = new Border
            {
                Background = new SolidColorBrush(GetElementColor(node.ElementType)),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                Child = headerStack
            };
            
            return (header, headerText, addressText);
        }

        private bool IsIoNode(PlcElementType elementType) =>
            elementType == PlcElementType.Input || elementType == PlcElementType.Output ||
            elementType == PlcElementType.InputBool || elementType == PlcElementType.InputInt ||
            elementType == PlcElementType.OutputBool || elementType == PlcElementType.OutputInt;

        private (StackPanel inlinePanel, bool isInputType) CreateInlineAddressEditor(VisualNode node, TextBlock capturedAddressText)
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

            // Address TextBox with validation
            var addrBox = new TextBox
            {
                Width = 200,
                Height = 28,
                FontSize = 11,
                Text = addrRef.Address >= 0 ? addrRef.Address.ToString() : "",
                ToolTip = "Enter a non-negative integer address (0-65535)",
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            // Input validation - only allow digits
            addrBox.PreviewTextInput += (s, ev) =>
            {
                // Only allow digits
                if (!char.IsDigit(ev.Text, 0))
                {
                    ev.Handled = true;
                }
            };
            
            // Prevent paste of invalid content
            addrBox.PreviewKeyDown += (s, ev) =>
            {
                if (ev.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Let Ctrl+V through, we'll validate on LostFocus
                }
            };
            
            addrBox.LostFocus += (s, ev) =>
            {
                ValidateAndUpdateAddress(addrBox, addrRef, node, capturedAddressText);
            };
            addrBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == System.Windows.Input.Key.Enter)
                {
                    if (ValidateAndUpdateAddress(addrBox, addrRef, node, capturedAddressText))
                    {
                        System.Windows.Input.Keyboard.ClearFocus();
                    }
                }
            };
            inlinePanel.Children.Add(addrBox);

            return (inlinePanel, isInputType);
        }
        
        /// <summary>
        /// Validates address input and updates the model. Returns true if valid.
        /// </summary>
        private bool ValidateAndUpdateAddress(TextBox addrBox, PlcAddressReference addrRef, VisualNode node, TextBlock addressTextDisplay)
        {
            var text = addrBox.Text.Trim();
            
            // Empty is treated as invalid - restore previous value
            if (string.IsNullOrEmpty(text))
            {
                addrBox.Text = addrRef.Address >= 0 ? addrRef.Address.ToString() : "0";
                addrBox.BorderBrush = SystemColors.ControlDarkBrush;
                addrBox.ToolTip = "Enter a non-negative integer address (0-65535)";
                return false;
            }
            
            // Check if valid integer
            if (!int.TryParse(text, out int addr) || addr < 0)
            {
                // Invalid - show visual feedback
                addrBox.BorderBrush = Brushes.Red;
                addrBox.ToolTip = "Invalid address. Must be a non-negative integer (0-65535)";
                addrBox.SelectAll();
                return false;
            }
            
            // Valid input - clear error state and update
            addrBox.BorderBrush = SystemColors.ControlDarkBrush;
            addrBox.ToolTip = "Enter a non-negative integer address (0-65535)";
            addrRef.Address = addr;
            addressTextDisplay.Text = node.AddressDisplay;
            return true;
        }

        private (Grid contentGrid, StackPanel contentStack, TextBlock liveText) CreateNodeContent(VisualNode node)
        {
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
            
            // Center content
            var contentStack = new StackPanel 
            { 
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Name label for non-I/O nodes
            if (!IsIoNode(node.ElementType))
            {
                contentStack.Children.Add(new TextBlock 
                { 
                    Text = node.Name, 
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = node.Name
                });
            }
            
            // Live value indicator
            var liveText = new TextBlock
            {
                Text = "",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            contentStack.Children.Add(liveText);
            
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
            
            Grid.SetColumn(outputStack, 2);
            contentGrid.Children.Add(outputStack);
            
            return (contentGrid, contentStack, liveText);
        }

        private void SetupNodeEventHandlers(VisualNode node, Border header, TextBlock liveText, 
            TextBlock headerText, TextBlock addressText, Color originalColor)
        {
            _nodeEventHandlers[node.Id] = new List<(object, PropertyChangedEventHandler)>();
            
            PropertyChangedEventHandler nodePropertyHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(VisualNode.CurrentValue) ||
                    e.PropertyName == nameof(VisualNode.ShowLiveValues))
                {
                    UpdateLiveValueDisplay(node, liveText, header, originalColor);
                }
                
                if (e.PropertyName == nameof(VisualNode.Input1Address) ||
                    e.PropertyName == nameof(VisualNode.ElementType))
                {
                    headerText.Text = node.DisplayName;
                    addressText.Text = node.AddressDisplay;
                }
            };
            node.PropertyChanged += nodePropertyHandler;
            _nodeEventHandlers[node.Id].Add((node, nodePropertyHandler));
            
            PropertyChangedEventHandler addressHandler = (s, e) => 
            {
                headerText.Text = node.DisplayName;
                addressText.Text = node.AddressDisplay;
            };
            node.Input1Address.PropertyChanged += addressHandler;
            _nodeEventHandlers[node.Id].Add((node.Input1Address, addressHandler));
            
            node.OutputAddress.PropertyChanged += addressHandler;
            _nodeEventHandlers[node.Id].Add((node.OutputAddress, addressHandler));
        }

        private void UpdateLiveValueDisplay(VisualNode node, TextBlock liveText, Border header, Color originalColor)
        {
            if (node.ShowLiveValues)
            {
                UpdateLiveTextForElementType(node, liveText);
                liveText.Visibility = Visibility.Visible;
                header.Background = new SolidColorBrush(node.CurrentValue
                    ? Color.FromRgb(40, 160, 40)
                    : Color.FromRgb(160, 40, 40));
            }
            else
            {
                liveText.Visibility = Visibility.Collapsed;
                header.Background = new SolidColorBrush(originalColor);
            }
        }

        private void UpdateLiveTextForElementType(VisualNode node, TextBlock liveText)
        {
            switch (node.ElementType)
            {
                case PlcElementType.InputBool:
                case PlcElementType.OutputBool:
                    liveText.Text = node.CurrentValue ? "● ON" : "● OFF";
                    liveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
                    break;
                    
                case PlcElementType.InputInt:
                    var actualValue = GetActualRegisterValue(node);
                    liveText.Text = $"● VAL:{actualValue}";
                    liveText.Foreground = Brushes.Cyan;
                    break;
                    
                case PlcElementType.OutputInt:
                    var outputValue = GetOutputRegisterValue(node);
                    liveText.Text = $"● VAL:{outputValue}";
                    liveText.Foreground = Brushes.Cyan;
                    break;
                    
                case PlcElementType.Input:
                case PlcElementType.Output:
                    if (node.Input1Address?.Area == PlcArea.HoldingRegister ||
                        node.Input1Address?.Area == PlcArea.InputRegister)
                    {
                        var actualLegacyValue = GetActualRegisterValue(node);
                        liveText.Text = $"● VAL:{actualLegacyValue}";
                        liveText.Foreground = Brushes.Cyan;
                    }
                    else
                    {
                        liveText.Text = node.CurrentValue ? "● ON" : "● OFF";
                        liveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
                    }
                    break;
                    
                case PlcElementType.MATH_ADD:
                case PlcElementType.MATH_SUB:
                case PlcElementType.MATH_MUL:
                case PlcElementType.MATH_DIV:
                    var mathResult = GetOutputRegisterValue(node);
                    liveText.Text = $"● VAL:{mathResult}";
                    liveText.Foreground = Brushes.Orange;
                    break;
                    
                case PlcElementType.COMPARE_EQ:
                case PlcElementType.COMPARE_NE:
                case PlcElementType.COMPARE_GT:
                case PlcElementType.COMPARE_LT:
                case PlcElementType.COMPARE_GE:
                case PlcElementType.COMPARE_LE:
                    liveText.Text = node.CurrentValue ? "● TRUE" : "● FALSE";
                    liveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
                    break;
                    
                default:
                    liveText.Text = node.CurrentValue ? "● ON" : "● OFF";
                    liveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
                    break;
            }
        }

        private Border? CreateNodeFooter(VisualNode node)
        {
            if (!node.HasParameters && node.ElementType != PlcElementType.RS)
                return null;
            
            var footerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            AddFooterControls(node, footerPanel);

            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                CornerRadius = new CornerRadius(0, 0, 6, 6),
                Padding = new Thickness(4, 2, 4, 2),
                Child = footerPanel
            };
            
            return footer;
        }

        private void AddFooterControls(VisualNode node, StackPanel footerPanel)
        {
            switch (node.ElementType)
            {
                case PlcElementType.TON:
                case PlcElementType.TOF:
                case PlcElementType.TP:
                    footerPanel.Children.Add(new TextBlock { Text = "ms:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                    var timerBox = new TextBox { Width = 50, Height = 18, FontSize = 10, Text = node.TimerPresetMs.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
                    timerBox.LostFocus += (s, ev) => { if (int.TryParse(timerBox.Text, out int v) && v >= 0) node.TimerPresetMs = v; };
                    footerPanel.Children.Add(timerBox);
                    break;
                    
                case PlcElementType.CTU:
                case PlcElementType.CTD:
                case PlcElementType.CTC:
                    footerPanel.Children.Add(new TextBlock { Text = "Pre:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                    var counterBox = new TextBox { Width = 50, Height = 18, FontSize = 10, Text = node.CounterPreset.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
                    counterBox.LostFocus += (s, ev) => { if (int.TryParse(counterBox.Text, out int v)) node.CounterPreset = v; };
                    footerPanel.Children.Add(counterBox);
                    break;
                    
                case PlcElementType.COMPARE_EQ:
                case PlcElementType.COMPARE_NE:
                case PlcElementType.COMPARE_GT:
                case PlcElementType.COMPARE_LT:
                case PlcElementType.COMPARE_GE:
                case PlcElementType.COMPARE_LE:
                    footerPanel.Children.Add(new TextBlock { Text = "Val:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                    var cmpBox = new TextBox { Width = 50, Height = 18, FontSize = 10, Text = node.CompareValue.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
                    cmpBox.LostFocus += (s, ev) => { if (int.TryParse(cmpBox.Text, out int v)) node.CompareValue = v; };
                    footerPanel.Children.Add(cmpBox);
                    break;
                    
                case PlcElementType.MATH_ADD:
                case PlcElementType.MATH_SUB:
                case PlcElementType.MATH_MUL:
                case PlcElementType.MATH_DIV:
                    footerPanel.Children.Add(new TextBlock { Text = "Const:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                    var mathBox = new TextBox { Width = 50, Height = 18, FontSize = 10, Text = node.CompareValue.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
                    mathBox.LostFocus += (s, ev) => { if (int.TryParse(mathBox.Text, out int v)) node.CompareValue = v; };
                    footerPanel.Children.Add(mathBox);
                    break;
                    
                case PlcElementType.RS:
                    footerPanel.Children.Add(new TextBlock { Text = "Set Dom:", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,2,0) });
                    var setDomCheck = new System.Windows.Controls.CheckBox { IsChecked = node.SetDominant, VerticalAlignment = VerticalAlignment.Center };
                    setDomCheck.Checked += (s, ev) => node.SetDominant = true;
                    setDomCheck.Unchecked += (s, ev) => node.SetDominant = false;
                    footerPanel.Children.Add(setDomCheck);
                    break;
            }
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
            var button = sender as Button;
            if (button?.Tag == null)
            {
                return;
            }

            var nodeId = button.Tag.ToString();
            var node = _viewModel?.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
            {
                return;
            }

            // Only I/O blocks should have configure buttons
            if (node.ElementType != PlcElementType.Input && node.ElementType != PlcElementType.Output &&
                node.ElementType != PlcElementType.InputBool && node.ElementType != PlcElementType.InputInt &&
                node.ElementType != PlcElementType.OutputBool && node.ElementType != PlcElementType.OutputInt)
            {
                return;
            }

            // Determine which address to configure based on block type
            bool isInputType = node.ElementType == PlcElementType.Input || 
                              node.ElementType == PlcElementType.InputBool || 
                              node.ElementType == PlcElementType.InputInt;
            
            string connectorType = isInputType ? "Output" : "Input1";
            string dialogTitle = isInputType ? "Configure Input Tag" : "Configure Output Tag";

            // Pre-populate dialog with current address
            var addrRef = isInputType ? node.Input1Address : node.OutputAddress;
            
            // Create dialog with initial values from the address reference
            var initialArea = addrRef?.Area ?? PlcArea.HoldingRegister;
            var initialAddress = addrRef?.Address ?? 0;
            
            var testDialog = new TestDialog(initialArea, initialAddress)
            {
                Owner = Window.GetWindow(this),
                Title = dialogTitle
            };
            
            testDialog.ShowDialog();
            if (testDialog.DialogResult == true && addrRef != null)
            {
                // Update with test dialog results
                addrRef.Area = testDialog.SelectedArea;
                addrRef.Address = testDialog.SelectedAddress;
                addrRef.Not = testDialog.SelectedAddress < 0; // Simplified
                
                // Refresh canvas to show the updated address
                RefreshCanvas();
            }
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
        
        private void Node_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null) return;
            
            var border = sender as Border;
            if (border?.DataContext is not VisualNode node) return;
            
            // Select the node first
            _viewModel.SelectNode(node);
            
            // Create context menu
            var contextMenu = new ContextMenu();
            
            // Delete menu item
            var deleteItem = new MenuItem
            {
                Header = "Delete",
                Icon = new TextBlock { Text = "🗑️", FontSize = 12 }
            };
            deleteItem.Click += (s, ev) =>
            {
                _viewModel.DeleteNodeCommand.Execute(node);
                AddDebugMessage($"Deleted node {node.Id} via context menu");
            };
            contextMenu.Items.Add(deleteItem);
            
            // Show the context menu
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            contextMenu.PlacementTarget = border;
            contextMenu.IsOpen = true;
            
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
        
        private Ellipse? FindEllipseByTag(DependencyObject parent, string connectorType)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
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
            
            // Use constants for layout calculations
            double contentH = node.Height - NodeHeaderHeight;
            double yBase = node.Y + NodeHeaderHeight;

            return connectorType switch
            {
                "Output" => new Point(node.X + node.Width - ConnectorOffset, yBase + contentH * SingleInputVerticalRatio),
                "Input2" => new Point(node.X + ConnectorOffset, yBase + contentH * DualInputBottomRatio),
                _        => new Point(node.X + ConnectorOffset, yBase + contentH * DualInputTopRatio)   // Input1 or default
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

                var address = node.Input1Address;
                int value = 0;
                
                if (address?.Area == PlcArea.HoldingRegister && address.Address >= 0 && address.Address < dataStore.HoldingRegisters.Count)
                {
                    // Debug: Check both 0-based and 1-based addressing
                    int directValue = dataStore.HoldingRegisters[address.Address];
                    int offsetValue = address.Address > 0 ? dataStore.HoldingRegisters[address.Address - 1] : 0;
                    
                    value = directValue;
                }
                else if (address?.Area == PlcArea.InputRegister && address.Address >= 0 && address.Address < dataStore.InputRegisters.Count)
                {
                    value = dataStore.InputRegisters[address.Address];
                }
                else if (address?.Area == PlcArea.Coil && address.Address >= 0 && address.Address < dataStore.CoilDiscretes.Count)
                {
                    value = dataStore.CoilDiscretes[address.Address] ? 1 : 0;
                }
                else if (address?.Area == PlcArea.DiscreteInput && address.Address >= 0 && address.Address < dataStore.InputDiscretes.Count)
                {
                    value = dataStore.InputDiscretes[address.Address] ? 1 : 0;
                }
                
                return value;
            }
            catch (Exception ex)
            {
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
                // Reflection failed, ignore
            }
        }
    }
}
