// Test
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Views
{
    public partial class VisualNodeEditorView : UserControl
    {
        private const string PaletteDragFormat = "ModbusForge.VisualNodeType";
        private const string TagDragFormat = "ModbusForge.Avalonia.Tag";
        private const string TagDragTextPrefix = "MF|Tag|";
        private const double DragThreshold = 4.0;
        private const double PortHitTolerance = 24.0;

        private Canvas? _nodeCanvas;
        private ScrollViewer? _canvasScrollViewer;
        private IReadOnlyList<VisualNode> _draggedNodes = Array.Empty<VisualNode>();
        private Dictionary<VisualNode, (double X, double Y)> _dragStartPositions = new();
        private Point _dragStartPoint;
        private IPointer? _dragPointer;
        private bool _isDraggingNode;

        private PaletteItem? _paletteDragItem;
        private Point _paletteDragStart;
        private IPointer? _palettePointer;
        private bool _paletteDragStarted;

        private bool _isMarqueeActive;
        private Point _marqueeStartPoint;
        private IPointer? _marqueePointer;

        private bool _isPanning;
        private Point _panStartPoint;
        private Vector _panStartOffset;

        private bool _isConnecting;
        private VisualNode? _connectionSourceNode;
        private string _connectionSourceConnector = string.Empty;
        private IPointer? _connectionPointer;
        private Line? _tempConnectionLine;

        private const string ProgramTreeDragFormat = "ModbusForge.ProgramTreeItem";

        private IProgramTreeItem? _treeDragItem;
        private Point _treeDragStart;
        private IPointer? _treeDragPointer;
        private bool _treeDragStarted;

        private TreeView? _programTreeView;

        public VisualNodeEditorView()
        {
            InitializeComponent();
            _nodeCanvas = this.FindControl<Canvas>("NodeCanvas");
            _canvasScrollViewer = this.FindControl<ScrollViewer>("CanvasScrollViewer");
            _tempConnectionLine = this.FindControl<Line>("TempConnectionLine");
            _programTreeView = this.FindControl<TreeView>("ProgramTreeView");
            AddHandler(KeyDownEvent, View_KeyDown, RoutingStrategies.Tunnel);
            _nodeCanvas?.AddHandler(DragDrop.DragOverEvent, Canvas_DragOver, RoutingStrategies.Bubble);
            _nodeCanvas?.AddHandler(DragDrop.DropEvent, Canvas_Drop, RoutingStrategies.Bubble);
            _nodeCanvas?.AddHandler(PointerPressedEvent, NodeCanvas_PreviewPointerPressed, RoutingStrategies.Tunnel);

            if (_programTreeView != null)
            {
                _programTreeView.AddHandler(PointerPressedEvent, TreeView_PointerPressed, RoutingStrategies.Tunnel);
                _programTreeView.AddHandler(PointerMovedEvent, TreeView_PointerMoved, RoutingStrategies.Bubble);
                _programTreeView.AddHandler(PointerReleasedEvent, TreeView_PointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            }
        }

        private VisualNodeEditorViewModel? ViewModel => DataContext as VisualNodeEditorViewModel;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void NodeCanvas_PreviewPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (ViewModel == null || _nodeCanvas == null)
            {
                return;
            }

            var point = e.GetCurrentPoint(_nodeCanvas);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            var source = e.Source as Visual;
            if (source == null)
            {
                return;
            }

            // Skip when clicking on a connection port (Port_PointerPressed handles those in bubble).
            if (source is Ellipse ellipse && (ellipse.Classes.Contains("Input1Port") || ellipse.Classes.Contains("Input2Port") || ellipse.Classes.Contains("OutputPort")))
            {
                return;
            }

            var border = source is Border b ? b : source.GetVisualAncestors().OfType<Border>().FirstOrDefault();
            if (border == null || border.DataContext is not VisualNode node)
            {
                return;
            }

            // The Border's own bubble handler will normally handle selection, but if the click
            // originates on a child that marks the event handled (e.g. the live-value TextBox),
            // the Border bubble never fires. Select the node here during the tunnel phase.
            var extendSelection = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
            ViewModel.SelectNode(node, extendSelection);
        }

        private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (ViewModel == null || _nodeCanvas == null)
            {
                return;
            }

            var point = e.GetCurrentPoint(_nodeCanvas);

            // Middle-mouse pan (works over empty canvas or nodes)
            if (point.Properties.IsMiddleButtonPressed)
            {
                if (_isConnecting)
                {
                    CancelConnectionDrag();
                    ViewModel.SelectedConnection = null;
                }

                _isPanning = true;
                _panStartPoint = _canvasScrollViewer != null ? e.GetPosition(_canvasScrollViewer) : e.GetPosition(_nodeCanvas);
                _panStartOffset = _canvasScrollViewer?.Offset ?? new Vector(0, 0);
                _panStartPoint = e.GetPosition(_canvasScrollViewer);
                e.Pointer.Capture(_nodeCanvas);
                _nodeCanvas.Cursor = new Cursor(StandardCursorType.Hand);
                e.Handled = true;
                return;
            }

            if (e.Source != sender)
            {
                return;
            }

            if (_isConnecting)
            {
                CancelConnectionDrag();
                ViewModel.SelectedConnection = null;
                e.Handled = true;
                return;
            }

            if (ViewModel.IsConnectMode)
            {
                ViewModel.CancelConnectCommand.Execute(null);
            }

            ViewModel.SelectedConnection = null;

            if (!point.Properties.IsLeftButtonPressed)
            {
                ViewModel.ClearSelection();
                e.Handled = true;
                return;
            }

            var extendSelection = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
            var zoom = Math.Max(ViewModel.ZoomLevel, 0.01);
            var position = point.Position;
            var logicalPosition = new Point(position.X / zoom, position.Y / zoom);

            _isMarqueeActive = true;
            _marqueeStartPoint = logicalPosition;
            _marqueePointer = e.Pointer;
            e.Pointer.Capture(_nodeCanvas);

            ViewModel.StartMarquee(logicalPosition.X, logicalPosition.Y, extendSelection);
            e.Handled = true;

            // Same as for node clicks: keep keyboard shortcuts active on the canvas.
            _nodeCanvas.Focus();
        }

        private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isPanning && _canvasScrollViewer != null)
            {
                if (!e.GetCurrentPoint(_nodeCanvas).Properties.IsMiddleButtonPressed)
                {
                    FinishPan(e.Pointer);
                    return;
                }

                var current = e.GetPosition(_canvasScrollViewer);
                var delta = new Point(_panStartPoint.X - current.X, _panStartPoint.Y - current.Y);
                _canvasScrollViewer.Offset = _panStartOffset + new Vector(delta.X, delta.Y);
                e.Handled = true;
                return;
            }

            if (_isConnecting && _connectionSourceNode != null && _connectionPointer == e.Pointer
                && _tempConnectionLine != null && _nodeCanvas != null && ViewModel != null)
            {
                var start = GetPortPosition(_connectionSourceNode, _connectionSourceConnector);
                var connectionPoint = e.GetPosition(_nodeCanvas);
                var connectionZoom = Math.Max(ViewModel.ZoomLevel, 0.01);
                var end = new Point(connectionPoint.X / connectionZoom, connectionPoint.Y / connectionZoom);

                _tempConnectionLine.StartPoint = start;
                _tempConnectionLine.EndPoint = end;
                e.Handled = true;
                return;
            }

            if (!_isMarqueeActive || ViewModel == null || _marqueePointer != e.Pointer || _nodeCanvas == null)
            {
                return;
            }

            if (!e.GetCurrentPoint(_nodeCanvas).Properties.IsLeftButtonPressed)
            {
                FinishMarquee(e.Pointer);
                return;
            }

            var marqueePosition = e.GetPosition(_nodeCanvas);
            var marqueeZoom = Math.Max(ViewModel.ZoomLevel, 0.01);
            ViewModel.UpdateMarquee(marqueePosition.X / marqueeZoom, marqueePosition.Y / marqueeZoom);
            e.Handled = true;
        }

        private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isPanning)
            {
                FinishPan(e.Pointer);
                e.Handled = true;
                return;
            }

            if (_isConnecting)
            {
                TryCompleteConnection(e);
                CancelConnectionDrag();
                e.Handled = true;
                return;
            }

            if (!_isMarqueeActive || _marqueePointer != e.Pointer)
            {
                return;
            }

            FinishMarquee(e.Pointer);
            e.Handled = true;
        }

        private void Canvas_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_isPanning)
            {
                FinishPan(null);
            }

            if (_isMarqueeActive)
            {
                FinishMarquee(null);
            }

            if (_isConnecting)
            {
                CancelConnectionDrag();
            }
        }

        private void Port_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Ellipse port || ViewModel == null || _nodeCanvas == null || _tempConnectionLine == null)
            {
                return;
            }

            var point = e.GetCurrentPoint(port);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (port.DataContext is not VisualNode node)
            {
                return;
            }

            var connector = GetPortConnector(port);
            if (connector == null)
            {
                return;
            }

            if (node.OutputPortNames.Contains(connector))
            {
                if (ViewModel.IsConnectMode)
                {
                    ViewModel.CancelConnectCommand.Execute(null);
                }

                _isConnecting = true;
                _connectionSourceNode = node;
                _connectionSourceConnector = connector;
                _connectionPointer = e.Pointer;
                _connectionPointer.Capture(_nodeCanvas);

                var start = GetPortPosition(node, connector);
                var canvasPosition = e.GetPosition(_nodeCanvas);
                var zoom = Math.Max(ViewModel.ZoomLevel, 0.01);
                var end = new Point(canvasPosition.X / zoom, canvasPosition.Y / zoom);

                _tempConnectionLine.StartPoint = start;
                _tempConnectionLine.EndPoint = end;
                _tempConnectionLine.IsVisible = true;

                e.Handled = true;
            }
            else
            {
                if (_isConnecting)
                {
                    e.Handled = true;
                    return;
                }

                if (ViewModel.IsConnectMode)
                {
                    if (ViewModel.ConnectionSourceNode != null
                        && !ReferenceEquals(ViewModel.ConnectionSourceNode, node))
                    {
                        ViewModel.TryConnectNodes(ViewModel.ConnectionSourceNode, node, connector);
                    }

                    e.Handled = true;
                    return;
                }

                ViewModel.SelectNode(node, false);
                e.Handled = true;
            }
        }

        private void ConnectionLine_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Path path || ViewModel == null || path.DataContext is not ConnectionLine line)
            {
                return;
            }

            if (e.GetCurrentPoint(path).Properties.IsRightButtonPressed)
            {
                ShowConnectionContextMenu(path, line);
                e.Handled = true;
                return;
            }

            ViewModel.SelectConnection(line.ConnectionId);
            e.Handled = true;
        }

        private static string? GetPortConnector(Ellipse port)
        {
            if (port.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                return tag;
            }

            if (port.Classes.Contains("OutputPort"))
            {
                return "Output";
            }

            if (port.Classes.Contains("Input2Port"))
            {
                return "Input2";
            }

            if (port.Classes.Contains("Input1Port"))
            {
                return "Input1";
            }

            return null;
        }

        private static Point GetPortPosition(VisualNode node, string connector)
        {
            const double HeaderHeight = 24;
            var contentH = node.Height - HeaderHeight;

            if (connector == "Input2" && node.HasSecondInput)
            {
                return new Point(node.X, node.Y + HeaderHeight + contentH * 0.667);
            }

            if (connector == "Input1" || (connector?.StartsWith("Input") == true))
            {
                return new Point(node.X, node.Y + HeaderHeight + contentH * 0.333);
            }

            // Output ports are distributed on the right edge.
            var outputPortNames = node.OutputPortNames;
            var portIndex = outputPortNames.IndexOf(connector ?? "Output");
            if (portIndex < 0) portIndex = 0;
            var outputCount = Math.Max(outputPortNames.Count, 1);
            var yRatio = (portIndex + 1.0) / (outputCount + 1.0);
            return new Point(node.X + node.Width, node.Y + HeaderHeight + contentH * yRatio);
        }

        private void TryCompleteConnection(PointerEventArgs e)
        {
            if (_connectionSourceNode == null || ViewModel == null)
            {
                return;
            }

            var (target, connector) = GetPortAt(e);
            if (target != null && !ReferenceEquals(_connectionSourceNode, target)
                && connector is "Input1" or "Input2")
            {
                ViewModel.TryConnectNodes(_connectionSourceNode, target, connector, _connectionSourceConnector);
            }
        }

        private (VisualNode? Node, string? Connector) GetPortAt(PointerEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not { } topLevel)
            {
                return (null, null);
            }

            var position = e.GetPosition(topLevel);
            var hit = topLevel.InputHitTest(position);
            if (hit is Ellipse port && port.DataContext is VisualNode node)
            {
                var connector = GetPortConnector(port);
                if (connector is "Input1" or "Input2")
                {
                    return (node, connector);
                }
            }

            return (null, null);
        }

        private void CancelConnectionDrag()
        {
            _isConnecting = false;
            _connectionSourceNode = null;
            _connectionSourceConnector = string.Empty;
            _connectionPointer?.Capture(null);
            _connectionPointer = null;

            if (_tempConnectionLine != null)
            {
                _tempConnectionLine.IsVisible = false;
            }
        }

        private void FinishMarquee(IPointer? pointer)
        {
            if (!_isMarqueeActive || ViewModel == null)
            {
                return;
            }

            _isMarqueeActive = false;
            _marqueePointer = null;
            ViewModel.EndMarquee();
            pointer?.Capture(null);
        }

        private void FinishPan(IPointer? pointer)
        {
            _isPanning = false;
            if (_nodeCanvas != null)
            {
                _nodeCanvas.Cursor = Cursor.Default;
            }

            pointer?.Capture(null);
        }

        private void CanvasScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (ViewModel == null || _canvasScrollViewer == null || _nodeCanvas == null)
            {
                return;
            }

            if ((e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
            {
                var delta = e.Delta.Y > 0 ? 0.1 : -0.1;
                var oldZoom = ViewModel.ZoomLevel;
                var newZoom = Math.Clamp(Math.Round(oldZoom + delta, 2), 0.25, 4.0);

                if (Math.Abs(newZoom - oldZoom) > 0.001)
                {
                    var canvasPos = e.GetPosition(_nodeCanvas);
                    var viewportPos = e.GetPosition(_canvasScrollViewer);

                    _canvasScrollViewer.Offset = new Vector(
                        canvasPos.X - (viewportPos.X / newZoom),
                        canvasPos.Y - (viewportPos.Y / newZoom));

                    ViewModel.ZoomLevel = newZoom;
                }

                e.Handled = true;
            }
            else if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
            {
                _canvasScrollViewer.Offset = new Vector(
                    _canvasScrollViewer.Offset.X - e.Delta.Y,
                    _canvasScrollViewer.Offset.Y);
                e.Handled = true;
            }
        }

        private void Canvas_DragOver(object? sender, DragEventArgs e)
        {
            if (TryGetDraggedElementType(e.Data, out _) || TryGetDraggedTag(e.Data, out _))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void Canvas_Drop(object? sender, DragEventArgs e)
        {
            if (ViewModel == null || sender is not Canvas canvas)
            {
                return;
            }

            var point = e.GetPosition(canvas);
            var zoom = Math.Max(ViewModel.ZoomLevel, 0.01);
            var logicalPoint = new Point(point.X / zoom, point.Y / zoom);

            if (TryGetDraggedTag(e.Data, out var tag))
            {
                if (TryGetNodeInputPortAt(e.Source, logicalPoint, out var targetNode, out var connector))
                {
                    ViewModel.BindTagToNodeInput(targetNode, tag, connector);
                    e.DragEffects = DragDropEffects.Copy;
                    e.Handled = true;
                }
                else
                {
                    var node = ViewModel.AddNodeForTagAt(tag, logicalPoint.X, logicalPoint.Y);
                    if (node != null)
                    {
                        e.DragEffects = DragDropEffects.Copy;
                        e.Handled = true;
                    }
                }

                return;
            }

            if (TryGetDraggedElementType(e.Data, out var elementType))
            {
                // The canvas is rendered through a ScaleTransform. Use the same
                // logical-coordinate conversion as node dragging.
                var node = ViewModel.AddNodeAt(elementType, logicalPoint.X, logicalPoint.Y);
                if (node != null)
                {
                    e.DragEffects = DragDropEffects.Copy;
                    e.Handled = true;
                }
            }
        }

        private void Palette_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (ViewModel == null || sender is not ListBox listBox)
            {
                return;
            }

            var point = e.GetCurrentPoint(listBox);
            if (!point.Properties.IsLeftButtonPressed
                || FindPaletteItem(e.Source) is not { } item)
            {
                return;
            }

            ViewModel.SelectedPaletteItem = item;
            _paletteDragItem = item;
            _paletteDragStart = e.GetPosition(listBox);
            _palettePointer = e.Pointer;
            _paletteDragStarted = false;
            e.Pointer.Capture(listBox);
        }

        private async void Palette_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_paletteDragItem == null || _palettePointer != e.Pointer
                || sender is not ListBox listBox)
            {
                return;
            }

            if (!e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed)
            {
                ResetPaletteDrag(e.Pointer);
                return;
            }

            var current = e.GetPosition(listBox);
            var deltaX = current.X - _paletteDragStart.X;
            var deltaY = current.Y - _paletteDragStart.Y;
            if (_paletteDragStarted || Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < DragThreshold)
            {
                return;
            }

            _paletteDragStarted = true;
            var item = _paletteDragItem;
            var data = new DataObject();
            data.Set(PaletteDragFormat, item.ElementType.ToString());
            data.Set(DataFormats.Text, item.ElementType.ToString());
            e.Pointer.Capture(null);

            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
            finally
            {
                ResetPaletteDrag(e.Pointer);
            }
        }

        private void Palette_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_palettePointer == e.Pointer)
            {
                ResetPaletteDrag(e.Pointer);
            }
        }

        private void Palette_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (ViewModel == null || sender is not ListBox listBox)
            {
                return;
            }

            var item = FindPaletteItem(e.Source);
            if (item == null)
            {
                return;
            }

            ViewModel.SelectedPaletteItem = item;
            ViewModel.AddNode();
            e.Handled = true;
        }

        private static PaletteItem? FindPaletteItem(object? source)
        {
            for (var control = source as Control; control != null; control = control.Parent as Control)
            {
                if (control.DataContext is PaletteItem item)
                {
                    return item;
                }
            }

            return null;
        }

        private static bool TryGetDraggedElementType(IDataObject data, out PlcElementType elementType)
        {
            elementType = default;
            object? value = null;
            if (data.Contains(PaletteDragFormat))
            {
                value = data.Get(PaletteDragFormat);
            }
            else if (data.Contains(DataFormats.Text))
            {
                value = data.GetText();
            }

            if (value is PlcElementType directType)
            {
                elementType = directType;
                return true;
            }

            if (value is not string text || !Enum.TryParse(text, ignoreCase: true, out PlcElementType parsedType))
            {
                return false;
            }

            if (!NodeDescriptors.All.Any(descriptor => descriptor.ElementType == parsedType))
            {
                return false;
            }

            elementType = parsedType;
            return true;
        }

        private bool TryGetDraggedTag(IDataObject data, out Tag? tag)
        {
            tag = null;

            if (data.Contains(TagDragFormat))
            {
                if (data.Get(TagDragFormat) is Tag directTag)
                {
                    tag = directTag;
                    return true;
                }
            }

            if (data.Contains(DataFormats.Text))
            {
                var text = data.GetText();
                if (!string.IsNullOrEmpty(text) && text.StartsWith(TagDragTextPrefix, StringComparison.Ordinal))
                {
                    var id = text.Substring(TagDragTextPrefix.Length);
                    tag = ViewModel?.FindTagById(id);
                    return tag != null;
                }
            }

            return false;
        }

        private bool TryGetNodeInputPortAt(object? source, Point logicalPoint, out VisualNode? node, out string? connector)
        {
            node = null;
            connector = null;

            if (source is Control control)
            {
                for (var current = control; current != null; current = current.Parent as Control)
                {
                    if (current.DataContext is not VisualNode visualNode)
                        continue;

                    if (current.Classes.Contains("Input1Port"))
                    {
                        node = visualNode;
                        connector = "Input1";
                        return true;
                    }

                    if (current.Classes.Contains("Input2Port"))
                    {
                        node = visualNode;
                        connector = visualNode.HasSecondInput ? "Input2" : "Input1";
                        return true;
                    }
                }
            }

            // Fallback geometric test for the left input-port region of any node.
            if (ViewModel == null)
                return false;

            foreach (var visualNode in ViewModel.Nodes)
            {
                if (logicalPoint.X >= visualNode.X - PortHitTolerance
                    && logicalPoint.X <= visualNode.X + PortHitTolerance
                    && logicalPoint.Y >= visualNode.Y
                    && logicalPoint.Y <= visualNode.Y + visualNode.Height)
                {
                    node = visualNode;
                    var relativeY = logicalPoint.Y - visualNode.Y;
                    connector = visualNode.HasSecondInput && relativeY > visualNode.Height / 2.0
                        ? "Input2"
                        : "Input1";
                    return true;
                }
            }

            return false;
        }

        private void ResetPaletteDrag(IPointer? pointer)
        {
            pointer?.Capture(null);
            _palettePointer = null;
            _paletteDragItem = null;
            _paletteDragStarted = false;
        }

        private void Node_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (ViewModel == null || sender is not Border border)
            {
                return;
            }

            var point = e.GetCurrentPoint(border);
            if (point.Properties.IsRightButtonPressed && border.DataContext is VisualNode rightNode)
            {
                ViewModel.SelectNode(rightNode, false);
                ShowNodeContextMenu(border, rightNode);
                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed || border.DataContext is not VisualNode node)
            {
                return;
            }

            var extendsSelection = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
            var wasSelected = node.IsSelected;
            var wasConnectMode = ViewModel.IsConnectMode;
            ViewModel.SelectNode(node, extendsSelection);
            if (wasConnectMode || (extendsSelection && wasSelected && !node.IsSelected))
            {
                e.Handled = true;
                return;
            }

            // If the user clicked the live-value TextBox, let it get focus but keep the node selected.
            if (e.Source is TextBox)
            {
                return;
            }

            _nodeCanvas ??= this.FindControl<Canvas>("NodeCanvas");
            _draggedNodes = ViewModel.GetNodesForDrag(node);
            _dragStartPositions = _draggedNodes.ToDictionary(
                selectedNode => selectedNode,
                selectedNode => (selectedNode.X, selectedNode.Y));
            _dragStartPoint = _nodeCanvas is { } canvas
                ? e.GetPosition(canvas)
                : e.GetPosition(border);
            _dragPointer = e.Pointer;
            _isDraggingNode = true;
            e.Pointer.Capture(border);
            e.Handled = true;

            // The node itself is not focusable; give the canvas focus so the view's
            // keyboard shortcuts (Ctrl+Z / Ctrl+Y) keep working after a node click.
            _nodeCanvas?.Focus();
        }

        private void Node_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDraggingNode || _draggedNodes.Count == 0 || ViewModel == null)
            {
                return;
            }

            if (!e.GetCurrentPoint(sender as Visual ?? this).Properties.IsLeftButtonPressed)
            {
                FinishNodeDrag(e.Pointer, commit: true);
                return;
            }

            var currentPoint = _nodeCanvas is { } canvas
                ? e.GetPosition(canvas)
                : e.GetPosition(this);
            var zoom = Math.Max(ViewModel.ZoomLevel, 0.01);
            var deltaX = (currentPoint.X - _dragStartPoint.X) / zoom;
            var deltaY = (currentPoint.Y - _dragStartPoint.Y) / zoom;
            foreach (var node in _draggedNodes)
            {
                if (_dragStartPositions.TryGetValue(node, out var start))
                {
                    ViewModel.SetNodePosition(node, start.X + deltaX, start.Y + deltaY);
                }
            }

            e.Handled = true;
        }

        private void Node_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDraggingNode) return;
            FinishNodeDrag(e.Pointer, commit: true);
            e.Handled = true;
        }

        private void Node_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_isDraggingNode)
            {
                FinishNodeDrag(null, commit: true);
            }
        }

        private void FinishNodeDrag(IPointer? pointer, bool commit)
        {
            if (!_isDraggingNode || ViewModel == null)
            {
                _isDraggingNode = false;
                _draggedNodes = Array.Empty<VisualNode>();
                _dragStartPositions = new Dictionary<VisualNode, (double X, double Y)>();
                _dragPointer = null;
                return;
            }

            var capturedPointer = pointer ?? _dragPointer;
            var nodes = _draggedNodes;
            var oldPositions = _dragStartPositions;
            _isDraggingNode = false;
            _draggedNodes = Array.Empty<VisualNode>();
            _dragStartPositions = new Dictionary<VisualNode, (double X, double Y)>();
            _dragPointer = null;
            capturedPointer?.Capture(null);

            if (commit)
            {
                ViewModel.CommitNodeMoves(oldPositions);
            }
            else
            {
                foreach (var node in nodes)
                {
                    if (oldPositions.TryGetValue(node, out var start))
                    {
                        ViewModel.SetNodePosition(node, start.X, start.Y);
                    }
                }
            }
        }

        private void LiveValue_GotFocus(object? sender, GotFocusEventArgs e)
        {
            if (sender is Control control && control.DataContext is VisualNode node)
            {
                node.IsEditingLiveValue = true;
            }
        }

        private void LiveValue_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is Control control && control.DataContext is VisualNode node)
            {
                node.IsEditingLiveValue = false;
            }
        }

        private void LiveValue_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                Focus();
            }
        }

        private void IncreaseGridSize_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.GridSize += 4;
        }

        private void DecreaseGridSize_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.GridSize -= 4;
        }

        private void View_KeyDown(object? sender, KeyEventArgs e)
        {
            if (ViewModel == null) return;

            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.Z)
            {
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    ViewModel.RedoCommand.Execute(null);
                }
                else
                {
                    ViewModel.UndoCommand.Execute(null);
                }

                e.Handled = true;
                return;
            }

            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.Y)
            {
                ViewModel.RedoCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (_isDraggingNode)
                {
                    FinishNodeDrag(_dragPointer, commit: false);
                    e.Handled = true;
                }
                else if (_isConnecting)
                {
                    CancelConnectionDrag();
                    e.Handled = true;
                }
                else if (ViewModel.IsConnectMode)
                {
                    ViewModel.CancelConnectCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Source is TextBox textBox && textBox.DataContext is IProgramTreeItem item)
                {
                    item.IsRenaming = false;
                    e.Handled = true;
                }
            }

            if ((e.Key == Key.Delete || e.Key == Key.Back) && e.Source is not TextBox)
            {
                ViewModel.RemoveNodeCommand.Execute(null);
                e.Handled = true;
            }
        }

        #region POU Tree Rename

        private void TreeItem_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not Grid grid || grid.DataContext is not IProgramTreeItem item)
            {
                return;
            }

            var viewPanel = grid.FindControl<StackPanel>("ViewPanel");
            var renameBox = grid.FindControl<TextBox>("RenameBox");
            if (viewPanel != null && renameBox != null)
            {
                item.IsRenaming = true;
                renameBox.Focus();
                renameBox.SelectAll();
                e.Handled = true;
            }
        }

        private void RenameBox_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is IProgramTreeItem item)
            {
                item.IsRenaming = false;
            }
        }

        private void RenameBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not IProgramTreeItem item)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                item.IsRenaming = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                item.IsRenaming = false;
                e.Handled = true;
            }
        }

        #endregion

        #region POU Tree Drag / Drop

        private void TreeView_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (ViewModel == null || sender is not TreeView treeView || e.Source is TextBox)
            {
                return;
            }

            var treeViewItem = FindParentTreeViewItem(e.Source as Control);
            if (treeViewItem?.DataContext is not IProgramTreeItem item)
            {
                return;
            }

            var point = e.GetCurrentPoint(treeView);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _treeDragItem = item;
            _treeDragStart = e.GetPosition(treeView);
            _treeDragPointer = e.Pointer;
            _treeDragStarted = false;
        }

        private async void TreeView_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_treeDragItem == null || _treeDragPointer != e.Pointer || sender is not TreeView treeView)
            {
                return;
            }

            if (!e.GetCurrentPoint(treeView).Properties.IsLeftButtonPressed)
            {
                ResetTreeDrag(e.Pointer);
                return;
            }

            var current = e.GetPosition(treeView);
            var deltaX = current.X - _treeDragStart.X;
            var deltaY = current.Y - _treeDragStart.Y;
            if (_treeDragStarted || Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < DragThreshold)
            {
                return;
            }

            _treeDragStarted = true;
            var item = _treeDragItem;
            var data = new DataObject();
            data.Set(ProgramTreeDragFormat, item.Id);
            e.Pointer.Capture(null);

            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            }
            finally
            {
                ResetTreeDrag(e.Pointer);
            }
        }

        private void TreeView_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_treeDragPointer == e.Pointer)
            {
                ResetTreeDrag(e.Pointer);
            }
        }

        private void TreeView_DragOver(object? sender, DragEventArgs e)
        {
            e.Handled = true;

            if (ViewModel == null || !e.Data.Contains(ProgramTreeDragFormat))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var source = GetDragSource(e.Data);
            var target = FindParentTreeViewItem(e.Source as Control)?.DataContext as IProgramTreeItem;
            e.DragEffects = GetAllowedDragEffect(source, target);
        }

        private void TreeView_Drop(object? sender, DragEventArgs e)
        {
            e.Handled = true;

            if (ViewModel == null || !e.Data.Contains(ProgramTreeDragFormat))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var source = GetDragSource(e.Data);
            if (source == null)
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var treeViewItem = FindParentTreeViewItem(e.Source as Control);
            var target = treeViewItem?.DataContext as IProgramTreeItem;
            var position = GetDropPosition(treeViewItem, e, source, target);

            if (target == null)
            {
                ViewModel.MoveItem(source, null, DropPosition.Into);
                e.DragEffects = DragDropEffects.Move;
                return;
            }

            if (GetAllowedDragEffect(source, target) == DragDropEffects.None)
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            // Convert a program dropped near a folder into a move-into that folder.
            if (source is ProgramModel && target is ProgramFolder)
            {
                position = DropPosition.Into;
            }

            // Folders cannot be dropped onto programs.
            if (source is ProgramFolder && target is ProgramModel)
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            ViewModel.MoveItem(source, target, position);
            e.DragEffects = DragDropEffects.Move;
        }

        private static TreeViewItem? FindParentTreeViewItem(Control? control)
        {
            for (var current = control; current != null; current = current.Parent as Control)
            {
                if (current is TreeViewItem tvi)
                {
                    return tvi;
                }
            }

            return null;
        }

        private IProgramTreeItem? GetDragSource(IDataObject data)
        {
            var id = data.Get(ProgramTreeDragFormat) as string;
            if (string.IsNullOrEmpty(id) || ViewModel == null)
            {
                return null;
            }

            return ViewModel.FindTreeItem(id);
        }

        private DragDropEffects GetAllowedDragEffect(IProgramTreeItem? source, IProgramTreeItem? target)
        {
            if (source == null)
            {
                return DragDropEffects.None;
            }

            if (target == null)
            {
                return DragDropEffects.Move;
            }

            if (ReferenceEquals(source, target))
            {
                return DragDropEffects.None;
            }

            if (source is ProgramModel)
            {
                return target is ProgramFolder || target is ProgramModel ? DragDropEffects.Move : DragDropEffects.None;
            }

            if (source is ProgramFolder sourceFolder && target is ProgramFolder targetFolder)
            {
                return ViewModel?.IsDescendantOf(sourceFolder, targetFolder) == true
                    ? DragDropEffects.None
                    : DragDropEffects.Move;
            }

            return DragDropEffects.None;
        }

        private static DropPosition GetDropPosition(TreeViewItem? targetItem, DragEventArgs e, IProgramTreeItem source, IProgramTreeItem? target)
        {
            if (targetItem == null || target is null || (target is not ProgramModel && target is not ProgramFolder))
            {
                return DropPosition.Into;
            }

            var position = e.GetPosition(targetItem);
            var height = targetItem.Bounds.Height;
            if (height <= 0 || !double.IsFinite(height))
            {
                return DropPosition.Into;
            }

            if (source is ProgramModel)
            {
                // Programs can only be reordered relative to other programs.
                return position.Y < height / 2.0 ? DropPosition.Before : DropPosition.After;
            }

            // Folders: top/bottom quarter is a reorder; middle half is a move-into.
            if (position.Y < height / 4.0)
            {
                return DropPosition.Before;
            }

            if (position.Y > height * 3.0 / 4.0)
            {
                return DropPosition.After;
            }

            return DropPosition.Into;
        }

        private void ResetTreeDrag(IPointer? pointer)
        {
            pointer?.Capture(null);
            _treeDragPointer = null;
            _treeDragItem = null;
            _treeDragStarted = false;
        }

        #endregion

        #region Context Menus

        private void ShowNodeContextMenu(Border border, VisualNode node)
        {
            var menu = new ContextMenu { Placement = PlacementMode.Pointer };

            var deleteItem = new MenuItem
            {
                Header = "Delete",
                Icon = new TextBlock { Text = "×", FontSize = 12 }
            };
            deleteItem.Click += (_, _) =>
            {
                ViewModel?.SelectNode(node, false);
                ViewModel?.RemoveNodeCommand.Execute(null);
            };
            menu.Items.Add(deleteItem);

            if (node.ElementType == PlcElementType.SignalGenerator)
            {
                var configItem = new MenuItem
                {
                    Header = "Configure Waveform...",
                    Icon = new TextBlock { Text = "…", FontSize = 12 }
                };
                configItem.Click += async (_, _) =>
                {
                    ViewModel?.SelectNode(node, false);

                    var window = new SignalGeneratorConfigWindow
                    {
                        DataContext = new SignalGeneratorConfigViewModel(node)
                    };

                    if (this.VisualRoot is Window owner)
                    {
                        var result = await window.ShowDialog<bool?>(owner);
                        if (result == true && window.DataContext is SignalGeneratorConfigViewModel vm)
                        {
                            node.Waveform = vm.Waveform;
                            node.PeriodMs = vm.PeriodMs;
                            node.Amplitude = vm.Amplitude;
                            node.Offset = vm.Offset;
                        }
                    }
                };
                menu.Items.Insert(0, configItem);
            }

            menu.Open(border);
        }

        private void ShowConnectionContextMenu(Path path, ConnectionLine line)
        {
            var menu = new ContextMenu { Placement = PlacementMode.Pointer };

            var deleteItem = new MenuItem
            {
                Header = "Delete Connection",
                Icon = new TextBlock { Text = "×", FontSize = 12 }
            };
            deleteItem.Click += (_, _) =>
            {
                ViewModel?.SelectConnection(line.ConnectionId);
                ViewModel?.RemoveConnectionCommand.Execute(null);
            };
            menu.Items.Add(deleteItem);

            menu.Open(path);
        }

        #endregion
    }
}
