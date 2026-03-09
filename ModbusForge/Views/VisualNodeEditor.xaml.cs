using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ModbusForge.Models;
using ModbusForge.ViewModels;

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
        
        public VisualNodeEditor()
        {
            InitializeComponent();
            DataContextChanged += VisualNodeEditor_DataContextChanged;
            KeyDown += VisualNodeEditor_KeyDown;
            MouseUp += VisualNodeEditor_MouseUp;
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
            // ESC key cancels connection mode
            if (e.Key == Key.Escape && _isConnecting)
            {
                CancelConnection();
                e.Handled = true;
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
                Padding = new Thickness(8, 4, 8, 4)
            };
            var headerText = new TextBlock 
            { 
                Text = node.DisplayName, 
                FontWeight = FontWeights.Bold, 
                Foreground = Brushes.White,
                FontSize = 12
            };
            header.Child = headerText;
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
            contentStack.Children.Add(nameText);
            
            // Live value indicator — always present, hidden until ShowLiveValues is on
            var liveText = new TextBlock
            {
                Text = "",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            contentStack.Children.Add(liveText);
            
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
                        capturedLiveText.Text = node.CurrentValue ? "● ON" : "● OFF";
                        capturedLiveText.Foreground = node.CurrentValue ? Brushes.LimeGreen : Brushes.Red;
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
                // Refresh header label when the source address changes
                if (e.PropertyName == nameof(VisualNode.Input1Address) ||
                    e.PropertyName == nameof(VisualNode.ElementType))
                {
                    capturedHeaderText.Text = node.DisplayName;
                }
            };
            // Also update header when inner address properties change (e.g. after right-click config)
            node.Input1Address.PropertyChanged  += (s, e) => capturedHeaderText.Text = node.DisplayName;
            node.OutputAddress.PropertyChanged  += (s, e) => capturedHeaderText.Text = node.DisplayName;
            
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
            
            // Footer
            if (node.HasParameters)
            {
                var footer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                    CornerRadius = new CornerRadius(0, 0, 6, 6),
                    Padding = new Thickness(4, 2, 4, 2)
                };
                var footerText = new TextBlock 
                { 
                    Text = node.ParameterDisplay, 
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                footer.Child = footerText;
                Grid.SetRow(footer, 2);
                grid.Children.Add(footer);
            }
            
            border.Child = grid;
            return border;
        }
        
        private Ellipse CreateConnector(string nodeId, string connectorType, bool isInput)
        {
            var ellipse = new Ellipse
            {
                Style = (Style)FindResource(isInput ? "InputConnectorStyle" : "OutputConnectorStyle"),
                Tag = $"{nodeId},{connectorType}",
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Right-click to configure address"
            };
            ellipse.MouseLeftButtonDown += Connector_MouseLeftButtonDown;
            ellipse.MouseRightButtonDown += Connector_MouseRightButtonDown;
            return ellipse;
        }

        private void Connector_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null) return;
            var ellipse = sender as Ellipse;
            if (ellipse == null) return;

            var tag = ellipse.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            var parts = tag.Split(',');
            var nodeId = parts[0];
            var connectorType = parts[1];

            var node = _viewModel.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return;

            // Grab custom entries from parent window so tags reflect real configuration
            var mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
            var customEntries = mainVm?.CustomEntries
                ?? System.Linq.Enumerable.Empty<ModbusForge.Models.CustomEntry>();

            var dialog = new ConnectorConfigWindow(nodeId, connectorType, node.Name, customEntries)
            {
                Owner = Window.GetWindow(this)
            };

            // Pre-populate dialog with current address.
            // Source nodes read FROM Input1Address; their output connector exposes that read source.
            var addrRef = connectorType switch
            {
                "Input1" => node.Input1Address,
                "Input2" => node.Input2Address,
                "Output" when node.ElementType == PlcElementType.Source => node.Input1Address,
                _ => node.OutputAddress
            };
            if (dialog.DataContext is ConnectorConfigViewModel vm && addrRef != null)
            {
                vm.IsLinkedToAddress = addrRef.Address >= 0;
                vm.SelectedArea = addrRef.Area;
                vm.Address = addrRef.Address;
                vm.IsInverted = addrRef.Not;
            }

            // MetroWindow ShowDialog() may return null - check Result directly instead
            dialog.ShowDialog();
            if (dialog.Result != null && dialog.Result.IsConfigured)
            {
                var result = dialog.Result;
                addrRef.Area = result.Area;
                addrRef.Address = result.Address;
                addrRef.Not = result.Not;
                ellipse.ToolTip = $"{result.Area}:{result.Address}{(result.Not ? " NOT" : "")}"; 
            }

            // Rebuild canvas so headers always reflect the latest address,
            // regardless of PropertyChanged subscription timing
            RefreshCanvas();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(RefreshConnections));

            e.Handled = true;
        }
        
        private Color GetElementColor(PlcElementType elementType)
        {
            return elementType switch
            {
                PlcElementType.Source => Color.FromRgb(76, 175, 80),
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
    }
}
